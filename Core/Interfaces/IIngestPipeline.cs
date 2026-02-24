using Core.Entities;

namespace Core.Interfaces;

public interface IIngestPipeline
{
    Task<IngestJob> IngestAsync(string documentId, string fileName, string content);
}
