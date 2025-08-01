namespace COA.CodeSearch.Contracts.Responses.BatchOperations;

public class BatchGetResultSummary
{
    public string type { get; set; } = string.Empty;
    public int matches { get; set; }
    public string summary { get; set; } = string.Empty;
}