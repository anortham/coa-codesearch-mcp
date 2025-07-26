using COA.CodeSearch.McpServer.Configuration;
using COA.CodeSearch.McpServer.Infrastructure;
using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Tools;
using COA.CodeSearch.McpServer.Tools.Registration;
using Microsoft.Build.Locator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

// Track server start time for version tool
// TEST COMMENT: Testing file watcher detection - added at 12:59 PM
// TEST COMMENT 2: Another test at 1:38 PM to trigger file watcher
Program.ServerStartTime = DateTime.UtcNow;

// Register MSBuild before anything else
SetupMSBuildEnvironment();
RegisterMSBuild();

// Handle command line arguments
if (args.Length > 0 && (args[0] == "--help" || args[0] == "-h"))
{
    ShowHelp();
    return 0;
}

// Early cleanup of stuck write.lock files before any services start
await PerformEarlyStartupCleanup();

// Build and run the host
var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((context, config) =>
    {
        config
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables("MCP_");
    })
    .UseSerilog((context, services, configuration) =>
    {
        // Get path resolution service to determine log directory
        var pathResolution = new PathResolutionService(context.Configuration);
        var logDirectory = pathResolution.GetLogsPath();
        
        configuration
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .MinimumLevel.Override("COA.CodeSearch.McpServer.Services.FileIndexingService", LogEventLevel.Warning)
            .MinimumLevel.Override("COA.CodeSearch.McpServer.Services.LuceneIndexService", LogEventLevel.Warning)
            .MinimumLevel.Override("COA.CodeSearch.McpServer.Tests", LogEventLevel.Error)
            .WriteTo.File(
                path: Path.Combine(logDirectory, "codesearch-.log"),
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: 10_485_760, // 10MB
                retainedFileCountLimit: 7,
                shared: true,
                flushToDiskInterval: TimeSpan.FromSeconds(1),
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}"
            )
            .Enrich.FromLogContext();
    }, preserveStaticLogger: false)
    .ConfigureLogging((context, logging) =>
    {
        logging.ClearProviders();
        // MCP protocol requires that only stderr is used for logging
        // stdout is reserved for JSON-RPC communication
        
        // Redirect console output to stderr
        Console.SetOut(Console.Error);
        
        logging.AddSimpleConsole(options =>
        {
            options.ColorBehavior = Microsoft.Extensions.Logging.Console.LoggerColorBehavior.Disabled;
            options.IncludeScopes = false;
            options.TimestampFormat = "HH:mm:ss ";
        });
        
        // Configure logging from appsettings.json which includes namespace-specific levels
        logging.AddConfiguration(context.Configuration.GetSection("Logging"));
    })
    .ConfigureServices((context, services) =>
    {
        // Configuration
        services.Configure<ResponseLimitOptions>(
            context.Configuration.GetSection("ResponseLimits"));
        
        // Infrastructure services
        services.AddSingleton<IResponseSizeEstimator, ResponseSizeEstimator>();
        services.AddSingleton<IResultTruncator, ResultTruncator>();
        services.AddMemoryCache(); // Required for DetailRequestCache
        services.AddSingleton<IDetailRequestCache, DetailRequestCache>();
        services.AddHttpClient(); // For TypeScript installer
        
        // Core services
        services.AddSingleton<IPathResolutionService, PathResolutionService>();
        services.AddSingleton<IContextAwarenessService, ContextAwarenessService>();
        services.AddSingleton<ICircuitBreakerService, CircuitBreakerService>();
        services.AddSingleton<IErrorHandlingService, ErrorHandlingService>();
        services.AddSingleton<IQueryCacheService, QueryCacheService>();
        services.AddSingleton<IFieldSelectorService, FieldSelectorService>();
        services.AddSingleton<IStreamingResultService, StreamingResultService>();
        services.AddSingleton<CodeAnalysisService>();
        services.AddSingleton<ToolRegistry>();
        
        // Lucene services
        services.AddSingleton<LuceneIndexService>();
        services.AddSingleton<ILuceneWriterManager>(provider => provider.GetRequiredService<LuceneIndexService>());
        services.AddSingleton<ILuceneIndexService>(provider => provider.GetRequiredService<LuceneIndexService>());
        services.AddSingleton<IIndexingMetricsService, IndexingMetricsService>();
        services.AddSingleton<IBatchIndexingService, BatchIndexingService>();
        services.AddSingleton<FileIndexingService>();
        
        // Memory lifecycle configuration
        services.Configure<MemoryLifecycleOptions>(
            context.Configuration.GetSection("MemoryLifecycle"));
        
        // Memory lifecycle service
        services.AddSingleton<MemoryLifecycleService>();
        services.AddSingleton<IFileChangeSubscriber>(provider => provider.GetRequiredService<MemoryLifecycleService>());
        services.AddHostedService(provider => provider.GetRequiredService<MemoryLifecycleService>());
        
        // File watching service - must be registered after IFileChangeSubscriber implementations
        services.AddSingleton<FileWatcherService>();
        services.AddHostedService(provider => provider.GetRequiredService<FileWatcherService>());
        
        // Auto-reindex service - runs on startup to catch changes made between sessions
        services.AddHostedService<WorkspaceAutoIndexService>();
        
        // Claude Memory System
        services.AddSingleton<ClaudeMemoryService>();
        services.AddSingleton<JsonMemoryBackupService>();
        
        // Flexible Memory System
        services.AddSingleton<IMemoryValidationService, MemoryValidationService>();
        services.AddSingleton<FlexibleMemoryService>();
        services.AddSingleton<IMemoryService>(sp => sp.GetRequiredService<FlexibleMemoryService>());
        services.AddSingleton<FlexibleMemoryTools>();
        services.AddSingleton<FlexibleMemorySearchToolV2>();
        services.AddSingleton<MemoryLinkingTools>();
        services.AddSingleton<ChecklistTools>();
        services.AddSingleton<TimelineTool>();
        
        // Query Expansion for Memory Intelligence
        services.AddSingleton<IQueryExpansionService, QueryExpansionService>();
        services.AddSingleton<IContextAwarenessService, ContextAwarenessService>();
        
        // TypeScript Analysis
        services.AddSingleton<TypeScriptAnalysisService>();
        services.AddSingleton<ITypeScriptAnalysisService>(provider => provider.GetRequiredService<TypeScriptAnalysisService>());
        services.AddSingleton<TypeScriptTextAnalysisService>();
        
        // Razor/Blazor Analysis
        services.AddSingleton<RazorServerLocator>();
        services.AddSingleton<RazorVirtualDocumentManager>();
        services.AddSingleton<RazorLspClient>();
        services.AddSingleton<RazorPositionMapper>();
        services.AddSingleton<EmbeddedRazorAnalyzer>();
        services.AddSingleton<RazorAnalysisService>();
        services.AddSingleton<IRazorAnalysisService>(provider => provider.GetRequiredService<RazorAnalysisService>());
        
        
        // Initialize TypeScript on startup
        services.AddHostedService<TypeScriptInitializationService>();
        
        // Lucene lifecycle management
        services.AddHostedService<LuceneLifecycleService>();
        
        // Register all tools
        services.AddSingleton<GoToDefinitionTool>();
        services.AddSingleton<FindReferencesTool>();
        services.AddSingleton<FindReferencesToolV2>();
        services.AddSingleton<SearchSymbolsToolV2>();
        services.AddSingleton<GetDiagnosticsToolV2>();
        services.AddSingleton<TypeScriptHoverInfoTool>();
        services.AddSingleton<GetHoverInfoTool>();
        services.AddSingleton<GetImplementationsToolV2>();
        services.AddSingleton<GetDocumentSymbolsTool>();
        services.AddSingleton<GetCallHierarchyToolV2>();
        services.AddSingleton<RenameSymbolToolV2>();
        services.AddSingleton<BatchOperationsToolV2>();
        services.AddSingleton<AdvancedSymbolSearchTool>();
        services.AddSingleton<DependencyAnalysisToolV2>();
        services.AddSingleton<ProjectStructureAnalysisToolV2>();
        services.AddSingleton<FastTextSearchToolV2>();
        services.AddSingleton<FastFileSearchToolV2>();
        services.AddSingleton<FastRecentFilesTool>();
        services.AddSingleton<FastFileSizeAnalysisTool>();
        services.AddSingleton<FastSimilarFilesTool>();
        services.AddSingleton<FastDirectorySearchTool>();
        services.AddSingleton<StreamingTextSearchTool>();
        services.AddSingleton<IndexWorkspaceTool>();
        services.AddSingleton<ClaudeMemoryTools>();
        services.AddSingleton<SetLoggingTool>();
        services.AddSingleton<GetVersionTool>();
        services.AddSingleton<IndexHealthCheckTool>();
        
        
        // TypeScript tools
        services.AddSingleton<TypeScriptGoToDefinitionTool>();
        services.AddSingleton<TypeScriptFindReferencesTool>();
        services.AddSingleton<TypeScriptSearchTool>();
        services.AddSingleton<TypeScriptRenameTool>();
        services.AddSingleton<TypeScriptHoverInfoTool>();
        
        // Blazor tools
        services.AddSingleton<BlazorGoToDefinitionTool>();
        services.AddSingleton<BlazorFindReferencesTool>();
        services.AddSingleton<BlazorHoverInfoTool>();
        services.AddSingleton<BlazorRenameSymbolTool>();
        services.AddSingleton<BlazorGetDocumentSymbolsTool>();
        services.AddSingleton<BlazorGetDiagnosticsTool>();
        
        
        // Memory caching for performance optimization
        services.AddMemoryCache();
        
        // Register the MCP server as a hosted service and notification service
        services.AddSingleton<McpServer>();
        services.AddSingleton<INotificationService>(provider => provider.GetRequiredService<McpServer>());
        services.AddHostedService<McpServer>(provider => provider.GetRequiredService<McpServer>());
    })
    .UseConsoleLifetime(options =>
    {
        options.SuppressStatusMessages = true;
    })
    .Build();

// Register all tools before starting the host
using (var scope = host.Services.CreateScope())
{
    var toolRegistry = scope.ServiceProvider.GetRequiredService<ToolRegistry>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    
    logger.LogInformation("Registering tools before server startup...");
    AllToolRegistrations.RegisterAll(toolRegistry, scope.ServiceProvider);
    logger.LogInformation("Tool registration complete");
}

await host.RunAsync();
return 0;

static void SetupMSBuildEnvironment()
{
    // Suppress MSBuild console output to avoid interfering with MCP protocol
    Environment.SetEnvironmentVariable("MSBUILDLOGASYNC", "0");
    Environment.SetEnvironmentVariable("MSBUILDDISABLENODEREUSE", "1");
    Environment.SetEnvironmentVariable("MSBUILDNOINPROCNODE", "1");
    Environment.SetEnvironmentVariable("MSBUILDLOGTASKINPUTS", "0");
    Environment.SetEnvironmentVariable("MSBUILDTARGETOUTPUTLOGGING", "0");
    Environment.SetEnvironmentVariable("MSBUILDCONSOLELOGGERPARAMETERS", "NoSummary;Verbosity=quiet");
    Environment.SetEnvironmentVariable("MSBUILDENABLEALLPROPERTYFUNCTIONS", "1");
    Environment.SetEnvironmentVariable("MSBUILDLOGVERBOSERETHROW", "0");
    Environment.SetEnvironmentVariable("MSBUILDLOGIMPORTS", "0");
    Environment.SetEnvironmentVariable("MSBUILDLOGGINGPREPROCESSOR", "0");
    Environment.SetEnvironmentVariable("MSBUILDLOGALLPROJECTFROMSOLUTION", "0");
    Environment.SetEnvironmentVariable("DOTNET_CLI_CAPTURE_TIMING", "0");
    Environment.SetEnvironmentVariable("NUGET_SHOW_STACK", "false");
    Environment.SetEnvironmentVariable("ROSLYN_COMPILER_LOCATION", "");
    Environment.SetEnvironmentVariable("ROSLYN_ANALYZERS_ENABLED", "false");
}

static void RegisterMSBuild()
{
    if (!MSBuildLocator.IsRegistered)
    {
        try
        {
            // Find all available MSBuild instances
            var instances = MSBuildLocator.QueryVisualStudioInstances()
                .OrderByDescending(instance => instance.Version)
                .ToList();
            
            if (instances.Any())
            {
                var selected = instances.First();
                Console.Error.WriteLine($"Registering MSBuild: {selected.Name} {selected.Version} at {selected.MSBuildPath}");
                MSBuildLocator.RegisterInstance(selected);
            }
            else
            {
                Console.Error.WriteLine("No MSBuild instances found, using defaults");
                MSBuildLocator.RegisterDefaults();
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to register MSBuild: {ex.Message}");
            // Try to register defaults as fallback
            try
            {
                MSBuildLocator.RegisterDefaults();
            }
            catch
            {
                // If all else fails, continue without MSBuild
                Console.Error.WriteLine("WARNING: MSBuild registration failed completely");
            }
        }
    }
}

static async Task PerformEarlyStartupCleanup()
{
    try
    {
        // Create minimal configuration for PathResolutionService
        var configBuilder = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables("MCP_");
        var configuration = configBuilder.Build();
        
        // Create minimal services needed for cleanup
        var pathResolution = new PathResolutionService(configuration);
        
        // Create a simple console logger for startup
        using var loggerFactory = LoggerFactory.Create(builder => 
            builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        var logger = loggerFactory.CreateLogger("Startup");
        
        // Check for stuck locks and warn (but don't auto-clean)
        await LuceneIndexService.DiagnoseStuckIndexesOnStartupAsync(pathResolution, logger);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"WARNING: Early startup cleanup failed: {ex.Message}");
        // Don't fail startup - just log and continue
    }
}

static void ShowHelp()
{
    Console.WriteLine("COA CodeSearch MCP Server - High-performance code search and navigation");
    Console.WriteLine();
    Console.WriteLine("Usage: coa-codesearch-mcp [stdio]");
    Console.WriteLine();
    Console.WriteLine("Runs in STDIO mode for MCP clients (default)");
}


/// <summary>
/// Program class to hold static properties
/// </summary>
public partial class Program
{
    /// <summary>
    /// Server start time for uptime tracking
    /// </summary>
    public static DateTime ServerStartTime { get; set; }
}