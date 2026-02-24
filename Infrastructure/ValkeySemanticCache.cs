using System.Text.Json;
using Core.Entities;
using Core.Interfaces;
using Core.Settings;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Infrastructure;

public class ValkeySemanticCache : ISemanticCache
{
    private readonly IDatabase _db;
    private const string IndexName = "idx:cache";
    private const string KeyPrefix = "cache:";
    private readonly TimeSpan _defaultTtl = TimeSpan.FromHours(24);

    public ValkeySemanticCache(IConnectionMultiplexer redis, IOptions<OrchestratorSettings> options)
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
                "Prompt", "TEXT",
                "Response", "TEXT",
                "Vector", "VECTOR", "HNSW", "6", "TYPE", "FLOAT32", "DIM", vectorDimension.ToString(), "DISTANCE_METRIC", "COSINE",
                "SourceChunkIdsTag", "TAG"
            };
            _db.Execute("FT.CREATE", args);
        }
    }

    public async Task StoreAsync(CacheEntry entry)
    {
        var key = $"{KeyPrefix}{entry.Id}";
        
        var sourceChunkIdsJson = JsonSerializer.Serialize(entry.SourceChunkIds);
        var sourceChunkIdsTag = string.Join(",", entry.SourceChunkIds);
        
        var hashEntries = new HashEntry[]
        {
            new HashEntry("Id", entry.Id),
            new HashEntry("Prompt", entry.Prompt),
            new HashEntry("Response", entry.Response),
            new HashEntry("SourceChunkIds", sourceChunkIdsJson),
            new HashEntry("SourceChunkIdsTag", sourceChunkIdsTag),
            new HashEntry("CreatedAt", entry.CreatedAt.ToUnixTimeSeconds()),
            new HashEntry("ExpiresAt", entry.ExpiresAt.ToUnixTimeSeconds()),
            new HashEntry("Vector", GetVectorBytes(entry.Vector))
        };

        await _db.HashSetAsync(key, hashEntries);
        
        // TTL-based expiry
        var ttl = entry.ExpiresAt - DateTimeOffset.UtcNow;
        if (ttl <= TimeSpan.Zero)
        {
            ttl = _defaultTtl;
        }
        await _db.KeyExpireAsync(key, ttl);
    }

    public async Task<SimilarityResult<CacheEntry>?> SearchAsync(float[] vector, double minimumScore)
    {
        var query = "*=>[KNN 1 @Vector $query_vector AS score]";
        var args = new object[]
        {
            IndexName,
            query,
            "PARAMS", "2", "query_vector", GetVectorBytes(vector),
            "DIALECT", "2"
        };

        var result = await _db.ExecuteAsync("FT.SEARCH", args);
        var searchResults = (RedisResult[])result!;

        if (searchResults.Length < 3)
        {
            return null; // No results or expired key with no fields
        }

        var key = (string)searchResults[1]!;

        // Expired keys may still appear in the index without field data
        RedisResult[] fields;
        try
        {
            fields = (RedisResult[])searchResults[2]!;
        }
        catch (InvalidCastException)
        {
            return null;
        }
        
        var entry = new CacheEntry();
        double score = 0;

        for (int j = 0; j < fields.Length; j += 2)
        {
            var fieldName = (string)fields[j]!;
            var fieldValue = fields[j + 1];

            switch (fieldName)
            {
                case "Id": entry.Id = (string)fieldValue!; break;
                case "Prompt": entry.Prompt = (string)fieldValue!; break;
                case "Response": entry.Response = (string)fieldValue!; break;
                case "SourceChunkIds": 
                    var json = (string)fieldValue!;
                    if (!string.IsNullOrEmpty(json))
                    {
                        entry.SourceChunkIds = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
                    }
                    break;
                case "CreatedAt": entry.CreatedAt = DateTimeOffset.FromUnixTimeSeconds((long)fieldValue); break;
                case "ExpiresAt": entry.ExpiresAt = DateTimeOffset.FromUnixTimeSeconds((long)fieldValue); break;
                case "score": score = 1.0 - (double)fieldValue; break; // Cosine distance to similarity
            }
        }
        
        if (score >= minimumScore)
        {
            return new SimilarityResult<CacheEntry>(entry, score);
        }

        return null;
    }

    public async Task InvalidateByChunkIdsAsync(List<string> chunkIds)
    {
        if (chunkIds == null || !chunkIds.Any())
        {
            return;
        }

        var tags = string.Join(" | ", chunkIds.Select(EscapeTagValue));
        var query = $"@SourceChunkIdsTag:{{{tags}}}";
        const int pageSize = 1000;

        while (true)
        {
            // Always query offset 0 since we delete results each iteration
            var args = new object[]
            {
                IndexName, query, "NOCONTENT",
                "LIMIT", "0", pageSize.ToString()
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

            var totalCount = (int)searchResults[0];
            if (keysToDelete.Count >= totalCount)
                break;
        }
    }

    private static string EscapeTagValue(string value)
    {
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