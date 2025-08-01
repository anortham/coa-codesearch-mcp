namespace COA.CodeSearch.Contracts.Responses.FileSizeAnalysis;

public class SizeOutlier
{
    public string file { get; set; } = string.Empty;
    public string path { get; set; } = string.Empty;
    public string size { get; set; } = string.Empty;
    public double zScore { get; set; }
}