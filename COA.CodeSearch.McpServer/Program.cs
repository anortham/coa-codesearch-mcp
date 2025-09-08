using COA.Mcp.Framework.Server;
using COA.CodeSearch.McpServer.Providers;
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
using System.IO;
using System.Linq;
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
        services.AddSingleton<IParameterDefaultsService, ParameterDefaultsService>();
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
                        services.AddSingleton<AdvancedPatternMatcher>();
                services.AddSingleton<SmartQueryPreprocessor>();
        
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
        // ResourceStorageProvider created lazily in ConfigureResources callback
        // services.AddSingleton<SearchResultResourceProvider>();
    }

    /// <summary>
    /// Run startup cleanup for write.lock files
    /// </summary>
    private static async Task RunStartupCleanupAsync(IConfiguration configuration)
    {
        // Create services for cleanup using proper dependency injection pattern
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddSingleton<IPathResolutionService, PathResolutionService>();
        // WorkspaceRegistry removed - using hybrid local indexing model
        services.AddMemoryCache(); // Still useful for other caching needs
        services.AddLogging(logging => logging.AddSerilog());
        services.AddSingleton<IWriteLockManager, WriteLockManager>();
        
        // Use using statement to properly dispose of the service provider
        using var serviceProvider = services.BuildServiceProvider();
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
            
            // Register resource provider for serving ResourceStorage data
            builder.Services.AddScoped<Providers.ResourceStorageProvider>();
            
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
            
            // Editing tools (NEW - Enable dogfooding!)
            builder.Services.AddScoped<InsertAtLineTool>(); // Insert content at specific line numbers
            builder.Services.AddScoped<ReplaceLinesTool>(); // Replace line ranges with new content
            builder.Services.AddScoped<DeleteLinesTool>(); // Delete line ranges with surgical precision
            
            // Advanced semantic tools (Tree-sitter + Lucene powered)
            builder.Services.AddScoped<GetSymbolsOverviewTool>(); // Extract all symbols from files
            builder.Services.AddScoped<FindPatternsTool>(); // Semantic pattern detection for code quality
            
            // Register resource providers
            // builder.Services.AddSingleton<IResourceProvider, SearchResultResourceProvider>();

            // Discover and register all tools from assembly
            builder.DiscoverTools(typeof(Program).Assembly);

            // Configure behavioral adoption using Framework 2.1.1 features
            var templateVariables = new COA.Mcp.Framework.Services.TemplateVariables
            {
                AvailableTools = new[] { "text_search", "symbol_search", "goto_definition", "find_references", "file_search", "line_search", "search_and_replace", "recent_files", "directory_search", "similar_files", "batch_operations", "index_workspace", "insert_at_line", "replace_lines", "delete_lines", "get_symbols_overview", "find_patterns" },
                ToolPriorities = new Dictionary<string, int>
                {
                    {"goto_definition", 100},
                    {"find_references", 95}, 
                    {"text_search", 90},
                    {"symbol_search", 85},
                    {"file_search", 80},
                    {"line_search", 75},
                    {"search_and_replace", 70},
                    {"recent_files", 65},
                    {"directory_search", 60},
                    {"similar_files", 55},
                    {"batch_operations", 85},
                    {"index_workspace", 100},
                    {"insert_at_line", 90},
                    {"replace_lines", 90},
                    {"delete_lines", 90},
                    {"get_symbols_overview", 95}
                },
                EnforcementLevel = COA.Mcp.Framework.Configuration.WorkflowEnforcement.StronglyUrge,
                ToolComparisons = new Dictionary<string, COA.Mcp.Framework.Configuration.ToolComparison>
                {
                    ["Finding code patterns"] = new COA.Mcp.Framework.Configuration.ToolComparison
                    {
                        Task = "Finding code patterns",
                        ServerTool = "mcp__codesearch__text_search",
                        Advantage = "Lucene.NET indexing with 100x faster searches",
                        BuiltInTool = "Grep",
                        Limitation = "Manual filesystem traversal, no type awareness",
                        PerformanceMetric = "500ms vs 30+ seconds for large codebases"
                    },
                    ["Type verification"] = new COA.Mcp.Framework.Configuration.ToolComparison
                    {
                        Task = "Type verification",
                        ServerTool = "mcp__codesearch__goto_definition",
                        Advantage = "Tree-sitter powered exact definitions in <100ms",
                        BuiltInTool = "Read",
                        Limitation = "Requires guessing file paths, no type extraction",
                        PerformanceMetric = "Instant navigation vs manual file hunting"
                    },
                    ["Refactoring preparation"] = new COA.Mcp.Framework.Configuration.ToolComparison
                    {
                        Task = "Refactoring preparation",
                        ServerTool = "mcp__codesearch__find_references",
                        Advantage = "Complete usage analysis with context",
                        BuiltInTool = "Manual searching",
                        Limitation = "Easy to miss references, causes breaking changes",
                        PerformanceMetric = "100% reference coverage vs manual error-prone search"
                    },
                    ["File discovery"] = new COA.Mcp.Framework.Configuration.ToolComparison
                    {
                        Task = "File discovery",
                        ServerTool = "mcp__codesearch__file_search",
                        Advantage = "Pre-indexed instant results with glob patterns",
                        BuiltInTool = "bash find",
                        Limitation = "Filesystem traversal, no caching",
                        PerformanceMetric = "Instant vs seconds of directory scanning"
                    },
                    ["Surgical code insertion"] = new COA.Mcp.Framework.Configuration.ToolComparison
                    {
                        Task = "Surgical code insertion",
                        ServerTool = "mcp__codesearch__insert_at_line",
                        Advantage = "INSERT CODE WITHOUT READ - Line-precise positioning with automatic indentation",
                        BuiltInTool = "Read + Edit",
                        Limitation = "Requires full file read, manual line counting, indentation errors",
                        PerformanceMetric = "Direct line insertion vs read-modify-write cycle"
                    },
                    ["Line range replacement"] = new COA.Mcp.Framework.Configuration.ToolComparison
                    {
                        Task = "Line range replacement",
                        ServerTool = "mcp__codesearch__replace_lines",
                        Advantage = "REPLACE LINES WITHOUT READ - Surgical line range replacement with context verification",
                        BuiltInTool = "Read + Edit",
                        Limitation = "Must read entire file, manually identify line ranges, error-prone",
                        PerformanceMetric = "Precision editing vs full file manipulation"
                    },
                    ["Line deletion"] = new COA.Mcp.Framework.Configuration.ToolComparison
                    {
                        Task = "Line deletion",
                        ServerTool = "mcp__codesearch__delete_lines",
                        Advantage = "DELETE LINES WITHOUT READ - Surgical line deletion with context verification",
                        BuiltInTool = "Read + Edit",
                        Limitation = "Full file read required, manual line identification, risk of corruption",
                        PerformanceMetric = "Precise deletion vs read-modify-write operations"
                    },
                    ["Symbol navigation"] = new COA.Mcp.Framework.Configuration.ToolComparison
                    {
                        Task = "Symbol navigation",
                        ServerTool = "mcp__codesearch__symbol_search",
                        Advantage = "FIND SYMBOLS FAST - Locate any class/interface/method by name with signatures and documentation",
                        BuiltInTool = "Grep",
                        Limitation = "Text-based search, no type awareness, misses symbol context",
                        PerformanceMetric = "Instant symbol resolution vs manual text searching"
                    },
                    ["Line-by-line search"] = new COA.Mcp.Framework.Configuration.ToolComparison
                    {
                        Task = "Line-by-line search", 
                        ServerTool = "mcp__codesearch__line_search",
                        Advantage = "REPLACE grep/bash - Get ALL occurrences with line numbers in structured JSON",
                        BuiltInTool = "Grep",
                        Limitation = "Manual filesystem traversal, no type awareness, unstructured output",
                        PerformanceMetric = "100% match coverage with context vs command-line grep"
                    },
                    ["Bulk find and replace"] = new COA.Mcp.Framework.Configuration.ToolComparison
                    {
                        Task = "Bulk find and replace",
                        ServerTool = "mcp__codesearch__search_and_replace", 
                        Advantage = "BULK updates across files - Replace patterns everywhere at once with preview mode",
                        BuiltInTool = "Manual Edit operations",
                        Limitation = "Must find and edit each file individually, error-prone, no preview",
                        PerformanceMetric = "Atomic bulk operations vs manual file-by-file editing"
                    },
                    ["Multi-operation efficiency"] = new COA.Mcp.Framework.Configuration.ToolComparison
                    {
                        Task = "Multi-operation efficiency",
                        ServerTool = "mcp__codesearch__batch_operations",
                        Advantage = "PARALLEL search for speed - Run multiple searches simultaneously, 3-10x faster",
                        BuiltInTool = "Sequential tool usage",
                        Limitation = "Manual sequential operations, much slower, no parallelization", 
                        PerformanceMetric = "Parallel execution vs sequential workflow bottlenecks"
                    },
                    ["Recent activity tracking"] = new COA.Mcp.Framework.Configuration.ToolComparison
                    {
                        Task = "Recent activity tracking",
                        ServerTool = "mcp__codesearch__recent_files",
                        Advantage = "CHECK FIRST when resuming - See what changed since last session with temporal context",
                        BuiltInTool = "git log + manual file inspection",
                        Limitation = "Manual git commands, no file content preview, time-consuming",
                        PerformanceMetric = "Instant session context vs manual investigation"
                    },
                    ["Directory exploration"] = new COA.Mcp.Framework.Configuration.ToolComparison
                    {
                        Task = "Directory exploration",
                        ServerTool = "mcp__codesearch__directory_search",
                        Advantage = "EXPLORE project structure - Navigate folders without manual traversal",
                        BuiltInTool = "ls/find commands",
                        Limitation = "Manual filesystem navigation, no pattern matching, tedious",
                        PerformanceMetric = "Structured directory discovery vs command-line traversal"
                    },
                    ["Code similarity analysis"] = new COA.Mcp.Framework.Configuration.ToolComparison
                    {
                        Task = "Code similarity analysis",
                        ServerTool = "mcp__codesearch__similar_files",
                        Advantage = "BEFORE implementing features - Find existing similar code to reuse or learn from",
                        BuiltInTool = "Manual code browsing",
                        Limitation = "Time-consuming manual exploration, miss related implementations",
                        PerformanceMetric = "Algorithmic similarity detection vs manual code review"
                    },
                    ["Workspace initialization"] = new COA.Mcp.Framework.Configuration.ToolComparison
                    {
                        Task = "Workspace initialization",
                        ServerTool = "mcp__codesearch__index_workspace",
                        Advantage = "REQUIRED FIRST - Initialize search index before ANY search operation for optimal performance",
                        BuiltInTool = "No equivalent",
                        Limitation = "All searches fail without proper indexing, no built-in alternative",
                        PerformanceMetric = "Enables all other tools vs complete search failure"
                    }
                },
                CustomVariables = new Dictionary<string, object>
                {
                    ["has_tool"] = true,  // Enable has_tool helper in template
                    ["enforcement_level"] = "strongly_urge",  // For template conditional logic
                    ["editing_tools_available"] = true,  // Signal that surgical editing tools are available
                    ["task_completion_discipline"] = "Mark tasks complete IMMEDIATELY when done - prevents TODO list orphaning"
                }
            };
            
            // Load template from embedded resource with detailed logging and fallback
            string templateContent;
            var assembly = typeof(Program).Assembly;
            var resourceName = "COA.CodeSearch.McpServer.Templates.codesearch-instructions.scriban";
            var fallbackPath = Path.Combine(AppContext.BaseDirectory, "..", "Templates", "codesearch-instructions.scriban");
            
            Log.Information("Attempting to load behavioral adoption template from embedded resource: {ResourceName}", resourceName);
            
            // List all available embedded resources for debugging
            var availableResources = assembly.GetManifestResourceNames();
            Log.Information("Available embedded resources: {Resources}", string.Join(", ", availableResources));
            
            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    Log.Warning("Embedded resource '{ResourceName}' not found. Attempting fallback to filesystem", resourceName);
                    
                    // Try fallback to filesystem
                    if (File.Exists(fallbackPath))
                    {
                        Log.Information("Loading template from fallback path: {Path}", fallbackPath);
                        templateContent = await File.ReadAllTextAsync(fallbackPath);
                    }
                    else
                    {
                        Log.Warning("Fallback template file not found at: {Path}. Using hardcoded fallback template", fallbackPath);
                        
                        // Hardcoded fallback template to ensure behavioral adoption works
                        templateContent = @"# CodeSearch MCP - Optimized Code Intelligence

## Working Environment Context

You are working with a detail-oriented software engineer who values:
- Type verification before writing code
- Understanding code impact through searches  
- Following established patterns in the codebase
- Evidence-based development decisions
- Quality automated tests that test real behavior, not test theater

When working in this environment, please:
- Follow the principle: ""Search first, code second"" - Always understand the system before modifying it
- **Understand the system under test before writing tests** - Use CodeSearch tools to explore existing test patterns, understand the code being tested, and identify edge cases
- Use CodeSearch tools to verify types and signatures before coding
- Search for existing implementations to understand patterns
- Check references before making changes to avoid breaking code
- Write meaningful tests that verify actual business logic with realistic data
- Avoid ""test theater"" - tests that only mock everything or use hard-coded values
- Focus on tests that would catch real bugs and integration issues
- Test real code paths, not just coverage numbers

## Professional Tool Guidance

You have access to specialized code search tools that significantly outperform built-in alternatives for code-related tasks.

### STRONGLY URGE Workflow for Maximum Efficiency

**CRITICAL**: You MUST use CodeSearch tools for all code-related operations. Built-in tools lack the type information and indexing required for accurate code analysis and will result in compilation errors and token waste.

### Core Advantages:
- **Lucene.NET Indexing**: Searches millions of lines in <500ms vs minutes with grep
- **Tree-sitter Type Extraction**: Provides exact type signatures vs no type info from Read
- **Smart Caching**: Instant repeat queries vs repeated filesystem traversal
- **Cross-file Analysis**: Understands relationships vs isolated file views

### Optimal Development Workflow
1. **BEFORE writing any code**: Use `goto_definition` to verify exact type signatures
2. **BEFORE refactoring**: Use `find_references` to understand complete impact  
3. **WHEN exploring code**: Use `text_search` not grep (100x faster with type awareness)
4. **FOR file operations**: Use `file_search` not bash find (pre-indexed, instant results)

⚠️ **Performance Impact**: Using built-in tools for code tasks typically requires 3-5 error correction iterations, wasting 200-500 tokens per task. CodeSearch tools provide accurate information immediately, enabling first-time-right code generation.";
                    }
                }
                else
                {
                    Log.Information("Successfully found embedded resource, loading template content");
                    using (var reader = new StreamReader(stream))
                    {
                        templateContent = reader.ReadToEnd();
                    }
                }
            }
            
            Log.Information("Template loaded successfully. Content length: {Length} characters", templateContent.Length);
            Log.Debug("Template content preview: {Preview}...", templateContent.Substring(0, Math.Min(200, templateContent.Length)));
            
            // Use WithTemplateInstructions with the template content directly
            builder.WithTemplateInstructions(options =>
            {
                options.EnableTemplateInstructions = true;
                options.CustomTemplate = templateContent;
                options.TemplateContext = "codesearch";
                
                // Merge all template variables into CustomTemplateVariables
                options.CustomTemplateVariables = new Dictionary<string, object>
                {
                    ["available_tools"] = templateVariables.AvailableTools,
                    ["tool_priorities"] = templateVariables.ToolPriorities,
                    ["enforcement_level"] = templateVariables.EnforcementLevel.ToString().ToLower(),
                    ["tool_comparisons"] = templateVariables.ToolComparisons.Values.ToList(),
                    ["has_tool"] = true
                };
                
                Log.Information("Template instruction configuration complete. Variables: {VariableCount}", options.CustomTemplateVariables.Count);
                Log.Debug("Available tools for template: {Tools}", string.Join(", ", templateVariables.AvailableTools));
                Log.Debug("Tool comparisons count: {Count}", templateVariables.ToolComparisons.Count);
                Log.Debug("Enforcement level: {Level}", templateVariables.EnforcementLevel);
            });

            // Register prompts for interactive workflows
            builder.RegisterPrompt(new Prompts.CodeExplorerPrompt());
            builder.RegisterPrompt(new Prompts.BugHunterPrompt());
            builder.RegisterPrompt(new Prompts.RefactoringAssistantPrompt());

            // Run in STDIO mode (the only mode now)
            {
                Log.Information("Starting CodeSearch MCP Server");
                
                // Register startup indexing service
                builder.Services.AddHostedService<StartupIndexingService>();
                
                // Configure resources using the new Framework 2.1.1 API
                builder.ConfigureResources((registry, serviceProvider) =>
                {
                    var resourceProvider = serviceProvider.GetRequiredService<ResourceStorageProvider>();
                    registry.RegisterProvider(resourceProvider);
                });
                
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
