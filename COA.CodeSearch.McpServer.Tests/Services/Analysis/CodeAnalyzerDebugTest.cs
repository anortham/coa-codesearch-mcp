using Xunit;
using Xunit.Abstractions;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.TokenAttributes;
using Lucene.Net.Util;
using System.IO;
using System.Collections.Generic;
using COA.CodeSearch.McpServer.Services.Analysis;

namespace COA.CodeSearch.McpServer.Tests.Services.Analysis;

public class CodeAnalyzerDebugTest
{
    private readonly ITestOutputHelper _output;
    private const LuceneVersion Version = LuceneVersion.LUCENE_48;
    
    public CodeAnalyzerDebugTest(ITestOutputHelper output)
    {
        _output = output;
    }
    
    [Fact]
    public void Debug_Tokenizer_Output()
    {
        // Test cases
        var testCases = new[]
        {
            "public class UserService : IUserService",
            "[Fact] public void Test()",
            "std::cout << \"Hello\"",
            "object->method()",
            "handler = (req, res) => { }",
            "List<string> items",
            "user?.profile?.name",
            "value = input ?? \"default\"",
            "data := <-channel",
            "@property def name(self):",
        };
        
        var analyzer = new CodeAnalyzer(Version);
        
        foreach (var testCase in testCases)
        {
            _output.WriteLine($"\nInput: {testCase}");
            _output.WriteLine("Tokens:");
            
            var tokens = GetTokensWithDetails(analyzer, testCase);
            foreach (var token in tokens)
            {
                _output.WriteLine($"  '{token.Term}' [{token.Type}] at {token.StartOffset}-{token.EndOffset}");
            }
        }
    }
    
    private List<TokenInfo> GetTokensWithDetails(Analyzer analyzer, string text)
    {
        var tokens = new List<TokenInfo>();
        
        using (var tokenStream = analyzer.GetTokenStream("content", new StringReader(text)))
        {
            var termAttr = tokenStream.GetAttribute<ICharTermAttribute>();
            var offsetAttr = tokenStream.GetAttribute<IOffsetAttribute>();
            var typeAttr = tokenStream.GetAttribute<ITypeAttribute>();
            
            tokenStream.Reset();
            
            while (tokenStream.IncrementToken())
            {
                tokens.Add(new TokenInfo
                {
                    Term = termAttr.ToString(),
                    Type = typeAttr.Type,
                    StartOffset = offsetAttr.StartOffset,
                    EndOffset = offsetAttr.EndOffset
                });
            }
            
            tokenStream.End();
        }
        
        return tokens;
    }
    
    private class TokenInfo
    {
        public string Term { get; set; } = "";
        public string Type { get; set; } = "";
        public int StartOffset { get; set; }
        public int EndOffset { get; set; }
    }
}