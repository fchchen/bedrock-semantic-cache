using Core.Entities;

namespace Core.Interfaces;

public interface ILlmService
{
    Task<string> GenerateResponseAsync(string prompt, List<DocumentChunk> context);
}
