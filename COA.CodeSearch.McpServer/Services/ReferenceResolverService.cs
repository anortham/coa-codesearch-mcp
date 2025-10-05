using COA.CodeSearch.McpServer.Services.Julie;
using COA.CodeSearch.McpServer.Services.Sqlite;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Service for resolving identifier references to their target symbols on-demand.
/// Implements LSP-quality find_references without pre-resolving all references at extraction time.
/// </summary>
public interface IReferenceResolverService
{
    /// <summary>
    /// Find all references to a symbol by name.
    /// Returns resolved references with both identifier data and target symbol information.
    /// </summary>
    Task<List<ResolvedReference>> FindReferencesAsync(
        string workspacePath,
        string symbolName,
        bool caseSensitive = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Find all references to a specific symbol by ID.
    /// </summary>
    Task<List<ResolvedReference>> FindReferencesBySymbolIdAsync(
        string workspacePath,
        string symbolId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a resolved reference with both identifier and target symbol information
/// </summary>
public class ResolvedReference
{
    /// <summary>
    /// The identifier (reference/usage)
    /// </summary>
    public required JulieIdentifier Identifier { get; init; }

    /// <summary>
    /// The symbol that contains this identifier (e.g., which function contains this call)
    /// </summary>
    public JulieSymbol? ContainingSymbol { get; init; }

    /// <summary>
    /// The symbol this identifier refers to (resolved on-demand)
    /// </summary>
    public JulieSymbol? TargetSymbol { get; init; }

    /// <summary>
    /// Indicates if the target symbol was successfully resolved
    /// </summary>
    public bool IsResolved => TargetSymbol != null;
}

public class ReferenceResolverService : IReferenceResolverService
{
    private readonly ISQLiteSymbolService _sqliteService;
    private readonly ILogger<ReferenceResolverService> _logger;

    public ReferenceResolverService(
        ISQLiteSymbolService sqliteService,
        ILogger<ReferenceResolverService> logger)
    {
        _sqliteService = sqliteService ?? throw new ArgumentNullException(nameof(sqliteService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<List<ResolvedReference>> FindReferencesAsync(
        string workspacePath,
        string symbolName,
        bool caseSensitive = false,
        CancellationToken cancellationToken = default)
    {
        var resolvedReferences = new List<ResolvedReference>();

        // Step 1: Get all identifiers with the given name
        var identifiers = await _sqliteService.GetIdentifiersByNameAsync(
            workspacePath,
            symbolName,
            caseSensitive,
            cancellationToken);

        if (!identifiers.Any())
        {
            _logger.LogDebug("No identifiers found for symbol name: {SymbolName}", symbolName);
            return resolvedReferences;
        }

        _logger.LogInformation("Found {Count} identifier usages for '{SymbolName}'", identifiers.Count, symbolName);

        // Step 2: Get all symbols for resolution (cached in a dictionary for performance)
        var allSymbols = await _sqliteService.GetAllSymbolsAsync(workspacePath, cancellationToken);
        var symbolsById = allSymbols.ToDictionary(s => s.Id, s => s);

        // Step 3: Resolve each identifier
        foreach (var identifier in identifiers)
        {
            var resolved = ResolveIdentifier(identifier, symbolsById);
            resolvedReferences.Add(resolved);
        }

        var resolvedCount = resolvedReferences.Count(r => r.IsResolved);
        _logger.LogInformation("Resolved {ResolvedCount}/{TotalCount} references", resolvedCount, resolvedReferences.Count);

        return resolvedReferences;
    }

    public async Task<List<ResolvedReference>> FindReferencesBySymbolIdAsync(
        string workspacePath,
        string symbolId,
        CancellationToken cancellationToken = default)
    {
        var resolvedReferences = new List<ResolvedReference>();

        // Get the target symbol first
        var allSymbols = await _sqliteService.GetAllSymbolsAsync(workspacePath, cancellationToken);
        var targetSymbol = allSymbols.FirstOrDefault(s => s.Id == symbolId);

        if (targetSymbol == null)
        {
            _logger.LogWarning("Symbol not found: {SymbolId}", symbolId);
            return resolvedReferences;
        }

        // Find all identifiers that match the symbol name
        return await FindReferencesAsync(
            workspacePath,
            targetSymbol.Name,
            caseSensitive: false, // Match references regardless of case
            cancellationToken);
    }

    /// <summary>
    /// Resolve an identifier to its containing and target symbols.
    /// This is where the "on-demand" resolution happens.
    /// </summary>
    private ResolvedReference ResolveIdentifier(
        JulieIdentifier identifier,
        Dictionary<string, JulieSymbol> symbolsById)
    {
        JulieSymbol? containingSymbol = null;
        JulieSymbol? targetSymbol = null;

        // Resolve containing symbol (which function/class contains this identifier usage)
        if (!string.IsNullOrEmpty(identifier.ContainingSymbolId) &&
            symbolsById.TryGetValue(identifier.ContainingSymbolId, out var containing))
        {
            containingSymbol = containing;
        }

        // Resolve target symbol (what does this identifier refer to)
        // Strategy: Find a symbol with matching name in the same file or visible scope
        targetSymbol = ResolveTargetSymbol(identifier, symbolsById);

        return new ResolvedReference
        {
            Identifier = identifier,
            ContainingSymbol = containingSymbol,
            TargetSymbol = targetSymbol
        };
    }

    /// <summary>
    /// Resolve the target symbol for an identifier.
    /// Uses simple name matching with scope-based heuristics.
    /// </summary>
    private JulieSymbol? ResolveTargetSymbol(
        JulieIdentifier identifier,
        Dictionary<string, JulieSymbol> symbolsById)
    {
        // If already resolved (target_symbol_id set), use it
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

        // Heuristic 3: For member access, prefer field/property symbols
        if (identifier.Kind == "member_access")
        {
            var memberSymbol = candidates.FirstOrDefault(s =>
                s.Kind == "field" || s.Kind == "property");
            if (memberSymbol != null)
            {
                return memberSymbol;
            }
        }

        // Fallback: Return first candidate
        return candidates.First();
    }
}
