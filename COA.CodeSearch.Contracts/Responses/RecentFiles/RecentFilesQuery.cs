namespace COA.CodeSearch.Contracts.Responses.RecentFiles;

/// <summary>
/// Query parameters for recent files operation
/// CRITICAL: Property names match EXACTLY the anonymous type structure in RecentFilesResponseBuilder.cs line 101-106
/// </summary>
public class RecentFilesQuery
{
    public string timeFrame { get; set; } = string.Empty;      // EXACT casing from line 103!
    public string cutoff { get; set; } = string.Empty;         // EXACT casing from line 104!
    public string workspace { get; set; } = string.Empty;      // EXACT casing from line 105!
}