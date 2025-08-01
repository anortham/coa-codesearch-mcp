namespace COA.CodeSearch.Contracts.Responses.RecentFiles;

/// <summary>
/// Directory hotspot information
/// CRITICAL: Property names match EXACTLY the anonymous type structure in RecentFilesResponseBuilder.cs line 125-132
/// </summary>
public class HotspotDirectory
{
    public string path { get; set; } = string.Empty;         // EXACT casing from line 127!
    public int files { get; set; }                           // EXACT casing from line 128!
    public string size { get; set; } = string.Empty;         // EXACT casing from line 129!
    public string lastModified { get; set; } = string.Empty; // EXACT casing from line 130!
}

/// <summary>
/// Hotspot analysis container
/// CRITICAL: Property names match EXACTLY the anonymous type structure in RecentFilesResponseBuilder.cs line 123-132
/// </summary>
public class RecentFilesHotspots
{
    public List<HotspotDirectory> directories { get; set; } = new(); // EXACT casing from line 125!
}