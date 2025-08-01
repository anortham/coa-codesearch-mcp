namespace COA.CodeSearch.Contracts.Responses.RecentFiles;

/// <summary>
/// Action result item for AI agent operations
/// CRITICAL: Property names match EXACTLY the anonymous type structure in RecentFilesResponseBuilder.cs line 152-161
/// </summary>
public class ActionResult
{
    public string id { get; set; } = string.Empty;                           // EXACT casing from line 154!
    public string description { get; set; } = string.Empty;                  // EXACT casing from line 155!
    public string command { get; set; } = string.Empty;                      // EXACT casing from line 156!
    public Dictionary<string, object> parameters { get; set; } = new();      // EXACT casing from line 157!
    public int estimatedTokens { get; set; }                                 // EXACT casing from line 158!
    public string priority { get; set; } = string.Empty;                     // EXACT casing from line 159!
}