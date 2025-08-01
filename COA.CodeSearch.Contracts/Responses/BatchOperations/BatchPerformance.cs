using System.Collections.Generic;

namespace COA.CodeSearch.Contracts.Responses.BatchOperations;

public class BatchPerformance
{
    public bool parallel { get; set; }
    public string speedup { get; set; } = string.Empty;
    public List<BatchSlowestOperation> slowestOperations { get; set; } = new();
}