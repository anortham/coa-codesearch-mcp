using COA.CodeSearch.McpServer.Configuration;
using COA.CodeSearch.McpServer.Infrastructure;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Tools;
using COA.CodeSearch.McpServer.Tools.Registration;
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
        services.Configure<MemoryLimitsConfiguration>(
            context.Configuration.GetSection("MemoryLimits"));
        
        // Infrastructure services
        services.AddSingleton<IResponseSizeEstimator, ResponseSizeEstimator>();
        services.AddSingleton<IResultTruncator, ResultTruncator>();
        services.AddMemoryCache(); // Required for DetailRequestCache
        services.AddSingleton<IDetailRequestCache, DetailRequestCache>();
        services.AddHttpClient(); // For HTTP operations
        
        // Core services
        services.AddSingleton<IPathResolutionService, PathResolutionService>();
        services.AddSingleton<IContextAwarenessService, ContextAwarenessService>();
        services.AddSingleton<ICircuitBreakerService, CircuitBreakerService>();
        services.AddSingleton<IErrorHandlingService, ErrorHandlingService>();
        services.AddSingleton<IErrorRecoveryService, ErrorRecoveryService>();
        services.AddSingleton<IQueryCacheService, QueryCacheService>();
        services.AddSingleton<IFieldSelectorService, FieldSelectorService>();
        services.AddSingleton<IStreamingResultService, StreamingResultService>();
        services.AddSingleton<IMemoryPressureService, MemoryPressureService>();
        services.AddSingleton<ConfigurationValidationService>();
        services.AddSingleton<ToolRegistry>();
        services.AddSingleton<ToolUsageAnalyticsService>();
        
        // Resource services for MCP Resources capability
        services.AddSingleton<IResourceRegistry, ResourceRegistry>();
        services.AddSingleton<WorkspaceResourceProvider>();
        services.AddSingleton<SearchResultResourceProvider>();
        services.AddSingleton<MemoryResourceProvider>();
        services.AddSingleton<TypeDiscoveryResourceProvider>();
        services.AddSingleton<WorkflowStateResourceProvider>();
        services.AddSingleton<ToolDiscoveryResourceProvider>();
        services.AddSingleton<AiAgentOnboardingResourceProvider>();
        
        // Prompt services for MCP Prompts capability
        services.AddSingleton<IPromptRegistry, PromptRegistry>();
        services.AddSingleton<AdvancedSearchBuilderPrompt>();
        services.AddSingleton<RefactoringAssistantPrompt>();
        services.AddSingleton<TechnicalDebtAnalyzerPrompt>();
        services.AddSingleton<ArchitectureDocumenterPrompt>();
        services.AddSingleton<CodeReviewAssistantPrompt>();
        services.AddSingleton<TestCoverageImproverPrompt>();
        services.AddSingleton<PromptTemplateResourceProvider>();
        
        // Lucene services (non-analyzer dependent)
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
        
        // Flexible Memory System (analyzer for memory search with synonyms)
        services.AddSingleton<MemoryAnalyzer>();
        
        // Lucene services (uses path-based analyzer selection: StandardAnalyzer for code, MemoryAnalyzer for memory)
        services.AddSingleton<LuceneIndexService>();
        services.AddSingleton<ILuceneWriterManager>(provider => provider.GetRequiredService<LuceneIndexService>());
        services.AddSingleton<ILuceneIndexService>(provider => provider.GetRequiredService<LuceneIndexService>());
        
        services.AddSingleton<IMemoryValidationService, MemoryValidationService>();
        services.AddSingleton<MemoryFacetingService>();
        services.AddSingleton<FlexibleMemoryService>();
        services.AddSingleton<IMemoryService>(sp => sp.GetRequiredService<FlexibleMemoryService>());
        services.AddSingleton<FlexibleMemoryTools>();
        services.AddSingleton<FlexibleMemorySearchToolV2>();
        services.AddSingleton<ITokenEstimationService, TokenEstimationService>();
        services.AddSingleton<AIResponseBuilderService>();
        services.AddSingleton<MemoryLinkingTools>();
        services.AddSingleton<ChecklistTools>();
        services.AddSingleton<TimelineTool>();
        services.AddSingleton<AIContextService>();
        services.AddSingleton<LoadContextTool>();
        
        // Phase 3: Unified Memory Interface
        services.AddSingleton<UnifiedMemoryService>();
        services.AddSingleton<UnifiedMemoryTool>();
        
        // Query Expansion for Memory Intelligence
        services.AddSingleton<IQueryExpansionService, QueryExpansionService>();
        services.AddSingleton<IContextAwarenessService, ContextAwarenessService>();
        
        // Scoring services for improved search relevance
        services.AddSingleton<IScoringService, ScoringService>();
        services.AddSingleton<IResultConfidenceService, ResultConfidenceService>();
        
        // Phase 3: Semantic Search Layer
        services.Configure<EmbeddingOptions>(context.Configuration.GetSection("Embedding"));
        services.AddSingleton<IEmbeddingService, EmbeddingService>();
        services.AddSingleton<IVectorIndex, InMemoryVectorIndex>();
        services.AddSingleton<SemanticMemoryIndex>();
        services.AddSingleton<HybridMemorySearch>();
        
        // Event-driven semantic indexing (clean architecture)
        services.AddSingleton<IMemoryEventPublisher, MemoryEventPublisher>();
        services.AddHostedService<SemanticIndexingSubscriber>();
        
        // Phase 3: Memory Quality Validation
        services.AddSingleton<IQualityValidator, COA.CodeSearch.McpServer.Services.Quality.CompletenessValidator>();
        services.AddSingleton<IQualityValidator, COA.CodeSearch.McpServer.Services.Quality.RelevanceValidator>();
        services.AddSingleton<IQualityValidator, COA.CodeSearch.McpServer.Services.Quality.ConsistencyValidator>();
        services.AddSingleton<IMemoryQualityValidator, MemoryQualityValidationService>();
        services.AddSingleton<MemoryQualityAssessmentTool>();
        
        
        
        // Lucene lifecycle management
        services.AddHostedService<LuceneLifecycleService>();
        
        // Register remaining tools
        services.AddSingleton<FastTextSearchToolV2>();
        services.AddSingleton<FastFileSearchToolV2>();
        services.AddSingleton<FastRecentFilesTool>();
        services.AddSingleton<FastFileSizeAnalysisTool>();
        services.AddSingleton<FastSimilarFilesTool>();
        services.AddSingleton<FastDirectorySearchTool>();
        services.AddSingleton<StreamingTextSearchTool>();
        services.AddSingleton<IndexWorkspaceTool>();
        services.AddSingleton<BatchOperationsToolV2>();
        services.AddSingleton<ClaudeMemoryTools>();
        services.AddSingleton<SetLoggingTool>();
        services.AddSingleton<GetVersionTool>();
        services.AddSingleton<IndexHealthCheckTool>();
        services.AddSingleton<SystemHealthCheckTool>();
        services.AddSingleton<SearchAssistantTool>();
        services.AddSingleton<PatternDetectorTool>();
        services.AddSingleton<MemoryGraphNavigatorTool>();
        services.AddSingleton<ToolUsageAnalyticsTool>();
        services.AddSingleton<WorkflowDiscoveryTool>();
        services.AddSingleton<SemanticSearchTool>();
        services.AddSingleton<HybridSearchTool>();
        
        
        
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

// Validate configuration and register tools before starting the host
using (var scope = host.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    
    // Validate configuration first
    logger.LogInformation("Validating configuration before server startup...");
    var configValidator = scope.ServiceProvider.GetRequiredService<ConfigurationValidationService>();
    var validationResult = await configValidator.ValidateAllAsync();
    
    if (!validationResult.IsValid)
    {
        logger.LogCritical("Configuration validation FAILED - server cannot start safely");
        foreach (var error in validationResult.Errors)
        {
            logger.LogCritical("CONFIG ERROR: {Error}", error);
        }
        
        Console.Error.WriteLine("\n❌ CONFIGURATION VALIDATION FAILED");
        Console.Error.WriteLine("The server cannot start due to configuration errors:");
        foreach (var error in validationResult.Errors)
        {
            Console.Error.WriteLine($"  • {error}");
        }
        
        if (validationResult.Warnings.Count > 0)
        {
            Console.Error.WriteLine("\nAdditional warnings:");
            foreach (var warning in validationResult.Warnings)
            {
                Console.Error.WriteLine($"  ⚠ {warning}");
            }
        }
        
        Console.Error.WriteLine("\nPlease fix the configuration errors and restart the server.");
        return 1; // Exit with error code
    }
    
    if (validationResult.HasWarnings)
    {
        logger.LogWarning("Configuration has warnings but is valid - server will start");
        Console.Error.WriteLine("\n⚠ CONFIGURATION WARNINGS:");
        foreach (var warning in validationResult.Warnings)
        {
            Console.Error.WriteLine($"  • {warning}");
        }
        Console.Error.WriteLine("");
    }
    else
    {
        logger.LogInformation("✅ Configuration validation passed - all settings are valid");
    }
    
    // Register tools after configuration validation passes
    var toolRegistry = scope.ServiceProvider.GetRequiredService<ToolRegistry>();
    logger.LogInformation("Registering tools after successful configuration validation...");
    AllToolRegistrations.RegisterAll(toolRegistry, scope.ServiceProvider);
    logger.LogInformation("Tool registration complete");
    
    // Register resource providers for MCP Resources capability
    var resourceRegistry = scope.ServiceProvider.GetRequiredService<IResourceRegistry>();
    logger.LogInformation("Registering resource providers...");
    resourceRegistry.RegisterProvider(scope.ServiceProvider.GetRequiredService<WorkspaceResourceProvider>());
    resourceRegistry.RegisterProvider(scope.ServiceProvider.GetRequiredService<SearchResultResourceProvider>());
    resourceRegistry.RegisterProvider(scope.ServiceProvider.GetRequiredService<MemoryResourceProvider>());
    resourceRegistry.RegisterProvider(scope.ServiceProvider.GetRequiredService<TypeDiscoveryResourceProvider>());
    resourceRegistry.RegisterProvider(scope.ServiceProvider.GetRequiredService<WorkflowStateResourceProvider>());
    resourceRegistry.RegisterProvider(scope.ServiceProvider.GetRequiredService<ToolDiscoveryResourceProvider>());
    resourceRegistry.RegisterProvider(scope.ServiceProvider.GetRequiredService<PromptTemplateResourceProvider>());
    resourceRegistry.RegisterProvider(scope.ServiceProvider.GetRequiredService<AiAgentOnboardingResourceProvider>());
    logger.LogInformation("Resource provider registration complete");
    
    // Register prompt templates for MCP Prompts capability
    var promptRegistry = scope.ServiceProvider.GetRequiredService<IPromptRegistry>();
    logger.LogInformation("Registering prompt templates...");
    promptRegistry.RegisterPrompt(scope.ServiceProvider.GetRequiredService<AdvancedSearchBuilderPrompt>());
    promptRegistry.RegisterPrompt(scope.ServiceProvider.GetRequiredService<RefactoringAssistantPrompt>());
    promptRegistry.RegisterPrompt(scope.ServiceProvider.GetRequiredService<TechnicalDebtAnalyzerPrompt>());
    promptRegistry.RegisterPrompt(scope.ServiceProvider.GetRequiredService<ArchitectureDocumenterPrompt>());
    promptRegistry.RegisterPrompt(scope.ServiceProvider.GetRequiredService<CodeReviewAssistantPrompt>());
    promptRegistry.RegisterPrompt(scope.ServiceProvider.GetRequiredService<TestCoverageImproverPrompt>());
    logger.LogInformation("Prompt template registration complete");
}

await host.RunAsync();
return 0;



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
        
        // Smart tiered cleanup for stuck locks (auto-clean safe ones, diagnose risky ones)
        await LuceneIndexService.SmartStartupCleanupAsync(pathResolution, logger);
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