using System.Diagnostics;
using Core.Entities;
using Core.Interfaces;

namespace Core.Services;

public class RetrieverService
{
    private readonly IDocumentStore _documentStore;
    private static readonly ActivitySource ActivitySource = new("BedrockSemanticCache");
    
    // We'll make these properties so they can be easily mocked/configured, 
    // or we could use IOptions in a real app.
    public int TopK { get; set; } = 5;
    public double MinScoreThreshold { get; set; } = 0.75;

    public RetrieverService(IDocumentStore documentStore)
    {
        _documentStore = documentStore;
    }

    public async Task<List<DocumentChunk>> RetrieveAsync(float[] queryVector)
    {
        using var activity = ActivitySource.StartActivity("RetrieverService.RetrieveAsync");
        
        var results = await _documentStore.SearchAsync(queryVector, TopK);
        
        var filteredChunks = results
            .Where(r => r.Score >= MinScoreThreshold)
            .OrderByDescending(r => r.Score)
            .Select(r => r.Item)
            .ToList();
            
        activity?.SetTag("retrieved.total", results.Count);
        activity?.SetTag("retrieved.filtered", filteredChunks.Count);
        activity?.SetTag("threshold", MinScoreThreshold);

        return filteredChunks;
    }
}