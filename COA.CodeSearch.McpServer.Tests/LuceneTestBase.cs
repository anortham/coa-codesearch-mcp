using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using COA.CodeSearch.McpServer.Configuration;
using COA.CodeSearch.McpServer.Infrastructure;
using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Tools;
using COA.CodeSearch.McpServer.Tests.Helpers;
using COA.CodeSearch.McpServer.Models;
using System.Runtime.CompilerServices;

namespace COA.CodeSearch.McpServer.Tests;

public abstract class LuceneTestBase : IDisposable
{
    protected readonly IServiceProvider ServiceProvider;
    protected readonly ILogger<LuceneTestBase> Logger;
    private readonly string _tempDirectory;
    
    protected LuceneTestBase()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"lucene_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDirectory);
        
        var services = new ServiceCollection();
        
        // Add logging
        services.AddLogging(builder =>
        {
            builder.AddDebug();
            builder.SetMinimumLevel(LogLevel.Debug);
        });
        
        // Add configuration with test-specific index path
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ResponseLimits:MaxTokens"] = "20000",
                ["ResponseLimits:SafetyMargin"] = "0.8",
                ["ResponseLimits:DefaultMaxResults"] = "50",
                ["ResponseLimits:EnableTruncation"] = "true",
                ["ResponseLimits:EnablePagination"] = "true",
                ["Lucene:IndexBasePath"] = _tempDirectory
            })
            .Build();
        
        services.AddSingleton<IConfiguration>(configuration);
        
        // Configure options
        services.Configure<ResponseLimitOptions>(configuration.GetSection("ResponseLimits"));
        
        // Add infrastructure services
        services.AddSingleton<IResponseSizeEstimator, ResponseSizeEstimator>();
        services.AddSingleton<IResultTruncator, ResultTruncator>();
        services.AddMemoryCache();
        services.AddSingleton<IDetailRequestCache, DetailRequestCache>();
        services.AddSingleton<IFieldSelectorService, FieldSelectorService>();
        services.AddSingleton<AIResponseBuilderService>();
        
        // Add response builders
        services.AddSingleton<COA.CodeSearch.McpServer.Services.ResponseBuilders.IResponseBuilderFactory, COA.CodeSearch.McpServer.Services.ResponseBuilders.ResponseBuilderFactory>();
        services.AddSingleton<COA.CodeSearch.McpServer.Services.ResponseBuilders.TextSearchResponseBuilder>();
        services.AddSingleton<COA.CodeSearch.McpServer.Services.ResponseBuilders.FileSearchResponseBuilder>();
        services.AddSingleton<COA.CodeSearch.McpServer.Services.ResponseBuilders.MemorySearchResponseBuilder>();
        services.AddSingleton<COA.CodeSearch.McpServer.Services.ResponseBuilders.DirectorySearchResponseBuilder>();
        services.AddSingleton<COA.CodeSearch.McpServer.Services.ResponseBuilders.SimilarFilesResponseBuilder>();
        services.AddSingleton<COA.CodeSearch.McpServer.Services.ResponseBuilders.RecentFilesResponseBuilder>();
        services.AddSingleton<COA.CodeSearch.McpServer.Services.ResponseBuilders.FileSizeAnalysisResponseBuilder>();
        services.AddSingleton<COA.CodeSearch.McpServer.Services.ResponseBuilders.FileSizeDistributionResponseBuilder>();
        services.AddSingleton<COA.CodeSearch.McpServer.Services.ResponseBuilders.BatchOperationsResponseBuilder>();
        
        // Add core services
        services.AddSingleton<IPathResolutionService, PathResolutionService>();
        services.AddSingleton<IIndexingMetricsService, IndexingMetricsService>();
        services.AddSingleton<IBatchIndexingService, BatchIndexingService>();
        services.AddSingleton<IErrorHandlingService, ErrorHandlingService>();
        services.AddSingleton<IErrorRecoveryService, ErrorRecoveryService>();
        services.AddSingleton<ICircuitBreakerService, CircuitBreakerService>();
        services.AddSingleton<IQueryCacheService, QueryCacheService>();
        services.AddSingleton<IStreamingResultService, StreamingResultService>();
        
        // Memory pressure service
        services.AddSingleton<IMemoryPressureService, MemoryPressureService>();
        
        // Configure memory limits 
        services.Configure<MemoryLimitsConfiguration>(config =>
        {
            config.MaxFileSize = 10 * 1024 * 1024; // 10MB
            config.MaxAllowedResults = 10000;
            config.MaxIndexingConcurrency = 8;
            config.EnableBackpressure = true;
        });
        
        // Lucene services - removed MemoryAnalyzer, using StandardAnalyzer only
        services.AddSingleton<LuceneIndexService>();
        services.AddSingleton<ILuceneWriterManager>(provider => provider.GetRequiredService<LuceneIndexService>());
        services.AddSingleton<ILuceneIndexService>(provider => provider.GetRequiredService<LuceneIndexService>());
        services.AddSingleton<FileIndexingService>();
        
        // Text search tools
        services.AddSingleton<FastTextSearchToolV2>();
        services.AddSingleton<FastFileSearchToolV2>();
        services.AddSingleton<IndexWorkspaceTool>();
        
        ServiceProvider = services.BuildServiceProvider();
        Logger = ServiceProvider.GetRequiredService<ILogger<LuceneTestBase>>();
    }
    
    protected string GetTestDataPath([CallerFilePath] string? sourceFilePath = null)
    {
        var directory = Path.GetDirectoryName(sourceFilePath) ?? throw new InvalidOperationException();
        return Path.Combine(directory, "TestData");
    }
    
    protected async Task<string> CreateTestFileAsync(string fileName, string content)
    {
        var filePath = Path.Combine(_tempDirectory, fileName);
        var fileDir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(fileDir) && !Directory.Exists(fileDir))
        {
            Directory.CreateDirectory(fileDir);
        }
        
        await File.WriteAllTextAsync(filePath, content);
        return filePath;
    }
    
    protected async Task<string> CreateTestDirectoryWithFilesAsync(string dirName, Dictionary<string, string> files)
    {
        var dirPath = Path.Combine(_tempDirectory, dirName);
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
    
    protected string GetTempDirectory() => _tempDirectory;
    
    protected string GetTestProjectPath()
    {
        // Return the temp directory for tests
        return _tempDirectory;
    }
    
    public void Dispose()
    {
        if (ServiceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }
        
        // Clean up temp directory
        if (Directory.Exists(_tempDirectory))
        {
            try
            {
                Directory.Delete(_tempDirectory, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}