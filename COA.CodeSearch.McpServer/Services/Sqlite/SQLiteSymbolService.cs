using COA.CodeSearch.McpServer.Services.Julie;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Services.Sqlite;

/// <summary>
/// Service for managing SQLite symbol database (Julie's schema).
/// Works with databases created by julie-extract CLI or creates schema manually.
/// </summary>
public class SQLiteSymbolService : ISQLiteSymbolService
{
    private readonly ILogger<SQLiteSymbolService> _logger;
    private readonly IPathResolutionService _pathResolution;
    private const string WorkspaceId = "primary"; // Julie's default workspace ID

    public SQLiteSymbolService(
        ILogger<SQLiteSymbolService> logger,
        IPathResolutionService pathResolution)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _pathResolution = pathResolution ?? throw new ArgumentNullException(nameof(pathResolution));
    }

    public string GetDatabasePath(string workspacePath)
    {
        var indexPath = _pathResolution.GetIndexPath(workspacePath);
        var dbDirectory = Path.Combine(indexPath, "db");

        // Ensure db directory exists
        if (!Directory.Exists(dbDirectory))
        {
            Directory.CreateDirectory(dbDirectory);
        }

        return Path.Combine(dbDirectory, "metadata.db");
    }

    public bool DatabaseExists(string workspacePath)
    {
        var dbPath = GetDatabasePath(workspacePath);
        return File.Exists(dbPath);
    }

    public async Task<bool> InitializeDatabaseAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        var dbPath = GetDatabasePath(workspacePath);
        var existed = File.Exists(dbPath);

        if (!existed)
        {
            // Create directory if it doesn't exist
            var directory = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _logger.LogInformation("Creating new SQLite database at {DbPath}", dbPath);
        }

        // Open connection and ensure schema exists (idempotent)
        // Disable pooling to ensure clean connection close
        var connectionString = $"Data Source={dbPath};Pooling=false";
        using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        // Enable WAL mode for concurrent access
        using (var walCmd = connection.CreateCommand())
        {
            walCmd.CommandText = "PRAGMA journal_mode=WAL;";
            await walCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // Create schema (idempotent - uses IF NOT EXISTS)
        await CreateSchemaAsync(connection, cancellationToken);

        // Checkpoint WAL and close cleanly to release locks for external tools
        using (var checkpointCmd = connection.CreateCommand())
        {
            checkpointCmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            await checkpointCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // Explicitly close and dispose before external tool access
        await connection.CloseAsync();
        await connection.DisposeAsync();

        // Clear any lingering connection pool entries
        SqliteConnection.ClearAllPools();

        return !existed;
    }

    private async Task CreateSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        // Create files table (matches Julie's schema)
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS files (
                    path TEXT PRIMARY KEY,
                    language TEXT NOT NULL,
                    hash TEXT NOT NULL,
                    size INTEGER NOT NULL,
                    last_modified INTEGER NOT NULL,
                    last_indexed INTEGER DEFAULT 0,
                    parse_cache BLOB,
                    symbol_count INTEGER DEFAULT 0,
                    content TEXT,
                    workspace_id TEXT NOT NULL DEFAULT 'primary'
                )";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // Create indexes on files table
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE INDEX IF NOT EXISTS idx_files_language ON files(language);
                CREATE INDEX IF NOT EXISTS idx_files_modified ON files(last_modified);
                CREATE INDEX IF NOT EXISTS idx_files_workspace ON files(workspace_id);";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // Create symbols table (matches Julie's schema)
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS symbols (
                    id TEXT PRIMARY KEY,
                    name TEXT NOT NULL,
                    kind TEXT NOT NULL,
                    language TEXT NOT NULL,
                    file_path TEXT NOT NULL REFERENCES files(path) ON DELETE CASCADE,
                    signature TEXT,
                    start_line INTEGER,
                    start_col INTEGER,
                    end_line INTEGER,
                    end_col INTEGER,
                    start_byte INTEGER,
                    end_byte INTEGER,
                    doc_comment TEXT,
                    visibility TEXT,
                    code_context TEXT,
                    parent_id TEXT REFERENCES symbols(id),
                    metadata TEXT,
                    file_hash TEXT,
                    last_indexed INTEGER DEFAULT 0,
                    semantic_group TEXT,
                    confidence REAL DEFAULT 1.0,
                    workspace_id TEXT NOT NULL DEFAULT 'primary'
                )";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // Create indexes on symbols table
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE INDEX IF NOT EXISTS idx_symbols_name ON symbols(name);
                CREATE INDEX IF NOT EXISTS idx_symbols_kind ON symbols(kind);
                CREATE INDEX IF NOT EXISTS idx_symbols_language ON symbols(language);
                CREATE INDEX IF NOT EXISTS idx_symbols_file ON symbols(file_path);
                CREATE INDEX IF NOT EXISTS idx_symbols_semantic ON symbols(semantic_group);
                CREATE INDEX IF NOT EXISTS idx_symbols_parent ON symbols(parent_id);
                CREATE INDEX IF NOT EXISTS idx_symbols_workspace ON symbols(workspace_id);";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        _logger.LogDebug("SQLite schema initialized");
    }

    public async Task<List<JulieSymbol>> GetAllSymbolsAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        var dbPath = GetDatabasePath(workspacePath);
        if (!File.Exists(dbPath))
        {
            return new List<JulieSymbol>();
        }

        using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM symbols WHERE workspace_id = @workspace_id";
        cmd.Parameters.AddWithValue("@workspace_id", WorkspaceId);

        return await ReadSymbolsAsync(cmd, cancellationToken);
    }

    public async Task<List<JulieSymbol>> GetSymbolsByNameAsync(string workspacePath, string name, CancellationToken cancellationToken = default)
    {
        var dbPath = GetDatabasePath(workspacePath);
        if (!File.Exists(dbPath))
        {
            return new List<JulieSymbol>();
        }

        using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM symbols WHERE workspace_id = @workspace_id AND name = @name";
        cmd.Parameters.AddWithValue("@workspace_id", WorkspaceId);
        cmd.Parameters.AddWithValue("@name", name);

        return await ReadSymbolsAsync(cmd, cancellationToken);
    }

    public async Task<List<JulieSymbol>> GetSymbolsByKindAsync(string workspacePath, string kind, CancellationToken cancellationToken = default)
    {
        var dbPath = GetDatabasePath(workspacePath);
        if (!File.Exists(dbPath))
        {
            return new List<JulieSymbol>();
        }

        using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM symbols WHERE workspace_id = @workspace_id AND kind = @kind";
        cmd.Parameters.AddWithValue("@workspace_id", WorkspaceId);
        cmd.Parameters.AddWithValue("@kind", kind);

        return await ReadSymbolsAsync(cmd, cancellationToken);
    }

    public async Task<List<JulieSymbol>> GetSymbolsForFileAsync(string workspacePath, string filePath, CancellationToken cancellationToken = default)
    {
        var dbPath = GetDatabasePath(workspacePath);
        if (!File.Exists(dbPath))
        {
            return new List<JulieSymbol>();
        }

        using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM symbols WHERE workspace_id = @workspace_id AND file_path = @file_path";
        cmd.Parameters.AddWithValue("@workspace_id", WorkspaceId);
        cmd.Parameters.AddWithValue("@file_path", filePath);

        return await ReadSymbolsAsync(cmd, cancellationToken);
    }

    public async Task UpsertFileSymbolsAsync(
        string workspacePath,
        string filePath,
        List<JulieSymbol> symbols,
        string fileContent,
        string language,
        string hash,
        long size,
        long lastModified,
        CancellationToken cancellationToken = default)
    {
        var dbPath = GetDatabasePath(workspacePath);
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync(cancellationToken);

        using var transaction = connection.BeginTransaction();

        try
        {
            // Upsert file record
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = @"
                    INSERT OR REPLACE INTO files
                    (path, language, hash, size, last_modified, last_indexed, symbol_count, content, workspace_id)
                    VALUES (@path, @language, @hash, @size, @last_modified, @last_indexed, @symbol_count, @content, @workspace_id)";

                cmd.Parameters.AddWithValue("@path", filePath);
                cmd.Parameters.AddWithValue("@language", language);
                cmd.Parameters.AddWithValue("@hash", hash);
                cmd.Parameters.AddWithValue("@size", size);
                cmd.Parameters.AddWithValue("@last_modified", lastModified);
                cmd.Parameters.AddWithValue("@last_indexed", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                cmd.Parameters.AddWithValue("@symbol_count", symbols.Count);
                cmd.Parameters.AddWithValue("@content", fileContent);
                cmd.Parameters.AddWithValue("@workspace_id", WorkspaceId);

                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            // Delete old symbols for this file
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "DELETE FROM symbols WHERE file_path = @file_path AND workspace_id = @workspace_id";
                cmd.Parameters.AddWithValue("@file_path", filePath);
                cmd.Parameters.AddWithValue("@workspace_id", WorkspaceId);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            // Insert new symbols
            foreach (var symbol in symbols)
            {
                using var cmd = connection.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = @"
                    INSERT INTO symbols
                    (id, name, kind, language, file_path, signature, start_line, start_col, end_line, end_col,
                     start_byte, end_byte, doc_comment, visibility, parent_id, workspace_id, file_hash, last_indexed)
                    VALUES (@id, @name, @kind, @language, @file_path, @signature, @start_line, @start_col, @end_line, @end_col,
                            @start_byte, @end_byte, @doc_comment, @visibility, @parent_id, @workspace_id, @file_hash, @last_indexed)";

                cmd.Parameters.AddWithValue("@id", symbol.Id);
                cmd.Parameters.AddWithValue("@name", symbol.Name);
                cmd.Parameters.AddWithValue("@kind", symbol.Kind);
                cmd.Parameters.AddWithValue("@language", symbol.Language);
                cmd.Parameters.AddWithValue("@file_path", symbol.FilePath);
                cmd.Parameters.AddWithValue("@signature", (object?)symbol.Signature ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@start_line", symbol.StartLine);
                cmd.Parameters.AddWithValue("@start_col", symbol.StartColumn);
                cmd.Parameters.AddWithValue("@end_line", symbol.EndLine);
                cmd.Parameters.AddWithValue("@end_col", symbol.EndColumn);
                cmd.Parameters.AddWithValue("@start_byte", 0); // Not provided by julie-extract JSON
                cmd.Parameters.AddWithValue("@end_byte", 0);
                cmd.Parameters.AddWithValue("@doc_comment", (object?)symbol.DocComment ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@visibility", (object?)symbol.Visibility ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@parent_id", (object?)symbol.ParentId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@workspace_id", WorkspaceId);
                cmd.Parameters.AddWithValue("@file_hash", hash);
                cmd.Parameters.AddWithValue("@last_indexed", DateTimeOffset.UtcNow.ToUnixTimeSeconds());

                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);

            _logger.LogDebug("Upserted file {FilePath} with {SymbolCount} symbols", filePath, symbols.Count);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task DeleteFileAsync(string workspacePath, string filePath, CancellationToken cancellationToken = default)
    {
        var dbPath = GetDatabasePath(workspacePath);
        if (!File.Exists(dbPath))
        {
            return;
        }

        using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM files WHERE path = @path AND workspace_id = @workspace_id";
        cmd.Parameters.AddWithValue("@path", filePath);
        cmd.Parameters.AddWithValue("@workspace_id", WorkspaceId);

        await cmd.ExecuteNonQueryAsync(cancellationToken);

        // Symbols cascade delete automatically via foreign key

        _logger.LogDebug("Deleted file {FilePath}", filePath);
    }

    public async Task<int> GetFileCountAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        var dbPath = GetDatabasePath(workspacePath);
        if (!File.Exists(dbPath))
        {
            return 0;
        }

        using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM files WHERE workspace_id = @workspace_id";
        cmd.Parameters.AddWithValue("@workspace_id", WorkspaceId);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }

    public async Task<int> GetSymbolCountAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        var dbPath = GetDatabasePath(workspacePath);
        if (!File.Exists(dbPath))
        {
            return 0;
        }

        using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM symbols WHERE workspace_id = @workspace_id";
        cmd.Parameters.AddWithValue("@workspace_id", WorkspaceId);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }

    private async Task<List<JulieSymbol>> ReadSymbolsAsync(SqliteCommand cmd, CancellationToken cancellationToken)
    {
        var symbols = new List<JulieSymbol>();

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            symbols.Add(new JulieSymbol
            {
                Id = reader.GetString(reader.GetOrdinal("id")),
                Name = reader.GetString(reader.GetOrdinal("name")),
                Kind = reader.GetString(reader.GetOrdinal("kind")),
                Language = reader.GetString(reader.GetOrdinal("language")),
                FilePath = reader.GetString(reader.GetOrdinal("file_path")),
                Signature = reader.IsDBNull(reader.GetOrdinal("signature")) ? null : reader.GetString(reader.GetOrdinal("signature")),
                StartLine = reader.GetInt32(reader.GetOrdinal("start_line")),
                StartColumn = reader.GetInt32(reader.GetOrdinal("start_col")),
                EndLine = reader.GetInt32(reader.GetOrdinal("end_line")),
                EndColumn = reader.GetInt32(reader.GetOrdinal("end_col")),
                DocComment = reader.IsDBNull(reader.GetOrdinal("doc_comment")) ? null : reader.GetString(reader.GetOrdinal("doc_comment")),
                Visibility = reader.IsDBNull(reader.GetOrdinal("visibility")) ? null : reader.GetString(reader.GetOrdinal("visibility")),
                ParentId = reader.IsDBNull(reader.GetOrdinal("parent_id")) ? null : reader.GetString(reader.GetOrdinal("parent_id"))
            });
        }

        return symbols;
    }
}
