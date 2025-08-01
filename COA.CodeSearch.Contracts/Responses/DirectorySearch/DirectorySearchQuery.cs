namespace COA.CodeSearch.Contracts.Responses.DirectorySearch;

public class DirectorySearchQuery
{
    public string text { get; set; } = string.Empty;
    public string type { get; set; } = string.Empty;
    public string workspace { get; set; } = string.Empty;
}