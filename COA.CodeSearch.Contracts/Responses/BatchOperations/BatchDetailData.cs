using System.Collections.Generic;

namespace COA.CodeSearch.Contracts.Responses.BatchOperations;

public class BatchDetailData
{
    public List<object> operations { get; set; } = new();
    public List<object> results { get; set; } = new();
    public List<object> operationSummaries { get; set; } = new();
    public object resultAnalysis { get; set; } = new();
}