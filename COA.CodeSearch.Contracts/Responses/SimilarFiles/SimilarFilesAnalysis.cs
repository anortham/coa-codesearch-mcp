using System.Collections.Generic;

namespace COA.CodeSearch.Contracts.Responses.SimilarFiles;

public class SimilarFilesAnalysis
{
    public List<string> patterns { get; set; } = new();
    public List<string> topTerms { get; set; } = new();
    public List<DirectoryPattern> directoryPatterns { get; set; } = new();
    public Dictionary<string, int> extensionDistribution { get; set; } = new();
}