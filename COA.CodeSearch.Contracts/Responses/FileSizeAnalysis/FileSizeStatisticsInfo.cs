namespace COA.CodeSearch.Contracts.Responses.FileSizeAnalysis;

public class FileSizeStatisticsInfo
{
    public string min { get; set; } = string.Empty;
    public string max { get; set; } = string.Empty;
    public string mean { get; set; } = string.Empty;
    public string median { get; set; } = string.Empty;
    public string stdDev { get; set; } = string.Empty;
    public SizeDistribution distribution { get; set; } = new();
}