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
        services.AddSingleton<IWorkspaceRegistryService, WorkspaceRegistryService>();
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
        services.AddSingleton<IWorkspaceRegistryService, WorkspaceRegistryService>();
        services.AddMemoryCache(); // Required by WorkspaceRegistryService
        services.AddLogging(logging => logging.AddSerilog());
        services.AddSingleton<IWriteLockManager, WriteLockManager>();
        
        var serviceProvider = services.BuildServiceProvider();
        var lockManager = serviceProvider.GetRequiredService<IWriteLockManager>();
        var registryService = serviceProvider.GetRequiredService<IWorkspaceRegistryService>();
        var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
        
        logger.LogInformation("Running startup cleanup and migration");
        
        // Check and run migration first
        if (await registryService.IsMigrationNeededAsync())
        {
            logger.LogInformation("Global registry not found - running migration from individual metadata files");
            var migrationResult = await registryService.MigrateFromIndividualMetadataAsync();
            
            if (migrationResult.Success)
            {
                logger.LogInformation("Migration completed - Workspaces: {WorkspaceCount}, Orphans: {OrphanCount}", 
                    migrationResult.WorkspacesMigrated, migrationResult.OrphansDiscovered);
                
                if (migrationResult.Errors.Any())
                {
                    logger.LogWarning("Migration completed with {ErrorCount} errors: {Errors}", 
                        migrationResult.Errors.Count, string.Join("; ", migrationResult.Errors));
                }
            }
            else
            {
                logger.LogError("Migration failed: {Errors}", string.Join("; ", migrationResult.Errors));
            }
        }
        
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
}
