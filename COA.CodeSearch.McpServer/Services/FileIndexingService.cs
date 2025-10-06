using Lucene.Net.Documents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services.Lucene;
using COA.CodeSearch.McpServer.Services.TypeExtraction;
using COA.CodeSearch.McpServer.Services.Julie;
using COA.CodeSearch.McpServer.Services.Sqlite;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Service responsible for indexing files into Lucene
/// Refactored to use ILuceneIndexService interface methods instead of direct IndexWriter access
/// </summary>
public class FileIndexingService : IFileIndexingService
{
    private readonly ILogger<FileIndexingService> _logger;
    private readonly IConfiguration _configuration;
    private readonly ILuceneIndexService _luceneIndexService;
    private readonly IPathResolutionService _pathResolution;
    private readonly IIndexingMetricsService _metricsService;
    private readonly ICircuitBreakerService _circuitBreakerService;
    private readonly IMemoryPressureService _memoryPressureService;
    private readonly ITypeExtractionService _typeExtractionService;
    private readonly IJulieExtractionService? _julieExtractionService;
    private readonly IJulieCodeSearchService? _julieCodeSearchService;
    private readonly ISQLiteSymbolService? _sqliteSymbolService;
    private readonly ISemanticIntelligenceService? _semanticIntelligenceService;
    private readonly MemoryLimitsConfiguration _memoryLimits;
    private readonly HashSet<string> _blacklistedExtensions;
    private readonly HashSet<string> _excludedDirectories;
    private const int MAX_FILE_SIZE = 10 * 1024 * 1024; // 10MB max file size

    public FileIndexingService(
        ILogger<FileIndexingService> logger,
        IConfiguration configuration,
        ILuceneIndexService luceneIndexService,
        IPathResolutionService pathResolution,
        IIndexingMetricsService metricsService,
        ICircuitBreakerService circuitBreakerService,
        IMemoryPressureService memoryPressureService,
        IOptions<MemoryLimitsConfiguration> memoryLimits,
        ITypeExtractionService typeExtractionService,
        IJulieExtractionService? julieExtractionService = null,
        IJulieCodeSearchService? julieCodeSearchService = null,
        ISQLiteSymbolService? sqliteSymbolService = null,
        ISemanticIntelligenceService? semanticIntelligenceService = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _luceneIndexService = luceneIndexService ?? throw new ArgumentNullException(nameof(luceneIndexService));
        _pathResolution = pathResolution ?? throw new ArgumentNullException(nameof(pathResolution));
        _metricsService = metricsService ?? throw new ArgumentNullException(nameof(metricsService));
        _circuitBreakerService = circuitBreakerService ?? throw new ArgumentNullException(nameof(circuitBreakerService));
        _memoryPressureService = memoryPressureService ?? throw new ArgumentNullException(nameof(memoryPressureService));
        _memoryLimits = memoryLimits?.Value ?? throw new ArgumentNullException(nameof(memoryLimits));
        _typeExtractionService = typeExtractionService ?? throw new ArgumentNullException(nameof(typeExtractionService));
        _julieExtractionService = julieExtractionService;
        _julieCodeSearchService = julieCodeSearchService;
        _sqliteSymbolService = sqliteSymbolService;
        _semanticIntelligenceService = semanticIntelligenceService;

        // Debug: Log julie service injection
        _logger.LogDebug("FileIndexingService initialized - Julie extract: {ExtractAvailable}, Julie codesearch: {CodeSearchAvailable}, SQLite: {SqliteAvailable}, Semantic: {SemanticAvailable}",
            _julieExtractionService?.IsAvailable() ?? false,
            _julieCodeSearchService?.IsAvailable() ?? false,
            _sqliteSymbolService != null,
            _semanticIntelligenceService?.IsAvailable() ?? false);

        // Initialize blacklisted extensions from configuration or defaults
        var blacklistedExts = configuration.GetSection("CodeSearch:Indexing:BlacklistedExtensions").Get<string[]>() 
            ?? PathConstants.DefaultBlacklistedExtensions;
        _blacklistedExtensions = new HashSet<string>(blacklistedExts, StringComparer.OrdinalIgnoreCase);
        
        // Initialize excluded directories
        var excluded = configuration.GetSection("CodeSearch:Lucene:ExcludedDirectories").Get<string[]>() 
            ?? PathConstants.DefaultExcludedDirectories;
        _excludedDirectories = new HashSet<string>(excluded, StringComparer.OrdinalIgnoreCase);
    }
        /// <summary>
        /// Extract only symbol names from content and type data for symbol-only search field
        /// </summary>
        private string ExtractSymbolsOnly(string content, TypeExtractionResult? typeData)
        {
            var symbols = new List<string>();
            
            // Add symbols from type extraction if available
            if (typeData?.Success == true)
            {
                symbols.AddRange(typeData.Types.Select(t => t.Name));
                symbols.AddRange(typeData.Methods.Select(m => m.Name));
            }
            
            // Add basic symbol extraction from content (classes, methods, variables)
            // This handles cases where type extraction fails or isn't available
            var symbolMatches = System.Text.RegularExpressions.Regex.Matches(content, 
                @"\b(?:class|interface|struct|enum|function|def|func|fn|method|var|let|const)\s+([A-Za-z_][A-Za-z0-9_]*)", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            foreach (System.Text.RegularExpressions.Match match in symbolMatches)
            {
                if (match.Groups.Count > 1)
                    symbols.Add(match.Groups[1].Value);
            }
            
            return string.Join(" ", symbols.Distinct());
        }

    public async Task<IndexingResult> IndexWorkspaceAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        var result = new IndexingResult { WorkspacePath = workspacePath };
        var startTime = DateTime.UtcNow;

        try
        {
            // Initialize the index
            var initResult = await _luceneIndexService.InitializeIndexAsync(workspacePath, cancellationToken);
            if (!initResult.Success)
            {
                result.Success = false;
                result.ErrorMessage = initResult.ErrorMessage;
                return result;
            }

            // Note: Index clearing/rebuilding is now handled by the calling tool
            // ForceRebuildIndexAsync handles schema recreation when needed
            // ClearIndexAsync is only used for document removal without schema changes

            // PHASE 1: Run julie-codesearch scan to populate SQLite database
            if (_julieCodeSearchService?.IsAvailable() == true)
            {
                var codeSearchStart = DateTime.UtcNow;
                _logger.LogInformation("🚀 Running julie-codesearch scan for SQLite database population...");

                try
                {
                    // Get SQLite database path (isolated in db/ subdirectory)
                    var indexPath = _pathResolution.GetIndexPath(workspacePath);
                    var dbDirectory = Path.Combine(indexPath, "db");
                    var sqlitePath = Path.Combine(dbDirectory, "workspace.db");

                    // Ensure db/ directory exists
                    Directory.CreateDirectory(dbDirectory);

                    // Enable detailed logging for debugging
                    var logFilePath = Path.Combine(dbDirectory, "julie-codesearch.log");

                    // Build ignore patterns from PathConstants (single source of truth)
                    var ignorePatterns = new List<string>();

                    // Add excluded directories as glob patterns (e.g., "bin" → "**/bin/**")
                    ignorePatterns.AddRange(_excludedDirectories.Select(dir => $"**/{dir}/**"));

                    // Add blacklisted extensions as glob patterns (e.g., ".log" → "**/*.log")
                    ignorePatterns.AddRange(_blacklistedExtensions.Select(ext => $"**/*{ext}"));

                    // Add project-specific ignore patterns from .codesearchignore file
                    var customPatterns = ReadCustomIgnorePatterns(workspacePath);
                    if (customPatterns.Any())
                    {
                        _logger.LogInformation("📝 Loaded {Count} custom ignore patterns from .codesearchignore", customPatterns.Count);
                        ignorePatterns.AddRange(customPatterns);
                    }

                    // Scan workspace with julie-codesearch
                    var scanResult = await _julieCodeSearchService.ScanDirectoryAsync(
                        workspacePath,
                        sqlitePath,
                        ignorePatterns: ignorePatterns,
                        logFilePath: logFilePath,
                        threads: null,      // Use CPU count
                        cancellationToken);

                    if (scanResult.Success)
                    {
                        var scanDuration = (DateTime.UtcNow - codeSearchStart).TotalSeconds;
                        _logger.LogInformation("✅ julie-codesearch scan complete: {Processed} files processed, {Skipped} skipped in {Duration:F2}s",
                            scanResult.ProcessedFiles,
                            scanResult.SkippedFiles,
                            scanDuration);
                    }
                    else
                    {
                        _logger.LogWarning("⚠️  julie-codesearch scan failed: {Error}", scanResult.ErrorMessage);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "julie-codesearch scan failed, continuing with Lucene-only indexing");
                }
            }

            // PHASE 2: Check if bulk mode is available and enabled (julie-extract for Lucene)
            var julieAvailable = _julieExtractionService?.IsAvailable() == true;
            var bulkModeConfig = _configuration.GetValue("CodeSearch:TypeExtraction:UseBulkMode", true);
            _logger.LogWarning("BULK MODE DEBUG: Julie service null? {IsNull}, IsAvailable: {Available}, Config: {Config}",
                _julieExtractionService == null,
                julieAvailable,
                bulkModeConfig);

            var useBulkMode = julieAvailable && bulkModeConfig;

            Dictionary<string, List<JulieSymbol>>? symbolCache = null;
            if (useBulkMode)
            {
                _logger.LogInformation("🚀 Bulk extraction mode enabled - pre-extracting symbols in parallel");
                var extractStart = DateTime.UtcNow;

                try
                {
                    symbolCache = await PreExtractSymbolsAsync(workspacePath, cancellationToken);
                    var extractDuration = (DateTime.UtcNow - extractStart).TotalSeconds;
                    _logger.LogInformation("✅ Pre-extracted {SymbolCount} symbols from {FileCount} files in {Duration:F2}s",
                        symbolCache.Values.Sum(s => s.Count),
                        symbolCache.Count,
                        extractDuration);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Bulk extraction failed, falling back to sequential extraction");
                    symbolCache = null;
                }
            }

            // PHASE 3: Run Lucene indexing and embedding generation in parallel
            Task<int> luceneTask = IndexDirectoryAsync(workspacePath, workspacePath, symbolCache, cancellationToken);
            Task embeddingTask = Task.CompletedTask;

            // Start embedding generation if available
            if (_semanticIntelligenceService?.IsAvailable() == true && _sqliteSymbolService != null)
            {
                var indexPath = _pathResolution.GetIndexPath(workspacePath);
                var sqlitePath = Path.Combine(indexPath, "db", "workspace.db");
                var vectorsPath = Path.Combine(indexPath, "vectors");

                // Ensure vectors directory exists
                Directory.CreateDirectory(vectorsPath);

                embeddingTask = Task.Run(async () =>
                {
                    try
                    {
                        _logger.LogInformation("🧠 Generating embeddings in parallel with Lucene indexing...");
                        var embeddingStart = DateTime.UtcNow;

                        var stats = await _semanticIntelligenceService.GenerateEmbeddingsAsync(
                            sqlitePath,
                            outputPath: vectorsPath,
                            model: "bge-small",
                            batchSize: 100,
                            limit: null, // Process all symbols
                            cancellationToken);

                        var embeddingDuration = (DateTime.UtcNow - embeddingStart).TotalSeconds;
                        _logger.LogInformation("✅ Embedding generation complete: {Symbols} symbols, {Embeddings} embeddings in {Duration:F2}s",
                            stats.SymbolsProcessed,
                            stats.EmbeddingsGenerated,
                            embeddingDuration);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Embedding generation failed, continuing with Lucene-only indexing");
                    }
                }, cancellationToken);
            }

            // Wait for Lucene indexing to complete
            result.IndexedFileCount = await luceneTask;

            // Fire-and-forget: Let embeddings continue in background without blocking
            // (Embeddings can take 40-60s; no need to block user-facing indexing operations)
            _ = embeddingTask.ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    _logger.LogWarning(t.Exception, "Background embedding generation encountered errors");
                }
            }, TaskScheduler.Default);

            // Commit changes
            await _luceneIndexService.CommitAsync(workspacePath, cancellationToken);
            
            result.Success = true;
            result.Duration = DateTime.UtcNow - startTime;
            
            _logger.LogInformation("Indexed workspace {WorkspacePath}: {FileCount} files in {Duration}ms",
                workspacePath, result.IndexedFileCount, result.Duration.TotalMilliseconds);
                
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to index workspace {WorkspacePath}", workspacePath);
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.Duration = DateTime.UtcNow - startTime;
            return result;
        }
    }
    /// <summary>
    /// Pre-extract symbols from entire workspace using parallel streaming.
    /// Returns a dictionary mapping file paths to their extracted symbols for fast lookup during indexing.
    /// </summary>
    private async Task<Dictionary<string, List<JulieSymbol>>> PreExtractSymbolsAsync(
        string workspacePath,
        CancellationToken cancellationToken)
    {
        if (_julieExtractionService == null)
        {
            return new Dictionary<string, List<JulieSymbol>>();
        }

        var symbolsByFile = new Dictionary<string, List<JulieSymbol>>(StringComparer.OrdinalIgnoreCase);
        var fileCount = 0;

        var symbolCount = 0;
        await foreach (var symbol in _julieExtractionService.StreamExtractDirectoryAsync(
            workspacePath,
            threads: null, // Use default (CPU count)
            cancellationToken))
        {
            symbolCount++;

            // Group symbols by file path
            if (!symbolsByFile.TryGetValue(symbol.FilePath, out var symbols))
            {
                symbols = new List<JulieSymbol>();
                symbolsByFile[symbol.FilePath] = symbols;
                fileCount++;

                _logger.LogDebug("📁 NEW FILE #{FileNum}: {FilePath} (total symbols so far: {SymbolCount})",
                    fileCount, symbol.FilePath, symbolCount);
            }

            symbols.Add(symbol);
        }

        _logger.LogInformation("Stream complete - processed {SymbolCount} symbols from {FileCount} files",
            symbolCount, fileCount);

        return symbolsByFile;
    }



    // Public interface implementation (delegates to internal optimized version)
    public async Task<int> IndexDirectoryAsync(
        string workspacePath,
        string directoryPath,
        CancellationToken cancellationToken = default)
    {
        return await IndexDirectoryAsync(workspacePath, directoryPath, symbolCache: null, cancellationToken);
    }



    public async Task<int> IndexDirectoryAsync(
        string workspacePath,
        string directoryPath,
        Dictionary<string, List<JulieSymbol>>? symbolCache = null,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;

        _logger.LogDebug("Starting directory indexing for {DirectoryPath} in workspace {WorkspacePath}",
            directoryPath, workspacePath);

        // Check for memory pressure
        if (_memoryPressureService.ShouldThrottleOperation("directory_indexing"))
        {
            _logger.LogWarning("Directory indexing throttled due to memory pressure");
            return 0;
        }

        var indexedCount = 0;
        var skippedCount = 0;
        var documents = new List<Document>();
        var batchSize = _configuration.GetValue("Lucene:BatchSize", 100);

        try
        {
            var files = GetFilesToIndex(directoryPath).ToList(); // Materialize to get count
            _logger.LogDebug("Found {FileCount} files to index in {DirectoryPath}", files.Count, directoryPath);

            foreach (var filePath in files)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogDebug("Indexing cancelled by request");
                    break;
                }

                try
                {
                    var document = await CreateDocumentFromFileAsync(filePath, workspacePath, symbolCache, cancellationToken);
                    if (document != null)
                    {
                        documents.Add(document);
                        _logger.LogTrace("Added document for file {FilePath}", filePath);
                        
                        // Batch index documents
                        if (documents.Count >= batchSize)
                        {
                            _logger.LogDebug("Indexing batch of {BatchSize} documents", documents.Count);
                            await _luceneIndexService.IndexDocumentsAsync(workspacePath, documents, cancellationToken);
                            indexedCount += documents.Count;
                            documents.Clear();
                        }
                    }
                    else
                    {
                        skippedCount++;
                        _logger.LogTrace("Skipped file {FilePath} (document creation returned null)", filePath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to index file {FilePath}", filePath);
                    skippedCount++;
                }
            }
            
            // Index remaining documents
            if (documents.Count > 0)
            {
                _logger.LogDebug("Indexing final batch of {BatchSize} documents", documents.Count);
                await _luceneIndexService.IndexDocumentsAsync(workspacePath, documents, cancellationToken);
                indexedCount += documents.Count;
            }
            
            // Record metrics
            var duration = DateTime.UtcNow - startTime;
            _metricsService.RecordFileIndexed(directoryPath, 0, duration, true);
            
            _logger.LogInformation("Directory indexing complete for {DirectoryPath}: {IndexedCount} indexed, {SkippedCount} skipped in {Duration}ms",
                directoryPath, indexedCount, skippedCount, duration.TotalMilliseconds);
            
            return indexedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to index directory {DirectoryPath}", directoryPath);
            var duration = DateTime.UtcNow - startTime;
            _metricsService.RecordFileIndexed(directoryPath, 0, duration, false, ex.Message);
            throw;
        }
    }

    public async Task<bool> IndexFileAsync(string workspacePath, string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("IndexFileAsync called - Workspace: {WorkspacePath}, File: {FilePath}",
                workspacePath, filePath);

            var document = await CreateDocumentFromFileAsync(filePath, workspacePath, symbolCache: null, cancellationToken);
            if (document != null)
            {
                await _luceneIndexService.IndexDocumentAsync(workspacePath, document, cancellationToken);
                await _luceneIndexService.CommitAsync(workspacePath, cancellationToken);
                
                // Verify the commit worked by checking document count
                var count = await _luceneIndexService.GetDocumentCountAsync(workspacePath, cancellationToken);
                _logger.LogDebug("After indexing {FilePath}, document count is: {Count}", filePath, count);
                
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to index file {FilePath}", filePath);
            return false;
        }
    }

    public async Task<bool> RemoveFileAsync(string workspacePath, string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            await _luceneIndexService.DeleteDocumentAsync(workspacePath, filePath, cancellationToken);
            await _luceneIndexService.CommitAsync(workspacePath, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove file {FilePath} from index", filePath);
            return false;
        }
    }

    private IEnumerable<string> GetFilesToIndex(string directoryPath)
    {
        // PHASE 1: Check if SQLite database exists and use it as source of truth
        if (_sqliteSymbolService != null && _sqliteSymbolService.DatabaseExists(directoryPath))
        {
            _logger.LogInformation("📖 Reading file list from SQLite database (source of truth)");
            List<FileRecord>? files = null;

            try
            {
                // Synchronously get files from SQLite (GetFilesToIndex is synchronous)
                files = _sqliteSymbolService.GetAllFilesAsync(directoryPath, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read from SQLite database, falling back to filesystem scan");
            }

            if (files != null)
            {
                _logger.LogInformation("✅ Found {FileCount} files in SQLite database", files.Count);

                foreach (var file in files)
                {
                    yield return file.Path;
                }

                yield break; // Done - SQLite is source of truth
            }
        }

        // PHASE 2: Fallback to filesystem scan (legacy mode, only if SQLite unavailable)
        _logger.LogWarning("⚠️  SQLite database not available, falling back to filesystem scan (not recommended)");

        if (!Directory.Exists(directoryPath))
        {
            _logger.LogWarning("Directory does not exist: {DirectoryPath}", directoryPath);
            yield break;
        }

        _logger.LogDebug("Starting file enumeration for {DirectoryPath}", directoryPath);
        var directoriesToProcess = new Stack<string>();
        directoriesToProcess.Push(directoryPath);
        var totalFilesFound = 0;
        var totalDirsProcessed = 0;
        
        while (directoriesToProcess.Count > 0)
        {
            var currentDir = directoriesToProcess.Pop();
            totalDirsProcessed++;
            
            if (!Directory.Exists(currentDir))
            {
                _logger.LogDebug("Directory no longer exists: {Directory}", currentDir);
                continue;
            }
                
            // Skip excluded directories by checking if any part of the path contains excluded directory names
            var relativePath = Path.GetRelativePath(directoryPath, currentDir);
            var pathParts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var shouldSkip = pathParts.Any(part => _excludedDirectories.Contains(part));
            
            if (shouldSkip)
            {
                _logger.LogDebug("Skipping excluded directory: {Directory} (contains excluded path component)", currentDir);
                continue;
            }
            
            _logger.LogTrace("Processing directory: {Directory}", currentDir);
            
            // Enumerate files in current directory
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(currentDir, "*", SearchOption.TopDirectoryOnly);
            }
            catch (UnauthorizedAccessException)
            {
                _logger.LogDebug("Access denied to directory: {Directory}", currentDir);
                continue;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error enumerating files in directory: {Directory}", currentDir);
                continue;
            }
            
            var fileList = files.ToList();
            _logger.LogTrace("Found {FileCount} files in {Directory}", fileList.Count, currentDir);
            
            // Process files in current directory
            foreach (var file in fileList)
            {
                bool shouldInclude = false;
                try
                {
                    var extension = Path.GetExtension(file);
                    
                    // Skip blacklisted extensions
                    if (_blacklistedExtensions.Contains(extension))
                    {
                        _logger.LogTrace("Skipping blacklisted file type {File} (extension: {Extension})", file, extension);
                        continue;
                    }
                    
                    // Check file size
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.Length > MAX_FILE_SIZE)
                    {
                        _logger.LogDebug("Skipping large file {File} ({Size} bytes)", file, fileInfo.Length);
                        continue;
                    }
                    
                    // Skip hidden/system files
                    if ((fileInfo.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0)
                        continue;
                    
                    shouldInclude = true;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking file: {File}", file);
                }
                
                if (shouldInclude)
                {
                    totalFilesFound++;
                    yield return file;
                }
            }
            
            // Add subdirectories to process
            IEnumerable<string> subdirectories;
            try
            {
                subdirectories = Directory.EnumerateDirectories(currentDir, "*", SearchOption.TopDirectoryOnly);
            }
            catch (UnauthorizedAccessException)
            {
                _logger.LogDebug("Access denied to subdirectories of: {Directory}", currentDir);
                continue;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error enumerating subdirectories in: {Directory}", currentDir);
                continue;
            }
            
            var subDirList = subdirectories.ToList();
            _logger.LogTrace("Found {SubDirCount} subdirectories in {Directory}", subDirList.Count, currentDir);
            
            foreach (var subDir in subDirList)
            {
                // Check if subdirectory path contains any excluded directory names
                var subRelativePath = Path.GetRelativePath(directoryPath, subDir);
                var subPathParts = subRelativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var subShouldSkip = subPathParts.Any(part => _excludedDirectories.Contains(part));
                
                if (!subShouldSkip)
                {
                    _logger.LogTrace("Adding subdirectory to process: {SubDir}", subDir);
                    directoriesToProcess.Push(subDir);
                }
                else
                {
                    _logger.LogTrace("Skipping excluded subdirectory: {SubDir} (contains excluded path component)", subDir);
                }
            }
        }
    }

    private async Task<Document?> CreateDocumentFromFileAsync(
        string filePath,
        string workspacePath,
        Dictionary<string, List<JulieSymbol>>? symbolCache = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists)
                return null;

            // Read file content
            string content;
            try
            {
                content = await File.ReadAllTextAsync(filePath, Encoding.UTF8, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not read file as UTF-8, skipping: {FilePath}", filePath);
                return null;
            }

            // Extract type information if enabled
            TypeExtractionResult? typeData = null;
            if (_configuration.GetValue("CodeSearch:TypeExtraction:Enabled", true))
            {
                // Check cache first (bulk mode)
                if (symbolCache != null && symbolCache.TryGetValue(filePath, out var cachedSymbols))
                {
                    // Convert cached symbols to TypeExtractionResult
                    typeData = ConvertJulieSymbolsToTypeData(cachedSymbols);
                }
                else
                {
                    // Fall back to single-file extraction
                    try
                    {
                        typeData = await _typeExtractionService.ExtractTypes(content, filePath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Failed to extract types from {FilePath}", filePath);
                    }
                }
            }

            // Get directory information
            var directoryPath = Path.GetDirectoryName(filePath) ?? "";
            var relativeDirectoryPath = Path.GetRelativePath(workspacePath, directoryPath);
            var directoryName = Path.GetFileName(directoryPath) ?? "";

            // Create Lucene document
            var document = new Document
            {
                // Core fields
                new StringField("path", filePath, Field.Store.YES),
                new StringField("relativePath", Path.GetRelativePath(workspacePath, filePath), Field.Store.YES),
                new TextField("content", content, Field.Store.YES), // KEEP - needed for line extraction
                                
                                // Multi-field indexing for different search modes
                                new TextField("content_symbols", ExtractSymbolsOnly(content, typeData), Field.Store.NO), // Symbol-only search (identifiers, class names)
                                new TextField("content_patterns", content, Field.Store.NO),     // Pattern-preserving search (special chars preserved)
                
                // Metadata fields
                new StringField("extension", fileInfo.Extension.ToLowerInvariant(), Field.Store.YES),
                new Int64Field("size", fileInfo.Length, Field.Store.YES),
                new Int64Field("modified", fileInfo.LastWriteTimeUtc.Ticks, Field.Store.YES),
                new StringField("filename", fileInfo.Name, Field.Store.YES),
                new StringField("filename_lower", fileInfo.Name.ToLowerInvariant(), Field.Store.NO),
                
                // Directory fields
                new StringField("directory", directoryPath, Field.Store.YES),
                new StringField("relativeDirectory", relativeDirectoryPath, Field.Store.YES),
                new StringField("directoryName", directoryName, Field.Store.YES),
                
                // Simple line count for statistics
                new Int32Field("line_count", content.Count(c => c == '\n') + 1, Field.Store.YES)
            };
            
            // Add type-specific fields if extraction succeeded
            if (typeData?.Success == true && (typeData.Types.Any() || typeData.Methods.Any()))
            {
                // Searchable field with all type names
                var allTypeNames = typeData.Types.Select(t => t.Name)
                    .Concat(typeData.Methods.Select(m => m.Name))
                    .Distinct()
                    .ToList();
                
                var typeNamesField = string.Join(" ", allTypeNames);
                _logger.LogDebug("Adding type_names field for {FilePath}: {TypeNames} (Count: {Count})", 
                    filePath, typeNamesField, allTypeNames.Count);
                    
                document.Add(new TextField("type_names", typeNamesField, Field.Store.NO));
                
                // Stored field with full type information (JSON) - using UTF-8 safe serialization
                var typeJson = JsonSerializer.Serialize(new
                {
                    types = typeData.Types,
                    methods = typeData.Methods,
                    language = typeData.Language
                }, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = false
                });
                document.Add(new StoredField("type_info", typeJson));
                
                // Add individual type definition fields for boosting
                foreach (var type in typeData.Types)
                {
                    document.Add(new TextField("type_def", $"{type.Kind} {type.Name}", Field.Store.NO));
                }
                
                // Count fields for statistics
                document.Add(new Int32Field("type_count", typeData.Types.Count, Field.Store.YES));
                document.Add(new Int32Field("method_count", typeData.Methods.Count, Field.Store.YES));
            }

            // Add searchable path components
            var pathParts = Path.GetRelativePath(workspacePath, filePath).Split(Path.DirectorySeparatorChar);
            foreach (var part in pathParts)
            {
                document.Add(new TextField("pathComponent", part, Field.Store.NO));
            }

            return document;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create document for file {FilePath}", filePath);
            return null;
        }
    }

    /// <summary>
    /// Convert JulieSymbols from cache to TypeExtractionResult format
    /// </summary>
    private TypeExtractionResult ConvertJulieSymbolsToTypeData(List<JulieSymbol> symbols)
    {
        var types = new List<TypeInfo>();
        var methods = new List<TypeExtraction.MethodInfo>();

        foreach (var symbol in symbols)
        {
            if (IsTypeSymbol(symbol.Kind))
            {
                types.Add(new TypeInfo
                {
                    Name = symbol.Name,
                    Kind = symbol.Kind,
                    Signature = symbol.Signature ?? symbol.Name,
                    Line = symbol.StartLine,
                    Column = symbol.StartColumn,
                    Modifiers = symbol.Visibility != null ? new List<string> { symbol.Visibility } : new()
                });
            }
            else if (IsMethodSymbol(symbol.Kind))
            {
                methods.Add(new TypeExtraction.MethodInfo
                {
                    Name = symbol.Name,
                    Signature = symbol.Signature ?? symbol.Name,
                    Line = symbol.StartLine,
                    Column = symbol.StartColumn,
                    Modifiers = symbol.Visibility != null ? new List<string> { symbol.Visibility } : new()
                });
            }
        }

        return new TypeExtractionResult
        {
            Success = true,
            Types = types,
            Methods = methods,
            Language = symbols.FirstOrDefault()?.Language
        };
    }

    private static bool IsTypeSymbol(string kind)
    {
        return kind.Equals("class", StringComparison.OrdinalIgnoreCase) ||
               kind.Equals("interface", StringComparison.OrdinalIgnoreCase) ||
               kind.Equals("struct", StringComparison.OrdinalIgnoreCase) ||
               kind.Equals("enum", StringComparison.OrdinalIgnoreCase) ||
               kind.Equals("trait", StringComparison.OrdinalIgnoreCase) ||
               kind.Equals("type", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMethodSymbol(string kind)
    {
        return kind.Equals("method", StringComparison.OrdinalIgnoreCase) ||
               kind.Equals("function", StringComparison.OrdinalIgnoreCase) ||
               kind.Equals("constructor", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Read custom ignore patterns from .codesearchignore file in workspace root.
    /// Auto-creates a template file with examples if it doesn't exist.
    ///
    /// File format:
    /// - One pattern per line
    /// - Lines starting with # are comments
    /// - Empty lines are ignored
    /// - Supports glob patterns (e.g., **/test/**, **/*.log)
    /// - Patterns are relative to workspace root
    ///
    /// Example .codesearchignore:
    ///   # Ignore all test directories
    ///   **/Tests/**
    ///   **/Resources/**
    ///
    ///   # Ignore specific files
    ///   **/*.generated.cs
    /// </summary>
    private List<string> ReadCustomIgnorePatterns(string workspacePath)
    {
        var patterns = new List<string>();
        var ignoreFilePath = Path.Combine(workspacePath, ".codesearchignore");

        if (!File.Exists(ignoreFilePath))
        {
            _logger.LogInformation("📝 Creating template .codesearchignore file at {Path}", ignoreFilePath);
            CreateTemplateIgnoreFile(ignoreFilePath);
            return patterns; // Template has all examples commented out
        }

        try
        {
            var lines = File.ReadAllLines(ignoreFilePath);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();

                // Skip empty lines and comments
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
                    continue;

                patterns.Add(trimmed);
                _logger.LogDebug("Added custom ignore pattern: {Pattern}", trimmed);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read .codesearchignore file at {Path}", ignoreFilePath);
        }

        return patterns;
    }

    /// <summary>
    /// Create a template .codesearchignore file with helpful examples (all commented out).
    /// Users can uncomment patterns they want to use.
    /// </summary>
    private void CreateTemplateIgnoreFile(string filePath)
    {
        try
        {
            var template = @"# CodeSearch Ignore Patterns
#
# Add patterns below to exclude files/directories from indexing.
# One pattern per line. Lines starting with # are comments.
# Supports glob patterns (**, *, ?, etc.)
#
# NOTE: CodeSearch already ignores common directories by default:
#   - node_modules, .git, .svn, .hg
#   - bin, obj, .vs, .vscode, .idea
#   - dist, build, coverage, .next, .nuxt
#   - __pycache__, .pytest_cache, .mypy_cache
#   - .coa (CodeSearch's own index directory)
#
# Examples (uncomment to use):

# Ignore all test resource directories
# **/Tests/Resources/**
# **/Resources/GoldenMaster/**

# Ignore test projects entirely
# **/*.Tests/**

# Ignore generated files
# **/*.g.cs
# **/*.generated.cs
# **/*.Designer.cs

# Ignore documentation directories
# **/docs/**
# **/documentation/**

# Ignore vendor/third-party code
# **/vendor/**
# **/third-party/**
# **/packages/**

# Ignore specific file types
# **/*.min.js
# **/*.min.css
# **/*.map

# Add your custom patterns below:

";

            File.WriteAllText(filePath, template);
            _logger.LogInformation("✅ Created .codesearchignore template - edit to customize ignore patterns");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create .codesearchignore template at {Path}", filePath);
        }
    }

}

/// <summary>
/// Interface for file indexing operations
/// </summary>
public interface IFileIndexingService
{
    Task<IndexingResult> IndexWorkspaceAsync(string workspacePath, CancellationToken cancellationToken = default);
    Task<int> IndexDirectoryAsync(string workspacePath, string directoryPath, CancellationToken cancellationToken = default);
    Task<bool> IndexFileAsync(string workspacePath, string filePath, CancellationToken cancellationToken = default);
    Task<bool> RemoveFileAsync(string workspacePath, string filePath, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of an indexing operation
/// </summary>
public class IndexingResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string WorkspacePath { get; set; } = string.Empty;
    public int IndexedFileCount { get; set; }
    public int SkippedFileCount { get; set; }
    public int ErrorCount { get; set; }
    public TimeSpan Duration { get; set; }
}