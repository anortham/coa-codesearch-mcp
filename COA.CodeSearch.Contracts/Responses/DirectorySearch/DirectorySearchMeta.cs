namespace COA.CodeSearch.Contracts.Responses.DirectorySearch;

public class DirectorySearchMeta
{
    public string mode { get; set; } = string.Empty;
    public bool truncated { get; set; }
    public int tokens { get; set; }
    public string format { get; set; } = string.Empty;
    public string cached { get; set; } = string.Empty;
}