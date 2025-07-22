using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Ensures TypeScript services are initialized on startup
/// </summary>
public class TypeScriptInitializationService : IHostedService
{
    private readonly ILogger<TypeScriptInitializationService> _logger;
    private readonly TypeScriptAnalysisService _typeScriptService;
    private readonly IConfiguration _configuration;

    public TypeScriptInitializationService(
        ILogger<TypeScriptInitializationService> logger,
        TypeScriptAnalysisService typeScriptService,
        IConfiguration configuration)
    {
        _logger = logger;
        _typeScriptService = typeScriptService;
        _configuration = configuration;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Checking if TypeScript initialization is needed...");
        
        // Check configuration first
        var skipTypeScript = _configuration.GetValue<bool>("TypeScript:SkipInitialization");
        if (skipTypeScript)
        {
            _logger.LogInformation("TypeScript initialization skipped by configuration");
            return;
        }
        
        // Check if any TypeScript/JavaScript files exist in common locations
        var hasTypeScriptFiles = await CheckForTypeScriptFilesAsync(cancellationToken);
        
        if (!hasTypeScriptFiles)
        {
            _logger.LogInformation("No TypeScript/JavaScript files detected - skipping TypeScript initialization");
            return;
        }
        
        _logger.LogInformation("TypeScript/JavaScript files detected - initializing TypeScript services...");
        
        try
        {
            var success = await _typeScriptService.InitializeAsync();
            
            if (success)
            {
                _logger.LogInformation("TypeScript services initialized successfully");
            }
            else
            {
                _logger.LogWarning("TypeScript services initialization failed - TypeScript features will be unavailable");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing TypeScript services");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
    
    private Task<bool> CheckForTypeScriptFilesAsync(CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            try
            {
            // Quick check in current directory and common subdirectories
            var searchPaths = new[]
            {
                Directory.GetCurrentDirectory(),
                Path.Combine(Directory.GetCurrentDirectory(), "src"),
                Path.Combine(Directory.GetCurrentDirectory(), "client"),
                Path.Combine(Directory.GetCurrentDirectory(), "frontend"),
                Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"),
                Path.Combine(Directory.GetCurrentDirectory(), "ClientApp"),
                Path.Combine(Directory.GetCurrentDirectory(), "app")
            };
            
            var extensions = new[] { "*.ts", "*.tsx", "*.js", "*.jsx" };
            
            foreach (var searchPath in searchPaths)
            {
                if (!Directory.Exists(searchPath))
                    continue;
                    
                foreach (var extension in extensions)
                {
                    // Just check if any files exist - don't enumerate all
                    var files = Directory.EnumerateFiles(searchPath, extension, SearchOption.AllDirectories)
                        .Take(1); // Stop after finding first file
                        
                    if (files.Any())
                    {
                        _logger.LogInformation("Found TypeScript/JavaScript files in {Path}", searchPath);
                        return true;
                    }
                    
                    // Check for cancellation periodically
                    if (cancellationToken.IsCancellationRequested)
                        return false;
                }
            }
            
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking for TypeScript files, assuming they exist");
            return true; // Assume TypeScript exists if we can't check
        }
        }, cancellationToken);
    }
}