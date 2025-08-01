using System.Collections.Generic;

namespace COA.CodeSearch.Contracts.Responses.SimilarFiles;

public class SimilarFilesResultItem
{
    public string file { get; set; } = string.Empty;
    public string path { get; set; } = string.Empty;
    public double score { get; set; }
    public string similarity { get; set; } = string.Empty;
    public long size { get; set; }
    public string sizeFormatted { get; set; } = string.Empty;
    public int matchingTerms { get; set; }
}