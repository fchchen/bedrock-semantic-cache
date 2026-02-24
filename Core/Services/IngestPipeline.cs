using Core.Entities;
using Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Core.Services;

public class IngestPipeline : IIngestPipeline
{
    private readonly IChunkingStrategy _chunker;
    private readonly IEmbeddingService _embeddingService;
    private readonly IDocumentStore _documentStore;
    private readonly JobStore _jobStore;
    private readonly IIngestTaskQueue _backgroundQueue;
    private readonly ILogger<IngestPipeline> _logger;

    public IngestPipeline(
        IChunkingStrategy chunker,
        IEmbeddingService embeddingService,
        IDocumentStore documentStore,
        JobStore jobStore,
        IIngestTaskQueue backgroundQueue,
        ILogger<IngestPipeline> logger)
    {
        _chunker = chunker;
        _embeddingService = embeddingService;
        _documentStore = documentStore;
        _jobStore = jobStore;
        _backgroundQueue = backgroundQueue;
        _logger = logger;
    }

    public async Task<IngestJob> IngestAsync(string documentId, string fileName, string content)
    {
        var job = new IngestJob
        {
            Id = Guid.NewGuid().ToString(),
            DocumentId = documentId,
            FileName = fileName,
            CreatedAt = DateTimeOffset.UtcNow,
            Status = "Processing"
        };

        _jobStore.AddOrUpdate(job);

        await _backgroundQueue.EnqueueAsync(ct => ProcessAsync(job, content));

        return job;
    }

    private const int MaxConcurrentEmbeddings = 5;

    private async Task ProcessAsync(IngestJob job, string content)
    {
        try
        {
            _logger.LogInformation("Starting ingestion for {FileName} ({DocumentId})", job.FileName, job.DocumentId);

            var textChunks = _chunker.Chunk(content);
            job.ChunkCount = textChunks.Count;
            _jobStore.AddOrUpdate(job);

            // Pre-compute char offsets so we can parallelize embedding + storage
            var offsets = new int[textChunks.Count];
            int offset = 0;
            for (int i = 0; i < textChunks.Count; i++)
            {
                offsets[i] = offset;
                offset += textChunks[i].Length;
            }

            using var semaphore = new SemaphoreSlim(MaxConcurrentEmbeddings);
            var tasks = textChunks.Select(async (text, i) =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var vector = await _embeddingService.GetEmbeddingAsync(text);
                    var chunk = new DocumentChunk
                    {
                        Id = Guid.NewGuid().ToString(),
                        DocumentId = job.DocumentId,
                        Text = text,
                        Vector = vector,
                        ChunkIndex = i,
                        CharOffset = offsets[i],
                        IngestTimestamp = DateTimeOffset.UtcNow
                    };
                    await _documentStore.StoreChunkAsync(chunk);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);

            job.Status = "Done";
            _jobStore.AddOrUpdate(job);
            _logger.LogInformation("Completed ingestion for {DocumentId}. Chunks: {Count}", job.DocumentId, textChunks.Count);
        }
        catch (Exception ex)
        {
            job.Status = "Failed";
            _jobStore.AddOrUpdate(job);
            _logger.LogError(ex, "Failed ingestion for {DocumentId}", job.DocumentId);
        }
    }
}