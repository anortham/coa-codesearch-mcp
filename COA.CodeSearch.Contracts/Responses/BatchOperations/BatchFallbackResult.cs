namespace COA.CodeSearch.Contracts.Responses.BatchOperations;

public class BatchFallbackResult
{
    public int index { get; set; }
    public string operation { get; set; } = string.Empty;
    public object? query { get; set; }
    public int matches { get; set; }
    public object summary { get; set; } = new();
    public object? result { get; set; }
}