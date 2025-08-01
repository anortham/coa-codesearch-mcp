namespace COA.CodeSearch.Contracts.Responses.SimilarFiles;

public class SimilarFilesResultsSummary
{
    public int included { get; set; }
    public int total { get; set; }
    public bool hasMore { get; set; }
}