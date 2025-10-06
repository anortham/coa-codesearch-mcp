using COA.CodeSearch.McpServer.Services.Julie;
using COA.CodeSearch.McpServer.Services.Sqlite;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Implementation of call path tracing using SQLite identifiers and recursive traversal.
/// </summary>
public class CallPathTracerService : ICallPathTracerService
{
    private readonly ISQLiteSymbolService _sqliteService;
    private readonly IReferenceResolverService _referenceResolver;
    private readonly ILogger<CallPathTracerService> _logger;

    public CallPathTracerService(
        ISQLiteSymbolService sqliteService,
        IReferenceResolverService referenceResolver,
        ILogger<CallPathTracerService> logger)
    {
        _sqliteService = sqliteService ?? throw new ArgumentNullException(nameof(sqliteService));
        _referenceResolver = referenceResolver ?? throw new ArgumentNullException(nameof(referenceResolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<List<CallPathNode>> TraceUpwardAsync(
        string workspacePath,
        string symbolName,
        int maxDepth = 10,
        bool caseSensitive = false,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Tracing upward for symbol: {SymbolName}, maxDepth: {MaxDepth}", symbolName, maxDepth);

        // Get all symbols for resolution
        var allSymbols = await _sqliteService.GetAllSymbolsAsync(workspacePath, cancellationToken);
        var symbolsById = allSymbols.ToDictionary(s => s.Id, s => s);

        return await TraceUpwardRecursiveAsync(workspacePath, symbolName, 0, maxDepth, caseSensitive, symbolsById, cancellationToken);
    }

    private async Task<List<CallPathNode>> TraceUpwardRecursiveAsync(
        string workspacePath,
        string symbolName,
        int currentDepth,
        int maxDepth,
        bool caseSensitive,
        Dictionary<string, JulieSymbol> symbolsById,
        CancellationToken cancellationToken)
    {
        // Stop if we've reached max depth
        if (currentDepth > maxDepth)
        {
            return new List<CallPathNode>();
        }

        // Find all identifiers that call this symbol
        var identifiers = await _sqliteService.GetIdentifiersByNameAsync(
            workspacePath,
            symbolName,
            caseSensitive,
            cancellationToken);

        // Filter to only 'call' kind identifiers
        var callIdentifiers = identifiers.Where(i => i.Kind == "call").ToList();

        if (!callIdentifiers.Any())
        {
            return new List<CallPathNode>();
        }

        if (currentDepth == 0)
        {
            _logger.LogInformation("Found {Count} direct callers for {SymbolName}", callIdentifiers.Count, symbolName);
        }

        // Build call path nodes
        var callPaths = new List<CallPathNode>();
        foreach (var identifier in callIdentifiers)
        {
            var node = CreateCallPathNode(identifier, symbolsById, currentDepth, CallDirection.Upward);
            callPaths.Add(node);

            // Recursively trace who calls the caller
            if (node.ContainingSymbol != null)
            {
                var children = await TraceUpwardRecursiveAsync(
                    workspacePath,
                    node.ContainingSymbol.Name,
                    currentDepth + 1,
                    maxDepth,
                    caseSensitive,
                    symbolsById,
                    cancellationToken);

                node.Children.AddRange(children);
            }
        }

        return callPaths;
    }

    public async Task<List<CallPathNode>> TraceDownwardAsync(
        string workspacePath,
        string symbolName,
        int maxDepth = 10,
        bool caseSensitive = false,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Tracing downward for symbol: {SymbolName}, maxDepth: {MaxDepth}", symbolName, maxDepth);

        // Get all symbols for resolution
        var allSymbols = await _sqliteService.GetAllSymbolsAsync(workspacePath, cancellationToken);
        var symbolsById = allSymbols.ToDictionary(s => s.Id, s => s);

        return await TraceDownwardRecursiveAsync(workspacePath, symbolName, 0, maxDepth, caseSensitive, symbolsById, cancellationToken);
    }

    private async Task<List<CallPathNode>> TraceDownwardRecursiveAsync(
        string workspacePath,
        string symbolName,
        int currentDepth,
        int maxDepth,
        bool caseSensitive,
        Dictionary<string, JulieSymbol> symbolsById,
        CancellationToken cancellationToken)
    {
        // Stop if we've reached max depth
        if (currentDepth > maxDepth)
        {
            return new List<CallPathNode>();
        }

        // Find the target symbol - prefer implementations over interfaces
        var candidates = symbolsById.Values
            .Where(s => s.Name.Equals(symbolName, caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (!candidates.Any())
        {
            if (currentDepth == 0)
            {
                _logger.LogWarning("Symbol not found: {SymbolName}", symbolName);
            }
            return new List<CallPathNode>();
        }

        // Prefer implementations over interface declarations
        var targetSymbol = SelectBestImplementation(candidates);

        if (targetSymbol == null)
        {
            if (currentDepth == 0)
            {
                _logger.LogWarning("Symbol not found: {SymbolName}", symbolName);
            }
            return new List<CallPathNode>();
        }

        // Get all identifiers contained within this symbol
        var identifiers = await _sqliteService.GetIdentifiersByContainingSymbolAsync(
            workspacePath,
            targetSymbol.Id,
            cancellationToken);

        // Filter to only 'call' kind identifiers
        var callIdentifiers = identifiers.Where(i => i.Kind == "call").ToList();

        if (!callIdentifiers.Any())
        {
            return new List<CallPathNode>();
        }

        if (currentDepth == 0)
        {
            _logger.LogInformation("Found {Count} direct callees for {SymbolName}", callIdentifiers.Count, symbolName);
        }

        // Build call path nodes
        var callPaths = new List<CallPathNode>();
        foreach (var identifier in callIdentifiers)
        {
            var node = CreateCallPathNode(identifier, symbolsById, currentDepth, CallDirection.Downward);
            callPaths.Add(node);

            // Recursively trace what the callee calls
            var targetSymbolForCall = ResolveTargetSymbol(identifier, symbolsById);
            if (targetSymbolForCall != null)
            {
                var children = await TraceDownwardRecursiveAsync(
                    workspacePath,
                    targetSymbolForCall.Name,
                    currentDepth + 1,
                    maxDepth,
                    caseSensitive,
                    symbolsById,
                    cancellationToken);

                node.Children.AddRange(children);
            }
        }

        return callPaths;
    }

    public async Task<BidirectionalCallPath> TraceBothDirectionsAsync(
        string workspacePath,
        string symbolName,
        int maxDepth = 10,
        bool caseSensitive = false,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Tracing both directions for symbol: {SymbolName}, maxDepth: {MaxDepth}",
            symbolName, maxDepth);

        // Get the target symbol
        var allSymbols = await _sqliteService.GetAllSymbolsAsync(workspacePath, cancellationToken);
        var targetSymbol = allSymbols.FirstOrDefault(s =>
            s.Name.Equals(symbolName, caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase));

        if (targetSymbol == null)
        {
            throw new InvalidOperationException($"Symbol not found: {symbolName}");
        }

        // Trace both directions in parallel
        var upwardTask = TraceUpwardAsync(workspacePath, symbolName, maxDepth, caseSensitive, cancellationToken);
        var downwardTask = TraceDownwardAsync(workspacePath, symbolName, maxDepth, caseSensitive, cancellationToken);

        await Task.WhenAll(upwardTask, downwardTask);

        return new BidirectionalCallPath
        {
            TargetSymbol = targetSymbol,
            Callers = await upwardTask,
            Callees = await downwardTask
        };
    }

    /// <summary>
    /// Create a CallPathNode from an identifier and symbol lookup.
    /// </summary>
    private CallPathNode CreateCallPathNode(
        JulieIdentifier identifier,
        Dictionary<string, JulieSymbol> symbolsById,
        int depth,
        CallDirection direction)
    {
        JulieSymbol? containingSymbol = null;
        if (!string.IsNullOrEmpty(identifier.ContainingSymbolId) &&
            symbolsById.TryGetValue(identifier.ContainingSymbolId, out var containing))
        {
            containingSymbol = containing;
        }

        var targetSymbol = ResolveTargetSymbol(identifier, symbolsById);

        return new CallPathNode
        {
            Identifier = identifier,
            ContainingSymbol = containingSymbol,
            TargetSymbol = targetSymbol,
            Depth = depth,
            Direction = direction,
            IsSemanticMatch = false,
            Confidence = 1.0
        };
    }

    /// <summary>
    /// Resolve the target symbol for an identifier using name matching heuristics.
    /// Mirrors ReferenceResolverService logic.
    /// </summary>
    private JulieSymbol? ResolveTargetSymbol(
        JulieIdentifier identifier,
        Dictionary<string, JulieSymbol> symbolsById)
    {
        // If already resolved, use it
        if (!string.IsNullOrEmpty(identifier.TargetSymbolId) &&
            symbolsById.TryGetValue(identifier.TargetSymbolId, out var preResolved))
        {
            return preResolved;
        }

        // Find matching symbols by name
        var candidates = symbolsById.Values
            .Where(s => s.Name.Equals(identifier.Name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (!candidates.Any())
        {
            return null;
        }

        // Heuristic 1: Prefer symbols in the same file
        var sameFileSymbol = candidates.FirstOrDefault(s => s.FilePath == identifier.FilePath);
        if (sameFileSymbol != null)
        {
            return sameFileSymbol;
        }

        // Heuristic 2: For method calls, prefer function/method symbols
        if (identifier.Kind == "call")
        {
            var methodSymbol = candidates.FirstOrDefault(s =>
                s.Kind == "function" || s.Kind == "method");
            if (methodSymbol != null)
            {
                return methodSymbol;
            }
        }

        // Fallback: Return first candidate
        return candidates.First();
    }

    /// <summary>
    /// Select the best implementation when multiple symbols with the same name exist.
    /// Prefers actual implementations over interface declarations.
    /// </summary>
    private JulieSymbol SelectBestImplementation(List<JulieSymbol> candidates)
    {
        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        // Heuristic 1: Prefer symbols with larger line ranges (implementations have more lines than declarations)
        var largestByLineCount = candidates
            .OrderByDescending(s => s.EndLine - s.StartLine)
            .FirstOrDefault();

        // Heuristic 2: Avoid interface files (typically named I{Name}.cs)
        var nonInterfaceFile = candidates
            .Where(s => !Path.GetFileName(s.FilePath).StartsWith("I", StringComparison.Ordinal))
            .OrderByDescending(s => s.EndLine - s.StartLine)
            .FirstOrDefault();

        if (nonInterfaceFile != null && (nonInterfaceFile.EndLine - nonInterfaceFile.StartLine) >= 3)
        {
            // If we found a non-interface file with at least 3 lines (likely an implementation), prefer it
            return nonInterfaceFile;
        }

        // Heuristic 3: Prefer the one with the most lines (likely the implementation)
        return largestByLineCount ?? candidates[0];
    }
}
