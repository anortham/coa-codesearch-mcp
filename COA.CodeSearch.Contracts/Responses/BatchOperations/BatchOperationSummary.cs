namespace COA.CodeSearch.Contracts.Responses.BatchOperations;

public class BatchOperationSummary
{
    public int index { get; set; }
    public string operation { get; set; } = string.Empty;
    public object parameters { get; set; } = new();
    public bool success { get; set; }
    public string timing { get; set; } = string.Empty;
}