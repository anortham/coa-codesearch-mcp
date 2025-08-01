using System.Collections.Generic;

namespace COA.CodeSearch.Contracts.Responses.DirectorySearch;

public class DirectorySearchAnalysis
{
    public List<string> patterns { get; set; } = new();
    public Dictionary<int, int> depthDistribution { get; set; } = new();
    public DirectorySearchHotspots hotspots { get; set; } = new();
}