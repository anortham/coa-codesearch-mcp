namespace COA.CodeSearch.Contracts.Responses.FileSizeAnalysis;

public class FileSizeMeta
{
    public string mode { get; set; } = string.Empty;
    public string analysisMode { get; set; } = string.Empty;
    public bool truncated { get; set; }
    public int tokens { get; set; }
    public string format { get; set; } = string.Empty;
}