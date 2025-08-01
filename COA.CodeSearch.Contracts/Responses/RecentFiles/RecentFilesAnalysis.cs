namespace COA.CodeSearch.Contracts.Responses.RecentFiles;

/// <summary>
/// Analysis information for recent files operation
/// CRITICAL: Property names match EXACTLY the anonymous type structure in RecentFilesResponseBuilder.cs line 120-134
/// </summary>
public class RecentFilesAnalysis
{
    public List<string> patterns { get; set; } = new();             // EXACT casing from line 122!
    public RecentFilesHotspots hotspots { get; set; } = new();      // EXACT casing from line 123!
    public ModificationPatterns activityPattern { get; set; } = new(); // EXACT casing from line 133!
}