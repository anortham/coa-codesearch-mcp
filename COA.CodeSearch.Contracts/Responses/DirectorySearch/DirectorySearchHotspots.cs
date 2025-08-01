using System.Collections.Generic;

namespace COA.CodeSearch.Contracts.Responses.DirectorySearch;

public class DirectorySearchHotspots
{
    public Dictionary<string, int> byParent { get; set; } = new();
    public List<DirectoryFileItem> byFileCount { get; set; } = new();
}