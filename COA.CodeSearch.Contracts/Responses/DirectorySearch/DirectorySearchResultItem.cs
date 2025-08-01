namespace COA.CodeSearch.Contracts.Responses.DirectorySearch;

public class DirectorySearchResultItem
{
    public string name { get; set; } = string.Empty;
    public string path { get; set; } = string.Empty;
    public int fileCount { get; set; }
    public int depth { get; set; }
    public double score { get; set; }
}