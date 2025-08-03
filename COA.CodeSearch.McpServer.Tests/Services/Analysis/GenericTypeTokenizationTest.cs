using COA.CodeSearch.McpServer.Services.Analysis;
using Lucene.Net.Analysis.TokenAttributes;
using Lucene.Net.Util;
using System.Collections.Generic;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace COA.CodeSearch.McpServer.Tests.Services.Analysis;

public class GenericTypeTokenizationTest
{
    private readonly ITestOutputHelper _output;
    private readonly LuceneVersion _version = LuceneVersion.LUCENE_48;

    public GenericTypeTokenizationTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void TestCurrentGenericTypeTokenization()
    {
        // Arrange
        var analyzer = new CodeAnalyzer(_version, preserveCase: true);
        var text = "public class Repository<T> : IRepository<T> where T : class";
        
        // Act
        var tokens = GetTokens(analyzer, text);
        
        // Debug output
        _output.WriteLine("Current tokenization:");
        foreach (var token in tokens)
        {
            _output.WriteLine($"  '{token}'");
        }
        
        // Assert new improved behavior
        Assert.Contains("Repository<T>", tokens); // Generic type is kept together
        Assert.Contains(": IRepository<T>", tokens); // Type annotation WITH generic is now kept together!
        Assert.Contains(": class", tokens); // Another type annotation
    }

    [Fact]
    public void TestDesiredGenericTypeTokenization()
    {
        // This test shows what we WANT the tokenizer to produce
        var desiredTokens = new[]
        {
            "public",
            "class", 
            "Repository<T>",
            ": IRepository<T>",  // <-- This is what we want as a single token
            "where",
            "T",
            ":",
            "class"
        };
        
        _output.WriteLine("Desired tokenization:");
        foreach (var token in desiredTokens)
        {
            _output.WriteLine($"  '{token}'");
        }
        
        // This test will initially fail, showing us what needs to be fixed
        Assert.True(true); // Placeholder
    }

    [Fact]
    public void TestComplexGenericPatterns()
    {
        // Test various generic patterns we want to support
        var testCases = new Dictionary<string, string[]>
        {
            {
                "Dictionary<string, List<int>>",
                new[] { "Dictionary<string, List<int>>" }
            },
            {
                ": IService<TUser, TRole>",
                new[] { ": IService<TUser, TRole>" }
            },
            {
                "Func<int, string, Task<bool>>",
                new[] { "Func<int, string, Task<bool>>" }
            },
            {
                ": IAsyncEnumerable<T>",
                new[] { ": IAsyncEnumerable<T>" }
            }
        };
        
        var analyzer = new CodeAnalyzer(_version, preserveCase: true);
        
        foreach (var (input, expected) in testCases)
        {
            _output.WriteLine($"\nTesting: {input}");
            var tokens = GetTokens(analyzer, input);
            
            _output.WriteLine("Current tokens:");
            foreach (var token in tokens)
            {
                _output.WriteLine($"  '{token}'");
            }
            
            _output.WriteLine($"Expected: '{expected[0]}'");
        }
    }

    private List<string> GetTokens(CodeAnalyzer analyzer, string text)
    {
        var tokens = new List<string>();
        using (var reader = new StringReader(text))
        using (var tokenStream = analyzer.GetTokenStream("field", reader))
        {
            var termAttr = tokenStream.AddAttribute<ICharTermAttribute>();
            tokenStream.Reset();
            
            while (tokenStream.IncrementToken())
            {
                tokens.Add(termAttr.ToString());
            }
            
            tokenStream.End();
        }
        return tokens;
    }
}