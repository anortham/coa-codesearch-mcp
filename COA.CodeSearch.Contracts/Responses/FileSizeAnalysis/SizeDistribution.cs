namespace COA.CodeSearch.Contracts.Responses.FileSizeAnalysis;

public class SizeDistribution
{
    public int tiny { get; set; }
    public int small { get; set; }
    public int medium { get; set; }
    public int large { get; set; }
    public int huge { get; set; }
}