namespace COA.CodeSearch.Contracts.Responses.RecentFiles;

/// <summary>
/// Results pagination summary
/// CRITICAL: Property names match EXACTLY the anonymous type structure in RecentFilesResponseBuilder.cs line 145-150
/// </summary>
public class ResultsSummary
{
    public int included { get; set; }       // EXACT casing from line 147!
    public int total { get; set; }          // EXACT casing from line 148!
    public bool hasMore { get; set; }       // EXACT casing from line 149!
}