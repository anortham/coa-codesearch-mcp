using System.Collections.Generic;

namespace COA.CodeSearch.Contracts.Responses.FileSizeAnalysis;

public class FileSizeAnalysis
{
    public List<string> patterns { get; set; } = new();
    public List<object> outliers { get; set; } = new();
    public FileSizeHotspots hotspots { get; set; } = new();
}