namespace COA.CodeSearch.Contracts.Responses.RecentFiles;

/// <summary>
/// Summary information for recent files operation
/// CRITICAL: Property names match EXACTLY the anonymous type structure in RecentFilesResponseBuilder.cs line 107-119
/// </summary>
public class RecentFilesSummary
{
    public int totalFound { get; set; }                       // EXACT casing from line 109!
    public string searchTime { get; set; } = string.Empty;   // EXACT casing from line 110!
    public long totalSize { get; set; }                      // EXACT casing from line 111!
    public string totalSizeFormatted { get; set; } = string.Empty; // EXACT casing from line 112!
    public string avgFileSize { get; set; } = string.Empty;  // EXACT casing from line 113!
    public RecentFilesDistribution distribution { get; set; } = new(); // EXACT casing from line 114!
}