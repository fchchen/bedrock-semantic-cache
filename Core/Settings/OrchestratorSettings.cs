namespace Core.Settings;

public class OrchestratorSettings
{
    public double SimilarityThreshold { get; set; } = 0.85; // For Cache HIT
    public double RetrievalMinScore { get; set; } = 0.70;   // For Document Retrieval
    public int TopK { get; set; } = 5;
    public int TtlHours { get; set; } = 24;
}