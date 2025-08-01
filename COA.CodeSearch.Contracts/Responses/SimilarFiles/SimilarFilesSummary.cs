namespace COA.CodeSearch.Contracts.Responses.SimilarFiles;

public class SimilarFilesSummary
{
    public int totalFound { get; set; }
    public string searchTime { get; set; } = string.Empty;
    public double avgSimilarity { get; set; }
    public SimilarityRanges similarityDistribution { get; set; } = new();
}