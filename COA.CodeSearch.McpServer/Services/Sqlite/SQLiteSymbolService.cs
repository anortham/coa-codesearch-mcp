using System.IO;
using System.Text.RegularExpressions;
using COA.CodeSearch.McpServer.Services.Embeddings;
using COA.CodeSearch.McpServer.Services.Julie;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Services.Sqlite;

/// <summary>
/// Service for managing SQLite symbol database (Julie's schema).
/// Works with databases created by julie-extract CLI or creates schema manually.
/// Supports semantic search via sqlite-vec extension and ONNX embeddings.
/// </summary>
public class SQLiteSymbolService : ISQLiteSymbolService
{
    private readonly ILogger<SQLiteSymbolService> _logger;
    private readonly IPathResolutionService _pathResolution;
    private readonly ISqliteVecExtensionService _vecExtension;
    private readonly IEmbeddingService _embeddingService;

    public SQLiteSymbolService(
        ILogger<SQLiteSymbolService> logger,
        IPathResolutionService pathResolution,
        ISqliteVecExtensionService vecExtension,
        IEmbeddingService embeddingService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _pathResolution = pathResolution ?? throw new ArgumentNullException(nameof(pathResolution));
        _vecExtension = vecExtension ?? throw new ArgumentNullException(nameof(vecExtension));
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
    }

    /// <summary>
    /// Creates an optimized connection string with connection pooling enabled.
    /// Cache=Shared allows multiple connections to share the same page cache for better performance.
    /// </summary>
    private string GetConnectionString(string dbPath, bool enablePooling = true)
    {
        if (enablePooling)
        {
            // Pooling=true is default, but explicit for clarity
            // Cache=Shared enables shared page cache across connections
            return $"Data Source={dbPath};Cache=Shared";
        }
        else
        {
            // Disable pooling for initialization/cleanup operations
            return $"Data Source={dbPath};Pooling=false";
        }
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

        return Path.Combine(dbDirectory, "workspace.db");
    }

    public bool DatabaseExists(string workspacePath)
    {
        var dbPath = GetDatabasePath(workspacePath);
        var exists = File.Exists(dbPath);
        _logger.LogDebug("DatabaseExists check: {DbPath} → {Exists}", dbPath, exists);
        return exists;
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
        var connectionString = GetConnectionString(dbPath, enablePooling: false);
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
        // Load sqlite-vec extension for semantic search
        if (_vecExtension.IsAvailable())
        {
            try
            {
                _vecExtension.LoadExtension(connection);
                _logger.LogInformation("✅ sqlite-vec extension loaded for semantic search");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load sqlite-vec extension - semantic search disabled");
            }
        }

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

        // Create vector table for semantic search (if extension loaded)
        if (_vecExtension.IsAvailable() && _embeddingService.IsAvailable())
        {
            try
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    CREATE VIRTUAL TABLE IF NOT EXISTS symbol_embeddings USING vec0(
                        symbol_id TEXT PRIMARY KEY,
                        embedding FLOAT[384]
                    )";
                await cmd.ExecuteNonQueryAsync(cancellationToken);
                _logger.LogInformation("✅ Created symbol_embeddings vector table for semantic search");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create symbol_embeddings vector table");
            }
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

        using var connection = new SqliteConnection(GetConnectionString(dbPath));
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM symbols";

        return await ReadSymbolsAsync(cmd, cancellationToken);
    }

    public async Task<List<JulieSymbol>> GetSymbolsByNameAsync(string workspacePath, string name, bool caseSensitive = true, CancellationToken cancellationToken = default)
    {
        var dbPath = GetDatabasePath(workspacePath);
        if (!File.Exists(dbPath))
        {
            return new List<JulieSymbol>();
        }

        using var connection = new SqliteConnection(GetConnectionString(dbPath));
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        // Use COLLATE NOCASE for case-insensitive searches
        cmd.CommandText = caseSensitive
            ? "SELECT * FROM symbols WHERE name = @name"
            : "SELECT * FROM symbols WHERE name = @name COLLATE NOCASE";
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

        using var connection = new SqliteConnection(GetConnectionString(dbPath));
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM symbols WHERE kind = @kind";
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

        using var connection = new SqliteConnection(GetConnectionString(dbPath));
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM symbols WHERE file_path = @file_path";
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
        using var connection = new SqliteConnection(GetConnectionString(dbPath));
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
                cmd.Parameters.AddWithValue("@workspace_id", workspacePath);

                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            // Delete old symbols for this file
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "DELETE FROM symbols WHERE file_path = @file_path";
                cmd.Parameters.AddWithValue("@file_path", filePath);
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
                cmd.Parameters.AddWithValue("@workspace_id", workspacePath);
                cmd.Parameters.AddWithValue("@file_hash", hash);
                cmd.Parameters.AddWithValue("@last_indexed", DateTimeOffset.UtcNow.ToUnixTimeSeconds());

                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            // Generate embeddings for semantic search (if available)
            if (_embeddingService.IsAvailable() && symbols.Count > 0)
            {
                try
                {
                    // Delete old embeddings for this file's symbols
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.Transaction = transaction;
                        cmd.CommandText = @"
                            DELETE FROM symbol_embeddings
                            WHERE symbol_id IN (SELECT id FROM symbols WHERE file_path = @file_path)";
                        cmd.Parameters.AddWithValue("@file_path", filePath);
                        await cmd.ExecuteNonQueryAsync(cancellationToken);
                    }

                    // Generate embeddings for each symbol
                    foreach (var symbol in symbols)
                    {
                        // Create embedding text from symbol info
                        var embeddingText = BuildSymbolEmbeddingText(symbol);
                        var embedding = await _embeddingService.GenerateEmbeddingAsync(embeddingText, cancellationToken);

                        // Insert embedding
                        using var cmd = connection.CreateCommand();
                        cmd.Transaction = transaction;
                        cmd.CommandText = @"
                            INSERT INTO symbol_embeddings (symbol_id, embedding)
                            VALUES (@symbol_id, @embedding)";
                        cmd.Parameters.AddWithValue("@symbol_id", symbol.Id);

                        // Serialize embedding as JSON array for vec0
                        var embeddingJson = "[" + string.Join(",", embedding) + "]";
                        cmd.Parameters.AddWithValue("@embedding", embeddingJson);

                        await cmd.ExecuteNonQueryAsync(cancellationToken);
                    }

                    _logger.LogDebug("Generated embeddings for {SymbolCount} symbols in {FilePath}", symbols.Count, filePath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to generate embeddings for {FilePath} - continuing without semantic search", filePath);
                    // Don't fail the whole transaction if embeddings fail
                }
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

        using var connection = new SqliteConnection(GetConnectionString(dbPath));
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM files WHERE path = @path";
        cmd.Parameters.AddWithValue("@path", filePath);

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

        using var connection = new SqliteConnection(GetConnectionString(dbPath));
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM files";

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

        using var connection = new SqliteConnection(GetConnectionString(dbPath));
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM symbols";

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }

    public async Task<List<FileRecord>> GetAllFilesAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        var dbPath = GetDatabasePath(workspacePath);
        if (!File.Exists(dbPath))
        {
            _logger.LogWarning("SQLite database does not exist at {DbPath}", dbPath);
            return new List<FileRecord>();
        }

        using var connection = new SqliteConnection(GetConnectionString(dbPath));
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT path, content, language, size, last_modified
            FROM files
            ORDER BY path";

        var files = new List<FileRecord>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            files.Add(new FileRecord(
                Path: reader.GetString(reader.GetOrdinal("path")),
                Content: reader.IsDBNull(reader.GetOrdinal("content")) ? null : reader.GetString(reader.GetOrdinal("content")),
                Language: reader.GetString(reader.GetOrdinal("language")),
                Size: reader.GetInt64(reader.GetOrdinal("size")),
                LastModified: reader.GetInt64(reader.GetOrdinal("last_modified"))
            ));
        }

        _logger.LogDebug("Retrieved {FileCount} files from SQLite database", files.Count);
        return files;
    }

    public async Task<FileRecord?> GetFileByPathAsync(string workspacePath, string filePath, CancellationToken cancellationToken = default)
    {
        var dbPath = GetDatabasePath(workspacePath);
        if (!File.Exists(dbPath))
        {
            _logger.LogWarning("SQLite database does not exist at {DbPath}", dbPath);
            return null;
        }

        using var connection = new SqliteConnection(GetConnectionString(dbPath));
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT path, content, language, size, last_modified
            FROM files
            WHERE path = @filePath";
        cmd.Parameters.AddWithValue("@filePath", filePath);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return new FileRecord(
                Path: reader.GetString(reader.GetOrdinal("path")),
                Content: reader.IsDBNull(reader.GetOrdinal("content")) ? null : reader.GetString(reader.GetOrdinal("content")),
                Language: reader.GetString(reader.GetOrdinal("language")),
                Size: reader.GetInt64(reader.GetOrdinal("size")),
                LastModified: reader.GetInt64(reader.GetOrdinal("last_modified"))
            );
        }

        return null;
    }

    public async Task<List<FileRecord>> GetRecentFilesAsync(
        string workspacePath,
        long cutoffTimestamp,
        int maxResults,
        string? extensionFilter = null,
        CancellationToken cancellationToken = default)
    {
        var dbPath = GetDatabasePath(workspacePath);
        if (!File.Exists(dbPath))
        {
            _logger.LogWarning("SQLite database does not exist at {DbPath}", dbPath);
            return new List<FileRecord>();
        }

        using var connection = new SqliteConnection(GetConnectionString(dbPath));
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();

        // Build query with optional extension filter
        var whereClause = "WHERE last_modified >= @cutoff";
        if (!string.IsNullOrEmpty(extensionFilter))
        {
            // Parse extension filter (e.g., ".cs,.js" -> [".cs", ".js"])
            var extensions = extensionFilter
                .Split(',')
                .Select(e => e.Trim().StartsWith('.') ? e.Trim() : "." + e.Trim())
                .ToList();

            // Add extension filter using SQL IN clause
            var extParams = string.Join(",", extensions.Select((_, i) => $"@ext{i}"));
            whereClause += $" AND (path LIKE '%' || @ext0";
            for (int i = 1; i < extensions.Count; i++)
            {
                whereClause += $" OR path LIKE '%' || @ext{i}";
            }
            whereClause += ")";

            // Add parameters
            for (int i = 0; i < extensions.Count; i++)
            {
                cmd.Parameters.AddWithValue($"@ext{i}", extensions[i]);
            }
        }

        cmd.CommandText = $@"
            SELECT path, content, language, size, last_modified
            FROM files
            {whereClause}
            ORDER BY last_modified DESC
            LIMIT @limit";

        cmd.Parameters.AddWithValue("@cutoff", cutoffTimestamp);
        cmd.Parameters.AddWithValue("@limit", maxResults);

        var files = new List<FileRecord>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            files.Add(new FileRecord(
                Path: reader.GetString(reader.GetOrdinal("path")),
                Content: reader.IsDBNull(reader.GetOrdinal("content")) ? null : reader.GetString(reader.GetOrdinal("content")),
                Language: reader.GetString(reader.GetOrdinal("language")),
                Size: reader.GetInt64(reader.GetOrdinal("size")),
                LastModified: reader.GetInt64(reader.GetOrdinal("last_modified"))
            ));
        }

        _logger.LogDebug("Found {Count} recent files modified after {Cutoff}", files.Count, cutoffTimestamp);
        return files;
    }
    public async Task<List<FileRecord>> SearchFilesByPatternAsync(
        string workspacePath,
        string pattern,
        bool searchFullPath = true,
        string? extensionFilter = null,
        int maxResults = 100,
        CancellationToken cancellationToken = default)
    {
        var dbPath = GetDatabasePath(workspacePath);
        if (!File.Exists(dbPath))
        {
            _logger.LogWarning("SQLite database does not exist at {DbPath}", dbPath);
            return new List<FileRecord>();
        }

        using var connection = new SqliteConnection(GetConnectionString(dbPath));
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();

        // Build WHERE clause for pattern matching
        // Use GLOB for case-sensitive pattern matching with *, ?, etc.
        // For cross-platform consistency, convert pattern to lowercase and use LIKE with LOWER()
        var patternLower = pattern.ToLowerInvariant();
        
        // Convert glob pattern to SQL LIKE pattern (replace * with %, ? with _)
        var sqlPattern = patternLower.Replace("*", "%").Replace("?", "_");
        
        string whereClause;
        if (searchFullPath)
        {
            whereClause = "WHERE LOWER(path) LIKE @pattern";
        }
        else
        {
            // Extract filename from path and match against it
            // SQLite doesn't have a built-in basename(), so we need to use clever substring
            // For simplicity, we'll just search the path and ensure pattern doesn't contain /
            whereClause = "WHERE (LOWER(path) LIKE '%/' || @pattern OR LOWER(path) LIKE @pattern)";
        }

        // Add extension filter if provided
        if (!string.IsNullOrEmpty(extensionFilter))
        {
            var extensions = extensionFilter
                .Split(',')
                .Select(e => e.Trim().StartsWith('.') ? e.Trim() : "." + e.Trim())
                .ToList();

            whereClause += " AND (";
            for (int i = 0; i < extensions.Count; i++)
            {
                if (i > 0) whereClause += " OR ";
                whereClause += $"LOWER(path) LIKE '%' || LOWER(@ext{i})";
                cmd.Parameters.AddWithValue($"@ext{i}", extensions[i]);
            }
            whereClause += ")";
        }

        cmd.CommandText = $@"
            SELECT path, content, language, size, last_modified
            FROM files
            {whereClause}
            ORDER BY path
            LIMIT @limit";

        cmd.Parameters.AddWithValue("@pattern", sqlPattern);
        cmd.Parameters.AddWithValue("@limit", maxResults);

        var files = new List<FileRecord>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            files.Add(new FileRecord(
                Path: reader.GetString(reader.GetOrdinal("path")),
                Content: reader.IsDBNull(reader.GetOrdinal("content")) ? null : reader.GetString(reader.GetOrdinal("content")),
                Language: reader.GetString(reader.GetOrdinal("language")),
                Size: reader.GetInt64(reader.GetOrdinal("size")),
                LastModified: reader.GetInt64(reader.GetOrdinal("last_modified"))
            ));
        }

        _logger.LogDebug("Found {Count} files matching pattern '{Pattern}'", files.Count, pattern);
        return files;
    }

    public async Task<List<string>> SearchDirectoriesByPatternAsync(
        string workspacePath,
        string pattern,
        bool includeHidden = false,
        int maxResults = 100,
        CancellationToken cancellationToken = default)
    {
        var dbPath = GetDatabasePath(workspacePath);
        if (!File.Exists(dbPath))
        {
            _logger.LogWarning("SQLite database does not exist at {DbPath}", dbPath);
            return new List<string>();
        }

        using var connection = new SqliteConnection(GetConnectionString(dbPath));
        await connection.OpenAsync(cancellationToken);

        // Convert glob pattern to SQL LIKE pattern (replace * with %, ? with _)
        var patternLower = pattern.ToLowerInvariant();
        var sqlPattern = patternLower.Replace("*", "%").Replace("?", "_");

        // Query all file paths and extract directories in C# (simpler and more reliable than complex SQL)
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT path FROM files WHERE path LIKE '%/%' ORDER BY path";

        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var filePath = reader.GetString(0);
            var dirPath = Path.GetDirectoryName(filePath)?.Replace('\\', '/');

            if (!string.IsNullOrWhiteSpace(dirPath))
            {
                // Check if directory matches pattern (using simple wildcard matching)
                if (MatchesPattern(dirPath, sqlPattern))
                {
                    // Filter hidden directories if needed
                    if (!includeHidden && IsHiddenDirectory(dirPath))
                        continue;

                    directories.Add(dirPath);

                    if (directories.Count >= maxResults)
                        break;
                }
            }
        }

        var result = directories.OrderBy(d => d).ToList();
        _logger.LogDebug("Found {Count} directories matching pattern '{Pattern}'", result.Count, pattern);
        return result;
    }

    private static bool MatchesPattern(string text, string pattern)
    {
        // Convert SQL LIKE pattern to regex for matching
        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace("%", ".*")
            .Replace("_", ".") + "$";
        return Regex.IsMatch(text, regexPattern, RegexOptions.IgnoreCase);
    }

    private static bool IsHiddenDirectory(string dirPath)
    {
        // Check if any segment of the path starts with '.'
        return dirPath.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment.StartsWith('.'));
    }

    public async Task<List<FileRecord>> SearchWithFTS5Async(
        string workspacePath,
        string searchPattern,
        int maxResults = 100,
        string? filePattern = null,
        CancellationToken cancellationToken = default)
    {
        var dbPath = GetDatabasePath(workspacePath);
        if (!File.Exists(dbPath))
        {
            _logger.LogWarning("SQLite database does not exist at {DbPath}", dbPath);
            return new List<FileRecord>();
        }

        using var connection = new SqliteConnection(GetConnectionString(dbPath));
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();

        // Build FTS5 MATCH query
        // Join with files table to get full file information
        var whereClause = "WHERE files_fts MATCH @pattern";

        // Add file pattern filter if specified (e.g., "*.cs")
        if (!string.IsNullOrEmpty(filePattern))
        {
            // Convert glob pattern to SQL LIKE pattern
            var likePattern = filePattern.Replace("*", "%").Replace("?", "_");
            whereClause += " AND files.path LIKE @filePattern";
        }

        cmd.CommandText = $@"
            SELECT files.rowid, files.path, files.content, files.language, files.size, files.last_modified
            FROM files_fts
            JOIN files ON files_fts.rowid = files.rowid
            {whereClause}
            LIMIT @limit";

        cmd.Parameters.AddWithValue("@pattern", searchPattern);
        cmd.Parameters.AddWithValue("@limit", maxResults);

        if (!string.IsNullOrEmpty(filePattern))
        {
            var likePattern = filePattern.Replace("*", "%").Replace("?", "_");
            cmd.Parameters.AddWithValue("@filePattern", likePattern);
        }

        var files = new List<FileRecord>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            files.Add(new FileRecord(
                Path: reader.GetString(reader.GetOrdinal("path")),
                Content: reader.IsDBNull(reader.GetOrdinal("content")) ? null : reader.GetString(reader.GetOrdinal("content")),
                Language: reader.GetString(reader.GetOrdinal("language")),
                Size: reader.GetInt64(reader.GetOrdinal("size")),
                LastModified: reader.GetInt64(reader.GetOrdinal("last_modified"))
            ));
        }

        _logger.LogDebug("FTS5 search for '{Pattern}' found {Count} files", searchPattern, files.Count);
        return files;
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

    public async Task<int> GetIdentifierCountByNameAsync(
        string workspacePath,
        string name,
        bool caseSensitive = true,
        CancellationToken cancellationToken = default)
    {
        var dbPath = GetDatabasePath(workspacePath);
        if (!File.Exists(dbPath))
        {
            return 0;
        }

        using var connection = new SqliteConnection(GetConnectionString(dbPath));
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = caseSensitive
            ? "SELECT COUNT(*) FROM identifiers WHERE name = @name"
            : "SELECT COUNT(*) FROM identifiers WHERE name = @name COLLATE NOCASE";
        cmd.Parameters.AddWithValue("@name", name);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result != null ? Convert.ToInt32(result) : 0;
    }


    public async Task<List<JulieIdentifier>> GetIdentifiersByNameAsync(
        string workspacePath,
        string name,
        bool caseSensitive = true,
        CancellationToken cancellationToken = default)
    {
        var dbPath = GetDatabasePath(workspacePath);
        if (!File.Exists(dbPath))
        {
            _logger.LogWarning("Database not found at {Path}", dbPath);
            return new List<JulieIdentifier>();
        }

        var identifiers = new List<JulieIdentifier>();

        using var connection = new SqliteConnection(GetConnectionString(dbPath));
        await connection.OpenAsync(cancellationToken);

        var query = caseSensitive
            ? "SELECT * FROM identifiers WHERE name = @name"
            : "SELECT * FROM identifiers WHERE name = @name COLLATE NOCASE";

        using var cmd = connection.CreateCommand();
        cmd.CommandText = query;
        cmd.Parameters.AddWithValue("@name", name);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            identifiers.Add(new JulieIdentifier
            {
                Id = reader.GetString(reader.GetOrdinal("id")),
                Name = reader.GetString(reader.GetOrdinal("name")),
                Kind = reader.GetString(reader.GetOrdinal("kind")),
                Language = reader.GetString(reader.GetOrdinal("language")),
                FilePath = reader.GetString(reader.GetOrdinal("file_path")),
                StartLine = reader.GetInt32(reader.GetOrdinal("start_line")),
                StartColumn = reader.GetInt32(reader.GetOrdinal("start_col")),
                EndLine = reader.GetInt32(reader.GetOrdinal("end_line")),
                EndColumn = reader.GetInt32(reader.GetOrdinal("end_col")),
                StartByte = reader.IsDBNull(reader.GetOrdinal("start_byte")) ? null : reader.GetInt32(reader.GetOrdinal("start_byte")),
                EndByte = reader.IsDBNull(reader.GetOrdinal("end_byte")) ? null : reader.GetInt32(reader.GetOrdinal("end_byte")),
                ContainingSymbolId = reader.IsDBNull(reader.GetOrdinal("containing_symbol_id")) ? null : reader.GetString(reader.GetOrdinal("containing_symbol_id")),
                TargetSymbolId = reader.IsDBNull(reader.GetOrdinal("target_symbol_id")) ? null : reader.GetString(reader.GetOrdinal("target_symbol_id")),
                Confidence = reader.GetFloat(reader.GetOrdinal("confidence")),
                CodeContext = reader.IsDBNull(reader.GetOrdinal("code_context")) ? null : reader.GetString(reader.GetOrdinal("code_context"))
            });
        }

        return identifiers;
    }

    public async Task<List<JulieIdentifier>> GetIdentifiersByKindAsync(
        string workspacePath,
        string kind,
        CancellationToken cancellationToken = default)
    {
        var dbPath = GetDatabasePath(workspacePath);
        if (!File.Exists(dbPath))
        {
            _logger.LogWarning("Database not found at {Path}", dbPath);
            return new List<JulieIdentifier>();
        }

        var identifiers = new List<JulieIdentifier>();

        using var connection = new SqliteConnection(GetConnectionString(dbPath));
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM identifiers WHERE kind = @kind";
        cmd.Parameters.AddWithValue("@kind", kind);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            identifiers.Add(ReadIdentifierFromReader(reader));
        }

        return identifiers;
    }

    public async Task<List<JulieIdentifier>> GetIdentifiersForFileAsync(
        string workspacePath,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var dbPath = GetDatabasePath(workspacePath);
        if (!File.Exists(dbPath))
        {
            _logger.LogWarning("Database not found at {Path}", dbPath);
            return new List<JulieIdentifier>();
        }

        var identifiers = new List<JulieIdentifier>();

        using var connection = new SqliteConnection(GetConnectionString(dbPath));
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM identifiers WHERE file_path = @filePath";
        cmd.Parameters.AddWithValue("@filePath", filePath);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            identifiers.Add(ReadIdentifierFromReader(reader));
        }

        return identifiers;
    }

    public async Task<List<JulieIdentifier>> GetIdentifiersByContainingSymbolAsync(
        string workspacePath,
        string containingSymbolId,
        CancellationToken cancellationToken = default)
    {
        var dbPath = GetDatabasePath(workspacePath);
        if (!File.Exists(dbPath))
        {
            _logger.LogWarning("Database not found at {Path}", dbPath);
            return new List<JulieIdentifier>();
        }

        var identifiers = new List<JulieIdentifier>();

        using var connection = new SqliteConnection(GetConnectionString(dbPath));
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM identifiers WHERE containing_symbol_id = @containingSymbolId";
        cmd.Parameters.AddWithValue("@containingSymbolId", containingSymbolId);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            identifiers.Add(ReadIdentifierFromReader(reader));
        }

        return identifiers;
    }

    private JulieIdentifier ReadIdentifierFromReader(SqliteDataReader reader)
    {
        return new JulieIdentifier
        {
            Id = reader.GetString(reader.GetOrdinal("id")),
            Name = reader.GetString(reader.GetOrdinal("name")),
            Kind = reader.GetString(reader.GetOrdinal("kind")),
            Language = reader.GetString(reader.GetOrdinal("language")),
            FilePath = reader.GetString(reader.GetOrdinal("file_path")),
            StartLine = reader.GetInt32(reader.GetOrdinal("start_line")),
            StartColumn = reader.GetInt32(reader.GetOrdinal("start_col")),
            EndLine = reader.GetInt32(reader.GetOrdinal("end_line")),
            EndColumn = reader.GetInt32(reader.GetOrdinal("end_col")),
            StartByte = reader.IsDBNull(reader.GetOrdinal("start_byte")) ? null : reader.GetInt32(reader.GetOrdinal("start_byte")),
            EndByte = reader.IsDBNull(reader.GetOrdinal("end_byte")) ? null : reader.GetInt32(reader.GetOrdinal("end_byte")),
            ContainingSymbolId = reader.IsDBNull(reader.GetOrdinal("containing_symbol_id")) ? null : reader.GetString(reader.GetOrdinal("containing_symbol_id")),
            TargetSymbolId = reader.IsDBNull(reader.GetOrdinal("target_symbol_id")) ? null : reader.GetString(reader.GetOrdinal("target_symbol_id")),
            Confidence = reader.GetFloat(reader.GetOrdinal("confidence")),
            CodeContext = reader.IsDBNull(reader.GetOrdinal("code_context")) ? null : reader.GetString(reader.GetOrdinal("code_context"))
        };
    }

    public bool IsSemanticSearchAvailable()
    {
        return _vecExtension.IsAvailable() && _embeddingService.IsAvailable();
    }

    public async Task<List<SemanticSymbolMatch>> SearchSymbolsSemanticAsync(
        string workspacePath,
        string query,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        if (!IsSemanticSearchAvailable())
        {
            _logger.LogWarning("Semantic search not available - sqlite-vec or embedding service missing");
            return new List<SemanticSymbolMatch>();
        }

        var dbPath = GetDatabasePath(workspacePath);
        if (!File.Exists(dbPath))
        {
            _logger.LogWarning("SQLite database does not exist at {DbPath}", dbPath);
            return new List<SemanticSymbolMatch>();
        }

        // Generate query embedding
        var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);
        var queryEmbeddingJson = "[" + string.Join(",", queryEmbedding) + "]";

        using var connection = new SqliteConnection(GetConnectionString(dbPath));
        await connection.OpenAsync(cancellationToken);

        // Load vec extension
        _vecExtension.LoadExtension(connection);

        var results = new List<SemanticSymbolMatch>();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT
                s.*,
                distance
            FROM symbol_embeddings se
            JOIN symbols s ON se.symbol_id = s.id
            WHERE se.embedding MATCH @query_embedding
              AND k = @limit
            ORDER BY distance";

        cmd.Parameters.AddWithValue("@query_embedding", queryEmbeddingJson);
        cmd.Parameters.AddWithValue("@limit", limit);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var symbol = new JulieSymbol
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
            };

            var distance = reader.GetFloat(reader.GetOrdinal("distance"));
            // Convert distance to similarity score (0-1, higher is better)
            // sqlite-vec returns cosine distance (0 = identical, 2 = opposite)
            var similarity = 1.0f - (distance / 2.0f);

            results.Add(new SemanticSymbolMatch(symbol, similarity));
        }

        _logger.LogDebug("Semantic search for '{Query}' found {Count} results", query, results.Count);
        return results;
    }

    private static string BuildSymbolEmbeddingText(JulieSymbol symbol)
    {
        // Build rich text representation for embedding
        // Include: kind, name, signature, doc comments
        var parts = new List<string>();

        // Add kind
        parts.Add(symbol.Kind);

        // Add name
        parts.Add(symbol.Name);

        // Add signature if available
        if (!string.IsNullOrEmpty(symbol.Signature))
        {
            parts.Add(symbol.Signature);
        }

        // Add doc comment if available
        if (!string.IsNullOrEmpty(symbol.DocComment))
        {
            // Clean up doc comment (remove comment markers)
            var cleanDoc = symbol.DocComment
                .Replace("///", "")
                .Replace("//", "")
                .Replace("/*", "")
                .Replace("*/", "")
                .Trim();
            parts.Add(cleanDoc);
        }

        return string.Join(" ", parts);
    }
}

//this is a test