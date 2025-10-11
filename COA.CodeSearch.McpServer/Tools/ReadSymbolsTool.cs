using System.ComponentModel;
using COA.Mcp.Framework;
using COA.Mcp.Framework.Base;
using COA.Mcp.Framework.Models;
using COA.Mcp.Framework.TokenOptimization;
using COA.Mcp.Framework.TokenOptimization.ResponseBuilders;
using COA.Mcp.Framework.TokenOptimization.Models;
using COA.Mcp.Framework.TokenOptimization.Caching;
using COA.Mcp.Framework.TokenOptimization.Storage;
using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Services.Sqlite;
using COA.CodeSearch.McpServer.Services.Julie;
using COA.CodeSearch.McpServer.Tools.Models;
using COA.Mcp.Framework.Interfaces;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Extracts specific symbol implementations from files using byte-offset precision for token-efficient reading
/// </summary>
public class ReadSymbolsTool : CodeSearchToolBase<ReadSymbolsParameters, AIOptimizedResponse<ReadSymbolsResult>>, IPrioritizedTool
{
    private readonly IResponseCacheService _cacheService;
    private readonly IResourceStorageService _storageService;
    private readonly ICacheKeyGenerator _keyGenerator;
    private readonly IPathResolutionService _pathResolutionService;
    private readonly ILogger<ReadSymbolsTool> _logger;
    private readonly ISQLiteSymbolService? _sqliteService;

    /// <summary>
    /// Initializes a new instance of the ReadSymbolsTool with required dependencies.
    /// </summary>
    public ReadSymbolsTool(
        IServiceProvider serviceProvider,
        IResponseCacheService cacheService,
        IResourceStorageService storageService,
        ICacheKeyGenerator keyGenerator,
        IPathResolutionService pathResolutionService,
        ILogger<ReadSymbolsTool> logger,
        ISQLiteSymbolService? sqliteService = null) : base(serviceProvider, logger)
    {
        _cacheService = cacheService;
        _storageService = storageService;
        _keyGenerator = keyGenerator;
        _pathResolutionService = pathResolutionService;
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
    public override string Name => ToolNames.ReadSymbols;

    /// <summary>
    /// Gets the tool description explaining its purpose and usage scenarios.
    /// </summary>
    public override string Description =>
        "READ SPECIFIC SYMBOLS - Extract symbol implementations without reading entire files (80-95% token savings). " +
        "You are skilled at surgical code reading - this tool provides byte-offset precision. " +
        "Use AFTER get_symbols_overview to see structure, THEN use this to get specific implementations. " +
        "Byte-offset extraction with dependency analysis (what symbols call, what calls them, inheritance). " +
        "Results are surgically precise - no code bleeding into neighbors, trust the exact boundaries.";

    /// <summary>
    /// Gets the tool category for classification purposes.
    /// </summary>
    public override ToolCategory Category => ToolCategory.Query;

    /// <summary>
    /// Gets the priority level for this tool. Higher values indicate higher priority.
    /// </summary>
    public int Priority => 92;

    /// <summary>
    /// Gets the preferred usage scenarios for this tool.
    /// </summary>
    public string[] PreferredScenarios => new[] { "implementation_reading", "code_understanding", "refactoring_prep", "token_efficient", "surgical_extraction" };

    /// <summary>
    /// Executes the read symbols operation to extract specific symbol implementations.
    /// </summary>
    protected override async Task<AIOptimizedResponse<ReadSymbolsResult>> ExecuteInternalAsync(
        ReadSymbolsParameters parameters,
        CancellationToken cancellationToken)
    {
        // Validate required parameters
        if (string.IsNullOrWhiteSpace(parameters.FilePath))
        {
            return new AIOptimizedResponse<ReadSymbolsResult>
            {
                Success = false,
                Error = new ErrorInfo
                {
                    Code = "READ_SYMBOLS_ERROR",
                    Message = "File path is required"
                }
            };
        }

        if (parameters.SymbolNames == null || parameters.SymbolNames.Count == 0)
        {
            return new AIOptimizedResponse<ReadSymbolsResult>
            {
                Success = false,
                Error = new ErrorInfo
                {
                    Code = "READ_SYMBOLS_ERROR",
                    Message = "At least one symbol name is required"
                }
            };
        }

        var filePath = parameters.FilePath;

        // Convert to absolute path
        if (!Path.IsPathRooted(filePath))
        {
            filePath = Path.GetFullPath(filePath);
        }

        // Use provided workspace path or default to current workspace
        var workspacePath = string.IsNullOrWhiteSpace(parameters.WorkspacePath)
            ? _pathResolutionService.GetPrimaryWorkspacePath()
            : Path.GetFullPath(parameters.WorkspacePath);

        // Validate file exists
        if (!File.Exists(filePath))
        {
            return new AIOptimizedResponse<ReadSymbolsResult>
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
                            "Use search_files tool to find the correct file"
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
            var cached = await _cacheService.GetAsync<AIOptimizedResponse<ReadSymbolsResult>>(cacheKey);
            if (cached != null)
            {
                _logger.LogDebug("Returning cached read_symbols for {FilePath}", filePath);
                return cached;
            }
        }

        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            _logger.LogInformation("Reading symbols {SymbolNames} from {FilePath}",
                string.Join(", ", parameters.SymbolNames), filePath);

            // Check if SQLite database exists
            if (_sqliteService == null || !_sqliteService.DatabaseExists(workspacePath))
            {
                _logger.LogWarning("SQLite database not found for workspace {WorkspacePath}", workspacePath);
                return new AIOptimizedResponse<ReadSymbolsResult>
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
                                "Try read_symbols again"
                            }
                        }
                    }
                };
            }

            // Get symbols from SQLite by name + file
            var symbols = await _sqliteService.GetSymbolsForFileAsync(workspacePath, filePath, cancellationToken);

            if (symbols == null || symbols.Count == 0)
            {
                return new AIOptimizedResponse<ReadSymbolsResult>
                {
                    Success = false,
                    Error = new ErrorInfo
                    {
                        Code = "NO_SYMBOLS_FOUND",
                        Message = $"No symbols found in {Path.GetFileName(filePath)}. File may not be indexed or contains no extractable symbols.",
                        Recovery = new RecoveryInfo
                        {
                            Steps = new[]
                            {
                                "Ensure the file is indexed (run index_workspace)",
                                "Check if the file contains valid code",
                                "Use get_symbols_overview to see all available symbols"
                            }
                        }
                    }
                };
            }

            // Filter to requested symbol names (case-insensitive)
            var requestedSymbols = symbols
                .Where(s => parameters.SymbolNames.Any(name =>
                    s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            // Build the result
            var result = await BuildReadSymbolsResultAsync(
                requestedSymbols,
                symbols, // Pass all symbols for inheritance lookups
                parameters.SymbolNames,
                filePath,
                workspacePath,
                parameters,
                stopwatch.Elapsed,
                cancellationToken);

            stopwatch.Stop();

            // Create response
            var response = new AIOptimizedResponse<ReadSymbolsResult>
            {
                Success = true,
                Data = new AIResponseData<ReadSymbolsResult> { Results = result },
                Message = result.SymbolCount > 0
                    ? $"Extracted {result.SymbolCount} symbol(s) from {Path.GetFileName(filePath)}"
                    : $"No matching symbols found in {Path.GetFileName(filePath)}"
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
            _logger.LogError(ex, "Read symbols failed for {FilePath}", filePath);

            return new AIOptimizedResponse<ReadSymbolsResult>
            {
                Success = false,
                Error = new ErrorInfo
                {
                    Code = "READ_SYMBOLS_ERROR",
                    Message = $"Failed to read symbols: {ex.Message}",
                    Recovery = new RecoveryInfo
                    {
                        Steps = new[]
                        {
                            "Ensure the workspace is indexed",
                            "Check if the file path is valid",
                            "Try with a different file or symbols"
                        }
                    }
                }
            };
        }
    }

    private async Task<ReadSymbolsResult> BuildReadSymbolsResultAsync(
        List<JulieSymbol> matchedSymbols,
        List<JulieSymbol> allSymbols,
        List<string> requestedNames,
        string filePath,
        string workspacePath,
        ReadSymbolsParameters parameters,
        TimeSpan extractionTime,
        CancellationToken cancellationToken)
    {
        var result = new ReadSymbolsResult
        {
            FilePath = filePath,
            Language = matchedSymbols.FirstOrDefault()?.Language ?? "unknown",
            ExtractionTime = extractionTime,
            Success = true
        };

        // Track not found symbols
        var foundNames = matchedSymbols.Select(s => s.Name.ToLowerInvariant()).ToHashSet();
        result.NotFoundSymbols = requestedNames
            .Where(name => !foundNames.Contains(name.ToLowerInvariant()))
            .ToList();
        result.NotFoundCount = result.NotFoundSymbols.Count;

        // Get file content from SQLite
        var fileRecord = await _sqliteService!.GetFileByPathAsync(workspacePath, filePath, cancellationToken);
        if (fileRecord == null || string.IsNullOrEmpty(fileRecord.Content))
        {
            _logger.LogWarning("File content not found in database for {FilePath}", filePath);
            return result;
        }

        var fileContent = fileRecord.Content;
        int tokenBudget = parameters.MaxTokens;
        int tokensUsed = 0;

        // Extract each symbol with byte-offset precision
        foreach (var symbol in matchedSymbols)
        {
            // Check token budget
            if (tokensUsed >= tokenBudget)
            {
                result.Truncated = true;
                _logger.LogWarning("Token budget exceeded, truncating remaining symbols");
                break;
            }

            var symbolCode = await ExtractSymbolCodeAsync(
                symbol,
                fileContent,
                allSymbols, // Pass all symbols for inheritance lookups
                workspacePath,
                parameters,
                cancellationToken);

            result.Symbols.Add(symbolCode);
            tokensUsed += symbolCode.EstimatedTokens;
        }

        result.SymbolCount = result.Symbols.Count;
        result.EstimatedTokens = tokensUsed;

        return result;
    }

    private Task<SymbolCode> ExtractSymbolCodeAsync(
        JulieSymbol symbol,
        string fileContent,
        List<JulieSymbol> allSymbols,
        string workspacePath,
        ReadSymbolsParameters parameters,
        CancellationToken cancellationToken)
    {
        var symbolCode = new SymbolCode
        {
            Name = symbol.Name,
            Kind = symbol.Kind,
            Signature = symbol.Signature,
            StartLine = symbol.StartLine,
            EndLine = symbol.EndLine,
            StartColumn = symbol.StartColumn,
            EndColumn = symbol.EndColumn
        };

        // Extract code based on detail level
        if (parameters.DetailLevel == "signature")
        {
            // Signature only - just return the signature
            symbolCode.Code = symbol.Signature ?? symbol.Name;
            symbolCode.EstimatedTokens = EstimateTokens(symbolCode.Code);
        }
        else
        {
            // Implementation or full - use byte offsets for surgical precision
            if (symbol.StartByte.HasValue && symbol.EndByte.HasValue)
            {
                // Byte-offset extraction (surgical precision - no bleeding into neighbors)
                int startByte = symbol.StartByte.Value;
                int endByte = symbol.EndByte.Value;

                // Extract code using byte offsets
                int length = endByte - startByte;
                if (startByte >= 0 && startByte < fileContent.Length && length > 0)
                {
                    length = Math.Min(length, fileContent.Length - startByte);
                    symbolCode.Code = fileContent.Substring(startByte, length);

                    // Note: Context lines for byte extraction would require line boundary detection
                    // For now, byte extraction is exact - no context bleeding
                }
                else
                {
                    _logger.LogWarning("Invalid byte offsets for symbol {SymbolName}: start={Start}, end={End}",
                        symbol.Name, startByte, endByte);
                    symbolCode.Code = $"// Error: Invalid byte offsets for {symbol.Name}";
                }

                symbolCode.EstimatedTokens = EstimateTokens(symbolCode.Code);
            }
            else
            {
                // Fallback to line-based extraction (when byte offsets not available)
                _logger.LogDebug("Using line-based extraction for {SymbolName} (no byte offsets)", symbol.Name);

                var lines = fileContent.Split('\n');
                int startIdx = Math.Max(0, symbol.StartLine - 1);
                int endIdx = Math.Min(lines.Length - 1, symbol.EndLine - 1);

                var extractedLines = new List<string>();
                for (int i = startIdx; i <= endIdx; i++)
                {
                    extractedLines.Add(lines[i]);
                }

                symbolCode.Code = string.Join("\n", extractedLines);
                symbolCode.EstimatedTokens = EstimateTokens(symbolCode.Code);
            }
        }

        // Add dependency analysis if requested (Phase 2)
        return AddDependencyAnalysisAsync(symbolCode, symbol, allSymbols, workspacePath, parameters, cancellationToken);
    }

    private async Task<SymbolCode> AddDependencyAnalysisAsync(
        SymbolCode symbolCode,
        JulieSymbol symbol,
        List<JulieSymbol> allSymbols,
        string workspacePath,
        ReadSymbolsParameters parameters,
        CancellationToken cancellationToken)
    {
        if (!parameters.IncludeDependencies && !parameters.IncludeCallers && !parameters.IncludeInheritance)
        {
            return symbolCode;
        }

        try
        {
            // Dependencies: What does this symbol call?
            if (parameters.IncludeDependencies)
            {
                var allIdentifiers = new List<JulieIdentifier>();

                // Get identifiers directly in this symbol
                var directIdentifiers = await _sqliteService!.GetIdentifiersByContainingSymbolAsync(
                    workspacePath, symbol.Id, cancellationToken);
                allIdentifiers.AddRange(directIdentifiers);

                // For classes/interfaces, also get identifiers from child symbols (methods, properties)
                if (symbol.Kind == "class" || symbol.Kind == "interface" || symbol.Kind == "struct")
                {
                    var childSymbols = allSymbols.Where(s => s.ParentId == symbol.Id).ToList();
                    foreach (var child in childSymbols)
                    {
                        var childIdentifiers = await _sqliteService!.GetIdentifiersByContainingSymbolAsync(
                            workspacePath, child.Id, cancellationToken);
                        allIdentifiers.AddRange(childIdentifiers);
                    }
                }

                symbolCode.Dependencies = allIdentifiers
                    .Where(i => i.Kind == "call") // Focus on method/function calls
                    .Select(i => new SymbolReference
                    {
                        Name = i.Name,
                        Kind = i.Kind,
                        FilePath = i.FilePath,
                        Line = i.StartLine,
                        Column = i.StartColumn
                    })
                    .Take(20) // Limit to prevent token explosion
                    .ToList();
            }

            // Callers: What calls this symbol?
            if (parameters.IncludeCallers)
            {
                var callers = await _sqliteService!.GetIdentifiersByNameAsync(
                    workspacePath, symbol.Name, caseSensitive: false, cancellationToken);

                symbolCode.Callers = callers
                    .Where(i => i.Kind == "call") // Focus on actual calls
                    .Select(i => new SymbolReference
                    {
                        Name = i.Name,
                        Kind = i.Kind,
                        FilePath = i.FilePath,
                        Line = i.StartLine,
                        Column = i.StartColumn
                    })
                    .Take(20) // Limit to prevent token explosion
                    .ToList();
            }

            // Inheritance: Base classes and interfaces
            if (parameters.IncludeInheritance && (symbol.Kind == "class" || symbol.Kind == "interface"))
            {
                var relationships = await _sqliteService!.GetRelationshipsForSymbolsAsync(
                    workspacePath,
                    new List<string> { symbol.Id },
                    new List<string> { "extends", "implements" },
                    cancellationToken);

                if (relationships.TryGetValue(symbol.Id, out var symbolRelationships))
                {
                    symbolCode.Inheritance = new InheritanceInfo();

                    foreach (var rel in symbolRelationships)
                    {
                        // Find target symbol by ID from the already-loaded symbols list
                        var targetSymbol = allSymbols.FirstOrDefault(s => s.Id == rel.ToSymbolId);

                        if (targetSymbol == null)
                        {
                            _logger.LogDebug("Target symbol {TargetId} not found in current file", rel.ToSymbolId);
                            continue;
                        }

                        if (rel.Kind.Equals("extends", StringComparison.OrdinalIgnoreCase))
                        {
                            symbolCode.Inheritance.BaseClass = targetSymbol.Name;
                        }
                        else if (rel.Kind.Equals("implements", StringComparison.OrdinalIgnoreCase))
                        {
                            symbolCode.Inheritance.Interfaces.Add(targetSymbol.Name);
                        }
                    }
                }
                else
                {
                    symbolCode.Inheritance = new InheritanceInfo(); // Empty but initialized
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to add dependency analysis for symbol {SymbolName}", symbol.Name);
            // Continue without dependency info rather than fail completely
        }

        return symbolCode;
    }

    private int EstimateTokens(string text)
    {
        // Rough estimation: ~4 characters per token
        return (text.Length / 4) + 1;
    }
}
