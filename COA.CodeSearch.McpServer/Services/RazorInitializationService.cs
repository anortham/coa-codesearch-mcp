using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Ensures Razor services are initialized on startup to prevent delays during tool execution
/// </summary>
public class RazorInitializationService : IHostedService
{
    private readonly ILogger<RazorInitializationService> _logger;
    private readonly IRazorAnalysisService _razorService;
    private readonly IConfiguration _configuration;

    public RazorInitializationService(
        ILogger<RazorInitializationService> logger,
        IRazorAnalysisService razorService,
        IConfiguration configuration)
    {
        _logger = logger;
        _razorService = razorService;
        _configuration = configuration;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Checking if Razor initialization is needed...");
            
            // Check configuration first
            var skipRazor = _configuration.GetValue<bool>("Razor:SkipInitialization");
            if (skipRazor)
            {
                _logger.LogInformation("Razor initialization skipped by configuration");
                return;
            }
            
            // Check if any Razor files exist in common locations
            var hasRazorFiles = await CheckForRazorFilesAsync(cancellationToken);
            
            if (!hasRazorFiles)
            {
                _logger.LogInformation("No Razor (.razor) files detected - skipping Razor initialization");
                return;
            }
            
            _logger.LogInformation("Razor files detected - initializing Razor services...");
            
            try
            {
                // Add a timeout to prevent hanging during initialization
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(15)); // 15 second timeout
                
                var success = await _razorService.InitializeAsync(cts.Token);
                
                if (success)
                {
                    _logger.LogInformation("Razor services initialized successfully");
                }
                else
                {
                    _logger.LogWarning("Razor services initialization failed - Razor features will be unavailable");
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Razor services initialization timed out - falling back to embedded mode");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing Razor services");
            }
        }
        catch (Exception ex)
        {
            // Never let this hosted service crash the entire application
            _logger.LogError(ex, "Critical error in Razor initialization service - continuing without Razor");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
    
    private Task<bool> CheckForRazorFilesAsync(CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            try
            {
                // Quick check in current directory and common subdirectories
                var searchPaths = new[]
                {
                    Directory.GetCurrentDirectory(),
                    Path.Combine(Directory.GetCurrentDirectory(), "Views"),
                    Path.Combine(Directory.GetCurrentDirectory(), "Pages"),
                    Path.Combine(Directory.GetCurrentDirectory(), "Components"),
                    Path.Combine(Directory.GetCurrentDirectory(), "Shared"),
                    Path.Combine(Directory.GetCurrentDirectory(), "Areas"),
                    Path.Combine(Directory.GetCurrentDirectory(), "Client"),
                    Path.Combine(Directory.GetCurrentDirectory(), "Server")
                };
                
                foreach (var searchPath in searchPaths)
                {
                    if (!Directory.Exists(searchPath))
                        continue;
                        
                    // Just check if any .razor files exist - don't enumerate all
                    var files = Directory.EnumerateFiles(searchPath, "*.razor", SearchOption.AllDirectories)
                        .Take(1); // Stop after finding first file
                        
                    if (files.Any())
                    {
                        _logger.LogInformation("Found Razor files in {Path}", searchPath);
                        return true;
                    }
                    
                    // Check for cancellation periodically
                    if (cancellationToken.IsCancellationRequested)
                        return false;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error checking for Razor files, assuming they don't exist");
                return false; // Assume Razor doesn't exist if we can't check
            }
        }, cancellationToken);
    }
}