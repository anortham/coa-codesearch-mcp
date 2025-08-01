namespace COA.CodeSearch.Contracts.Responses.DirectorySearch;

public class DirectorySearchSummary
{
    public int totalFound { get; set; }
    public string searchTime { get; set; } = string.Empty;
    public string performance { get; set; } = string.Empty;
    public double avgDepth { get; set; }
}