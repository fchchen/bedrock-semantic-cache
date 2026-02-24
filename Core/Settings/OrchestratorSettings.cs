namespace Core.Settings;

public class OrchestratorSettings
{
    public double SimilarityThreshold { get; set; } = 0.85;
    public int TopK { get; set; } = 5;
    public int TtlHours { get; set; } = 24;
}