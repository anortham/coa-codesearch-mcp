using COA.Mcp.Framework.Server;
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
        
        // Resource providers
        // services.AddSingleton<SearchResultResourceProvider>();
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

            // Register tools in DI first (required for constructor dependencies)
            // Search tools - commented out until properly implemented
            builder.Services.AddScoped<IndexWorkspaceTool>();
            // builder.Services.AddScoped<TextSearchTool>();
            // builder.Services.AddScoped<FileSearchTool>();
            // builder.Services.AddScoped<DirectorySearchTool>();
            // builder.Services.AddScoped<RecentFilesTool>();
            // builder.Services.AddScoped<SimilarFilesTool>();
            
            // Utility tools
            builder.Services.AddScoped<HelloWorldTool>();
            builder.Services.AddScoped<SystemInfoTool>();

            // Discover and register all tools from assembly
            builder.DiscoverTools(typeof(Program).Assembly);


            // Register prompts if needed
            // builder.RegisterPromptType<CodeExplorerPrompt>();
            // builder.RegisterPromptType<BugFinderPrompt>();

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
