namespace COA.CodeSearch.Contracts.Responses.SimilarFiles;

public class DirectoryPattern
{
    public string directory { get; set; } = string.Empty;
    public int count { get; set; }
    public double avgScore { get; set; }
}