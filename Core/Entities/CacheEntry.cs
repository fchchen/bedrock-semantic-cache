namespace Core.Entities;

public class CacheEntry
{
    public string Id { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string Response { get; set; } = string.Empty;
    public float[] Vector { get; set; } = Array.Empty<float>();
    public List<string> SourceChunkIds { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
