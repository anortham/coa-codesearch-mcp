using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Get document symbols (outline) for Blazor (.razor) files using Razor Language Server
/// </summary>
public class BlazorGetDocumentSymbolsTool : ITool
{
    public string ToolName => "blazor_get_document_symbols";
    public string Description => "Get document outline/symbols for Blazor (.razor) files - shows component structure, methods, properties";
    public ToolCategory Category => ToolCategory.Analysis;
    
    private readonly ILogger<BlazorGetDocumentSymbolsTool> _logger;
    private readonly IRazorAnalysisService _razorAnalysisService;

    public BlazorGetDocumentSymbolsTool(
        ILogger<BlazorGetDocumentSymbolsTool> logger,
        IRazorAnalysisService razorAnalysisService)
    {
        _logger = logger;
        _razorAnalysisService = razorAnalysisService;
    }

    /// <summary>
    /// Gets document symbols for a Blazor file
    /// </summary>
    public async Task<object> ExecuteAsync(
        string filePath,
        bool includeMembers = true,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Blazor GetDocumentSymbols request for {FilePath}", filePath);

            // Validate file path
            if (string.IsNullOrEmpty(filePath))
            {
                return CreateErrorResponse("File path is required");
            }

            // Check if this is a Razor file
            if (!IsRazorFile(filePath))
            {
                return CreateErrorResponse($"File {filePath} is not a Blazor (.razor) file");
            }

            // Check if file exists
            if (!File.Exists(filePath))
            {
                return CreateErrorResponse($"File not found: {filePath}");
            }

            // Check if Razor analysis service is available
            if (!_razorAnalysisService.IsAvailable)
            {
                // Try to initialize the service
                var initialized = await _razorAnalysisService.InitializeAsync(cancellationToken);
                if (!initialized)
                {
                    return CreateErrorResponse("Razor Language Server is not available. Please install VS Code with the C# extension.");
                }
            }

            // Get document symbols from Razor analysis service
            var symbols = await _razorAnalysisService.GetDocumentSymbolsAsync(filePath, cancellationToken);
            
            if (symbols == null || symbols.Length == 0)
            {
                return new
                {
                    success = true,
                    symbols = Array.Empty<object>(),
                    count = 0,
                    filePath,
                    message = "No symbols found in the document",
                    suggestions = new[]
                    {
                        "Ensure the Blazor component has @code blocks with methods, properties, or fields",
                        "Check that the file contains valid C# code within the component",
                        "Empty or markup-only components may not have discoverable symbols"
                    },
                    metadata = new
                    {
                        tool = "blazor_get_document_symbols",
                        languageServer = "rzls",
                        includeMembers,
                        timestamp = DateTime.UtcNow
                    }
                };
            }

            // Process and format the symbols
            var formattedSymbols = ProcessDocumentSymbols(symbols, includeMembers);

            var result = new
            {
                success = true,
                symbols = formattedSymbols.symbols,
                count = formattedSymbols.count,
                filePath,
                structure = formattedSymbols.structure,
                settings = new
                {
                    includeMembers
                },
                raw = symbols, // Include raw LSP response for debugging
                metadata = new
                {
                    tool = "blazor_get_document_symbols",
                    languageServer = "rzls",
                    includeMembers,
                    timestamp = DateTime.UtcNow
                }
            };

            _logger.LogInformation("Successfully retrieved {Count} symbols for {FilePath}", 
                formattedSymbols.count, filePath);

            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Blazor GetDocumentSymbols operation was cancelled");
            return CreateErrorResponse("Operation was cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Blazor GetDocumentSymbols for {FilePath}", filePath);
            return CreateErrorResponse($"Error getting document symbols: {ex.Message}");
        }
    }

    private bool IsRazorFile(string filePath)
    {
        return Path.GetExtension(filePath).Equals(".razor", StringComparison.OrdinalIgnoreCase);
    }

    private (object[] symbols, int count, object structure) ProcessDocumentSymbols(object[] rawSymbols, bool includeMembers)
    {
        try
        {
            var processedSymbols = new List<object>();
            var symbolCounts = new Dictionary<string, int>();

            foreach (var symbol in rawSymbols)
            {
                // This is a simplified processor for LSP DocumentSymbol format
                // In practice, you'd parse the actual JSON structure
                var processedSymbol = ProcessSingleSymbol(symbol, includeMembers);
                if (processedSymbol != null)
                {
                    processedSymbols.Add(processedSymbol);
                    
                    // Count symbols by type
                    var symbolType = GetSymbolType(processedSymbol);
                    symbolCounts[symbolType] = symbolCounts.GetValueOrDefault(symbolType, 0) + 1;
                }
            }

            var structure = new
            {
                totalSymbols = processedSymbols.Count,
                symbolsByType = symbolCounts,
                hasCodeBlocks = symbolCounts.GetValueOrDefault("method", 0) > 0 || 
                               symbolCounts.GetValueOrDefault("property", 0) > 0 ||
                               symbolCounts.GetValueOrDefault("field", 0) > 0,
                componentStructure = new
                {
                    hasConstructor = symbolCounts.ContainsKey("constructor"),
                    methodCount = symbolCounts.GetValueOrDefault("method", 0),
                    propertyCount = symbolCounts.GetValueOrDefault("property", 0),
                    fieldCount = symbolCounts.GetValueOrDefault("field", 0)
                }
            };

            return (processedSymbols.ToArray(), processedSymbols.Count, structure);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error processing document symbols, returning raw data");
            
            // Fallback: return raw symbols with basic structure
            return (rawSymbols, rawSymbols.Length, new
            {
                totalSymbols = rawSymbols.Length,
                processed = false,
                error = ex.Message
            });
        }
    }

    private object? ProcessSingleSymbol(object rawSymbol, bool includeMembers)
    {
        try
        {
            // Since we're dealing with raw LSP responses, we'll create a simplified representation
            // In a production system, you'd properly parse the LSP DocumentSymbol JSON structure
            
            return new
            {
                name = "Symbol", // Would extract from LSP response
                kind = "unknown", // Would map from LSP SymbolKind
                range = new
                {
                    start = new { line = 0, character = 0 },
                    end = new { line = 0, character = 0 }
                },
                detail = "LSP Symbol", // Would extract detail from response
                children = includeMembers ? Array.Empty<object>() : null,
                note = "Symbol parsing requires LSP JSON structure analysis"
            };
        }
        catch
        {
            return null;
        }
    }

    private string GetSymbolType(object symbol)
    {
        // This would analyze the symbol to determine its type
        // For now, return a default
        return "unknown";
    }

    private object CreateErrorResponse(string message)
    {
        return new
        {
            success = false,
            error = message,
            symbols = Array.Empty<object>(),
            count = 0,
            metadata = new
            {
                tool = "blazor_get_document_symbols",
                timestamp = DateTime.UtcNow
            }
        };
    }
}