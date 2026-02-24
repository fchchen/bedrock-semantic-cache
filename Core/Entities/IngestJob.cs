namespace Core.Entities;

public class IngestJob
{
    public string Id { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public int ChunkCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
