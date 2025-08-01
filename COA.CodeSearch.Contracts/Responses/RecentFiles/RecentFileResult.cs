namespace COA.CodeSearch.Contracts.Responses.RecentFiles;

/// <summary>
/// Individual recent file result item for response JSON
/// CRITICAL: Property names match EXACTLY the anonymous type structure in RecentFilesResponseBuilder.cs line 135-144
/// </summary>
public class RecentFileResultItem
{
    public string file { get; set; } = string.Empty;          // EXACT casing from line 137!
    public string path { get; set; } = string.Empty;          // EXACT casing from line 138!
    public string modified { get; set; } = string.Empty;      // EXACT casing from line 139!
    public string modifiedAgo { get; set; } = string.Empty;   // EXACT casing from line 140!
    public long size { get; set; }                            // EXACT casing from line 141!
    public string sizeFormatted { get; set; } = string.Empty; // EXACT casing from line 142!
    public string extension { get; set; } = string.Empty;     // EXACT casing from line 143!
}