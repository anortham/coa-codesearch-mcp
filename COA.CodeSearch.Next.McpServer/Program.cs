using COA.Mcp.Framework.Server;
using COA.Mcp.Framework.TokenOptimization;
using COA.Mcp.Framework.TokenOptimization.Caching;
using COA.Mcp.Framework.TokenOptimization.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using COA.CodeSearch.Next.McpServer.Services;
using COA.CodeSearch.Next.McpServer.Tools;
using Serilog;
using System.Reflection;

namespace COA.CodeSearch.Next.McpServer;

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
        services.Configure<COA.CodeSearch.Next.McpServer.Models.MemoryLimitsConfiguration>(
            configuration.GetSection("CodeSearch:MemoryPressure"));
        
        // Register core services
        services.AddSingleton<IPathResolutionService, PathResolutionService>();
        services.AddSingleton<ICircuitBreakerService, CircuitBreakerService>();
        services.AddSingleton<IMemoryPressureService, MemoryPressureService>();
        services.AddSingleton<IQueryCacheService, QueryCacheService>();
        
        // Register Lucene services
        services.AddSingleton<COA.CodeSearch.Next.McpServer.Services.Lucene.ILuceneIndexService, 
                              COA.CodeSearch.Next.McpServer.Services.Lucene.LuceneIndexService>();
        
        // Register indexing services
        services.AddSingleton<IIndexingMetricsService, IndexingMetricsService>();
        services.AddSingleton<IBatchIndexingService, BatchIndexingService>();
        services.AddSingleton<IFileIndexingService, FileIndexingService>();
        
        // Register support services
        services.AddSingleton<IFieldSelectorService, FieldSelectorService>();
        services.AddSingleton<IErrorRecoveryService, ErrorRecoveryService>();
        
        // FileWatcher as background service
        services.AddSingleton<FileWatcherService>();
        services.AddHostedService<FileWatcherService>(provider => provider.GetRequiredService<FileWatcherService>());
        
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
        services.AddLogging(logging => logging.AddSerilog());
        services.AddSingleton<IWriteLockManager, WriteLockManager>();
        
        var serviceProvider = services.BuildServiceProvider();
        var lockManager = serviceProvider.GetRequiredService<IWriteLockManager>();
        var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
        
        logger.LogInformation("Running startup write.lock cleanup");
        
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
    /// </summary>
    private static void ConfigureSerilog(IConfiguration configuration)
    {
        // Create a temporary path service to get the logs directory - exactly like ProjectKnowledge
        var tempPathService = new PathResolutionService(configuration);
        var logsPath = tempPathService.GetLogsPath();
        tempPathService.EnsureDirectoryExists(logsPath);
        
        var logFile = Path.Combine(logsPath, "codesearch-.log");

        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .WriteTo.File(
                logFile,
                rollingInterval: RollingInterval.Day,
                rollOnFileSizeLimit: true,
                fileSizeLimitBytes: 10 * 1024 * 1024, // 10MB
                retainedFileCountLimit: 7, // Keep 7 days of logs
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}"
            )
            .CreateLogger();
    }

    public static async Task Main(string[] args)
    {
        // Determine mode from args early
        bool isHttpMode = args.Contains("--mode") && args.Contains("http");
        bool isServiceMode = args.Contains("--service") || isHttpMode;

        // Load configuration early for logging setup
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddEnvironmentVariables()
            .Build();

        // Configure Serilog early - FILE ONLY (no console to avoid breaking STDIO)
        ConfigureSerilog(configuration);

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
            
            // Register tools in DI first (required for constructor dependencies)
            // Search tools
            builder.Services.AddScoped<IndexWorkspaceTool>();
            builder.Services.AddScoped<TextSearchTool>(); // Uses BaseResponseBuilder pattern
            builder.Services.AddScoped<FileSearchTool>();
            builder.Services.AddScoped<RecentFilesTool>(); // New! Framework 1.5.2 implementation
            builder.Services.AddScoped<DirectorySearchTool>(); // New! Directory search implementation
            builder.Services.AddScoped<SimilarFilesTool>(); // New! Find similar files using MoreLikeThis
            
            // Register resource providers
            // builder.Services.AddSingleton<IResourceProvider, SearchResultResourceProvider>();

            // Discover and register all tools from assembly
            builder.DiscoverTools(typeof(Program).Assembly);

            // Register prompts for interactive workflows
            builder.RegisterPrompt(new Prompts.CodeExplorerPrompt());
            builder.RegisterPrompt(new Prompts.BugHunterPrompt());
            builder.RegisterPrompt(new Prompts.RefactoringAssistantPrompt());

            // Check if running as HTTP service
            if (isServiceMode)
            {
                Log.Information("Starting CodeSearch in HTTP service mode");
                
                // Configure HTTP mode with ASP.NET Core
                var webBuilder = WebApplication.CreateBuilder(args);
                
                // Add services from MCP builder
                foreach (var service in builder.Services)
                {
                    webBuilder.Services.Add(service);
                }
                
                // Add ASP.NET Core services
                webBuilder.Services.AddControllers();
                webBuilder.Services.AddEndpointsApiExplorer();
                
                // Configure Kestrel
                webBuilder.WebHost.ConfigureKestrel(options =>
                {
                    var port = configuration.GetValue<int>("CodeSearch:HttpPort", 5020);
                    options.ListenLocalhost(port);
                });
                
                var app = webBuilder.Build();
                
                // Configure pipeline
                app.UseRouting();
                app.MapControllers();
                
                // Health check endpoint
                app.MapGet("/health", () => Results.Ok(new 
                { 
                    Status = "Healthy", 
                    Service = "CodeSearch",
                    Version = "2.0.0",
                    Mode = "HTTP"
                }));
                
                await app.RunAsync();
            }
            else
            {
                // Run in STDIO mode (default for Claude Code)
                Log.Information("Starting CodeSearch in STDIO mode");
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
