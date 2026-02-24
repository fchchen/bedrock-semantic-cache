using System.Diagnostics;
using Core.Entities;
using Core.Interfaces;
using Core.Settings;
using Microsoft.Extensions.Options;

namespace Core.Services;

public class RetrieverService
{
    private readonly IDocumentStore _documentStore;
    private readonly OrchestratorSettings _settings;
    private static readonly ActivitySource ActivitySource = new("BedrockSemanticCache");
    
    public RetrieverService(IDocumentStore documentStore, IOptions<OrchestratorSettings> options)
    {
        _documentStore = documentStore;
        _settings = options.Value;
    }

    public async Task<List<DocumentChunk>> RetrieveAsync(float[] queryVector)
    {
        using var activity = ActivitySource.StartActivity("RetrieverService.RetrieveAsync");
        
        var results = await _documentStore.SearchAsync(queryVector, _settings.TopK);
        
        var filteredChunks = results
            .Where(r => r.Score >= _settings.RetrievalMinScore)
            .OrderByDescending(r => r.Score)
            .Select(r => r.Item)
            .ToList();
            
        activity?.SetTag("retrieved.total", results.Count);
        activity?.SetTag("retrieved.filtered", filteredChunks.Count);
        activity?.SetTag("threshold", _settings.RetrievalMinScore);

        return filteredChunks;
    }
}