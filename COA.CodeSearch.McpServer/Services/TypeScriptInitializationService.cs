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

    public TypeScriptInitializationService(
        ILogger<TypeScriptInitializationService> logger,
        TypeScriptAnalysisService typeScriptService)
    {
        _logger = logger;
        _typeScriptService = typeScriptService;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initializing TypeScript services...");
        
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
}