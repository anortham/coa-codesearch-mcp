using System.Collections.Generic;

namespace COA.CodeSearch.Contracts.Responses.FileSizeAnalysis;

public class FileSizeHotspots
{
    public List<HotspotDirectory> byDirectory { get; set; } = new();
    public List<HotspotExtension> byExtension { get; set; } = new();
}