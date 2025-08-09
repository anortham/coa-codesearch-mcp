using Lucene.Net.Documents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using COA.CodeSearch.Next.McpServer.Models;
using COA.CodeSearch.Next.McpServer.Services.Lucene;
using System.Collections.Concurrent;
using System.Text;

namespace COA.CodeSearch.Next.McpServer.Services;

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
        IOptions<MemoryLimitsConfiguration> memoryLimits)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _luceneIndexService = luceneIndexService ?? throw new ArgumentNullException(nameof(luceneIndexService));
        _pathResolution = pathResolution ?? throw new ArgumentNullException(nameof(pathResolution));
        _metricsService = metricsService ?? throw new ArgumentNullException(nameof(metricsService));
        _circuitBreakerService = circuitBreakerService ?? throw new ArgumentNullException(nameof(circuitBreakerService));
        _memoryPressureService = memoryPressureService ?? throw new ArgumentNullException(nameof(memoryPressureService));
        _memoryLimits = memoryLimits?.Value ?? throw new ArgumentNullException(nameof(memoryLimits));
        
        // Initialize supported extensions from configuration or defaults
        var extensions = configuration.GetSection("Lucene:SupportedExtensions").Get<string[]>() 
            ?? new[] { 
                ".cs", ".js", ".ts", ".py", ".java", ".cpp", ".c", ".h", ".go", ".rs", ".rb", ".php", ".sql",
                ".html", ".css", ".json", ".xml", ".md", ".txt", ".yml", ".yaml", ".toml", ".ini", ".log",
                ".sh", ".bat", ".ps1", ".dockerfile", ".makefile", ".gradle", ".csproj", ".sln", ".config"
            };
        _supportedExtensions = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);
        
        // Initialize excluded directories
        var excluded = configuration.GetSection("Lucene:ExcludedDirectories").Get<string[]>() 
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

            // Clear existing index if requested
            if (initResult.IsNewIndex)
            {
                await _luceneIndexService.ClearIndexAsync(workspacePath, cancellationToken);
            }

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
        
        // Check for memory pressure
        if (_memoryPressureService.ShouldThrottleOperation("directory_indexing"))
        {
            _logger.LogWarning("Directory indexing throttled due to memory pressure");
            return 0;
        }

        var indexedCount = 0;
        var documents = new List<Document>();
        var batchSize = _configuration.GetValue("Lucene:BatchSize", 100);

        try
        {
            var files = GetFilesToIndex(directoryPath);
            
            foreach (var filePath in files)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    var document = await CreateDocumentFromFileAsync(filePath, workspacePath, cancellationToken);
                    if (document != null)
                    {
                        documents.Add(document);
                        
                        // Batch index documents
                        if (documents.Count >= batchSize)
                        {
                            await _luceneIndexService.IndexDocumentsAsync(workspacePath, documents, cancellationToken);
                            indexedCount += documents.Count;
                            documents.Clear();
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to index file {FilePath}", filePath);
                }
            }
            
            // Index remaining documents
            if (documents.Count > 0)
            {
                await _luceneIndexService.IndexDocumentsAsync(workspacePath, documents, cancellationToken);
                indexedCount += documents.Count;
            }
            
            // Record metrics
            var duration = DateTime.UtcNow - startTime;
            _metricsService.RecordFileIndexed(directoryPath, 0, duration, true);
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
            var document = await CreateDocumentFromFileAsync(filePath, workspacePath, cancellationToken);
            if (document != null)
            {
                await _luceneIndexService.IndexDocumentAsync(workspacePath, document, cancellationToken);
                await _luceneIndexService.CommitAsync(workspacePath, cancellationToken);
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
            return Enumerable.Empty<string>();

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.System | FileAttributes.Hidden
        };

        return Directory.EnumerateFiles(directoryPath, "*", options)
            .Where(file => 
            {
                // Check if file should be indexed
                var fileName = Path.GetFileName(file);
                var extension = Path.GetExtension(file);
                var directory = Path.GetDirectoryName(file);
                
                // Skip excluded directories
                if (directory != null && _excludedDirectories.Any(excluded => 
                    directory.Contains(excluded, StringComparison.OrdinalIgnoreCase)))
                    return false;
                
                // Check supported extensions
                if (!_supportedExtensions.Contains(extension))
                    return false;
                
                // Check file size
                var fileInfo = new FileInfo(file);
                if (fileInfo.Length > MAX_FILE_SIZE)
                {
                    _logger.LogDebug("Skipping large file {File} ({Size} bytes)", file, fileInfo.Length);
                    return false;
                }
                
                return true;
            });
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

            // Create Lucene document
            var document = new Document
            {
                new StringField("path", filePath, Field.Store.YES),
                new StringField("relativePath", Path.GetRelativePath(workspacePath, filePath), Field.Store.YES),
                new TextField("content", content, Field.Store.YES),
                new StringField("extension", fileInfo.Extension.ToLowerInvariant(), Field.Store.YES),
                new Int64Field("size", fileInfo.Length, Field.Store.YES),
                new Int64Field("modified", fileInfo.LastWriteTimeUtc.Ticks, Field.Store.YES),
                new TextField("filename", fileInfo.Name, Field.Store.YES)
            };

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