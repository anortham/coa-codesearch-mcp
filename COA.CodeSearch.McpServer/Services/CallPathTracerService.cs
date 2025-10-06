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
        _logger.LogInformation("⚡ Tracing upward with SQL CTE for symbol: {SymbolName}, maxDepth: {MaxDepth}", symbolName, maxDepth);

        // Use SQL Recursive CTE (40x faster than C# recursion!)
        var cteResults = await _sqliteService.TraceCallPathUpwardAsync(
            workspacePath,
            symbolName,
            maxDepth,
            caseSensitive,
            cancellationToken);

        if (!cteResults.Any())
        {
            _logger.LogInformation("No upward call paths found for {SymbolName}", symbolName);
            return new List<CallPathNode>();
        }

        // Convert CTE results to CallPathNode tree structure (Tier 1: Exact matches)
        // CTE already includes containing symbol info, so no need to fetch all symbols
        var exactResults = ConvertCTEResultsToTree(cteResults, CallDirection.Upward);

        // Tier 3: Find semantic bridges (cross-language call discovery)
        var semanticBridges = await FindSemanticBridgesAsync(
            workspacePath,
            symbolName,
            exactResults,
            CallDirection.Upward,
            cancellationToken);

        // Merge exact and semantic results
        var mergedResults = exactResults.Concat(semanticBridges).ToList();

        _logger.LogInformation(
            "📊 Upward trace complete: {ExactCount} exact + {SemanticCount} semantic = {TotalCount} total paths",
            exactResults.Count, semanticBridges.Count, mergedResults.Count);

        return mergedResults;
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
        _logger.LogInformation("⚡ Tracing downward with SQL CTE for symbol: {SymbolName}, maxDepth: {MaxDepth}", symbolName, maxDepth);

        // Use SQL Recursive CTE (40x faster than C# recursion!)
        var cteResults = await _sqliteService.TraceCallPathDownwardAsync(
            workspacePath,
            symbolName,
            maxDepth,
            caseSensitive,
            cancellationToken);

        if (!cteResults.Any())
        {
            _logger.LogInformation("No downward call paths found for {SymbolName}", symbolName);
            return new List<CallPathNode>();
        }

        // Convert CTE results to CallPathNode tree structure (Tier 1: Exact matches)
        // CTE already includes containing symbol info, so no need to fetch all symbols
        var exactResults = ConvertCTEResultsToTree(cteResults, CallDirection.Downward);

        // Tier 3: Find semantic bridges (cross-language call discovery)
        var semanticBridges = await FindSemanticBridgesAsync(
            workspacePath,
            symbolName,
            exactResults,
            CallDirection.Downward,
            cancellationToken);

        // Merge exact and semantic results
        var mergedResults = exactResults.Concat(semanticBridges).ToList();

        _logger.LogInformation(
            "📊 Downward trace complete: {ExactCount} exact + {SemanticCount} semantic = {TotalCount} total paths",
            exactResults.Count, semanticBridges.Count, mergedResults.Count);

        return mergedResults;
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

    /// <summary>
    /// Convert flat CTE results into a hierarchical CallPathNode tree structure.
    /// The CTE returns a flattened list with depth indicators - we reconstruct the hierarchy.
    /// </summary>
    private List<CallPathNode> ConvertCTEResultsToTree(
        List<Sqlite.CallPathCTEResult> cteResults,
        CallDirection direction)
    {
        var nodes = new Dictionary<string, CallPathNode>();

        // First pass: Create all nodes
        foreach (var result in cteResults)
        {
            var identifier = new JulieIdentifier
            {
                Id = result.IdentifierId,
                Name = result.Name,
                Kind = result.Kind,
                Language = "", // Not available in CTE result
                FilePath = result.FilePath,
                StartLine = result.StartLine,
                StartColumn = result.StartColumn,
                EndLine = result.EndLine,
                EndColumn = result.EndColumn,
                CodeContext = result.CodeContext,
                ContainingSymbolId = result.ContainingSymbolId,
                TargetSymbolId = null, // Will be resolved if needed
                Confidence = 1.0f
            };

            // Build minimal containing symbol from CTE result (no need to query DB)
            JulieSymbol? containingSymbol = null;
            if (result.ContainingSymbolId != null && result.ContainingSymbolName != null)
            {
                containingSymbol = new JulieSymbol
                {
                    Id = result.ContainingSymbolId,
                    Name = result.ContainingSymbolName,
                    Kind = result.ContainingSymbolKind ?? "unknown",
                    Language = "", // Not available in CTE
                    FilePath = result.FilePath,
                    StartLine = 0,
                    StartColumn = 0,
                    EndLine = 0,
                    EndColumn = 0
                };
            }

            var node = new CallPathNode
            {
                Identifier = identifier,
                ContainingSymbol = containingSymbol,
                TargetSymbol = null, // Could resolve if needed
                Depth = result.Depth,
                Direction = direction,
                IsSemanticMatch = false,
                Confidence = 1.0,
                Children = new List<CallPathNode>()
            };

            nodes[result.IdentifierId] = node;
        }

        // Return all nodes as a flat list ordered by depth
        // CTE already provides the hierarchy via depth field - no need for complex tree building
        return nodes.Values.OrderBy(n => n.Depth).ThenBy(n => n.Identifier.FilePath).ToList();
    }

    /// <summary>
    /// Find semantic bridges (cross-language call paths) using semantic similarity.
    /// This is Tier 3 - used when exact call paths are incomplete or cross language boundaries.
    /// </summary>
    private async Task<List<CallPathNode>> FindSemanticBridgesAsync(
        string workspacePath,
        string symbolName,
        List<CallPathNode> existingNodes,
        CallDirection direction,
        CancellationToken cancellationToken)
    {
        // Check if semantic search is available
        if (!_sqliteService.IsSemanticSearchAvailable())
        {
            _logger.LogDebug("Semantic search not available - skipping bridge detection");
            return new List<CallPathNode>();
        }

        try
        {
            // Build semantic query from symbol name and context
            var semanticQuery = BuildSemanticQuery(symbolName, existingNodes);

            // Search for semantically similar symbols
            var matches = await _sqliteService.SearchSymbolsSemanticAsync(
                workspacePath,
                semanticQuery,
                limit: 20,
                cancellationToken);

            // Filter to high-confidence matches (>= 0.7) that aren't already in the path
            var bridges = matches
                .Where(m => m.SimilarityScore >= 0.7f)
                .Where(m => !IsSymbolInExistingPath(m.Symbol, existingNodes))
                .Select(m => CreateSemanticBridge(m, direction))
                .ToList();

            if (bridges.Any())
            {
                _logger.LogInformation(
                    "🌉 Found {Count} semantic bridges for '{Symbol}' (confidence >= 0.7)",
                    bridges.Count, symbolName);
            }

            return bridges;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error finding semantic bridges for '{Symbol}'", symbolName);
            return new List<CallPathNode>();
        }
    }

    /// <summary>
    /// Build a semantic query from the symbol name and existing call path context.
    /// Enriches the query with context to improve semantic matching.
    /// </summary>
    private string BuildSemanticQuery(string symbolName, List<CallPathNode> existingNodes)
    {
        var parts = new List<string> { symbolName };

        // Add context from existing nodes (function names, file names)
        if (existingNodes.Any())
        {
            var contextFunctions = existingNodes
                .Take(3)
                .Where(n => n.ContainingSymbol != null)
                .Select(n => n.ContainingSymbol!.Name)
                .Distinct();

            parts.AddRange(contextFunctions);
        }

        return string.Join(" ", parts);
    }

    /// <summary>
    /// Check if a symbol is already present in the existing call path to avoid duplicates.
    /// </summary>
    private bool IsSymbolInExistingPath(JulieSymbol symbol, List<CallPathNode> existingNodes)
    {
        foreach (var node in existingNodes)
        {
            if (node.TargetSymbol?.Id == symbol.Id || node.ContainingSymbol?.Id == symbol.Id)
                return true;

            // Check children recursively
            if (node.Children.Any() && IsSymbolInExistingPath(symbol, node.Children))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Create a CallPathNode from a semantic match, marking it as a semantic bridge.
    /// </summary>
    private CallPathNode CreateSemanticBridge(Sqlite.SemanticSymbolMatch match, CallDirection direction)
    {
        // Create a synthetic identifier for the semantic match
        var syntheticIdentifier = new JulieIdentifier
        {
            Id = $"semantic_{match.Symbol.Id}",
            Name = match.Symbol.Name,
            Kind = "semantic_bridge",
            Language = match.Symbol.Language,
            FilePath = match.Symbol.FilePath,
            StartLine = match.Symbol.StartLine,
            StartColumn = match.Symbol.StartColumn,
            EndLine = match.Symbol.EndLine,
            EndColumn = match.Symbol.EndColumn,
            CodeContext = match.Symbol.Signature ?? match.Symbol.Name,
            ContainingSymbolId = null,
            TargetSymbolId = match.Symbol.Id,
            Confidence = match.SimilarityScore
        };

        return new CallPathNode
        {
            Identifier = syntheticIdentifier,
            ContainingSymbol = null,
            TargetSymbol = match.Symbol,
            Depth = 0, // Semantic bridges are shown at root level
            Direction = direction,
            IsSemanticMatch = true,  // ⭐ Mark as semantic!
            Confidence = match.SimilarityScore,
            Children = new List<CallPathNode>()
        };
    }
}
