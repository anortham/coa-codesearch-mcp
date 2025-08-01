using System.Collections.Generic;

namespace COA.CodeSearch.Contracts.Responses.BatchOperations;

public class BatchResultAnalysis
{
    public int totalMatches { get; set; }
    public int highMatchOperations { get; set; }
    public int avgMatchesPerOperation { get; set; }
    public List<BatchCommonFile> commonFiles { get; set; } = new();
}