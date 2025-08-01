namespace COA.CodeSearch.Contracts.Responses.BatchOperations;

public class BatchSummary
{
    public int totalOperations { get; set; }
    public int completedOperations { get; set; }
    public int totalMatches { get; set; }
    public string totalTime { get; set; } = string.Empty;
    public string avgTimePerOperation { get; set; } = string.Empty;
}