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
    private readonly IBackgroundTaskQueue _backgroundQueue;
    private readonly ILogger<IngestPipeline> _logger;

    public IngestPipeline(
        IChunkingStrategy chunker,
        IEmbeddingService embeddingService,
        IDocumentStore documentStore,
        JobStore jobStore,
        IBackgroundTaskQueue backgroundQueue,
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

    private async Task ProcessAsync(IngestJob job, string content)
    {
        try
        {
            _logger.LogInformation("Starting ingestion for {FileName} ({DocumentId})", job.FileName, job.DocumentId);

            var textChunks = _chunker.Chunk(content);
            job.ChunkCount = textChunks.Count;
            _jobStore.AddOrUpdate(job);

            int charOffset = 0;
            for (int i = 0; i < textChunks.Count; i++)
            {
                var text = textChunks[i];
                var vector = await _embeddingService.GetEmbeddingAsync(text);

                var chunk = new DocumentChunk
                {
                    Id = Guid.NewGuid().ToString(),
                    DocumentId = job.DocumentId,
                    Text = text,
                    Vector = vector,
                    ChunkIndex = i,
                    CharOffset = charOffset,
                    IngestTimestamp = DateTimeOffset.UtcNow
                };

                await _documentStore.StoreChunkAsync(chunk);
                charOffset += text.Length;
            }

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