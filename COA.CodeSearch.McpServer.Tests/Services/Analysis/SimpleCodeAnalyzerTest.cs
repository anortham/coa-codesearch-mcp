using Xunit;
using Xunit.Abstractions;
using COA.CodeSearch.McpServer.Services.Analysis;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.TokenAttributes;
using Lucene.Net.Util;
using System.IO;

namespace COA.CodeSearch.McpServer.Tests.Services.Analysis;

public class SimpleCodeAnalyzerTest
{
    private readonly ITestOutputHelper _output;
    
    public SimpleCodeAnalyzerTest(ITestOutputHelper output)
    {
        _output = output;
    }
    
    [Fact]
    public void Test_Simple_Tokenization()
    {
        var testCases = new[]
        {
            ": ITool",
            "[Fact]",
            "std::cout",
            "->method",
            "List<string>",
            "<<",
            "...",
            "?.",
            "??"
        };
        
        var analyzer = new CodeAnalyzer(LuceneVersion.LUCENE_48);
        
        foreach (var text in testCases)
        {
            using (var tokenStream = analyzer.GetTokenStream("content", new StringReader(text)))
            {
                var termAttr = tokenStream.GetAttribute<ICharTermAttribute>();
                tokenStream.Reset();
                
                _output.WriteLine($"Input: '{text}'");
                _output.WriteLine("Tokens:");
                
                while (tokenStream.IncrementToken())
                {
                    _output.WriteLine($"  '{termAttr}'");
                }
                
                _output.WriteLine("");
                tokenStream.End();
            }
        }
    }
}