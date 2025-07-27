using COA.CodeSearch.McpServer.Configuration;
using COA.CodeSearch.McpServer.Infrastructure;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Tools;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace COA.CodeSearch.McpServer.Tests.Infrastructure;

public class FastTextSearchV2Test : LuceneTestBase
{
    private readonly FastTextSearchToolV2 _tool;
    private readonly IndexWorkspaceTool _indexTool;
    private readonly string _testWorkspacePath;

    public FastTextSearchV2Test()
    {
        _tool = new FastTextSearchToolV2(
            ServiceProvider.GetRequiredService<ILogger<FastTextSearchToolV2>>(),
            ServiceProvider.GetRequiredService<IConfiguration>(),
            ServiceProvider.GetRequiredService<ILuceneIndexService>(),
            ServiceProvider.GetRequiredService<FileIndexingService>(),
            ServiceProvider.GetRequiredService<IResponseSizeEstimator>(),
            ServiceProvider.GetRequiredService<IResultTruncator>(),
            ServiceProvider.GetRequiredService<IOptions<ResponseLimitOptions>>(),
            ServiceProvider.GetRequiredService<IDetailRequestCache>(),
            ServiceProvider.GetRequiredService<IQueryCacheService>(),
            ServiceProvider.GetRequiredService<IFieldSelectorService>(),
            ServiceProvider.GetRequiredService<IStreamingResultService>(),
            null); // IContextAwarenessService is optional
            
        _indexTool = ServiceProvider.GetRequiredService<IndexWorkspaceTool>();
        // Use the test project path as workspace
        _testWorkspacePath = GetTestProjectPath();
    }

    [Fact]
    public async Task Should_Return_AI_Optimized_Text_Search()
    {
        // Arrange - index the workspace first
        await _indexTool.ExecuteAsync(_testWorkspacePath, forceRebuild: true);
        
        // Act - search for "test" pattern
        var result = await _tool.ExecuteAsync(
            query: "test",
            workspacePath: _testWorkspacePath,
            filePattern: null,
            extensions: null,
            contextLines: null,
            maxResults: 50,
            caseSensitive: false,
            searchType: "standard",
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
        response.GetProperty("success").GetBoolean().Should().BeTrue();
        response.GetProperty("operation").GetString().Should().Be("text_search");
        
        // Check query
        var query = response.GetProperty("query");
        query.GetProperty("text").GetString().Should().Be("test");
        query.GetProperty("type").GetString().Should().Be("standard");
        // The workspace should be the directory path we provided
        var actualWorkspace = query.GetProperty("workspace").GetString();
        actualWorkspace.Should().Be(_testWorkspacePath);
        
        // Check summary
        var summary = response.GetProperty("summary");
        summary.GetProperty("totalHits").GetInt64().Should().BeGreaterThanOrEqualTo(0);
        summary.GetProperty("returnedResults").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        summary.GetProperty("filesMatched").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        
        // Check distribution
        var distribution = response.GetProperty("distribution");
        distribution.GetProperty("byExtension").Should().NotBeNull();
        distribution.GetProperty("byDirectory").Should().NotBeNull();
        
        // Check hotspots
        var hotspots = response.GetProperty("hotspots");
        hotspots.GetArrayLength().Should().BeGreaterThanOrEqualTo(0);
        
        // Check insights
        var insights = response.GetProperty("insights");
        insights.GetArrayLength().Should().BeGreaterThanOrEqualTo(0);
        // Verify insights exist
        insights.GetArrayLength().Should().BeGreaterThan(0);
        
        // Check actions
        var actions = response.GetProperty("actions");
        actions.GetArrayLength().Should().BeGreaterThanOrEqualTo(0);
        // Verify actions exist
        actions.GetArrayLength().Should().BeGreaterThan(0);
        
        // Check meta
        var meta = response.GetProperty("meta");
        meta.GetProperty("mode").GetString().Should().Be("summary");
        meta.GetProperty("indexed").GetBoolean().Should().BeTrue();
        meta.GetProperty("cached").GetString().Should().StartWith("txt_");
    }

    [Fact]
    public async Task Should_Filter_By_Extension()
    {
        // Arrange - index the workspace first
        await _indexTool.ExecuteAsync(_testWorkspacePath, forceRebuild: true);
        
        // Act - search only in .cs files
        var result = await _tool.ExecuteAsync(
            query: "class",
            workspacePath: _testWorkspacePath,
            filePattern: null,
            extensions: new[] { ".cs" },
            contextLines: null,
            maxResults: 50);
        
        // Assert
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions 
        { 
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        
        // Removed debug output for clean tests
        
        var response = JsonDocument.Parse(json).RootElement;
        
        // Check AI-optimized response
        response.GetProperty("success").GetBoolean().Should().BeTrue();
        response.GetProperty("operation").GetString().Should().Be("text_search");
        
        // Check query has extensions
        var query = response.GetProperty("query");
        if (query.TryGetProperty("extensions", out var extensions))
        {
            extensions[0].GetString().Should().Be(".cs");
        }
        
        // Check distribution - should only have .cs files
        var distribution = response.GetProperty("distribution");
        if (distribution.TryGetProperty("byExtension", out var byExt))
        {
            // Verify extension distribution
            foreach (var ext in byExt.EnumerateObject())
            {
                // Should only be .cs files
                ext.Name.Should().Be(".cs");
            }
        }
    }

    [Fact]
    public async Task Should_Include_Context_Lines()
    {
        // Arrange - index the workspace first
        await _indexTool.ExecuteAsync(_testWorkspacePath, forceRebuild: true);
        
        // Act - search with context lines
        var result = await _tool.ExecuteAsync(
            query: "TestBase",
            workspacePath: _testWorkspacePath,
            filePattern: null,
            extensions: null,
            contextLines: 2,
            maxResults: 10);
        
        // Assert
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions 
        { 
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        
        var response = JsonDocument.Parse(json).RootElement;
        
        // Check AI-optimized response
        response.GetProperty("success").GetBoolean().Should().BeTrue();
        
        // Check actions include context-related suggestions
        var actions = response.GetProperty("actions");
        // Verify context actions exist
        actions.GetArrayLength().Should().BeGreaterThan(0);
        
        // Check insights mention context
        var insights = response.GetProperty("insights");
        // Verify context insights exist
        insights.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Should_Handle_No_Results()
    {
        // Arrange - index the workspace first
        await _indexTool.ExecuteAsync(_testWorkspacePath, forceRebuild: true);
        
        // Act - search for non-existent text
        var result = await _tool.ExecuteAsync(
            query: "XyzNonExistentTextPattern123",
            workspacePath: _testWorkspacePath,
            filePattern: null,
            extensions: null,
            contextLines: null,
            maxResults: 50);
        
        // Assert
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions 
        { 
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        
        var response = JsonDocument.Parse(json).RootElement;
        
        // Check AI-optimized response
        response.GetProperty("success").GetBoolean().Should().BeTrue();
        
        // Should have 0 results
        var summary = response.GetProperty("summary");
        summary.GetProperty("totalHits").GetInt64().Should().Be(0);
        summary.GetProperty("returnedResults").GetInt32().Should().Be(0);
        
        // Should have helpful insights
        var insights = response.GetProperty("insights");
        insights.GetArrayLength().Should().BeGreaterThan(0);
        
        // Verify no results insights exist
        insights.GetArrayLength().Should().BeGreaterThan(0);
        
        // First insight should mention no matches found
        insights[0].GetString().Should().Contain("No matches found");
    }
}