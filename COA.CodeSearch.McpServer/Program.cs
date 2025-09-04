using COA.Mcp.Framework.Server;
using COA.Mcp.Framework.TokenOptimization;
using COA.Mcp.Framework.TokenOptimization.Caching;
using COA.Mcp.Framework.TokenOptimization.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Tools;
using Serilog;
using System.Reflection;
using System.Runtime.InteropServices;
using TreeSitter;

namespace COA.CodeSearch.McpServer;

public class Program
{
    /// <summary>
    /// Configure shared services used by both STDIO and HTTP modes
    /// </summary>
    private static void ConfigureSharedServices(IServiceCollection services, IConfiguration configuration)
    {
        // Register configuration
        services.AddSingleton<IConfiguration>(configuration);

        // Register Memory Cache for query caching
        services.AddMemoryCache();
        
        // Register configuration models
        services.Configure<COA.CodeSearch.McpServer.Models.MemoryLimitsConfiguration>(
            configuration.GetSection("CodeSearch:MemoryPressure"));
        
        // Register core services
        services.AddSingleton<IPathResolutionService, PathResolutionService>();
        // WorkspaceRegistry removed - using hybrid local indexing model
        services.AddSingleton<ICircuitBreakerService, CircuitBreakerService>();
        services.AddSingleton<IMemoryPressureService, MemoryPressureService>();
        services.AddSingleton<IQueryCacheService, QueryCacheService>();
        
        // Register Lucene services
        services.AddSingleton<COA.CodeSearch.McpServer.Services.Lucene.ILuceneIndexService, 
                              COA.CodeSearch.McpServer.Services.Lucene.LuceneIndexService>();
        
        // Register indexing services
        services.AddSingleton<IIndexingMetricsService, IndexingMetricsService>();
        services.AddSingleton<IBatchIndexingService, BatchIndexingService>();
        services.AddSingleton<IFileIndexingService, FileIndexingService>();
        
        // Register support services
        services.AddSingleton<IFieldSelectorService, FieldSelectorService>();
        services.AddSingleton<IErrorRecoveryService, ErrorRecoveryService>();
        services.AddSingleton<SmartSnippetService>();
        
        // API services for HTTP mode
        services.AddSingleton<ConfidenceCalculatorService>();
        
        // Simplified line-aware search service
        services.AddSingleton<LineAwareSearchService>();
        
        // Query preprocessing for code-aware search
        services.AddSingleton<QueryPreprocessor>();
        
        // Type extraction services (optional - graceful fallback if not available)
        services.AddSingleton<COA.CodeSearch.McpServer.Services.TypeExtraction.ITypeExtractionService, 
                              COA.CodeSearch.McpServer.Services.TypeExtraction.TypeExtractionService>();
        services.AddSingleton<COA.CodeSearch.McpServer.Services.TypeExtraction.IQueryTypeDetector, 
                              COA.CodeSearch.McpServer.Services.TypeExtraction.QueryTypeDetector>();
        
        // ProjectKnowledge integration services
        services.AddHttpClient<IProjectKnowledgeService, ProjectKnowledgeService>();
        services.AddSingleton<SmartDocumentationService>();
        
        // FileWatcher as background service - register properly for auto-start
        services.AddSingleton<FileWatcherService>();
        services.AddHostedService(provider => provider.GetRequiredService<FileWatcherService>());
        
        // Write lock management
        services.AddSingleton<IWriteLockManager, WriteLockManager>();
        
        // VS Code Bridge integration (graceful degradation if unavailable)
        services.Configure<COA.VSCodeBridge.VSCodeBridgeOptions>(options =>
        {
            options.Url = "ws://localhost:7823/mcp";
            options.AutoConnect = false; // Disabled to prevent log flooding when bridge not running
            options.ThrowOnConnectionFailure = false; // Graceful degradation
            options.ThrowOnDisplayFailure = false;
        });
        services.AddSingleton<COA.VSCodeBridge.VSCodeBridge>(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<COA.VSCodeBridge.VSCodeBridgeOptions>>();
            var logger = serviceProvider.GetService<ILogger<COA.VSCodeBridge.VSCodeBridge>>();
            return new COA.VSCodeBridge.VSCodeBridge(options, logger);
        });
        services.AddSingleton<COA.VSCodeBridge.IVSCodeBridge>(serviceProvider => 
            serviceProvider.GetRequiredService<COA.VSCodeBridge.VSCodeBridge>());
        
        // Token Optimization services
        services.AddSingleton<ITokenEstimator, DefaultTokenEstimator>();
        // Note: IInsightGenerator and IActionGenerator may be internal to framework
        // services.AddSingleton<IInsightGenerator, InsightGenerator>();
        // services.AddSingleton<IActionGenerator, ActionGenerator>();
        
        // Configure cache eviction policy - LRU with 100MB limit
        services.AddSingleton<ICacheEvictionPolicy>(sp => 
            new LruEvictionPolicy(maxMemoryBytes: 100_000_000, targetMemoryUsageRatio: 0.8));
        
        // Register caching services with eviction policy
        services.AddSingleton<IResponseCacheService, ResponseCacheService>();
        services.AddSingleton<IResourceStorageService, ResourceStorageService>();
        services.AddSingleton<ICacheKeyGenerator, CacheKeyGenerator>();
        
        // Resource providers
        // services.AddSingleton<SearchResultResourceProvider>();
    }

    /// <summary>
    /// Run startup cleanup for write.lock files
    /// </summary>
    private static async Task RunStartupCleanupAsync(IConfiguration configuration)
    {
        // Create temporary services for cleanup
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddSingleton<IPathResolutionService, PathResolutionService>();
        // WorkspaceRegistry removed - using hybrid local indexing model
        services.AddMemoryCache(); // Still useful for other caching needs
        services.AddLogging(logging => logging.AddSerilog());
        services.AddSingleton<IWriteLockManager, WriteLockManager>();
        
        var serviceProvider = services.BuildServiceProvider();
        var lockManager = serviceProvider.GetRequiredService<IWriteLockManager>();
        var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
        
        logger.LogInformation("Running startup cleanup");
        
        // Migration and registry no longer needed with hybrid local indexing model
        
        logger.LogInformation("Running write.lock cleanup");
        
        try
        {
            var result = await lockManager.SmartStartupCleanupAsync();
            
            if (result.StuckLocksFound > 0)
            {
                logger.LogWarning("Found {Count} stuck locks that may require manual intervention", 
                    result.StuckLocksFound);
            }
            
            logger.LogInformation(
                "Startup cleanup complete - Test artifacts: {Test}, Workspace locks: {Workspace}, Stuck: {Stuck}",
                result.TestArtifactsRemoved, result.WorkspaceLocksRemoved, result.StuckLocksFound);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to run startup cleanup - continuing anyway");
        }
    }
    
    /// <summary>
    /// Configure Serilog with file logging only (no console to avoid breaking STDIO)
    /// Uses a shared log file for both STDIO and HTTP processes to consolidate logging
    /// </summary>
    private static void ConfigureSerilog(IConfiguration configuration, string[]? args = null)
    {
        // Create a temporary path service to get the logs directory - exactly like ProjectKnowledge
        using var loggerFactory = LoggerFactory.Create(builder => { }); // Empty logger for initialization
        var tempLogger = loggerFactory.CreateLogger<PathResolutionService>();
        var tempPathService = new PathResolutionService(configuration, tempLogger);
        var logsPath = tempPathService.GetLogsPath();
        tempPathService.EnsureDirectoryExists(logsPath);
        
        // Use a shared log file name (no process-specific suffix)
        var logFile = Path.Combine(logsPath, "codesearch-.log");
        
        // Determine process mode for logging context
        var processMode = "STDIO";
        if (args?.Contains("--mode") == true && args?.Contains("http") == true)
        {
            processMode = "HTTP";
        }
        else if (args?.Contains("--service") == true)
        {
            processMode = "SERVICE";
        }

        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .Enrich.WithProperty("ProcessMode", processMode) // Add process mode to all log entries
            .WriteTo.File(
                logFile,
                rollingInterval: RollingInterval.Day,
                rollOnFileSizeLimit: true,
                fileSizeLimitBytes: 10 * 1024 * 1024, // 10MB
                retainedFileCountLimit: 7, // Keep 7 days of logs
                shared: true, // CRITICAL: Allow multiple processes to write to same file
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{ProcessMode}] {SourceContext} {Message:lj}{NewLine}{Exception}"
            )
            .CreateLogger();
    }

    public static async Task Main(string[] args)
    {
        // CodeSearch now runs in STDIO mode only (HTTP API removed)

        // Load configuration early for logging setup
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddEnvironmentVariables()
            .Build();

        // Configure Serilog early - FILE ONLY (no console to avoid breaking STDIO)
        ConfigureSerilog(configuration, args);

        // Configure native resolver for Tree-sitter on macOS (Homebrew/system installs)
        ConfigureTreeSitterNativeResolver();

        try
        {
            // Run write.lock cleanup before starting
            await RunStartupCleanupAsync(configuration);
            
            // Use framework's builder
            var builder = new McpServerBuilder()
                .WithServerInfo("CodeSearch", "2.0.0")
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddSerilog(); // Use Serilog for all logging
                });

            // Configure shared services
            ConfigureSharedServices(builder.Services, configuration);
            
            // TODO: Figure out how to use token optimization with McpServerBuilder
            // Currently AddMcpFramework conflicts with McpServerBuilder's registry

            // Register response builders
            builder.Services.AddScoped<ResponseBuilders.SearchResponseBuilder>();
            builder.Services.AddScoped<ResponseBuilders.FileSearchResponseBuilder>();
            builder.Services.AddScoped<ResponseBuilders.IndexResponseBuilder>();
            builder.Services.AddScoped<ResponseBuilders.RecentFilesResponseBuilder>();
            builder.Services.AddScoped<ResponseBuilders.DirectorySearchResponseBuilder>();
            builder.Services.AddScoped<ResponseBuilders.SimilarFilesResponseBuilder>();
            builder.Services.AddScoped<ResponseBuilders.LineSearchResponseBuilder>();
            builder.Services.AddScoped<ResponseBuilders.SearchAndReplaceResponseBuilder>();
            
            // Register tools in DI first (required for constructor dependencies)
            // Search tools
            builder.Services.AddScoped<IndexWorkspaceTool>();
            builder.Services.AddScoped<TextSearchTool>(); // Uses BaseResponseBuilder pattern
            builder.Services.AddScoped<FileSearchTool>();
            builder.Services.AddScoped<BatchOperationsTool>(); // Batch operations for multiple searches
            builder.Services.AddScoped<RecentFilesTool>(); // New! Framework 1.5.2 implementation
            builder.Services.AddScoped<DirectorySearchTool>(); // New! Directory search implementation
            builder.Services.AddScoped<SimilarFilesTool>(); // New! Find similar files using MoreLikeThis
            builder.Services.AddScoped<LineSearchTool>(); // New! Grep-like line-level search
            builder.Services.AddScoped<SearchAndReplaceTool>(); // New! Consolidates Search→Read→Edit workflow
            
            // Navigation tools (from CodeNav consolidation)
            // TODO: All tools should follow the response builder pattern for consistency
            // These tools use dedicated response builders for token optimization and consistent behavior
            builder.Services.AddScoped<SymbolSearchTool>(); // Find symbol definitions using Tree-sitter data
            builder.Services.AddScoped<FindReferencesTool>(); // Find all usages of a symbol
            builder.Services.AddScoped<GoToDefinitionTool>(); // Jump to symbol definition
            
            // Register resource providers
            // builder.Services.AddSingleton<IResourceProvider, SearchResultResourceProvider>();

            // Discover and register all tools from assembly
            builder.DiscoverTools(typeof(Program).Assembly);

            // Register prompts for interactive workflows
            builder.RegisterPrompt(new Prompts.CodeExplorerPrompt());
            builder.RegisterPrompt(new Prompts.BugHunterPrompt());
            builder.RegisterPrompt(new Prompts.RefactoringAssistantPrompt());

            // Run in STDIO mode (the only mode now)
            {
                Log.Information("Starting CodeSearch MCP Server");
                
                // Register startup indexing service
                builder.Services.AddHostedService<StartupIndexingService>();
                
                // Use STDIO transport
                builder.UseStdioTransport();
                
                // FileWatcherService will self-start when StartWatching is called
                // This ensures the same instance receives and processes events
                await builder.RunAsync();
            }
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "CodeSearch startup failed");
            throw;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    /// <summary>
    /// On macOS, resolve Tree-sitter native libraries from common system locations (Homebrew/MacPorts)
    /// so we don't need to bundle dylibs. No-ops on non-macOS platforms.
    /// </summary>
    private static void ConfigureTreeSitterNativeResolver()
    {
        if (!OperatingSystem.IsMacOS()) return;

        try
        {
            var tsAssembly = typeof(Parser).Assembly;
            NativeLibrary.SetDllImportResolver(tsAssembly, ResolveTreeSitterNative);

            // Also attach resolver for our own assembly so any custom P/Invoke bindings can resolve dylibs
            NativeLibrary.SetDllImportResolver(Assembly.GetExecutingAssembly(), ResolveTreeSitterNative);
        }
        catch
        {
            // Silent fallback; type extraction will gracefully degrade if loading fails
        }
    }

    private static IntPtr ResolveTreeSitterNative(string libraryName, Assembly assembly, DllImportSearchPath? _)
    {
        if (!OperatingSystem.IsMacOS()) return IntPtr.Zero;

        // Tree-sitter grammars are installed as libtree-sitter-<lang>.dylib (and core as libtree-sitter*.dylib)
        // Some managed calls may request plain language IDs (e.g., "typescript").
        // Resolve both patterns: lib<name>.dylib and libtree-sitter-<name>.dylib.
        var fileNames = new List<string>();
        fileNames.Add($"lib{libraryName}.dylib");
        if (!libraryName.StartsWith("tree-sitter", StringComparison.OrdinalIgnoreCase))
        {
            fileNames.Add($"libtree-sitter-{libraryName}.dylib");
        }

        // Probe common Homebrew/MacPorts install locations
        var prefixes = new[] { "/opt/homebrew", "/usr/local", "/opt/local" };

        // Common subpaths under the prefixes
        var commonSubpaths = new Func<string, IEnumerable<string>>(fn => new[]
        {
            Path.Combine("lib", fn),
            Path.Combine("opt", "tree-sitter", "lib", fn)
        });

        Log.Debug("Tree-sitter resolver: resolving '{LibraryName}'", libraryName);

        foreach (var prefix in prefixes)
        {
            foreach (var fn in fileNames)
            {
                foreach (var sub in commonSubpaths(fn))
                {
                    var candidate = Path.Combine(prefix, sub);
                    if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out var handle))
                    {
                        Log.Debug("Tree-sitter resolver: loaded '{LibraryName}' from '{Path}'", libraryName, candidate);
                        return handle;
                    }
                }
            }
        }

        // Allow env override with a list of directories (PATH-like)
        var extra = Environment.GetEnvironmentVariable("TREE_SITTER_NATIVE_PATHS");
        if (!string.IsNullOrWhiteSpace(extra))
        {
            Log.Debug("Tree-sitter resolver: probing TREE_SITTER_NATIVE_PATHS={Paths}", extra);
            foreach (var dir in extra.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                foreach (var fn in fileNames)
                {
                    var candidate = Path.Combine(dir, fn);
                    if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out var handle))
                    {
                        Log.Debug("Tree-sitter resolver: loaded '{LibraryName}' from '{Path}' via TREE_SITTER_NATIVE_PATHS", libraryName, candidate);
                        return handle;
                    }
                }
            }
        }

        Log.Debug("Tree-sitter resolver: failed to resolve '{LibraryName}', deferring to default", libraryName);
        return IntPtr.Zero; // Defer to default resolution if we didn't find it
    }
}
