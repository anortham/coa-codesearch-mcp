using COA.CodeSearch.McpServer.Services;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace COA.CodeSearch.McpServer.Tools;

public class TypeScriptHoverInfoTool
{
    private readonly ILogger<TypeScriptHoverInfoTool> _logger;
    private readonly ITypeScriptAnalysisService _tsService;

    public TypeScriptHoverInfoTool(
        ILogger<TypeScriptHoverInfoTool> logger,
        ITypeScriptAnalysisService tsService)
    {
        _logger = logger;
        _tsService = tsService;
    }

    public async Task<object> ExecuteAsync(
        string filePath,
        int line,
        int column,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("TypeScript GetHoverInfo request for {FilePath} at {Line}:{Column}", filePath, line, column);

            if (!_tsService.IsAvailable)
            {
                _logger.LogWarning("TypeScript server is not available for hover info");
                return new
                {
                    success = false,
                    error = "TypeScript server is not available. Please ensure Node.js is installed and in PATH."
                };
            }

            // Get quick info from TypeScript
            var quickInfo = await _tsService.GetQuickInfoAsync(filePath, line, column, cancellationToken);
            
            if (quickInfo == null)
            {
                return new
                {
                    success = false,
                    error = "No hover information available at the specified position"
                };
            }

            return new
            {
                success = true,
                hoverInfo = quickInfo
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TypeScript GetHoverInfo");
            return new
            {
                success = false,
                error = ex.Message
            };
        }
    }
}