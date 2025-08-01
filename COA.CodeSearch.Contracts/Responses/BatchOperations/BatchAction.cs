using System.Collections.Generic;

namespace COA.CodeSearch.Contracts.Responses.BatchOperations;

public class BatchAction
{
    public string id { get; set; } = string.Empty;
    public Dictionary<string, object> cmd { get; set; } = new();
    public int tokens { get; set; }
    public string priority { get; set; } = string.Empty;
}