namespace COA.CodeSearch.Contracts.Responses.SimilarFiles;

public class SimilarFilesMeta
{
    public string mode { get; set; } = string.Empty;
    public bool truncated { get; set; }
    public int tokens { get; set; }
    public string format { get; set; } = string.Empty;
    public string algorithm { get; set; } = string.Empty;
}