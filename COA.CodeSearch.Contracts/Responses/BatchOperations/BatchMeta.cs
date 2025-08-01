namespace COA.CodeSearch.Contracts.Responses.BatchOperations;

public class BatchMeta
{
    public string mode { get; set; } = string.Empty;
    public bool truncated { get; set; }
    public int tokens { get; set; }
    public string? detailRequestToken { get; set; }
    public BatchPerformance performance { get; set; } = new();
    public BatchAnalysis analysis { get; set; } = new();
}