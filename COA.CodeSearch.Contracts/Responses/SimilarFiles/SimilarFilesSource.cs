namespace COA.CodeSearch.Contracts.Responses.SimilarFiles;

public class SimilarFilesSource
{
    public string file { get; set; } = string.Empty;
    public string path { get; set; } = string.Empty;
    public long size { get; set; }
    public string sizeFormatted { get; set; } = string.Empty;
}