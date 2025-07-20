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

// Register MSBuild before anything else
SetupMSBuildEnvironment();
MSBuildLocator.RegisterDefaults();

// Handle command line arguments
if (args.Length > 0 && (args[0] == "--help" || args[0] == "-h"))
{
    ShowHelp();
    return 0;
}

// Build and run the host
var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((context, config) =>
    {
        config
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables("MCP_");
    })
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
        logging.SetMinimumLevel(LogLevel.Information);
        
        // Override from config
        var logLevel = context.Configuration["Logging:LogLevel:Default"];
        if (Enum.TryParse<LogLevel>(logLevel, out var level))
        {
            logging.SetMinimumLevel(level);
        }
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
        services.AddSingleton<CodeAnalysisService>();
        services.AddSingleton<ToolRegistry>();
        
        // Lucene services
        services.AddSingleton<LuceneIndexService>();
        services.AddSingleton<ILuceneWriterManager>(provider => provider.GetRequiredService<LuceneIndexService>());
        services.AddSingleton<ILuceneIndexService>(provider => provider.GetRequiredService<LuceneIndexService>());
        services.AddSingleton<FileIndexingService>();
        
        // File watching service
        services.AddSingleton<FileWatcherService>();
        services.AddHostedService(provider => provider.GetRequiredService<FileWatcherService>());
        
        // Auto-reindex service - runs on startup to catch changes made between sessions
        services.AddHostedService<WorkspaceAutoIndexService>();
        
        // Claude Memory System
        services.AddSingleton<ClaudeMemoryService>();
        services.AddSingleton<MemoryBackupService>();
        
        // File Logging Service
        services.AddSingleton<FileLoggingService>();
        
        // TypeScript Analysis
        services.AddSingleton<TypeScriptInstaller>();
        services.AddSingleton<TypeScriptAnalysisService>();
        services.AddSingleton<TypeScriptTextAnalysisService>();
        
        // Initialize TypeScript on startup
        services.AddHostedService<TypeScriptInitializationService>();
        
        // Lucene lifecycle management
        services.AddHostedService<LuceneLifecycleService>();
        
        // Register all tools
        services.AddSingleton<GoToDefinitionTool>();
        services.AddSingleton<FindReferencesTool>();
        services.AddSingleton<FindReferencesToolV2>();
        services.AddSingleton<SearchSymbolsTool>();
        services.AddSingleton<GetDiagnosticsTool>();
        services.AddSingleton<GetDiagnosticsToolV2>();
        services.AddSingleton<TypeScriptHoverInfoTool>();
        services.AddSingleton<GetHoverInfoTool>();
        services.AddSingleton<GetImplementationsTool>();
        services.AddSingleton<GetDocumentSymbolsTool>();
        services.AddSingleton<GetCallHierarchyTool>();
        services.AddSingleton<RenameSymbolTool>();
        services.AddSingleton<RenameSymbolToolV2>();
        services.AddSingleton<BatchOperationsTool>();
        services.AddSingleton<AdvancedSymbolSearchTool>();
        services.AddSingleton<DependencyAnalysisTool>();
        services.AddSingleton<DependencyAnalysisToolV2>();
        services.AddSingleton<ProjectStructureAnalysisTool>();
        services.AddSingleton<ProjectStructureAnalysisToolV2>();
        services.AddSingleton<FastTextSearchTool>();
        services.AddSingleton<FastFileSearchTool>();
        services.AddSingleton<FastRecentFilesTool>();
        services.AddSingleton<FastFileSizeAnalysisTool>();
        services.AddSingleton<FastSimilarFilesTool>();
        services.AddSingleton<FastDirectorySearchTool>();
        services.AddSingleton<IndexWorkspaceTool>();
        services.AddSingleton<ClaudeMemoryTools>();
        services.AddSingleton<SetLoggingTool>();
        
        // TypeScript tools
        services.AddSingleton<TypeScriptGoToDefinitionTool>();
        services.AddSingleton<TypeScriptFindReferencesTool>();
        services.AddSingleton<TypeScriptSearchTool>();
        
        // Register the MCP server as a hosted service
        services.AddHostedService<McpServer>();
        
        // Tool registration happens during startup
        services.AddHostedService<ToolRegistrationService>();
    })
    .UseConsoleLifetime(options =>
    {
        options.SuppressStatusMessages = true;
    })
    .Build();

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

static void ShowHelp()
{
    Console.WriteLine("COA CodeSearch MCP Server - High-performance code search and navigation");
    Console.WriteLine();
    Console.WriteLine("Usage: coa-codesearch-mcp [stdio]");
    Console.WriteLine();
    Console.WriteLine("Runs in STDIO mode for MCP clients (default)");
}

/// <summary>
/// Service that registers all tools on startup
/// </summary>
public class ToolRegistrationService : IHostedService
{
    private readonly ToolRegistry _toolRegistry;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ToolRegistrationService> _logger;

    public ToolRegistrationService(
        ToolRegistry toolRegistry,
        IServiceProvider serviceProvider,
        ILogger<ToolRegistrationService> logger)
    {
        _toolRegistry = toolRegistry;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Registering tools...");

        // Register all tools
        AllToolRegistrations.RegisterAll(_toolRegistry, _serviceProvider);

        _logger.LogInformation("Tool registration complete");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}