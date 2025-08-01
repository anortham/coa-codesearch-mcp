namespace COA.CodeSearch.Contracts.Responses.FileSizeAnalysis;

public class FileSizeDirectoryGroup
{
    public string directory { get; set; } = string.Empty;
    public int fileCount { get; set; }
    public long totalSize { get; set; }
    public double avgSize { get; set; }
}