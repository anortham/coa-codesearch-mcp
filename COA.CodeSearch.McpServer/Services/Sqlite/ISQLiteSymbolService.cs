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
    /// Get symbols by name (exact match)
    /// </summary>
    Task<List<JulieSymbol>> GetSymbolsByNameAsync(string workspacePath, string name, CancellationToken cancellationToken = default);

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
}
