using Core.Interfaces;
using System.Text.RegularExpressions;

namespace Infrastructure.Chunking;

public partial class SentenceAwareChunker : IChunkingStrategy
{
    private readonly int _targetChunkSize;
    private readonly int _overlap;

    [GeneratedRegex(@"(?<=[.!?])\s+(?=[A-Z])")]
    private static partial Regex SentenceBoundaryRegex();

    public SentenceAwareChunker(int targetChunkSize = 512, int overlap = 50)
    {
        _targetChunkSize = targetChunkSize;
        _overlap = overlap;
    }

    public List<string> Chunk(string text)
    {
        if (string.IsNullOrEmpty(text)) return new List<string>();

        var sentences = SentenceBoundaryRegex().Split(text);

        var chunks = new List<string>();
        string currentChunk = "";

        foreach (var sentence in sentences)
        {
            if (currentChunk.Length + sentence.Length > _targetChunkSize && !string.IsNullOrEmpty(currentChunk))
            {
                chunks.Add(currentChunk.Trim());
                
                // Keep the last part of the previous chunk for overlap
                int overlapStart = Math.Max(0, currentChunk.Length - _overlap);
                currentChunk = currentChunk.Substring(overlapStart) + " " + sentence;
            }
            else
            {
                currentChunk += (string.IsNullOrEmpty(currentChunk) ? "" : " ") + sentence;
            }
        }

        if (!string.IsNullOrWhiteSpace(currentChunk))
        {
            chunks.Add(currentChunk.Trim());
        }

        return chunks;
    }
}