namespace COA.CodeSearch.Contracts.Responses.RecentFiles;

/// <summary>
/// Metadata information for response
/// CRITICAL: Property names match EXACTLY the anonymous type structure in RecentFilesResponseBuilder.cs line 161-168
/// </summary>
public class ResponseMeta
{
    public string mode { get; set; } = string.Empty;          // EXACT casing from line 163!
    public bool truncated { get; set; }                       // EXACT casing from line 164!
    public int tokens { get; set; }                           // EXACT casing from line 165!
    public string format { get; set; } = string.Empty;        // EXACT casing from line 166!
    public bool indexed { get; set; }                         // EXACT casing from line 167!
}