using System.ComponentModel;
using COA.Mcp.Framework;
using COA.Mcp.Framework.Base;
using COA.Mcp.Framework.Models;
using COA.Mcp.Framework.TokenOptimization;
using COA.Mcp.Framework.TokenOptimization.ResponseBuilders;
using COA.Mcp.Framework.TokenOptimization.Models;
using COA.Mcp.Framework.TokenOptimization.Caching;
using COA.Mcp.Framework.TokenOptimization.Storage;
using COA.CodeSearch.McpServer.Services.Sqlite;
using COA.CodeSearch.McpServer.Services.Julie;
using COA.CodeSearch.McpServer.Tools.Models;
using COA.Mcp.Framework.Interfaces;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Extracts comprehensive symbol overview from a single file using SQLite
/// </summary>
public class GetSymbolsOverviewTool : CodeSearchToolBase<GetSymbolsOverviewParameters, AIOptimizedResponse<SymbolsOverviewResult>>, IPrioritizedTool
{
    private readonly IResponseCacheService _cacheService;
    private readonly IResourceStorageService _storageService;
    private readonly ICacheKeyGenerator _keyGenerator;
    private readonly ILogger<GetSymbolsOverviewTool> _logger;
    private readonly ISQLiteSymbolService? _sqliteService;

    /// <summary>
    /// Initializes a new instance of the GetSymbolsOverviewTool with required dependencies.
    /// </summary>
    /// <param name="serviceProvider">Service provider for dependency resolution</param>
    /// <param name="cacheService">Response caching service</param>
    /// <param name="storageService">Resource storage service</param>
    /// <param name="keyGenerator">Cache key generator</param>
    /// <param name="logger">Logger instance</param>
    /// <param name="sqliteService">SQLite symbol service for symbol lookups</param>
    public GetSymbolsOverviewTool(
        IServiceProvider serviceProvider,
        IResponseCacheService cacheService,
        IResourceStorageService storageService,
        ICacheKeyGenerator keyGenerator,
        ILogger<GetSymbolsOverviewTool> logger,
        ISQLiteSymbolService? sqliteService = null) : base(serviceProvider, logger)
    {
        _cacheService = cacheService;
        _storageService = storageService;
        _keyGenerator = keyGenerator;
        _logger = logger;
        _sqliteService = sqliteService;
    }

    /// <summary>
    /// This tool handles validation internally in ExecuteInternalAsync, so disable framework validation
    /// </summary>
    protected override bool ShouldValidateDataAnnotations => false;

    /// <summary>
    /// Gets the tool name identifier.
    /// </summary>
    public override string Name => ToolNames.GetSymbolsOverview;

    /// <summary>
    /// Gets the tool description explaining its purpose and usage scenarios.
    /// </summary>
    public override string Description => "EXTRACT ALL SYMBOLS from any file - Get complete overview of classes, methods, interfaces without reading entire files. Tree-sitter powered for accurate type information with line numbers.";

    /// <summary>
    /// Gets the tool category for classification purposes.
    /// </summary>
    public override ToolCategory Category => ToolCategory.Query;

    /// <summary>
    /// Gets the priority level for this tool. Higher values indicate higher priority.
    /// </summary>
    public int Priority => 95;

    /// <summary>
    /// Gets the preferred usage scenarios for this tool.
    /// </summary>
    public string[] PreferredScenarios => new[] { "file_exploration", "code_understanding", "api_discovery", "type_analysis", "quick_overview" };

    /// <summary>
    /// Executes the symbols overview operation to extract all symbols from a file.
    /// </summary>
    /// <param name="parameters">Symbols overview parameters including file path and extraction options</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>Symbols overview results with all extracted symbols and their details</returns>
    protected override async Task<AIOptimizedResponse<SymbolsOverviewResult>> ExecuteInternalAsync(
        GetSymbolsOverviewParameters parameters,
        CancellationToken cancellationToken)
    {
        // Validate required parameters
        if (string.IsNullOrWhiteSpace(parameters.FilePath))
        {
            return new AIOptimizedResponse<SymbolsOverviewResult>
            {
                Success = false,
                Error = new ErrorInfo
                {
                    Code = "SYMBOLS_OVERVIEW_ERROR",
                    Message = "File path is required"
                }
            };
        }
        var filePath = parameters.FilePath;

        // Convert to absolute path
        if (!Path.IsPathRooted(filePath))
        {
            filePath = Path.GetFullPath(filePath);
        }

        // Validate file exists
        if (!File.Exists(filePath))
        {
            return new AIOptimizedResponse<SymbolsOverviewResult>
            {
                Success = false,
                Error = new ErrorInfo
                {
                    Code = "FILE_NOT_FOUND",
                    Message = $"File not found: {filePath}",
                    Recovery = new RecoveryInfo
                    {
                        Steps = new[]
                        {
                            "Check if the file path is correct",
                            "Ensure the file exists and is accessible",
                            "Use file_search tool to find the correct file"
                        }
                    }
                }
            };
        }

        // Generate cache key
        var cacheKey = _keyGenerator.GenerateKey(Name, parameters);

        // Check cache first (unless explicitly disabled)
        if (!parameters.NoCache)
        {
            var cached = await _cacheService.GetAsync<AIOptimizedResponse<SymbolsOverviewResult>>(cacheKey);
            if (cached != null)
            {
                _logger.LogDebug("Returning cached symbols overview for {FilePath}", filePath);
                return cached;
            }
        }

        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            _logger.LogInformation("Getting symbols overview from {FilePath}", filePath);

            // Check if SQLite database exists
            if (_sqliteService == null || string.IsNullOrEmpty(parameters.WorkspacePath) ||
                !_sqliteService.DatabaseExists(parameters.WorkspacePath))
            {
                _logger.LogWarning("SQLite database not found for workspace {WorkspacePath}", parameters.WorkspacePath);
                return new AIOptimizedResponse<SymbolsOverviewResult>
                {
                    Success = false,
                    Error = new ErrorInfo
                    {
                        Code = "WORKSPACE_NOT_INDEXED",
                        Message = "Workspace has not been indexed. Run index_workspace first.",
                        Recovery = new RecoveryInfo
                        {
                            Steps = new[]
                            {
                                "Run mcp__codesearch__index_workspace with this workspace path",
                                "Wait for indexing to complete",
                                "Try get_symbols_overview again"
                            }
                        }
                    }
                };
            }

            // Query SQLite for symbols in this file (source of truth)
            _logger.LogDebug("Querying SQLite for symbols in {FilePath}", filePath);

            var symbols = await _sqliteService.GetSymbolsForFileAsync(
                parameters.WorkspacePath,
                filePath,
                cancellationToken);

            stopwatch.Stop();

            if (symbols == null || symbols.Count == 0)
            {
                _logger.LogInformation("No symbols found in {FilePath}", filePath);

                // Return empty result (file has no symbols, not an error)
                var emptyResult = new SymbolsOverviewResult
                {
                    FilePath = filePath,
                    Language = "unknown",
                    ExtractionTime = stopwatch.Elapsed,
                    Success = true,
                    TotalSymbols = 0
                };

                return new AIOptimizedResponse<SymbolsOverviewResult>
                {
                    Success = true,
                    Data = new AIResponseData<SymbolsOverviewResult> { Results = emptyResult },
                    Message = $"No symbols found in {Path.GetFileName(filePath)}"
                };
            }

            _logger.LogInformation("Found {SymbolCount} symbols in {FilePath}", symbols.Count, filePath);

            // Build comprehensive overview from SQLite symbols
            var result = BuildSymbolsOverview(symbols, filePath, stopwatch.Elapsed, parameters);

            // Create response
            var response = new AIOptimizedResponse<SymbolsOverviewResult>
            {
                Success = true,
                Data = new AIResponseData<SymbolsOverviewResult> { Results = result },
                Message = $"Extracted {result.TotalSymbols} symbols from {Path.GetFileName(filePath)}"
            };

            // Cache the response
            if (!parameters.NoCache)
            {
                await _cacheService.SetAsync(cacheKey, response, new CacheEntryOptions
                {
                    AbsoluteExpiration = TimeSpan.FromMinutes(10)
                });
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Symbols overview extraction failed for {FilePath}", filePath);

            return new AIOptimizedResponse<SymbolsOverviewResult>
            {
                Success = false,
                Error = new ErrorInfo
                {
                    Code = "SYMBOLS_OVERVIEW_ERROR",
                    Message = $"Failed to extract symbols overview: {ex.Message}",
                    Recovery = new RecoveryInfo
                    {
                        Steps = new[]
                        {
                            "Ensure the workspace is indexed",
                            "Check if the file path is valid",
                            "Try with a different file"
                        }
                    }
                }
            };
        }
    }
    
    private SymbolsOverviewResult BuildSymbolsOverview(
        List<JulieSymbol> symbols,
        string filePath,
        TimeSpan extractionTime,
        GetSymbolsOverviewParameters parameters)
    {
        var result = new SymbolsOverviewResult
        {
            FilePath = filePath,
            Language = symbols.FirstOrDefault()?.Language ?? "unknown",
            ExtractionTime = extractionTime,
            Success = true
        };

        // Julie stores all symbols in a flat list with Kind field
        // Categorize by kind: class, interface, struct, enum, function, method
        foreach (var symbol in symbols)
        {
            var kind = symbol.Kind.ToLowerInvariant();

            // Types (classes, interfaces, structs, enums)
            if (kind == "class" || kind == "interface" || kind == "struct" || kind == "record" || kind == "enum")
            {
                var typeOverview = new TypeOverview
                {
                    Name = symbol.Name,
                    Kind = symbol.Kind,
                    Signature = symbol.Signature ?? symbol.Name,
                    Line = parameters.IncludeLineNumbers ? symbol.StartLine : 0,
                    Column = parameters.IncludeLineNumbers ? symbol.StartColumn : 0,
                    Modifiers = new List<string>(), // Julie doesn't extract modifiers currently
                    BaseType = null, // Not available in JulieSymbol
                    Interfaces = null // Not available in JulieSymbol
                };

                // Note: Julie doesn't nest methods inside types - they're separate symbols
                // So we don't populate Methods list here

                switch (kind)
                {
                    case "class":
                    case "record":
                        result.Classes.Add(typeOverview);
                        break;
                    case "interface":
                        result.Interfaces.Add(typeOverview);
                        break;
                    case "struct":
                        result.Structs.Add(typeOverview);
                        break;
                    case "enum":
                        result.Enums.Add(typeOverview);
                        break;
                }
            }
            // Methods/Functions
            else if (parameters.IncludeMethods && (kind == "function" || kind == "method"))
            {
                var methodOverview = new MethodOverview
                {
                    Name = symbol.Name,
                    Signature = symbol.Signature ?? symbol.Name,
                    ReturnType = null, // Not extracted by Julie
                    Line = parameters.IncludeLineNumbers ? symbol.StartLine : 0,
                    Column = parameters.IncludeLineNumbers ? symbol.StartColumn : 0,
                    Modifiers = new List<string>(),
                    Parameters = new List<string>(),
                    ContainingType = null! // Julie symbols are flat, no nesting info
                };

                result.Methods.Add(methodOverview);
            }
        }

        // Calculate total symbols
        result.TotalSymbols = result.Classes.Count + result.Interfaces.Count +
                             result.Structs.Count + result.Enums.Count + result.Methods.Count;

        _logger.LogDebug("Built overview: {Classes} classes, {Interfaces} interfaces, {Structs} structs, {Enums} enums, {Methods} methods",
            result.Classes.Count, result.Interfaces.Count, result.Structs.Count, result.Enums.Count, result.Methods.Count);

        return result;
    }

}