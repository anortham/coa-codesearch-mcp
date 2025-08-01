namespace COA.CodeSearch.Contracts.Responses.FileSizeAnalysis;

public class FileSizeResultItem
{
    public string file { get; set; } = string.Empty;
    public string path { get; set; } = string.Empty;
    public long size { get; set; }
    public string sizeFormatted { get; set; } = string.Empty;
    public string extension { get; set; } = string.Empty;
    public double percentOfTotal { get; set; }
}