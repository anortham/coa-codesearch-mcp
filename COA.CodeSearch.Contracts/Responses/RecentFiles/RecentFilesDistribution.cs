namespace COA.CodeSearch.Contracts.Responses.RecentFiles;

/// <summary>
/// Distribution analysis for recent files
/// CRITICAL: Property names match EXACTLY the anonymous type structure in RecentFilesResponseBuilder.cs line 114-118
/// </summary>
public class RecentFilesDistribution
{
    public TimeBuckets byTime { get; set; } = new();          // EXACT casing from line 116!
    public Dictionary<string, int> byExtension { get; set; } = new();  // EXACT casing from line 117!
}