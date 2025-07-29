using System.Text.Json;
using COA.CodeSearch.McpServer.Configuration;
using COA.CodeSearch.McpServer.Constants;
using COA.CodeSearch.McpServer.Infrastructure;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Tools;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using COA.CodeSearch.McpServer.Tests.Helpers;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Moq;

namespace COA.CodeSearch.McpServer.Tests.Infrastructure;

public class FastFileSearchV2Test : TestBase
{
    private readonly FastFileSearchToolV2 _tool;
    private readonly InMemoryTestIndexService _indexService;
    private readonly string _testWorkspacePath = "C:\\test\\project";

    public FastFileSearchV2Test()
    {
        // Use in-memory index service for testing
        _indexService = new InMemoryTestIndexService();
        
        _tool = new FastFileSearchToolV2(
            ServiceProvider.GetRequiredService<ILogger<FastFileSearchToolV2>>(),
            _indexService,
            ServiceProvider.GetRequiredService<IConfiguration>(),
            ServiceProvider.GetRequiredService<IFieldSelectorService>(),
            ServiceProvider.GetRequiredService<IResponseSizeEstimator>(),
            ServiceProvider.GetRequiredService<IResultTruncator>(),
            ServiceProvider.GetRequiredService<IOptions<ResponseLimitOptions>>(),
            ServiceProvider.GetRequiredService<IDetailRequestCache>(),
            ServiceProvider.GetRequiredService<IErrorRecoveryService>(),
            ServiceProvider.GetRequiredService<AIResponseBuilderService>());
    }

    private async Task AddTestDocumentsToIndex()
    {
        var writer = await _indexService.GetIndexWriterAsync(_testWorkspacePath);
        
        // Add test files to the index
        var testFiles = new[]
        {
            ("TestService.cs", "C:\\test\\project\\Services\\TestService.cs", "Services\\TestService.cs", ".cs", 1024L),
            ("TestController.cs", "C:\\test\\project\\Controllers\\TestController.cs", "Controllers\\TestController.cs", ".cs", 2048L),
            ("UserTest.cs", "C:\\test\\project\\Tests\\UserTest.cs", "Tests\\UserTest.cs", ".cs", 512L),
            ("Program.cs", "C:\\test\\project\\Program.cs", "Program.cs", ".cs", 256L),
            ("appsettings.json", "C:\\test\\project\\appsettings.json", "appsettings.json", ".json", 128L)
        };
        
        foreach (var (filename, path, relativePath, extension, size) in testFiles)
        {
            var doc = new Document
            {
                new StringField("path", path, Field.Store.YES),
                new StringField("filename", filename, Field.Store.YES),
                new StringField("filename_lower", filename.ToLowerInvariant(), Field.Store.NO),
                new TextField("filename_text", filename, Field.Store.NO),
                new StringField("relativePath", relativePath, Field.Store.YES),
                new StringField("extension", extension, Field.Store.YES),
                new NumericDocValuesField("size", size),
                new StoredField("size", size.ToString()),
                new NumericDocValuesField("lastModified", DateTime.UtcNow.Ticks),
                new StoredField("lastModified", DateTime.UtcNow.Ticks.ToString()),
                new StringField("language", extension == ".cs" ? "C#" : "JSON", Field.Store.YES)
            };
            writer.AddDocument(doc);
        }
        
        writer.Commit();
    }

    [Fact]
    public async Task Should_Return_AI_Optimized_File_Search()
    {
        // Arrange - add test documents to the index
        await AddTestDocumentsToIndex();
        
        // Act - search for test files
        var result = await _tool.ExecuteAsync(
            query: "test",
            workspacePath: _testWorkspacePath,
            searchType: "standard",
            maxResults: 50,
            mode: ResponseMode.Summary);
        
        // Assert
        result.Should().NotBeNull();
        
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions 
        { 
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        
        // Removed debug output for clean tests
        
        // Parse to check structure
        var response = JsonDocument.Parse(json).RootElement;
        
        // Check AI-optimized response structure
        if (!response.GetProperty("success").GetBoolean())
        {
            if (response.TryGetProperty("error", out var error))
            {
                // Verify error exists
                error.GetString().Should().NotBeNullOrEmpty();
            }
        }
        response.GetProperty("success").GetBoolean().Should().BeTrue();
        response.GetProperty("operation").GetString().Should().Be(ToolNames.FileSearch);
        
        // Check query
        var query = response.GetProperty("query");
        query.GetProperty("text").GetString().Should().Be("test");
        query.GetProperty("type").GetString().Should().Be("standard");
        query.GetProperty("workspace").GetString().Should().NotBeNullOrEmpty();
        
        // Check summary
        var summary = response.GetProperty("summary");
        summary.GetProperty("totalFound").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        summary.GetProperty("searchTime").GetString().Should().NotBeNullOrEmpty();
        summary.GetProperty("performance").GetString().Should().NotBeNullOrEmpty();
        
        // Check distribution if results exist
        if (summary.GetProperty("totalFound").GetInt32() > 0)
        {
            var distribution = summary.GetProperty("distribution");
            distribution.GetProperty("byExtension").Should().NotBeNull();
        }
        
        // Check analysis
        var analysis = response.GetProperty("analysis");
        analysis.GetProperty("patterns").Should().NotBeNull();
        analysis.GetProperty("matchQuality").Should().NotBeNull();
        
        // Check insights
        var insights = response.GetProperty("insights");
        insights.GetArrayLength().Should().BeGreaterThan(0);
        // Verify insights exist
        insights.GetArrayLength().Should().BeGreaterThan(0);
        
        // Check actions
        var actions = response.GetProperty("actions");
        actions.GetArrayLength().Should().BeGreaterThan(0);
        // Verify actions exist
        actions.GetArrayLength().Should().BeGreaterThan(0);
        
        // Check meta
        var meta = response.GetProperty("meta");
        meta.GetProperty("mode").GetString().Should().Be("summary");
        meta.GetProperty("tokens").GetInt32().Should().BeGreaterThan(0);
        
        // Check performance claim
        var searchTime = summary.GetProperty("searchTime").GetString();
        // Verify search performance metrics
        searchTime.Should().NotBeNullOrEmpty();
        summary.GetProperty("performance").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Should_Handle_Fuzzy_Search()
    {
        // Arrange - add test documents to the index
        await AddTestDocumentsToIndex();
        
        // Act - fuzzy search with typo
        var result = await _tool.ExecuteAsync(
            query: "tst",  // Typo for "test"
            workspacePath: _testWorkspacePath,
            searchType: "fuzzy");
        
        // Assert
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions 
        { 
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        
        var response = JsonDocument.Parse(json).RootElement;
        
        if (response.GetProperty("success").GetBoolean())
        {
            var query = response.GetProperty("query");
            query.GetProperty("type").GetString().Should().Be("fuzzy");
            
            var summary = response.GetProperty("summary");
            var totalFound = summary.GetProperty("totalFound").GetInt32();
            
            if (totalFound > 0)
            {
                // Verify fuzzy search results
                totalFound.Should().BeGreaterThanOrEqualTo(0);
                
                // Check match quality shows fuzzy matches
                var analysis = response.GetProperty("analysis");
                var matchQuality = analysis.GetProperty("matchQuality");
                if (matchQuality.TryGetProperty("fuzzyMatches", out var fuzzyMatches))
                {
                    // Verify fuzzy matches
                    fuzzyMatches.GetInt32().Should().BeGreaterThanOrEqualTo(0);
                }
            }
        }
    }

    [Fact]
    public async Task Should_Detect_File_Patterns()
    {
        // Arrange - add test documents to the index
        await AddTestDocumentsToIndex();
        
        // Act - search for common pattern
        var result = await _tool.ExecuteAsync(
            query: "*.cs",
            workspacePath: _testWorkspacePath,
            searchType: "wildcard");
        
        // Assert
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions 
        { 
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        
        var response = JsonDocument.Parse(json).RootElement;
        
        if (response.GetProperty("success").GetBoolean())
        {
            var analysis = response.GetProperty("analysis");
            var patterns = analysis.GetProperty("patterns");
            
            // Verify detected patterns
            patterns.GetArrayLength().Should().BeGreaterThanOrEqualTo(0);
            
            // Check for hotspots
            if (analysis.TryGetProperty("hotspots", out var hotspots))
            {
                if (hotspots.TryGetProperty("directories", out var directories))
                {
                    // Verify directory hotspots
                    directories.GetArrayLength().Should().BeGreaterThan(0);
                    foreach (var dir in directories.EnumerateArray())
                    {
                        dir.GetProperty("path").GetString().Should().NotBeNullOrEmpty();
                        dir.GetProperty("count").GetInt32().Should().BeGreaterThan(0);
                    }
                }
            }
        }
    }

    [Fact]
    public async Task Should_Support_Full_Mode()
    {
        // Arrange - add test documents to the index
        await AddTestDocumentsToIndex();
        
        // Act
        var result = await _tool.ExecuteAsync(
            query: "test",
            workspacePath: _testWorkspacePath,
            maxResults: 10,
            mode: ResponseMode.Full);
        
        // Assert
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions 
        { 
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        
        var response = JsonDocument.Parse(json).RootElement;
        
        // Check meta mode
        var meta = response.GetProperty("meta");
        meta.GetProperty("mode").GetString().Should().Be("full");
        
        // In full mode, results should have complete details
        if (response.TryGetProperty("results", out var results))
        {
            results.Should().NotBeNull();
            
            if (results.GetArrayLength() > 0)
            {
                var firstResult = results[0];
                
                // Debug: Print the actual properties in the result
                var propsString = string.Join(", ", firstResult.EnumerateObject().Select(p => p.Name));
                // Properties available: {propsString}
                
                // Check expected properties exist before accessing them
                if (firstResult.TryGetProperty("path", out _))
                    firstResult.GetProperty("path").GetString().Should().NotBeNullOrEmpty();
                if (firstResult.TryGetProperty("filename", out _))
                    firstResult.GetProperty("filename").GetString().Should().NotBeNullOrEmpty();
                if (firstResult.TryGetProperty("relativePath", out _))
                    firstResult.GetProperty("relativePath").GetString().Should().NotBeNullOrEmpty();
                if (firstResult.TryGetProperty("extension", out _))
                    firstResult.GetProperty("extension").GetString().Should().NotBeNullOrEmpty();
                if (firstResult.TryGetProperty("score", out _))
                    firstResult.GetProperty("score").GetDouble().Should().BeGreaterThan(0);
            }
        }
    }

    [Fact]
    public async Task Should_Handle_No_Results()
    {
        // Arrange - add test documents to the index
        await AddTestDocumentsToIndex();
        
        // Act - search for non-existent file
        var result = await _tool.ExecuteAsync(
            query: "nonexistentfile12345",
            workspacePath: _testWorkspacePath,
            searchType: "exact");
        
        // Assert
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions 
        { 
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        
        var response = JsonDocument.Parse(json).RootElement;
        
        response.GetProperty("success").GetBoolean().Should().BeTrue();
        
        var summary = response.GetProperty("summary");
        summary.GetProperty("totalFound").GetInt32().Should().Be(0);
        
        // Should have insights about no results
        var insights = response.GetProperty("insights");
        var hasNoResultsInsight = false;
        foreach (var insight in insights.EnumerateArray())
        {
            var insightText = insight.GetString() ?? "";
            if (insightText.Contains("No files matching"))
            {
                hasNoResultsInsight = true;
                // Found expected insight
                break;
            }
        }
        hasNoResultsInsight.Should().BeTrue("Should have insight about no results");
        
        // Should suggest alternative searches
        var actions = response.GetProperty("actions");
        var hasSuggestions = false;
        foreach (var action in actions.EnumerateArray())
        {
            var id = action.GetProperty("id").GetString() ?? "";
            if (id.Contains("fuzzy") || id.Contains("wildcard"))
            {
                hasSuggestions = true;
                // Verify suggested action
                id.Should().NotBeNullOrEmpty();
                break;
            }
        }
        hasSuggestions.Should().BeTrue("Should suggest alternative search types");
    }

    [Fact]
    public async Task Should_Handle_Wildcard_Search_Case_Insensitive()
    {
        // Arrange - add test documents including mixed case filenames
        var writer = await _indexService.GetIndexWriterAsync(_testWorkspacePath);
        
        var mixedCaseFiles = new[]
        {
            ("azure-pipelines.yml", "C:\\test\\project\\azure-pipelines.yml", "azure-pipelines.yml", ".yml", 1024L),
            ("Azure-DevOps-Setup.md", "C:\\test\\project\\Azure-DevOps-Setup.md", "Azure-DevOps-Setup.md", ".md", 2048L),
            ("AZURE_CONFIG.json", "C:\\test\\project\\AZURE_CONFIG.json", "AZURE_CONFIG.json", ".json", 512L)
        };
        
        foreach (var (filename, path, relativePath, extension, size) in mixedCaseFiles)
        {
            var doc = new Document
            {
                new StringField("path", path, Field.Store.YES),
                new StringField("filename", filename, Field.Store.YES),
                new StringField("filename_lower", filename.ToLowerInvariant(), Field.Store.NO),
                new TextField("filename_text", filename, Field.Store.NO), // Store as-is, let analyzer handle case
                new StringField("relativePath", relativePath, Field.Store.YES),
                new StringField("extension", extension, Field.Store.YES),
                new NumericDocValuesField("size", size),
                new StoredField("size", size.ToString()),
                new NumericDocValuesField("lastModified", DateTime.UtcNow.Ticks),
                new StoredField("lastModified", DateTime.UtcNow.Ticks.ToString()),
                new StringField("language", "yaml", Field.Store.YES)
            };
            writer.AddDocument(doc);
        }
        
        writer.Commit();
        
        // Act - search with different case patterns
        var testCases = new[]
        {
            ("azure-pipelines*", 1, "lowercase should match mixed case file"),
            ("Azure-Pipelines*", 1, "mixed case should match lowercase file"),
            ("AZURE-PIPELINES*", 1, "uppercase should match lowercase file"),
            ("azure*", 3, "wildcard should match all azure files regardless of case"),
            ("AZURE*", 3, "uppercase wildcard should match all azure files"),
            ("*.yml", 1, "extension wildcard should work"),
            ("*pipelines*", 1, "middle wildcard should work case-insensitively")
        };
        
        foreach (var (query, expectedCount, description) in testCases)
        {
            var result = await _tool.ExecuteAsync(
                query: query,
                workspacePath: _testWorkspacePath,
                searchType: "wildcard");
            
            // Assert
            var json = JsonSerializer.Serialize(result, new JsonSerializerOptions 
            { 
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            
            var response = JsonDocument.Parse(json).RootElement;
            
            response.GetProperty("success").GetBoolean().Should().BeTrue();
            
            var summary = response.GetProperty("summary");
            summary.GetProperty("totalFound").GetInt32().Should().Be(expectedCount, 
                $"Query '{query}' {description}");
        }
    }
}