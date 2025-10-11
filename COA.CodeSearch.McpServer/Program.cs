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
using COA.CodeSearch.McpServer.Services.Julie;
using COA.CodeSearch.McpServer.Services.Lucene;
using COA.CodeSearch.McpServer.Services.Sqlite;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Tools;
using Lucene.Net.Util;
using Serilog;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

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
        services.AddSingleton<IFileIndexingService>(sp => new FileIndexingService(
            sp.GetRequiredService<ILogger<FileIndexingService>>(),
            sp.GetRequiredService<IConfiguration>(),
            sp.GetRequiredService<ILuceneIndexService>(),
            sp.GetRequiredService<IPathResolutionService>(),
            sp.GetRequiredService<IIndexingMetricsService>(),
            sp.GetRequiredService<ICircuitBreakerService>(),
            sp.GetRequiredService<IMemoryPressureService>(),
            sp.GetRequiredService<IOptions<MemoryLimitsConfiguration>>(),
            sp.GetRequiredService<IJulieCodeSearchService>(),     // Pass julie-codesearch service
            sp.GetRequiredService<ISQLiteSymbolService>(),         // Pass SQLite service
            sp.GetRequiredService<ISemanticIntelligenceService>() // Pass semantic service
        ));
        
        // Register support services
        services.AddSingleton<IFieldSelectorService, FieldSelectorService>();
        services.AddSingleton<IErrorRecoveryService, ErrorRecoveryService>();
        services.AddSingleton<SmartSnippetService>();
                        services.AddSingleton<AdvancedPatternMatcher>();
                services.AddSingleton<SmartQueryPreprocessor>();
        
        // Register modern file editing services
        services.AddScoped<UnifiedFileEditService>();
        services.AddSingleton<IWorkspacePermissionService, WorkspacePermissionService>();
        
        // API services for HTTP mode
        services.AddSingleton<ConfidenceCalculatorService>();
        
        // Simplified line-aware search service
        services.AddSingleton<LineAwareSearchService>();
        
        // Query preprocessing for code-aware search
        services.AddSingleton<QueryPreprocessor>();
        
        // Code analysis services  
        services.AddSingleton<COA.CodeSearch.McpServer.Services.Analysis.CodeAnalyzer>(provider =>
            new COA.CodeSearch.McpServer.Services.Analysis.CodeAnalyzer(LuceneVersion.LUCENE_48, 
                preserveCase: false, splitCamelCase: true));
        
        // Julie integration services for tree-sitter extraction and semantic search
        // Julie CodeSearch CLI service for SQLite-based indexing (scan + update commands)
        services.AddSingleton<COA.CodeSearch.McpServer.Services.Julie.IJulieCodeSearchService,
                              COA.CodeSearch.McpServer.Services.Julie.JulieCodeSearchService>();

        services.AddSingleton<COA.CodeSearch.McpServer.Services.Julie.ISemanticIntelligenceService,
                              COA.CodeSearch.McpServer.Services.Julie.SemanticIntelligenceService>();

        // Embedding service for generating vectors (replaces julie-semantic CLI)
        services.AddSingleton<COA.CodeSearch.McpServer.Services.Embeddings.IEmbeddingService,
                              COA.CodeSearch.McpServer.Services.Embeddings.EmbeddingService>();

        // SQLite symbol storage (canonical database for symbols)
        services.AddSingleton<COA.CodeSearch.McpServer.Services.Sqlite.ISQLiteSymbolService,
                              COA.CodeSearch.McpServer.Services.Sqlite.SQLiteSymbolService>();

        // SQLite vec extension for semantic vector search
        services.AddSingleton<COA.CodeSearch.McpServer.Services.Sqlite.ISqliteVecExtensionService,
                              COA.CodeSearch.McpServer.Services.Sqlite.SqliteVecExtensionService>();

        // Reference resolution (on-demand identifier → symbol resolution for find_references)
        services.AddSingleton<COA.CodeSearch.McpServer.Services.IReferenceResolverService,
                              COA.CodeSearch.McpServer.Services.ReferenceResolverService>();

        // Call path tracing (recursive call hierarchy analysis)
        services.AddSingleton<COA.CodeSearch.McpServer.Services.ICallPathTracerService,
                              COA.CodeSearch.McpServer.Services.CallPathTracerService>();

        // Documentation services (ProjectKnowledge integration removed)
        services.AddSingleton<SmartDocumentationService>();
        
        // FileWatcher as background service - register properly for auto-start
        services.AddSingleton<FileWatcherService>();
        services.AddHostedService(provider => provider.GetRequiredService<FileWatcherService>());
        
        // Write lock management
        services.AddSingleton<IWriteLockManager, WriteLockManager>();

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

        // Ensure .gitignore exists in .coa/codesearch to prevent committing indexes
        tempPathService.EnsureGitIgnoreExists();

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
                .WithServerInfo("CodeSearch", "2.1.8")
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddSerilog(); // Use Serilog for all logging
                })
                // Framework 2.1.12+ features: Opt-in production features that were previously default-enabled
                .EnableResourceCaching() // Important for ResourceStorageProvider functionality
                .WithAdvancedErrorRecovery(); // Production-grade error handling and recovery

            // Configure shared services
            ConfigureSharedServices(builder.Services, configuration);
            
            // Token optimization is active via response builders and DI services above

            // Register response builders
            builder.Services.AddScoped<ResponseBuilders.SearchResponseBuilder>();
            builder.Services.AddScoped<ResponseBuilders.FileSearchResponseBuilder>();
            builder.Services.AddScoped<ResponseBuilders.IndexResponseBuilder>();
            builder.Services.AddScoped<ResponseBuilders.RecentFilesResponseBuilder>();
            builder.Services.AddScoped<ResponseBuilders.DirectorySearchResponseBuilder>();
            builder.Services.AddScoped<ResponseBuilders.LineSearchResponseBuilder>();
            builder.Services.AddScoped<ResponseBuilders.SearchAndReplaceResponseBuilder>();
            
            // Register resource provider for serving ResourceStorage data
            builder.Services.AddScoped<Providers.ResourceStorageProvider>();
            
            // Register tools in DI first (required for constructor dependencies)
            // Search tools
            builder.Services.AddScoped<IndexWorkspaceTool>();
            builder.Services.AddScoped<TextSearchTool>(); // Uses BaseResponseBuilder pattern
            builder.Services.AddScoped<SearchFilesTool>(); // Unified file/directory search
            builder.Services.AddScoped<RecentFilesTool>(); // New! Framework 1.5.2 implementation
            builder.Services.AddScoped<LineSearchTool>(); // New! Grep-like line-level search
            builder.Services.AddScoped<SearchAndReplaceTool>(); // Enhanced! Uses DiffMatchPatch and workspace permissions

            // Navigation tools (from CodeNav consolidation)
            // TODO: All tools should follow the response builder pattern for consistency
            // These tools use dedicated response builders for token optimization and consistent behavior
            builder.Services.AddScoped<SymbolSearchTool>(); // Find symbol definitions using Tree-sitter data
            builder.Services.AddScoped<FindReferencesTool>(); // Find all usages of a symbol
            builder.Services.AddScoped<TraceCallPathTool>(); // Hierarchical call chain analysis
            builder.Services.AddScoped<GoToDefinitionTool>(); // Jump to symbol definition

            // Editing tools (NEW - Enable dogfooding!)
            builder.Services.AddScoped<EditLinesTool>(); // Unified line editing (insert/replace/delete)

            // Refactoring tools (AST-aware semantic refactoring)
            builder.Services.AddScoped<SmartRefactorTool>(); // Smart refactoring with symbol-aware transformations

            // Advanced semantic tools (Tree-sitter + Lucene powered)
            builder.Services.AddScoped<GetSymbolsOverviewTool>(); // Extract all symbols from files
            builder.Services.AddScoped<ReadSymbolsTool>(); // Read specific symbol implementations (token-efficient)
            builder.Services.AddScoped<FindPatternsTool>(); // Semantic pattern detection for code quality
            
            // Register resource providers
            // builder.Services.AddSingleton<IResourceProvider, SearchResultResourceProvider>();

            // Discover and register all tools from assembly
            builder.DiscoverTools(typeof(Program).Assembly);

            // Configure behavioral adoption using Framework 2.1.1 features
            var templateVariables = new COA.Mcp.Framework.Services.TemplateVariables
            {
                AvailableTools = new[] { "text_search", "symbol_search", "goto_definition", "find_references", "trace_call_path", "search_files", "line_search", "search_and_replace", "recent_files", "index_workspace", "edit_lines", "smart_refactor", "get_symbols_overview", "read_symbols", "find_patterns" },
                ToolPriorities = new Dictionary<string, int>
                {
                    {"goto_definition", 100},
                    {"find_references", 95},
                    {"trace_call_path", 92},
                    {"text_search", 90},
                    {"symbol_search", 85},
                    {"search_files", 82},
                    {"line_search", 75},
                    {"search_and_replace", 70},
                    {"recent_files", 65},
                    {"index_workspace", 100},
                    {"edit_lines", 92},
                    {"smart_refactor", 95},
                    {"get_symbols_overview", 95},
                    {"read_symbols", 92}
                },
                EnforcementLevel = COA.Mcp.Framework.Configuration.WorkflowEnforcement.StronglyUrge,
                ToolComparisons = new Dictionary<string, COA.Mcp.Framework.Configuration.ToolComparison>
                {
                    ["Finding code patterns"] = new COA.Mcp.Framework.Configuration.ToolComparison
                    {
                        Task = "Finding code patterns",
                        ServerTool = "mcp__codesearch__text_search",
                        Advantage = "Lucene.NET indexing with CamelCase tokenization finds 'UserService' when searching 'user service'",
                        BuiltInTool = "Grep",
                        Limitation = "Case-sensitive literal matching misses variations, no semantic understanding",
                        PerformanceMetric = "500ms vs 30+ seconds, finds 3x more relevant results"
                    },
                    ["Professional workflow initiation"] = new COA.Mcp.Framework.Configuration.ToolComparison
                    {
                        Task = "Starting development tasks",
                        ServerTool = "mcp__goldfish__todo + mcp__codesearch__text_search",
                        Advantage = "Organized task tracking with type verification prevents forgotten requirements",
                        BuiltInTool = "Starting to code immediately",
                        Limitation = "Ad-hoc approach leads to missed dependencies, breaking changes, rework",
                        PerformanceMetric = "2x fewer errors, 50% less rework through systematic planning"
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
                        ServerTool = "mcp__goldfish__checkpoint + mcp__codesearch__find_references",
                        Advantage = "Complete usage analysis with rollback capability - find ALL 47 references before changing signature",
                        BuiltInTool = "Manual searching + hoping for the best",
                        Limitation = "Easy to miss references in comments, tests, configs → runtime failures",
                        PerformanceMetric = "100% reference coverage + safe rollback vs 60% coverage with potential disasters"
                    },
                    ["Unified line editing"] = new COA.Mcp.Framework.Configuration.ToolComparison
                    {
                        Task = "Unified line editing",
                        ServerTool = "mcp__codesearch__edit_lines",
                        Advantage = "One tool for all line operations - insert, replace, delete with minimal parameters (3-4 vs 6-8)",
                        BuiltInTool = "Multiple tool calls",
                        Limitation = "Must remember which specific editing tool to use, more parameters to specify",
                        PerformanceMetric = "Simpler API: edit_lines(file, operation, line, content) vs separate tools"
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
                    ["Recent activity tracking"] = new COA.Mcp.Framework.Configuration.ToolComparison
                    {
                        Task = "Recent activity tracking",
                        ServerTool = "mcp__codesearch__recent_files",
                        Advantage = "CHECK FIRST when resuming - See what changed since last session with temporal context",
                        BuiltInTool = "git log + manual file inspection",
                        Limitation = "Manual git commands, no file content preview, time-consuming",
                        PerformanceMetric = "Instant session context vs manual investigation"
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
            
            // BEHAVIORAL ADOPTION PIPELINE: Stage 1 - Template Loading
            Log.Information("[BEHAVIORAL-ADOPTION] Stage 1: Template Loading - Attempting to load from embedded resource: {ResourceName}", resourceName);
            
            // List all available embedded resources for debugging
            var availableResources = assembly.GetManifestResourceNames();
            Log.Information("[BEHAVIORAL-ADOPTION] Available embedded resources: {Resources}", string.Join(", ", availableResources));
            
            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    Log.Warning("[BEHAVIORAL-ADOPTION] Embedded resource '{ResourceName}' not found. Attempting fallback to filesystem", resourceName);
                    
                    // Try fallback to filesystem
                    if (File.Exists(fallbackPath))
                    {
                        Log.Information("[BEHAVIORAL-ADOPTION] Loading template from fallback path: {Path}", fallbackPath);
                        templateContent = await File.ReadAllTextAsync(fallbackPath);
                    }
                    else
                    {
                        Log.Warning("[BEHAVIORAL-ADOPTION] Fallback template file not found at: {Path}. Using hardcoded fallback template", fallbackPath);
                        
                        // Hardcoded fallback template to ensure behavioral adoption works
                        templateContent = @"# Welcome to CodeSearch - Your Development Superpowers! 🚀

## The Joy of Confident Development

You have access to CodeSearch's powerful tools that transform coding into a satisfying, precise craft. These tools bring the confidence that comes from understanding code before changing it.

## What Makes Development Exciting

**The thrill of test-driven bug hunting:**
When you find a bug, you get to:
1. **Capture it first** - Write a failing test that reproduces the issue
2. **Fix it with confidence** - Your test guides you to the solution
3. **Celebrate success** - Watch that test turn green!

This approach is deeply satisfying - you've not just fixed a bug, you've built permanent protection against its return.

**The satisfaction of real testing:**
- Tests with realistic data that mirror production
- Integration tests that verify actual behavior
- Edge cases that catch bugs before users do
- Quality tests that give you confidence to refactor fearlessly

## Your CodeSearch Advantages

**Experience the satisfaction of:**
- ⚡ **Lightning-fast discovery** - `text_search` finds patterns instantly across millions of lines
- 🎯 **Surgical precision** - Make exact changes without file corruption
- 🔍 **Complete understanding** - `find_references` shows every impact before you change code
- 🚀 **First-time success** - `goto_definition` eliminates type guesswork

## The Winning Workflow

**This sequence feels smooth and creates flow state:**
1. **Discover** - Use `text_search` for instant pattern understanding
2. **Verify** - `goto_definition` gives you that ""aha!"" moment of type clarity
3. **Analyze** - `find_references` prevents those ""oh no"" moments
4. **Create** - Write code knowing it will work perfectly
5. **Test** - Build confidence through meaningful tests

**The best code comes from understanding, not guessing. CodeSearch gives you that understanding instantly, making development both successful and enjoyable.**";
                    }
                }
                else
                {
                    Log.Information("[BEHAVIORAL-ADOPTION] Successfully found embedded resource, loading template content");
                    using (var reader = new StreamReader(stream))
                    {
                        templateContent = reader.ReadToEnd();
                    }
                }
            }
            
            // BEHAVIORAL ADOPTION PIPELINE: Stage 2 - Template Content Verification
            var templateHash = templateContent.GetHashCode().ToString("X8");
            Log.Information("[BEHAVIORAL-ADOPTION] Stage 2: Template Content Loaded - Length: {Length} characters, Hash: {Hash}", templateContent.Length, templateHash);
            Log.Debug("[BEHAVIORAL-ADOPTION] Template content preview: {Preview}...", templateContent.Substring(0, Math.Min(200, templateContent.Length)));
            
            // BEHAVIORAL ADOPTION PIPELINE: Stage 3 - Variable Preparation
            Log.Information("[BEHAVIORAL-ADOPTION] Stage 3: Variable Preparation - Preparing {ToolCount} tools, {ComparisonCount} comparisons", 
                templateVariables.AvailableTools.Length, templateVariables.ToolComparisons.Count);
            
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
                
                // BEHAVIORAL ADOPTION PIPELINE: Stage 4 - Template Configuration
                Log.Information("[BEHAVIORAL-ADOPTION] Stage 4: Template Configuration Complete - Variables: {VariableCount}", options.CustomTemplateVariables.Count);
                Log.Information("[BEHAVIORAL-ADOPTION] Template variables configured:");
                Log.Information("[BEHAVIORAL-ADOPTION] - Available tools: {Tools}", string.Join(", ", templateVariables.AvailableTools));
                Log.Information("[BEHAVIORAL-ADOPTION] - Tool comparisons: {Count} comparisons", templateVariables.ToolComparisons.Count);
                Log.Information("[BEHAVIORAL-ADOPTION] - Enforcement level: {Level}", templateVariables.EnforcementLevel);
                Log.Information("[BEHAVIORAL-ADOPTION] - Template context: {Context}", options.TemplateContext);
                Log.Information("[BEHAVIORAL-ADOPTION] - Template instructions enabled: {Enabled}", options.EnableTemplateInstructions);
            })
            // BEHAVIORAL ADOPTION PIPELINE: Stage 5 - Tool Management Configuration
            .ConfigureToolManagement(config =>
            {
                Log.Information("[BEHAVIORAL-ADOPTION] Stage 5: Configuring Tool Management System");
                
                // Keep the defaults set by WithTemplateInstructions but add our customizations
                config.UseDefaultDescriptionProvider = true;
                config.EnableWorkflowSuggestions = true;
                config.EnableToolPrioritySystem = true;
                
                // Additional customizations for CodeSearch-specific needs
                config.IncludeAlternativeToolSuggestions = true; // Educational context about different approaches
                config.EmphasizeHighImpactWorkflows = true; // Focus on most beneficial guidance
                config.IncludeExpectedBenefits = true; // Evidence-based guidance with measurable justification
                config.MaxWorkflowSuggestionsInInstructions = 5; // Focused guidance without overwhelming
                
                Log.Information("[BEHAVIORAL-ADOPTION] Tool management configured:");
                Log.Information("[BEHAVIORAL-ADOPTION] - Workflow suggestions: {Enabled}", config.EnableWorkflowSuggestions);
                Log.Information("[BEHAVIORAL-ADOPTION] - Tool priority system: {Enabled}", config.EnableToolPrioritySystem);
                Log.Information("[BEHAVIORAL-ADOPTION] - Alternative suggestions: {Enabled}", config.IncludeAlternativeToolSuggestions);
                Log.Information("[BEHAVIORAL-ADOPTION] - High impact workflows: {Enabled}", config.EmphasizeHighImpactWorkflows);
            });

            // Register prompts for interactive workflows
            builder.RegisterPrompt(new Prompts.CodeExplorerPrompt());
            builder.RegisterPrompt(new Prompts.BugHunterPrompt());
            builder.RegisterPrompt(new Prompts.RefactoringAssistantPrompt());

            // Run in STDIO mode (the only mode now)
            {
                // BEHAVIORAL ADOPTION PIPELINE: Stage 6 - Server Startup
                Log.Information("[BEHAVIORAL-ADOPTION] Stage 6: Starting CodeSearch MCP Server with Enhanced Behavioral Adoption");
                Log.Information("[BEHAVIORAL-ADOPTION] Template hash: {Hash} | Enforcement: {Level} | Tools: {Count}", 
                    templateHash, templateVariables.EnforcementLevel, templateVariables.AvailableTools.Length);
                
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
                
                // BEHAVIORAL ADOPTION PIPELINE: Stage 7 - Final Preparation
                Log.Information("[BEHAVIORAL-ADOPTION] Stage 7: STDIO Transport Configured - Ready to deliver behavioral adoption instructions");
                Log.Information("[BEHAVIORAL-ADOPTION] Pipeline Complete: Template loaded → Variables prepared → Instructions configured → Server ready");
                Log.Information("[BEHAVIORAL-ADOPTION] Expected behavior: Claude should prefer CodeSearch tools and follow architect/implementer workflow");
                
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