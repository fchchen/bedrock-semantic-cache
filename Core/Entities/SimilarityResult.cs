namespace Core.Entities;

public class SimilarityResult<T>
{
    public T Item { get; set; }
    public double Score { get; set; }

    public SimilarityResult(T item, double score)
    {
        Item = item;
        Score = score;
    }
}
