namespace COA.CodeSearch.Contracts.Responses.BatchOperations;

public class BatchResultsSummary
{
    public int included { get; set; }
    public int total { get; set; }
    public bool hasMore { get; set; }
}