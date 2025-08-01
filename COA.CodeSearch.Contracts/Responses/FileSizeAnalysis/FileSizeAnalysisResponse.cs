using System.Collections.Generic;

namespace COA.CodeSearch.Contracts.Responses.FileSizeAnalysis;

public class FileSizeAnalysisResponse
{
    public bool success { get; set; }
    public string operation { get; set; } = string.Empty;
    public FileSizeQuery query { get; set; } = new();
    public FileSizeSummary summary { get; set; } = new();
    public FileSizeStatisticsInfo statistics { get; set; } = new();
    public FileSizeAnalysis analysis { get; set; } = new();
    public List<FileSizeResultItem> results { get; set; } = new();
    public FileSizeResultsSummary resultsSummary { get; set; } = new();
    public List<string> insights { get; set; } = new();
    public List<object> actions { get; set; } = new();
    public FileSizeMeta meta { get; set; } = new();
}