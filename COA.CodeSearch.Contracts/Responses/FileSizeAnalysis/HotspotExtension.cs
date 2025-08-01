namespace COA.CodeSearch.Contracts.Responses.FileSizeAnalysis;

public class HotspotExtension
{
    public string extension { get; set; } = string.Empty;
    public int count { get; set; }
    public string totalSize { get; set; } = string.Empty;
}