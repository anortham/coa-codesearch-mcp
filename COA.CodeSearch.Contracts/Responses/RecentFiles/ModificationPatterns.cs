namespace COA.CodeSearch.Contracts.Responses.RecentFiles;

/// <summary>
/// Modification pattern analysis results
/// CRITICAL: Property names match EXACTLY the anonymous type structure in RecentFilesResponseBuilder.cs line 405-410
/// </summary>
public class ModificationPatterns
{
    public bool burstActivity { get; set; }     // EXACT casing from line 407!
    public bool workingHours { get; set; }      // EXACT casing from line 408!
    public int peakHour { get; set; }           // EXACT casing from line 409!
}