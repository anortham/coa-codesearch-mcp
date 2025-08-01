namespace COA.CodeSearch.Contracts.Responses.BatchOperations;

public class BatchResultEntry
{
    public int index { get; set; }
    public string operation { get; set; } = string.Empty;
    public object? query { get; set; }
    public int matches { get; set; }
    public object summary { get; set; } = new();
    public bool success { get; set; }
    public string? error { get; set; }
    public object? result { get; set; }
}