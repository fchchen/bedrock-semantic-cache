using Core.Entities;

namespace Core.Interfaces;

public interface IDocumentStore
{
    Task StoreChunkAsync(DocumentChunk chunk);
    Task<List<SimilarityResult<DocumentChunk>>> SearchAsync(float[] vector, int topK);
    Task DeleteByDocumentIdAsync(string documentId);
}
