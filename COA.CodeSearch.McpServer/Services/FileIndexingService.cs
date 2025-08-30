using Lucene.Net.Documents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services.Lucene;
using COA.CodeSearch.McpServer.Services.TypeExtraction;
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
    private readonly ITypeExtractionService? _typeExtractionService;
    private readonly MemoryLimitsConfiguration _memoryLimits;
    private readonly HashSet<string> _supportedExtensions;
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
        ITypeExtractionService? typeExtractionService = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _luceneIndexService = luceneIndexService ?? throw new ArgumentNullException(nameof(luceneIndexService));
        _pathResolution = pathResolution ?? throw new ArgumentNullException(nameof(pathResolution));
        _metricsService = metricsService ?? throw new ArgumentNullException(nameof(metricsService));
        _circuitBreakerService = circuitBreakerService ?? throw new ArgumentNullException(nameof(circuitBreakerService));
        _memoryPressureService = memoryPressureService ?? throw new ArgumentNullException(nameof(memoryPressureService));
        _memoryLimits = memoryLimits?.Value ?? throw new ArgumentNullException(nameof(memoryLimits));
        _typeExtractionService = typeExtractionService;
        
        // Initialize supported extensions from configuration or defaults
        var extensions = configuration.GetSection("CodeSearch:Lucene:SupportedExtensions").Get<string[]>() 
            ?? new[] { 
                ".cs", ".js", ".ts", ".py", ".java", ".cpp", ".c", ".h", ".go", ".rs", ".rb", ".php", ".sql",
                ".html", ".css", ".json", ".xml", ".md", ".txt", ".yml", ".yaml", ".toml", ".ini",
                ".sh", ".bat", ".ps1", ".dockerfile", ".makefile", ".gradle", ".csproj", ".sln", ".config"
            };
        _supportedExtensions = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);
        
        // Initialize excluded directories
        var excluded = configuration.GetSection("CodeSearch:Lucene:ExcludedDirectories").Get<string[]>() 
            ?? PathConstants.DefaultExcludedDirectories;
        _excludedDirectories = new HashSet<string>(excluded, StringComparer.OrdinalIgnoreCase);
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

            // Index all files in the workspace
            result.IndexedFileCount = await IndexDirectoryAsync(workspacePath, workspacePath, cancellationToken);
            
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

    public async Task<int> IndexDirectoryAsync(string workspacePath, string directoryPath, CancellationToken cancellationToken = default)
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
                    var document = await CreateDocumentFromFileAsync(filePath, workspacePath, cancellationToken);
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
                
            var document = await CreateDocumentFromFileAsync(filePath, workspacePath, cancellationToken);
            if (document != null)
            {
                await _luceneIndexService.IndexDocumentAsync(workspacePath, document, cancellationToken);
                await _luceneIndexService.CommitAsync(workspacePath, cancellationToken);
                
                // Verify the commit worked by checking document count
                var count = await _luceneIndexService.GetDocumentCountAsync(workspacePath, cancellationToken);
                _logger.LogInformation("After indexing {FilePath}, document count is: {Count}", filePath, count);
                
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
                    
                    // Check supported extensions
                    if (!_supportedExtensions.Contains(extension))
                        continue;
                    
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

    private async Task<Document?> CreateDocumentFromFileAsync(string filePath, string workspacePath, CancellationToken cancellationToken)
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
            
            // Extract type information if service is available and enabled
            TypeExtractionResult? typeData = null;
            if (_typeExtractionService != null && _configuration.GetValue("CodeSearch:TypeExtraction:Enabled", true))
            {
                try
                {
                    typeData = _typeExtractionService.ExtractTypes(content, filePath);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to extract types from {FilePath}", filePath);
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
                    .Distinct();
                document.Add(new TextField("type_names", string.Join(" ", allTypeNames), Field.Store.NO));
                
                // Stored field with full type information (JSON)
                var typeJson = JsonSerializer.Serialize(new
                {
                    types = typeData.Types,
                    methods = typeData.Methods,
                    language = typeData.Language
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