using System.Collections.Generic;

namespace COA.CodeSearch.Contracts.Responses.SimilarFiles;

public class SimilarFilesAction
{
    public string id { get; set; } = string.Empty;
    public string description { get; set; } = string.Empty;
    public string command { get; set; } = string.Empty;
    public Dictionary<string, object> parameters { get; set; } = new();
    public int estimatedTokens { get; set; }
    public string priority { get; set; } = string.Empty;
}