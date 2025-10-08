using System.ComponentModel;
using COA.Mcp.Framework;
using COA.Mcp.Framework.Base;
using COA.Mcp.Framework.Models;
using COA.Mcp.Framework.Interfaces;
using COA.Mcp.Framework.TokenOptimization;
using COA.Mcp.Framework.TokenOptimization.ResponseBuilders;
using COA.Mcp.Framework.TokenOptimization.Models;
using COA.Mcp.Framework.TokenOptimization.Caching;
using COA.Mcp.Framework.TokenOptimization.Storage;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services.Sqlite;
using COA.CodeSearch.McpServer.Services.Julie;
using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Tools.Models;
using COA.CodeSearch.McpServer.ResponseBuilders;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Go to definition tool that jumps directly to where a symbol is defined using SQLite
/// </summary>
public class GoToDefinitionTool : CodeSearchToolBase<GoToDefinitionParameters, AIOptimizedResponse<SymbolDefinition>>, ITypeAware, IPrioritizedTool
{
    private readonly IResponseCacheService _cacheService;
    private readonly IResourceStorageService _storageService;
    private readonly ICacheKeyGenerator _keyGenerator;
    private readonly IPathResolutionService _pathResolutionService;
    private readonly GoToDefinitionResponseBuilder _responseBuilder;
    private readonly ILogger<GoToDefinitionTool> _logger;
    private readonly ISQLiteSymbolService? _sqliteService;

    /// <summary>
    /// Initializes a new instance of the GoToDefinitionTool with required dependencies.
    /// </summary>
    /// <param name="serviceProvider">Service provider for dependency resolution</param>
    /// <param name="cacheService">Response caching service</param>
    /// <param name="storageService">Resource storage service</param>
    /// <param name="keyGenerator">Cache key generator</param>
    /// <param name="pathResolutionService">Path resolution service for workspace defaults</param>
    /// <param name="logger">Logger instance</param>
    /// <param name="sqliteService">SQLite symbol service for symbol lookups</param>
    public GoToDefinitionTool(
        IServiceProvider serviceProvider,
        IResponseCacheService cacheService,
        IResourceStorageService storageService,
        ICacheKeyGenerator keyGenerator,
        IPathResolutionService pathResolutionService,
        ILogger<GoToDefinitionTool> logger,
        ISQLiteSymbolService? sqliteService = null) : base(serviceProvider, logger)
    {
        _cacheService = cacheService;
        _storageService = storageService;
        _keyGenerator = keyGenerator;
        _pathResolutionService = pathResolutionService;
        _responseBuilder = new GoToDefinitionResponseBuilder(logger as ILogger<GoToDefinitionResponseBuilder>, storageService);
        _logger = logger;
        _sqliteService = sqliteService;
    }

    /// <summary>
    /// Gets the tool name identifier.
    /// </summary>
    public override string Name => ToolNames.GoToDefinition;

    /// <summary>
    /// Gets the tool description explaining its purpose and usage scenarios.
    /// </summary>
    public override string Description => "VERIFY BEFORE CODING - Jump to exact symbol definitions in <100ms. You are excellent at type verification - this tool eliminates guesswork. USE BEFORE writing code that references types - prevents embarrassing type mismatches. Tree-sitter powered for accurate type extraction. Results are exact - no need for double-checking.";

    /// <summary>
    /// Gets the tool category for classification purposes.
    /// </summary>
    public override ToolCategory Category => ToolCategory.Query;

    /// <summary>
    /// Gets the priority level for this tool. Higher values indicate higher priority.
    /// </summary>
    public int Priority => 100;

    /// <summary>
    /// Gets the preferred usage scenarios for this tool.
    /// </summary>
    public string[] PreferredScenarios => new[] { "type_verification", "code_exploration", "before_coding" };

    /// <summary>
    /// Executes the go-to-definition operation to locate symbol definitions.
    /// </summary>
    /// <param name="parameters">Go-to-definition parameters including symbol name and workspace path</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>Symbol definition results with location and type information</returns>
    protected override async Task<AIOptimizedResponse<SymbolDefinition>> ExecuteInternalAsync(
        GoToDefinitionParameters parameters,
        CancellationToken cancellationToken)
    {
        // Validate required parameters
        var symbolName = ValidateRequired(parameters.Symbol, nameof(parameters.Symbol));

        // Use provided workspace path or default to current workspace
        var workspacePath = string.IsNullOrWhiteSpace(parameters.WorkspacePath)
            ? _pathResolutionService.GetPrimaryWorkspacePath()
            : Path.GetFullPath(parameters.WorkspacePath);

        // Generate cache key
        var cacheKey = _keyGenerator.GenerateKey(Name, parameters);

        // Check cache first (unless explicitly disabled)
        if (!parameters.NoCache)
        {
            var cached = await _cacheService.GetAsync<AIOptimizedResponse<SymbolDefinition>>(cacheKey);
            if (cached != null)
            {
                _logger.LogDebug("Returning cached goto definition result for {Symbol}", symbolName);
                return cached;
            }
        }

        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Check if SQLite database exists
            if (_sqliteService == null || !_sqliteService.DatabaseExists(workspacePath))
            {
                _logger.LogWarning("SQLite database not found for workspace {WorkspacePath}", workspacePath);
                return new AIOptimizedResponse<SymbolDefinition>
                {
                    Success = false,
                    Error = new COA.Mcp.Framework.Models.ErrorInfo
                    {
                        Code = "WORKSPACE_NOT_INDEXED",
                        Message = "Workspace has not been indexed. Run index_workspace first.",
                        Recovery = new COA.Mcp.Framework.Models.RecoveryInfo
                        {
                            Steps = new[]
                            {
                                "Run mcp__codesearch__index_workspace with this workspace path",
                                "Wait for indexing to complete",
                                "Try goto_definition again"
                            }
                        }
                    }
                };
            }

            // Query SQLite for the symbol (source of truth)
            _logger.LogDebug("Querying SQLite for symbol '{Symbol}' (caseSensitive={CaseSensitive})",
                symbolName, parameters.CaseSensitive);

            var sqliteSymbols = await _sqliteService.GetSymbolsByNameAsync(
                workspacePath,
                symbolName,
                parameters.CaseSensitive,
                cancellationToken);

            if (sqliteSymbols == null || sqliteSymbols.Count == 0)
            {
                _logger.LogInformation("Symbol '{Symbol}' not found in {Elapsed}ms",
                    symbolName, stopwatch.ElapsedMilliseconds);

                return new AIOptimizedResponse<SymbolDefinition>
                {
                    Success = false,
                    Error = new COA.Mcp.Framework.Models.ErrorInfo
                    {
                        Code = "SYMBOL_NOT_FOUND",
                        Message = $"Symbol '{symbolName}' not found in workspace",
                        Recovery = new COA.Mcp.Framework.Models.RecoveryInfo
                        {
                            Steps = new[]
                            {
                                "Check if the symbol name is spelled correctly",
                                "Try symbol_search for fuzzy/partial matching",
                                "Re-index workspace if the symbol was recently added"
                            }
                        }
                    }
                };
            }

            // Find best match (prefer types over methods)
            var matchingSymbol = FindBestMatch(sqliteSymbols, symbolName, parameters.CaseSensitive);

            if (matchingSymbol == null)
            {
                _logger.LogWarning("FindBestMatch returned null despite having {Count} symbols", sqliteSymbols.Count);
                return new AIOptimizedResponse<SymbolDefinition>
                {
                    Success = false,
                    Error = new COA.Mcp.Framework.Models.ErrorInfo
                    {
                        Code = "SYMBOL_NOT_FOUND",
                        Message = $"No exact match found for symbol '{symbolName}'",
                    }
                };
            }

            _logger.LogInformation("Found symbol '{Symbol}' in {Elapsed}ms",
                symbolName, stopwatch.ElapsedMilliseconds);

            var definition = await MapJulieSymbolToDefinitionAsync(
                matchingSymbol,
                workspacePath,
                parameters.ContextLines,
                cancellationToken);

            // Build response
            var context = new ResponseContext
            {
                ResponseMode = "adaptive",
                TokenLimit = parameters.ContextLines * 50 + 500,
                StoreFullResults = false,
                ToolName = Name,
                CacheKey = cacheKey,
                CustomMetadata = new Dictionary<string, object>
                {
                    ["symbolName"] = symbolName,
                    ["source"] = "sqlite"
                }
            };

            var response = await _responseBuilder.BuildResponseAsync(definition, context);

            // Cache the response
            if (!parameters.NoCache && response.Success)
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
            _logger.LogError(ex, "Goto definition failed for {Symbol} in {WorkspacePath}", symbolName, workspacePath);

            return new AIOptimizedResponse<SymbolDefinition>
            {
                Success = false,
                Error = new COA.Mcp.Framework.Models.ErrorInfo
                {
                    Code = "GOTO_DEFINITION_ERROR",
                    Message = $"Failed to find definition: {ex.Message}",
                    Recovery = new COA.Mcp.Framework.Models.RecoveryInfo
                    {
                        Steps = new[]
                        {
                            "Ensure the workspace is indexed",
                            "Check if the symbol name is correct",
                            "Try symbol_search for broader search"
                        }
                    }
                }
            };
        }
    }

    /// <summary>
    /// Finds the best matching symbol from a list based on case sensitivity.
    /// </summary>
    private JulieSymbol? FindBestMatch(List<JulieSymbol> symbols, string symbolName, bool caseSensitive)
    {
        if (symbols == null || symbols.Count == 0)
            return null;

        // Filter by case sensitivity
        var comparisonType = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var matches = symbols.Where(s => s.Name.Equals(symbolName, comparisonType)).ToList();

        if (matches.Count == 0)
            return null;

        // If single match, return it
        if (matches.Count == 1)
            return matches[0];

        // Multiple matches: prefer classes/types over methods
        var typeKinds = new[] { "class", "interface", "struct", "enum" };
        var typeMatch = matches.FirstOrDefault(s => typeKinds.Contains(s.Kind.ToLowerInvariant()));
        if (typeMatch != null)
            return typeMatch;

        // Otherwise return first match
        return matches[0];
    }

    /// <summary>
    /// Maps a JulieSymbol to SymbolDefinition with context extraction.
    /// </summary>
    private async Task<SymbolDefinition> MapJulieSymbolToDefinitionAsync(
        JulieSymbol symbol,
        string workspacePath,
        int contextLines,
        CancellationToken cancellationToken)
    {
        var definition = new SymbolDefinition
        {
            Name = symbol.Name,
            Kind = symbol.Kind,
            Signature = symbol.Signature ?? symbol.Name,
            FilePath = symbol.FilePath,
            Line = symbol.StartLine,
            Column = symbol.StartColumn,
            Language = symbol.Language,
            Score = 1.0f // SQLite exact match gets perfect score
        };

        // Add visibility as modifiers
        if (!string.IsNullOrEmpty(symbol.Visibility))
        {
            definition.Modifiers = new List<string> { symbol.Visibility };
        }

        // Extract context snippet if requested
        if (contextLines > 0 && _sqliteService != null)
        {
            try
            {
                var snippet = await GetContextFromSQLiteAsync(
                    workspacePath,
                    symbol.FilePath,
                    symbol.StartLine,
                    contextLines,
                    cancellationToken);

                definition.Snippet = snippet;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to extract context for {Symbol} in {FilePath}",
                    symbol.Name, symbol.FilePath);
            }
        }

        return definition;
    }

    /// <summary>
    /// Extracts context snippet from SQLite-stored file content.
    /// </summary>
    private async Task<string?> GetContextFromSQLiteAsync(
        string workspacePath,
        string filePath,
        int targetLine,
        int contextLines,
        CancellationToken cancellationToken)
    {
        if (_sqliteService == null)
            return null;

        // Get single file content from SQLite (efficient query)
        var fileRecord = await _sqliteService.GetFileByPathAsync(workspacePath, filePath, cancellationToken);

        if (fileRecord == null || string.IsNullOrEmpty(fileRecord.Content))
        {
            _logger.LogDebug("No content found in SQLite for {FilePath}", filePath);
            return null;
        }

        // Extract context lines
        var lines = fileRecord.Content.Split('\n');
        var startLine = Math.Max(0, targetLine - contextLines - 1); // -1 because line numbers are 1-based
        var endLine = Math.Min(lines.Length - 1, targetLine + contextLines - 1);

        var contextSnippet = new List<string>();
        for (int i = startLine; i <= endLine; i++)
        {
            contextSnippet.Add(lines[i]);
        }

        return string.Join("\n", contextSnippet);
    }

}