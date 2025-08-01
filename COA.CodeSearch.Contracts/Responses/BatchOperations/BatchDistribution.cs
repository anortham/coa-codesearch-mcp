using System.Collections.Generic;

namespace COA.CodeSearch.Contracts.Responses.BatchOperations;

public class BatchDistribution
{
    public Dictionary<string, int> byOperation { get; set; } = new();
    public List<BatchCommonFile> commonFiles { get; set; } = new();
}