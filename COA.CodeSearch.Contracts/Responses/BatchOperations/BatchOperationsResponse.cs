using System.Collections.Generic;

namespace COA.CodeSearch.Contracts.Responses.BatchOperations;

public class BatchOperationsResponse
{
    public bool success { get; set; }
    public string operation { get; set; } = string.Empty;
    public BatchQuery query { get; set; } = new();
    public BatchSummary summary { get; set; } = new();
    public List<object> results { get; set; } = new();
    public BatchResultsSummary resultsSummary { get; set; } = new();
    public BatchDistribution distribution { get; set; } = new();
    public List<string> insights { get; set; } = new();
    public List<object> actions { get; set; } = new();
    public BatchMeta meta { get; set; } = new();
    public string resourceUri { get; set; } = string.Empty;
}