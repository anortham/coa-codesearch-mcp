using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;
using COA.CodeSearch.McpServer.Tools;
using COA.CodeSearch.McpServer.Services;

namespace COA.CodeSearch.McpServer.Tests;

public class BatchOperationsTextSearchTests : TestBase
{
    private readonly ITestOutputHelper _output;

    public BatchOperationsTextSearchTests(ITestOutputHelper output)
    {
        _output = output;
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
                    pattern = "Test*",
                    workspacePath = GetTestProjectPath(),
                    searchType = "wildcard",
                    maxResults = 5
                }
            }
        };

        var json = JsonSerializer.Serialize(operations);
        var jsonElement = JsonDocument.Parse(json).RootElement;

        // Act
        var result = await tool.ExecuteAsync(jsonElement.GetProperty("operations"));

        // Assert
        Assert.NotNull(result);
        
        var resultJson = JsonSerializer.Serialize(result);
        _output.WriteLine($"Result: {resultJson}");

        dynamic dynamicResult = result;
        Assert.True(dynamicResult.success);
        Assert.Equal(2, dynamicResult.totalOperations);
        
        var results = dynamicResult.results as List<object>;
        Assert.NotNull(results);
        Assert.Equal(2, results.Count);

        // Check text search result
        dynamic textSearchResult = results[0];
        Assert.Equal("text_search", textSearchResult.type);
        Assert.True(textSearchResult.success);
        Assert.NotNull(textSearchResult.result);

        // Check symbol search result  
        dynamic symbolSearchResult = results[1];
        Assert.Equal("search_symbols", symbolSearchResult.type);
        Assert.True(symbolSearchResult.success);
        Assert.NotNull(symbolSearchResult.result);
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
            var result = await tool.ExecuteAsync(jsonElement.GetProperty("operations"));

            // Assert
            Assert.NotNull(result);
            dynamic dynamicResult = result;
            Assert.True(dynamicResult.success, $"Failed for operation type: {opType}");
            
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
        services.AddSingleton<LuceneIndexService>();
        services.AddSingleton<FileIndexingService>();
        services.AddScoped<CodeAnalysisService>();
        
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
        services.AddScoped<BatchOperationsTool>();

        return services;
    }
}