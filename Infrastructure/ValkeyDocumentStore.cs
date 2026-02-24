using System.Diagnostics;
using Core.Entities;
using Core.Interfaces;
using Core.Settings;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Text.Json;

namespace Infrastructure;

public class ValkeyDocumentStore : IDocumentStore
{
    private readonly IDatabase _db;
    private const string IndexName = "idx:documents";
    private const string KeyPrefix = "doc:";
    private const string SearchDialect = "2";
    private static readonly ActivitySource ActivitySource = new("BedrockSemanticCache");

    public ValkeyDocumentStore(IConnectionMultiplexer redis, IOptions<OrchestratorSettings> options)
    {
        _db = redis.GetDatabase();
        CreateIndexIfNotExists(options.Value.VectorDimension);
    }

    private void CreateIndexIfNotExists(int vectorDimension)
    {
        try
        {
            _db.Execute("FT.INFO", IndexName);
        }
        catch (RedisServerException ex) when (ex.Message.Contains("Unknown index name", StringComparison.OrdinalIgnoreCase))
        {
            var args = new object[]
            {
                IndexName,
                "ON", "HASH",
                "PREFIX", "1", KeyPrefix,
                "SCHEMA",
                "Text", "TEXT",
                "DocumentId", "TAG",
                "ChunkIndex", "NUMERIC",
                "IngestTimestamp", "NUMERIC",
                "Vector", "VECTOR", "HNSW", "6", "TYPE", "FLOAT32", "DIM", vectorDimension.ToString(), "DISTANCE_METRIC", "COSINE"
            };
            _db.Execute("FT.CREATE", args);
        }
    }

    public async Task StoreChunkAsync(DocumentChunk chunk)
    {
        using var activity = ActivitySource.StartActivity("DocumentStore.StoreChunkAsync");
        activity?.SetTag("document.id", chunk.DocumentId);
        activity?.SetTag("chunk.index", chunk.ChunkIndex);

        var key = $"{KeyPrefix}{chunk.Id}";
        var hashEntries = new HashEntry[]
        {
            new HashEntry("Id", chunk.Id),
            new HashEntry("DocumentId", chunk.DocumentId),
            new HashEntry("Text", chunk.Text),
            new HashEntry("ChunkIndex", chunk.ChunkIndex),
            new HashEntry("CharOffset", chunk.CharOffset),
            new HashEntry("IngestTimestamp", chunk.IngestTimestamp.ToUnixTimeSeconds()),
            new HashEntry("Vector", GetVectorBytes(chunk.Vector))
        };

        await _db.HashSetAsync(key, hashEntries);
    }

    public async Task<List<SimilarityResult<DocumentChunk>>> SearchAsync(float[] vector, int topK)
    {
        using var activity = ActivitySource.StartActivity("DocumentStore.SearchAsync");
        activity?.SetTag("top.k", topK);

        var query = $"*=>[KNN {topK} @Vector $query_vector AS score]";
        var args = new object[]
        {
            IndexName,
            query,
            "PARAMS", "2", "query_vector", GetVectorBytes(vector),
            "DIALECT", SearchDialect
        };

        var result = await _db.ExecuteAsync("FT.SEARCH", args);
        var searchResults = (RedisResult[])result!;
        
        var chunks = new List<SimilarityResult<DocumentChunk>>();
        
        // Results are [total_count, key1, [field1, value1, field2, value2...], key2, [field1, value1...]]
        // So we skip the first element (count) and iterate by 2
        for (int i = 1; i < searchResults.Length; i += 2)
        {
            var key = (string)searchResults[i]!;
            var fields = (RedisResult[])searchResults[i + 1]!;
            
            var chunk = new DocumentChunk();
            double score = 0;

            for (int j = 0; j < fields.Length; j += 2)
            {
                var fieldName = (string)fields[j]!;
                var fieldValue = fields[j + 1];

                switch (fieldName)
                {
                    case "Id": chunk.Id = (string)fieldValue!; break;
                    case "DocumentId": chunk.DocumentId = (string)fieldValue!; break;
                    case "Text": chunk.Text = (string)fieldValue!; break;
                    case "ChunkIndex": chunk.ChunkIndex = (int)fieldValue; break;
                    case "CharOffset": chunk.CharOffset = (int)fieldValue; break;
                    case "IngestTimestamp": chunk.IngestTimestamp = DateTimeOffset.FromUnixTimeSeconds((long)fieldValue); break;
                    case "score": score = 1.0 - (double)fieldValue; break; // Cosine distance to similarity
                }
            }
            
            chunks.Add(new SimilarityResult<DocumentChunk>(chunk, score));
        }

        activity?.SetTag("retrieved.count", chunks.Count);
        return chunks;
    }

    public async Task<List<string>> GetChunkIdsByDocumentIdAsync(string documentId)
    {
        using var activity = ActivitySource.StartActivity("DocumentStore.GetChunkIdsByDocumentIdAsync");
        activity?.SetTag("document.id", documentId);

        var escapedId = EscapeTagValue(documentId);
        var query = $"@DocumentId:{{{escapedId}}}";
        var chunkIds = new List<string>();
        const int pageSize = 1000;
        int offset = 0;

        while (true)
        {
            var args = new object[]
            {
                IndexName, query, "NOCONTENT",
                "LIMIT", offset.ToString(), pageSize.ToString(),
                "DIALECT", SearchDialect
            };

            var result = await _db.ExecuteAsync("FT.SEARCH", args);
            var searchResults = (RedisResult[])result!;
            var totalCount = (int)searchResults[0];

            for (int i = 1; i < searchResults.Length; i++)
            {
                var key = (string)searchResults[i]!;
                if (key.StartsWith(KeyPrefix))
                {
                    chunkIds.Add(key.Substring(KeyPrefix.Length));
                }
            }

            offset += pageSize;
            if (offset >= totalCount)
                break;
        }

        activity?.SetTag("chunk.count", chunkIds.Count);
        return chunkIds;
    }

    public async Task DeleteByDocumentIdAsync(string documentId)
    {
        using var activity = ActivitySource.StartActivity("DocumentStore.DeleteByDocumentIdAsync");
        activity?.SetTag("document.id", documentId);

        var escapedId = EscapeTagValue(documentId);
        var query = $"@DocumentId:{{{escapedId}}}";
        const int pageSize = 1000;
        int deletedTotal = 0;

        while (true)
        {
            // Always query offset 0 since we delete results each iteration
            var args = new object[]
            {
                IndexName, query, "NOCONTENT",
                "LIMIT", "0", pageSize.ToString(),
                "DIALECT", SearchDialect
            };

            var result = await _db.ExecuteAsync("FT.SEARCH", args);
            var searchResults = (RedisResult[])result!;

            var keysToDelete = new List<RedisKey>();
            for (int i = 1; i < searchResults.Length; i++)
            {
                keysToDelete.Add((string)searchResults[i]!);
            }

            if (!keysToDelete.Any())
                break;

            await _db.KeyDeleteAsync(keysToDelete.ToArray());
            deletedTotal += keysToDelete.Count;

            var totalCount = (int)searchResults[0];
            if (keysToDelete.Count >= totalCount)
                break;
        }
        activity?.SetTag("deleted.count", deletedTotal);
    }

    private static string EscapeTagValue(string value)
    {
        // Escape RediSearch special characters for TAG field queries
        return value
            .Replace("\\", "\\\\")
            .Replace("-", "\\-")
            .Replace(".", "\\.")
            .Replace("@", "\\@")
            .Replace(":", "\\:")
            .Replace("/", "\\/")
            .Replace("!", "\\!")
            .Replace("{", "\\{")
            .Replace("}", "\\}")
            .Replace("(", "\\(")
            .Replace(")", "\\)")
            .Replace("[", "\\[")
            .Replace("]", "\\]")
            .Replace("^", "\\^")
            .Replace("~", "\\~")
            .Replace("*", "\\*")
            .Replace("'", "\\'")
            .Replace("\"", "\\\"")
            .Replace("|", "\\|")
            .Replace(" ", "\\ ");
    }

    private byte[] GetVectorBytes(float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }
}