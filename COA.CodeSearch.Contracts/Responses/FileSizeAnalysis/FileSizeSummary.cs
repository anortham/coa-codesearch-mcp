namespace COA.CodeSearch.Contracts.Responses.FileSizeAnalysis;

public class FileSizeSummary
{
    public int totalFiles { get; set; }
    public string searchTime { get; set; } = string.Empty;
    public long totalSize { get; set; }
    public string totalSizeFormatted { get; set; } = string.Empty;
    public string avgSize { get; set; } = string.Empty;
    public string medianSize { get; set; } = string.Empty;
}