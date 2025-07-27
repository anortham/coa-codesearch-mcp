using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using COA.CodeSearch.McpServer.Configuration;
using COA.CodeSearch.McpServer.Infrastructure;
using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Tools;
using COA.CodeSearch.McpServer.Tools.Registration;
using System.Runtime.CompilerServices;

namespace COA.CodeSearch.McpServer.Tests;

public abstract class TestBase : IDisposable
{
    protected readonly IServiceProvider ServiceProvider;
    protected readonly ILogger<TestBase> Logger;
    
    protected TestBase()
    {
        var services = new ServiceCollection();
        
        // Add logging
        services.AddLogging(builder =>
        {
            builder.AddDebug();
            builder.SetMinimumLevel(LogLevel.Debug);
        });
        
        // Add configuration
        var testIndexPath = Path.Combine(Path.GetTempPath(), $"test_index_{Guid.NewGuid()}");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ResponseLimits:MaxTokens"] = "20000",
                ["ResponseLimits:SafetyMargin"] = "0.8",
                ["ResponseLimits:DefaultMaxResults"] = "50",
                ["ResponseLimits:EnableTruncation"] = "true",
                ["ResponseLimits:EnablePagination"] = "true",
                ["Lucene:IndexBasePath"] = testIndexPath
            })
            .Build();
        
        services.AddSingleton<IConfiguration>(configuration);
        
        // Configure options
        services.Configure<ResponseLimitOptions>(configuration.GetSection("ResponseLimits"));
        
        // Add infrastructure services
        services.AddSingleton<IResponseSizeEstimator, ResponseSizeEstimator>();
        services.AddSingleton<IResultTruncator, ResultTruncator>();
        services.AddMemoryCache(); // Required for DetailRequestCache
        services.AddSingleton<IDetailRequestCache, DetailRequestCache>();
        services.AddSingleton<IFieldSelectorService, FieldSelectorService>();
        
        // Add core services
        services.AddSingleton<IPathResolutionService, PathResolutionService>();
        services.AddSingleton<ToolRegistry>();
        services.AddSingleton<IIndexingMetricsService, IndexingMetricsService>();
        services.AddSingleton<IBatchIndexingService, BatchIndexingService>();
        
        // Lucene services
        services.AddSingleton<LuceneIndexService>();
        services.AddSingleton<ILuceneWriterManager>(provider => provider.GetRequiredService<LuceneIndexService>());
        services.AddSingleton<ILuceneIndexService>(provider => provider.GetRequiredService<LuceneIndexService>());
        services.AddSingleton<FileIndexingService>();
        
        // Memory services
        services.AddSingleton<IErrorHandlingService, ErrorHandlingService>();
        services.AddSingleton<IMemoryValidationService, MemoryValidationService>();
        services.AddSingleton<FlexibleMemoryService>();
        services.AddSingleton<FlexibleMemoryTools>();
        services.AddSingleton<MemoryLinkingTools>();
        services.AddSingleton<ChecklistTools>();
        
        // Memory intelligence services
        services.AddSingleton<IQueryExpansionService, QueryExpansionService>();
        services.AddSingleton<IContextAwarenessService, ContextAwarenessService>();
        
        // Text search tools
        services.AddSingleton<FastTextSearchToolV2>();
        services.AddSingleton<FastFileSearchToolV2>();
        services.AddSingleton<FastRecentFilesTool>();
        services.AddSingleton<FastFileSizeAnalysisTool>();
        services.AddSingleton<FastSimilarFilesTool>();
        services.AddSingleton<FastDirectorySearchTool>();
        services.AddSingleton<IndexWorkspaceTool>();
        
        ServiceProvider = services.BuildServiceProvider();
        Logger = ServiceProvider.GetRequiredService<ILogger<TestBase>>();
    }
    
    protected string GetTestDataPath([CallerFilePath] string? sourceFilePath = null)
    {
        var directory = Path.GetDirectoryName(sourceFilePath) ?? throw new InvalidOperationException();
        return Path.Combine(directory, "TestData");
    }
    
    protected string GetTestProjectPath()
    {
        // Get the source directory from compile-time path
        var sourceDirectory = Path.GetDirectoryName(GetTestDataPath()) ?? throw new InvalidOperationException();
        var testProjectPath = Path.GetFullPath(Path.Combine(sourceDirectory, "TestProjects", "TestProject1"));
        
        if (!Directory.Exists(testProjectPath))
        {
            Directory.CreateDirectory(testProjectPath);
        }
        
        return testProjectPath;
    }
    
    protected async Task<string> CreateTestFileAsync(string fileName, string content)
    {
        var testDataPath = GetTestDataPath();
        if (!Directory.Exists(testDataPath))
        {
            Directory.CreateDirectory(testDataPath);
        }
        
        var filePath = Path.Combine(testDataPath, fileName);
        await File.WriteAllTextAsync(filePath, content);
        return filePath;
    }
    
    protected async Task<string> CreateTestDirectoryAsync(string dirName, Dictionary<string, string> files)
    {
        var testDataPath = GetTestDataPath();
        var dirPath = Path.Combine(testDataPath, dirName);
        
        // Clean up if exists
        if (Directory.Exists(dirPath))
        {
            Directory.Delete(dirPath, true);
        }
        
        Directory.CreateDirectory(dirPath);
        
        foreach (var (fileName, content) in files)
        {
            var filePath = Path.Combine(dirPath, fileName);
            var fileDir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(fileDir) && !Directory.Exists(fileDir))
            {
                Directory.CreateDirectory(fileDir);
            }
            await File.WriteAllTextAsync(filePath, content);
        }
        
        return dirPath;
    }
    
    public void Dispose()
    {
        if (ServiceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }
        
        // Clean up test data
        var testDataPath = GetTestDataPath();
        if (Directory.Exists(testDataPath))
        {
            try
            {
                Directory.Delete(testDataPath, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}