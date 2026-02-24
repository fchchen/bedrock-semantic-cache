namespace Core.Entities;

public class DocumentChunk
{
    public string Id { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public float[] Vector { get; set; } = Array.Empty<float>();
    public int ChunkIndex { get; set; }
    public int CharOffset { get; set; }
    public DateTimeOffset IngestTimestamp { get; set; }
}
