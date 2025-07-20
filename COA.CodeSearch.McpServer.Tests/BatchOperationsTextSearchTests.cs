using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;
using COA.CodeSearch.McpServer.Tools;
using COA.CodeSearch.McpServer.Services;
using System.Linq;

namespace COA.CodeSearch.McpServer.Tests;

public class BatchOperationsTextSearchTests : TestBase
{
    private readonly ITestOutputHelper _output;
    private readonly string _tempDirectory;

    public BatchOperationsTextSearchTests(ITestOutputHelper output)
    {
        _output = output;
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"batch_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public async Task BatchOperations_WithTextSearch_PerformsCombinedOperations()
    {
        // Arrange
        var services = CreateServiceCollection();
        var provider = services.BuildServiceProvider();
        var tool = provider.GetRequiredService<BatchOperationsTool>();

        var operations = new
        {
            workspacePath = GetTestProjectPath(),
            operations = new object[]
            {
                new
                {
                    type = "text_search",
                    query = "TestMethod",
                    workspacePath = GetTestProjectPath(),
                    maxResults = 10
                },
                new
                {
                    type = "search_symbols",
                    searchPattern = "Test*",
                    workspacePath = GetTestProjectPath(),
                    searchType = "wildcard",
                    maxResults = 5
                }
            }
        };

        var json = JsonSerializer.Serialize(operations);
        var jsonElement = JsonDocument.Parse(json).RootElement;

        // Act
        var result = await tool.ExecuteAsync(jsonElement.GetProperty("operations"), GetTestProjectPath());

        // Assert
        Assert.NotNull(result);
        
        var resultJson = JsonSerializer.Serialize(result);
        _output.WriteLine($"Result: {resultJson}");

        // Parse the result as JSON to check the structure
        var jsonDoc = JsonDocument.Parse(resultJson);
        var root = jsonDoc.RootElement;
        
        Assert.True(root.TryGetProperty("success", out var successProp) && successProp.GetBoolean(), 
            "Batch operation should succeed");
        Assert.True(root.TryGetProperty("totalOperations", out var totalOpsProp) && totalOpsProp.GetInt32() == 2,
            "Should have 2 operations");
        
        // Check results array
        Assert.True(root.TryGetProperty("results", out var resultsProp) && resultsProp.GetArrayLength() == 2,
            "Should have 2 results");
        
        var resultsArray = resultsProp.EnumerateArray().ToList();
        
        // Check text search result
        var textSearchResult = resultsArray[0];
        Assert.Equal("text_search", textSearchResult.GetProperty("type").GetString());
        Assert.True(textSearchResult.GetProperty("success").GetBoolean());

        // Check symbol search result  
        var symbolSearchResult = resultsArray[1];
        Assert.Equal("search_symbols", symbolSearchResult.GetProperty("type").GetString());
        Assert.True(symbolSearchResult.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task BatchOperations_WithAllTextSearchVariants_AcceptsAllFormats()
    {
        // Test that all three text search operation type names work
        var services = CreateServiceCollection();
        var provider = services.BuildServiceProvider();
        var tool = provider.GetRequiredService<BatchOperationsTool>();

        var operationTypes = new[] { "text_search", "fast_text_search", "textSearch" };
        
        foreach (var opType in operationTypes)
        {
            var operations = new
            {
                workspacePath = GetTestProjectPath(),
                operations = new object[]
                {
                    new
                    {
                        type = opType,
                        query = "public",
                        workspacePath = GetTestProjectPath(),
                        maxResults = 5
                    }
                }
            };

            var json = JsonSerializer.Serialize(operations);
            var jsonElement = JsonDocument.Parse(json).RootElement;

            // Act
            var result = await tool.ExecuteAsync(jsonElement.GetProperty("operations"), GetTestProjectPath());

            // Assert
            Assert.NotNull(result);
            
            // The result should be a successful batch operation
            var resultJson = JsonSerializer.Serialize(result);
            _output.WriteLine($"Result for operation type '{opType}': {resultJson}");
            
            // Parse the result to check success
            var jsonDoc = JsonDocument.Parse(resultJson);
            var root = jsonDoc.RootElement;
            
            Assert.True(root.TryGetProperty("success", out var successProp) && successProp.GetBoolean(), 
                $"Batch operation failed for operation type: {opType}");
            
            _output.WriteLine($"Operation type '{opType}' succeeded");
        }
    }

    private static new string GetTestProjectPath()
    {
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestProjects", "TestProject1");
    }

    private IServiceCollection CreateServiceCollection()
    {
        var services = new ServiceCollection();
        
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .Build();

        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(builder => builder.AddConsole());
        
        // Register all required services
        services.AddSingleton<IPathResolutionService, PathResolutionService>();
        services.AddSingleton<LuceneIndexService>();
        services.AddSingleton<ILuceneWriterManager>(provider => provider.GetRequiredService<LuceneIndexService>());
        services.AddSingleton<ILuceneIndexService>(provider => provider.GetRequiredService<LuceneIndexService>());
        services.AddSingleton<FileIndexingService>();
        services.AddScoped<CodeAnalysisService>();
        
        // Register TypeScript services
        services.AddScoped<TypeScriptInstaller>();
        services.AddScoped<TypeScriptAnalysisService>();
        services.AddScoped<TypeScriptHoverInfoTool>();
        
        // Register all tools
        services.AddScoped<FastTextSearchTool>();
        services.AddScoped<GoToDefinitionTool>();
        services.AddScoped<FindReferencesTool>();
        services.AddScoped<SearchSymbolsTool>();
        services.AddScoped<GetDiagnosticsTool>();
        services.AddScoped<GetHoverInfoTool>();
        services.AddScoped<GetImplementationsTool>();
        services.AddScoped<GetDocumentSymbolsTool>();
        services.AddScoped<GetCallHierarchyTool>();
        services.AddScoped<DependencyAnalysisTool>();
        services.AddScoped<BatchOperationsTool>();

        return services;
    }
    
    #pragma warning disable xUnit1013 // Public method should be marked as test
    public new void Dispose()
    #pragma warning restore xUnit1013
    {
        // Cleanup temp directory first
        if (!string.IsNullOrEmpty(_tempDirectory) && Directory.Exists(_tempDirectory))
        {
            try
            {
                Directory.Delete(_tempDirectory, true);
            }
            catch { }
        }
        
        // Then call base dispose
        base.Dispose();
    }
}