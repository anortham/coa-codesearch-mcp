namespace COA.CodeSearch.Contracts.Responses.RecentFiles;

/// <summary>
/// Time-based file distribution buckets
/// CRITICAL: Property names match EXACTLY the anonymous type structure in RecentFilesResponseBuilder.cs line 42-48
/// </summary>
public class TimeBuckets
{
    public int lastHour { get; set; }        // EXACT casing from line 44!
    public int last24Hours { get; set; }     // EXACT casing from line 45!
    public int lastWeek { get; set; }        // EXACT casing from line 46!
    public int older { get; set; }           // EXACT casing from line 47!
}