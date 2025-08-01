namespace COA.CodeSearch.Contracts.Responses.DirectorySearch;

public class DirectorySearchResultsSummary
{
    public int included { get; set; }
    public int total { get; set; }
    public bool hasMore { get; set; }
}