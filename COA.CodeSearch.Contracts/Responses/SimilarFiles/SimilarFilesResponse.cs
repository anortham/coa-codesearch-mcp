using System.Collections.Generic;

namespace COA.CodeSearch.Contracts.Responses.SimilarFiles;

public class SimilarFilesResponse
{
    public bool success { get; set; }
    public string operation { get; set; } = string.Empty;
    public SimilarFilesSource source { get; set; } = new();
    public SimilarFilesSummary summary { get; set; } = new();
    public SimilarFilesAnalysis analysis { get; set; } = new();
    public List<SimilarFilesResultItem> results { get; set; } = new();
    public SimilarFilesResultsSummary resultsSummary { get; set; } = new();
    public List<string> insights { get; set; } = new();
    public List<object> actions { get; set; } = new();
    public SimilarFilesMeta meta { get; set; } = new();
}