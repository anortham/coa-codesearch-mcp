namespace COA.CodeSearch.Contracts.Responses.RecentFiles;

/// <summary>
/// Directory grouping information for recent files
/// CRITICAL: Property names match EXACTLY the anonymous type structure in RecentFilesResponseBuilder.cs line 55-61
/// </summary>
public class DirectoryGroup
{
    public string directory { get; set; } = string.Empty;     // EXACT casing from line 57!
    public int fileCount { get; set; }                        // EXACT casing from line 58!
    public long totalSize { get; set; }                       // EXACT casing from line 59!
    public DateTime mostRecent { get; set; }                  // EXACT casing from line 60!
}