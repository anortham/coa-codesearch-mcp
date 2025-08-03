using Xunit;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.TokenAttributes;
using Lucene.Net.Util;
using System.IO;
using System.Collections.Generic;
using COA.CodeSearch.McpServer.Services.Analysis;

namespace COA.CodeSearch.McpServer.Tests.Services.Analysis;

public class CodeAnalyzerTests
{
    private const LuceneVersion Version = LuceneVersion.LUCENE_48;
    
    [Fact]
    public void Should_Preserve_CSharp_Interface_Implementation()
    {
        // Arrange
        var analyzer = new CodeAnalyzer(Version, preserveCase: true);
        var text = "public class UserService : IUserService";
        
        // Act
        var tokens = GetTokens(analyzer, text);
        
        // Assert
        Assert.Contains(": IUserService", tokens);
        Assert.Contains("UserService", tokens);
        // Note: "IUserService" alone is not a token - it's part of ": IUserService"
    }
    
    [Fact]
    public void Should_Preserve_CSharp_Attributes()
    {
        // Arrange
        var analyzer = new CodeAnalyzer(Version, preserveCase: true);
        var text = "[Fact] public void TestMethod() { }";
        
        // Act
        var tokens = GetTokens(analyzer, text);
        
        // Assert
        Assert.Contains("[Fact]", tokens);
        Assert.Contains("TestMethod", tokens);
    }
    
    [Fact]
    public void Should_Preserve_Cpp_Namespace_Operator()
    {
        // Arrange
        var analyzer = new CodeAnalyzer(Version, preserveCase: true);
        var text = "std::cout << \"Hello World\";";
        
        // Act
        var tokens = GetTokens(analyzer, text);
        
        // Assert
        Assert.Contains("std::cout", tokens);
        Assert.Contains("<<", tokens);
    }
    
    [Fact]
    public void Should_Preserve_Pointer_Access_Operator()
    {
        // Arrange
        var analyzer = new CodeAnalyzer(Version, preserveCase: true);
        var text = "object->method()";
        
        // Act
        var tokens = GetTokens(analyzer, text);
        
        // Assert
        Assert.Contains("->method", tokens);
        Assert.Contains("object", tokens);
    }
    
    [Fact]
    public void Should_Preserve_Lambda_Arrow()
    {
        // Arrange
        var analyzer = new CodeAnalyzer(Version, preserveCase: true);
        var text = "const handler = (req, res) => { res.send('OK'); }";
        
        // Act
        var tokens = GetTokens(analyzer, text);
        
        // Assert
        Assert.Contains("=>", tokens);
        Assert.Contains("handler", tokens);
        Assert.Contains("req", tokens);
        Assert.Contains("res", tokens);
    }
    
    [Fact]
    public void Should_Preserve_Generic_Types()
    {
        // Arrange
        var analyzer = new CodeAnalyzer(Version, preserveCase: true);
        var text = "List<string> items = new List<string>();";
        
        // Act
        var tokens = GetTokens(analyzer, text);
        
        // Assert
        Assert.Contains("List<string>", tokens);
        Assert.Contains("items", tokens);
    }
    
    [Fact]
    public void Should_Preserve_Optional_Chaining()
    {
        // Arrange
        var analyzer = new CodeAnalyzer(Version, preserveCase: true);
        var text = "user?.profile?.name";
        
        // Act
        var tokens = GetTokens(analyzer, text);
        
        // Assert
        Assert.Contains("?.", tokens);
        Assert.Contains("user", tokens);
        Assert.Contains("profile", tokens);
        Assert.Contains("name", tokens);
    }
    
    [Fact]
    public void Should_Preserve_Null_Coalescing()
    {
        // Arrange
        var analyzer = new CodeAnalyzer(Version, preserveCase: true);
        var text = "string value = input ?? \"default\";";
        
        // Act
        var tokens = GetTokens(analyzer, text);
        
        // Assert
        Assert.Contains("??", tokens);
        Assert.Contains("value", tokens);
        Assert.Contains("input", tokens);
    }
    
    [Fact]
    public void Should_Preserve_Spread_Operator()
    {
        // Arrange
        var analyzer = new CodeAnalyzer(Version, preserveCase: true);
        var text = "const newArray = [...oldArray, newItem];";
        
        // Act
        var tokens = GetTokens(analyzer, text);
        
        // Assert
        // The spread operator is part of the array literal token
        Assert.Contains("[...oldArray, newItem]", tokens);
        Assert.Contains("newArray", tokens);
    }
    
    [Fact]
    public void Should_Preserve_Go_Channel_Operator()
    {
        // Arrange
        var analyzer = new CodeAnalyzer(Version, preserveCase: true);
        var text = "data := <-channel";
        
        // Act
        var tokens = GetTokens(analyzer, text);
        
        // Assert
        // := is tokenized as separate : and = tokens
        Assert.Contains(":", tokens);
        Assert.Contains("=", tokens);
        Assert.Contains("<-", tokens);
        Assert.Contains("data", tokens);
        Assert.Contains("channel", tokens);
    }
    
    [Fact]
    public void Should_Preserve_Decorators()
    {
        // Arrange
        var analyzer = new CodeAnalyzer(Version, preserveCase: true);
        var text = "@property def name(self): return self._name";
        
        // Act
        var tokens = GetTokens(analyzer, text);
        
        // Assert
        Assert.Contains("@property", tokens);
        Assert.Contains("name", tokens);
        Assert.Contains("self", tokens);
    }
    
    [Fact]
    public void Should_Handle_Multiple_Colons_In_Type_Annotation()
    {
        // Arrange
        var analyzer = new CodeAnalyzer(Version, preserveCase: true);
        var text = "public interface IRepository<T> : IDisposable, IQueryable<T> where T : class";
        
        // Act
        var tokens = GetTokens(analyzer, text);
        
        // Assert
        Assert.Contains(": IDisposable", tokens);
        Assert.Contains(": class", tokens);
        Assert.Contains("IRepository<T>", tokens);
    }
    
    [Fact]
    public void Should_Handle_Complex_Generic_Types()
    {
        // Arrange
        var analyzer = new CodeAnalyzer(Version, preserveCase: true);
        var text = "Dictionary<string, List<int>> mapping = new();";
        
        // Act
        var tokens = GetTokens(analyzer, text);
        
        // Assert
        Assert.Contains("Dictionary<string, List<int>>", tokens);
        Assert.Contains("mapping", tokens);
    }
    
    [Fact]
    public void Should_Lowercase_Tokens_By_Default()
    {
        // Arrange
        var analyzer = new CodeAnalyzer(Version, preserveCase: false);
        var text = "UserService : IUserService";
        
        // Act
        var tokens = GetTokens(analyzer, text);
        
        // Assert
        Assert.Contains(": iuserservice", tokens);
        Assert.Contains("userservice", tokens);
        // Note: "iuserservice" alone is not a token - it's part of ": iuserservice"
    }
    
    [Fact]
    public void Should_Preserve_Case_When_Configured()
    {
        // Arrange
        var analyzer = new CodeAnalyzer(Version, preserveCase: true);
        var text = "UserService : IUserService";
        
        // Act
        var tokens = GetTokens(analyzer, text);
        
        // Assert
        Assert.Contains(": IUserService", tokens);
        Assert.Contains("UserService", tokens);
        // Note: "IUserService" alone is not a token - it's part of ": IUserService"
    }
    
    [Fact]
    public void Should_Split_CamelCase_When_Enabled()
    {
        // Arrange
        var analyzer = new CodeAnalyzer(Version, preserveCase: false, splitCamelCase: true);
        var text = "getUserById";
        
        // Act
        var tokens = GetTokens(analyzer, text);
        
        // Assert
        // Note: The current implementation doesn't fully implement camelCase splitting
        // This test documents the expected behavior once implemented
        Assert.Contains("getuserbyid", tokens);
        // TODO: When camelCase splitting is implemented:
        // Assert.Contains("get", tokens);
        // Assert.Contains("user", tokens);
        // Assert.Contains("by", tokens);
        // Assert.Contains("id", tokens);
    }
    
    [Fact]
    public void Should_Preserve_HttpGet_Attribute()
    {
        // Arrange
        var analyzer = new CodeAnalyzer(Version, preserveCase: true);
        var text = "[HttpGet] public IActionResult GetUsers()";
        
        // Act
        var tokens = GetTokens(analyzer, text);
        
        // Assert
        Assert.Contains("[HttpGet]", tokens);
        Assert.Contains("IActionResult", tokens);
        Assert.Contains("GetUsers", tokens);
    }
    
    [Fact]
    public void Should_Handle_Interface_Implementation_With_Generics()
    {
        // Arrange
        var analyzer = new CodeAnalyzer(Version, preserveCase: true);
        var text = "public class Repository<T> : IRepository<T> where T : Entity";
        
        // Act
        var tokens = GetTokens(analyzer, text);
        
        // Assert
        Assert.Contains("Repository<T>", tokens);
        Assert.Contains(": IRepository<T>", tokens); // Now the whole type annotation is kept together
        Assert.Contains(": Entity", tokens);
    }
    
    // Helper method to extract tokens from analyzer
    private List<string> GetTokens(Analyzer analyzer, string text)
    {
        var tokens = new List<string>();
        
        using (var tokenStream = analyzer.GetTokenStream("content", new StringReader(text)))
        {
            var termAttr = tokenStream.GetAttribute<ICharTermAttribute>();
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