using System.Collections.Generic;

namespace COA.CodeSearch.Contracts.Responses.BatchOperations;

public class BatchQuery
{
    public int operationCount { get; set; }
    public Dictionary<string, int> operationTypes { get; set; } = new();
    public string workspace { get; set; } = string.Empty;
}