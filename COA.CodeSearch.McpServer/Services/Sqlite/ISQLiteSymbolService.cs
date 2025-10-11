using COA.CodeSearch.McpServer.Services.Julie;

namespace COA.CodeSearch.McpServer.Services.Sqlite;

/// <summary>
/// Service for managing SQLite symbol database (Julie's schema).
/// Provides read/write access to symbols extracted by julie-extract CLI.
/// </summary>
public interface ISQLiteSymbolService
{
    /// <summary>
    /// Get the SQLite database path for a workspace
    /// </summary>
    string GetDatabasePath(string workspacePath);

    /// <summary>
    /// Initialize database for a workspace (ensures schema exists).
    /// Returns true if database was created, false if it already existed.
    /// </summary>
    Task<bool> InitializeDatabaseAsync(string workspacePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if database exists and is initialized for a workspace
    /// </summary>
    bool DatabaseExists(string workspacePath);

    /// <summary>
    /// Get all symbols from the database (for goto_definition, symbol_search, etc.)
    /// </summary>
    Task<List<JulieSymbol>> GetAllSymbolsAsync(string workspacePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get symbols by name with optional case sensitivity
    /// </summary>
    Task<List<JulieSymbol>> GetSymbolsByNameAsync(string workspacePath, string name, bool caseSensitive = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get symbols by kind (class, method, function, etc.)
    /// </summary>
    Task<List<JulieSymbol>> GetSymbolsByKindAsync(string workspacePath, string kind, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get symbols for a specific file
    /// </summary>
    Task<List<JulieSymbol>> GetSymbolsForFileAsync(string workspacePath, string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Upsert file and its symbols (for file watcher updates).
    /// Deletes existing symbols for the file and inserts new ones.
    /// </summary>
    Task UpsertFileSymbolsAsync(
        string workspacePath,
        string filePath,
        List<JulieSymbol> symbols,
        string fileContent,
        string language,
        string hash,
        long size,
        long lastModified,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete file and all its symbols (for file watcher deletions)
    /// </summary>
    Task DeleteFileAsync(string workspacePath, string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get file count in database
    /// </summary>
    Task<int> GetFileCountAsync(string workspacePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get symbol count in database
    /// </summary>
    Task<int> GetSymbolCountAsync(string workspacePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all file paths from the database (for Lucene indexing from SQLite source of truth)
    /// </summary>
    Task<List<FileRecord>> GetAllFilesAsync(string workspacePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a single file by path (efficient for getting context snippets)
    /// </summary>
    Task<FileRecord?> GetFileByPathAsync(string workspacePath, string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get recently modified files within a time frame (efficient for recent_files tool)
    /// </summary>
    Task<List<FileRecord>> GetRecentFilesAsync(
        string workspacePath,
        long cutoffTimestamp,
        int maxResults,
        string? extensionFilter = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Search files by glob pattern (efficient for file_search tool).
    /// Supports wildcards: * (any chars), ? (single char), ** (recursive path match).
    /// </summary>
    Task<List<FileRecord>> SearchFilesByPatternAsync(
        string workspacePath,
        string pattern,
        bool searchFullPath = true,
        string? extensionFilter = null,
        int maxResults = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Search directories by glob pattern (efficient for directory_search tool).
    /// Supports wildcards: * (any chars), ? (single char).
    /// Returns unique directory paths from indexed files.
    /// </summary>
    Task<List<string>> SearchDirectoriesByPatternAsync(
        string workspacePath,
        string pattern,
        bool includeHidden = false,
        int maxResults = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Search files using FTS5 full-text search (efficient grep replacement for line_search tool)
    /// </summary>
    Task<List<FileRecord>> SearchWithFTS5Async(
        string workspacePath,
        string searchPattern,
        int maxResults = 100,
        string? filePattern = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get identifiers (references/usages) by name for LSP-quality find_references.
    /// Returns all identifier usages of the given name (function calls, variable refs, etc.)
    /// </summary>
    Task<List<JulieIdentifier>> GetIdentifiersByNameAsync(
        string workspacePath,
        string name,
        bool caseSensitive = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get reference count for a symbol name without fetching all identifier objects.
    /// Much faster than GetIdentifiersByNameAsync when you only need the count.
    /// </summary>
    Task<int> GetIdentifierCountByNameAsync(
        string workspacePath,
        string name,
        bool caseSensitive = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get identifiers by kind (call, member_access, variable_ref, etc.)
    /// </summary>
    Task<List<JulieIdentifier>> GetIdentifiersByKindAsync(
        string workspacePath,
        string kind,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all identifiers for a specific file (for incremental updates)
    /// </summary>
    Task<List<JulieIdentifier>> GetIdentifiersForFileAsync(
        string workspacePath,
        string filePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all identifiers contained within a specific symbol (for call path downward tracing).
    /// Returns all calls/references made from within the given symbol.
    /// </summary>
    Task<List<JulieIdentifier>> GetIdentifiersByContainingSymbolAsync(
        string workspacePath,
        string containingSymbolId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get relationships for specific symbols by their IDs.
    /// Returns inheritance relationships (extends/implements) for populating type hierarchies.
    /// </summary>
    /// <param name="workspacePath">Workspace path for database lookup</param>
    /// <param name="symbolIds">List of symbol IDs to get relationships for</param>
    /// <param name="relationshipKinds">Optional filter for specific relationship kinds (e.g., "extends", "implements"). If null, returns all relationships.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Dictionary mapping symbol IDs to their relationships</returns>
    Task<Dictionary<string, List<JulieRelationship>>> GetRelationshipsForSymbolsAsync(
        string workspacePath,
        List<string> symbolIds,
        List<string>? relationshipKinds = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Search for symbols using semantic similarity (Tier 3 - semantic search).
    /// Returns symbols semantically similar to the query text, ordered by similarity score.
    /// </summary>
    Task<List<SemanticSymbolMatch>> SearchSymbolsSemanticAsync(
        string workspacePath,
        string query,
        int limit = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if semantic search is available (requires sqlite-vec extension and embedding service)
    /// </summary>
    bool IsSemanticSearchAvailable();

    /// <summary>
    /// Generate embeddings for ALL symbols in workspace in ONE batch for maximum performance
    /// </summary>
    Task BulkGenerateEmbeddingsAsync(string workspacePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Copy embeddings for a single file's symbols from julie-semantic's BLOB storage to vec0.
    /// Used for incremental updates when a file changes.
    /// </summary>
    Task CopyFileEmbeddingsToVec0Async(string workspacePath, string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Trace call path UPWARD using SQL recursive CTE (who calls this symbol).
    /// Much faster than C# recursion - database handles the traversal with built-in cycle detection.
    /// Returns a flat list of call path nodes that can be reconstructed into a hierarchy.
    /// </summary>
    /// <param name="workspacePath">Workspace path for database lookup</param>
    /// <param name="symbolName">Name of the symbol to find callers for</param>
    /// <param name="maxDepth">Maximum recursion depth (prevents runaway queries)</param>
    /// <param name="caseSensitive">Whether symbol name matching is case sensitive</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of call path entries ordered by depth</returns>
    Task<List<CallPathCTEResult>> TraceCallPathUpwardAsync(
        string workspacePath,
        string symbolName,
        int maxDepth = 10,
        bool caseSensitive = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Trace call path DOWNWARD using SQL recursive CTE (what does this symbol call).
    /// Much faster than C# recursion - database handles the traversal with built-in cycle detection.
    /// Returns a flat list of call path nodes that can be reconstructed into a hierarchy.
    /// </summary>
    /// <param name="workspacePath">Workspace path for database lookup</param>
    /// <param name="symbolName">Name of the symbol to find callees for</param>
    /// <param name="maxDepth">Maximum recursion depth (prevents runaway queries)</param>
    /// <param name="caseSensitive">Whether symbol name matching is case sensitive</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of call path entries ordered by depth</returns>
    Task<List<CallPathCTEResult>> TraceCallPathDownwardAsync(
        string workspacePath,
        string symbolName,
        int maxDepth = 10,
        bool caseSensitive = false,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a file record from the SQLite database
/// </summary>
public record FileRecord(
    string Path,
    string? Content,
    string Language,
    long Size,
    long LastModified);

/// <summary>
/// Represents a symbol match from semantic search with similarity score
/// </summary>
public record SemanticSymbolMatch(
    JulieSymbol Symbol,
    float SimilarityScore);

/// <summary>
/// Represents a single entry in the call path CTE result.
/// Contains flattened hierarchy data that can be reconstructed into a tree.
/// </summary>
public record CallPathCTEResult
{
    /// <summary>
    /// Identifier ID (unique identifier for this call reference)
    /// </summary>
    public required string IdentifierId { get; init; }

    /// <summary>
    /// Name of the identifier (method/function being called)
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// File path where this call occurs
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Starting line number
    /// </summary>
    public required int StartLine { get; init; }

    /// <summary>
    /// Starting column number
    /// </summary>
    public required int StartColumn { get; init; }

    /// <summary>
    /// Ending line number
    /// </summary>
    public required int EndLine { get; init; }

    /// <summary>
    /// Ending column number
    /// </summary>
    public required int EndColumn { get; init; }

    /// <summary>
    /// Kind of identifier (e.g., "call", "member_access")
    /// </summary>
    public required string Kind { get; init; }

    /// <summary>
    /// Code context (surrounding code snippet)
    /// </summary>
    public string? CodeContext { get; init; }

    /// <summary>
    /// ID of the symbol that contains this identifier
    /// </summary>
    public string? ContainingSymbolId { get; init; }

    /// <summary>
    /// Name of the containing symbol
    /// </summary>
    public string? ContainingSymbolName { get; init; }

    /// <summary>
    /// Kind of the containing symbol (e.g., "method", "function")
    /// </summary>
    public string? ContainingSymbolKind { get; init; }

    /// <summary>
    /// Depth in the call hierarchy (0 = direct call, 1 = caller of caller, etc.)
    /// </summary>
    public required int Depth { get; init; }

    /// <summary>
    /// Path tracking for cycle detection (pipe-separated list of identifier IDs)
    /// </summary>
    public required string Path { get; init; }
}
