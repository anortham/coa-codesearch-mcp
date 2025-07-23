using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using COA.CodeSearch.McpServer.Configuration;
using COA.CodeSearch.McpServer.Infrastructure;
using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Tools;
using Microsoft.Build.Locator;
using System.Runtime.CompilerServices;

namespace COA.CodeSearch.McpServer.Tests;

public abstract class LuceneTestBase : IDisposable
{
    protected readonly IServiceProvider ServiceProvider;
    protected readonly CodeAnalysisService WorkspaceService;
    protected readonly ILogger<LuceneTestBase> Logger;
    private readonly string _tempDirectory;
    
    static LuceneTestBase()
    {
        // Register MSBuild once for all tests
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }
    }
    
    protected LuceneTestBase()
    {
        // Create a unique temp directory for this test
        _tempDirectory = Path.Combine(Path.GetTempPath(), "COA_Tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDirectory);
        
        var services = new ServiceCollection();
        
        // Add logging
        services.AddLogging(builder =>
        {
            builder.AddDebug();
            builder.SetMinimumLevel(LogLevel.Debug);
        });
        
        // Add configuration with test-specific paths
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["McpServer:MaxWorkspaces"] = "5",
                ["McpServer:WorkspaceTimeout"] = "00:30:00",
                ["McpServer:ParallelismDegree"] = "4",
                ["ResponseLimits:MaxTokens"] = "20000",
                ["ResponseLimits:SafetyMargin"] = "0.8",
                ["ResponseLimits:DefaultMaxResults"] = "50",
                ["ResponseLimits:EnableTruncation"] = "true",
                ["ResponseLimits:EnablePagination"] = "true",
                ["FileWatcher:Enabled"] = "false", // Disable file watcher for tests
                ["WorkspaceAutoIndex:Enabled"] = "false", // Disable auto-indexing for tests
                ["Paths:BasePath"] = _tempDirectory // Use temp directory for test data
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
        
        // Add path resolution service
        services.AddSingleton<IPathResolutionService, PathResolutionService>();
        
        // Add Lucene services
        services.AddSingleton<ILuceneIndexService, LuceneIndexService>();
        services.AddSingleton<FileIndexingService>();
        services.AddSingleton<IndexWorkspaceTool>();
        services.AddSingleton<FileWatcherService>();
        services.AddSingleton<WorkspaceAutoIndexService>();
        services.AddSingleton<LuceneLifecycleService>();
        
        // Add TypeScript services (required for some tests)
        services.AddSingleton<TypeScriptInitializationService>();
        services.AddSingleton<TypeScriptAnalysisService>();
        
        // Add code analysis services
        services.AddSingleton<CodeAnalysisService>();
        
        ServiceProvider = services.BuildServiceProvider();
        WorkspaceService = ServiceProvider.GetRequiredService<CodeAnalysisService>();
        Logger = ServiceProvider.GetRequiredService<ILogger<LuceneTestBase>>();
        
        // Preload the test workspace
        var testProjectPath = GetTestProjectPath();
        try
        {
            var loadTask = WorkspaceService.GetProjectAsync(testProjectPath);
            loadTask.Wait(TimeSpan.FromSeconds(10));
        }
        catch (Exception ex)
        {
            Logger.LogWarning("Failed to preload test workspace: {Error}", ex.Message);
        }
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
        var testProjectPath = Path.GetFullPath(Path.Combine(sourceDirectory, "TestProjects", "TestProject1", "TestProject1.csproj"));
        
        if (!File.Exists(testProjectPath))
        {
            throw new FileNotFoundException($"Test project not found at: {testProjectPath}");
        }
        
        return testProjectPath;
    }
    
    protected string GetTestCodePath()
    {
        var projectPath = GetTestProjectPath();
        var projectDir = Path.GetDirectoryName(projectPath) ?? throw new InvalidOperationException();
        var codePath = Path.Combine(projectDir, "TestCode.cs");
        
        if (!File.Exists(codePath))
        {
            throw new FileNotFoundException($"Test code file not found at: {codePath}");
        }
        
        return codePath;
    }
    
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
    
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Clean up temp directory
            if (Directory.Exists(_tempDirectory))
            {
                try
                {
                    Directory.Delete(_tempDirectory, true);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning("Failed to delete temp directory: {Error}", ex.Message);
                }
            }
            
            // Dispose service provider
            if (ServiceProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}