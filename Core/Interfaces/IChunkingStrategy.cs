namespace Core.Interfaces;

public interface IChunkingStrategy
{
    List<string> Chunk(string text);
}
