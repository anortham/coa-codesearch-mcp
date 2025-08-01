namespace COA.CodeSearch.Contracts.Responses.RecentFiles;

/// <summary>
/// Complete response structure for recent files operation
/// CRITICAL: Property names match EXACTLY the anonymous type structure in RecentFilesResponseBuilder.cs line 97-169
/// </summary>
public class RecentFilesResponse
{
    public bool success { get; set; }                                   // EXACT casing from line 99!
    public string operation { get; set; } = string.Empty;              // EXACT casing from line 100!
    public RecentFilesQuery query { get; set; } = new();               // EXACT casing from line 101!
    public RecentFilesSummary summary { get; set; } = new();           // EXACT casing from line 107!
    public RecentFilesAnalysis analysis { get; set; } = new();         // EXACT casing from line 120!
    public List<RecentFileResultItem> results { get; set; } = new();   // EXACT casing from line 135!
    public ResultsSummary resultsSummary { get; set; } = new();        // EXACT casing from line 145!
    public List<string> insights { get; set; } = new();                // EXACT casing from line 151!
    public List<object> actions { get; set; } = new();                 // EXACT casing from line 152! (Keep as object for now)
    public ResponseMeta meta { get; set; } = new();                    // EXACT casing from line 161!
}