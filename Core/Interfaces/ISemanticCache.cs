using Core.Entities;

namespace Core.Interfaces;

public interface ISemanticCache
{
    Task StoreAsync(CacheEntry entry);
    Task<SimilarityResult<CacheEntry>?> SearchAsync(float[] vector, double minimumScore);
    Task InvalidateByChunkIdsAsync(List<string> chunkIds);
}
