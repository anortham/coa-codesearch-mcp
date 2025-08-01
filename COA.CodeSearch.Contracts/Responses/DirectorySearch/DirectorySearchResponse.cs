using System.Collections.Generic;

namespace COA.CodeSearch.Contracts.Responses.DirectorySearch;

public class DirectorySearchResponse
{
    public bool success { get; set; }
    public string operation { get; set; } = string.Empty;
    public DirectorySearchQuery query { get; set; } = new();
    public DirectorySearchSummary summary { get; set; } = new();
    public DirectorySearchAnalysis analysis { get; set; } = new();
    public List<DirectorySearchResultItem> results { get; set; } = new();
    public DirectorySearchResultsSummary resultsSummary { get; set; } = new();
    public List<string> insights { get; set; } = new();
    public List<object> actions { get; set; } = new();
    public DirectorySearchMeta meta { get; set; } = new();
}