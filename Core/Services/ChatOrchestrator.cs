using Core.Entities;
using Core.Interfaces;
using Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Core.Services;

public class ChatOrchestrator
{
    private readonly IEmbeddingService _embeddingService;
    private readonly ISemanticCache _semanticCache;
    private readonly RetrieverService _retrieverService;
    private readonly ILlmService _llmService;
    private readonly ILogger<ChatOrchestrator> _logger;
    private readonly OrchestratorSettings _settings;

    public ChatOrchestrator(
        IEmbeddingService embeddingService,
        ISemanticCache semanticCache,
        RetrieverService retrieverService,
        ILlmService llmService,
        IOptions<OrchestratorSettings> options,
        ILogger<ChatOrchestrator> logger)
    {
        _embeddingService = embeddingService;
        _semanticCache = semanticCache;
        _retrieverService = retrieverService;
        _llmService = llmService;
        _logger = logger;
        _settings = options.Value;
    }

    public async Task<ChatResponse> ProcessChatAsync(string prompt)
    {
        // 1. Embed user query
        _logger.LogInformation("Embedding user prompt...");
        var queryVector = await _embeddingService.GetEmbeddingAsync(prompt);

        // 2. Search cache index
        _logger.LogInformation("Searching semantic cache...");
        var cacheResult = await _semanticCache.SearchAsync(queryVector, _settings.SimilarityThreshold);

        // 3a. Cache HIT
        if (cacheResult != null)
        {
            _logger.LogInformation("Cache HIT. Score: {Score}", cacheResult.Score);
            return new ChatResponse
            {
                Answer = cacheResult.Item.Response,
                CacheStatus = "HIT",
                SourceChunkIds = cacheResult.Item.SourceChunkIds
            };
        }

        // 3b. Cache MISS -> 4. Retrieve document chunks
        _logger.LogInformation("Cache MISS. Retrieving chunks...");
        var chunks = await _retrieverService.RetrieveAsync(queryVector);

        // 5. Generate LLM response
        _logger.LogInformation("Generating LLM response with {Count} chunks context...", chunks.Count);
        var llmResponse = await _llmService.GenerateResponseAsync(prompt, chunks);

        // 6. Store in cache (background)
        var sourceChunkIds = chunks.Select(c => c.Id).ToList();
        var cacheEntry = new CacheEntry
        {
            Id = Guid.NewGuid().ToString(),
            Prompt = prompt,
            Response = llmResponse,
            Vector = queryVector,
            SourceChunkIds = sourceChunkIds,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(_settings.TtlHours)
        };

        _ = Task.Run(async () =>
        {
            try
            {
                await _semanticCache.StoreAsync(cacheEntry);
                _logger.LogInformation("Successfully stored response in semantic cache.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to store response in semantic cache.");
            }
        });

        // 7. Return response
        return new ChatResponse
        {
            Answer = llmResponse,
            CacheStatus = "MISS",
            SourceChunkIds = sourceChunkIds
        };
    }
}

public class ChatResponse
{
    public string Answer { get; set; } = string.Empty;
    public string CacheStatus { get; set; } = string.Empty;
    public List<string> SourceChunkIds { get; set; } = new();
}