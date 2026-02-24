using Core.Interfaces;

namespace Infrastructure.Chunking;

public class FixedSizeChunker : IChunkingStrategy
{
    private readonly int _chunkSize;
    private readonly int _overlap;

    public FixedSizeChunker(int chunkSize = 512, int overlap = 50)
    {
        _chunkSize = chunkSize;
        _overlap = overlap;
    }

    public List<string> Chunk(string text)
    {
        if (string.IsNullOrEmpty(text)) return new List<string>();

        // Simplistic implementation using character count instead of actual tokens for demo
        var chunks = new List<string>();
        int i = 0;
        while (i < text.Length)
        {
            int length = Math.Min(_chunkSize, text.Length - i);
            chunks.Add(text.Substring(i, length));
            i += _chunkSize - _overlap;
            
            // Prevent infinite loop if overlap >= chunkSize
            if (_chunkSize - _overlap <= 0) break;
        }

        return chunks;
    }
}