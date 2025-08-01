using System.Collections.Generic;

namespace COA.CodeSearch.Contracts.Responses.BatchOperations;

public class BatchAnalysis
{
    public string effectiveness { get; set; } = string.Empty;
    public int highMatchOperations { get; set; }
    public int avgMatchesPerOperation { get; set; }
}