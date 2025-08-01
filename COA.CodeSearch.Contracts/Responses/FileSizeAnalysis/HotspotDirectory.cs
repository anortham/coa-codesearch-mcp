namespace COA.CodeSearch.Contracts.Responses.FileSizeAnalysis;

public class HotspotDirectory
{
    public string path { get; set; } = string.Empty;
    public int files { get; set; }
    public string totalSize { get; set; } = string.Empty;
    public string avgSize { get; set; } = string.Empty;
}