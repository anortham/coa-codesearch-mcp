using Lucene.Net.Documents;
using Lucene.Net.Index;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using COA.CodeSearch.McpServer.Models;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;
using System.Text;

namespace COA.CodeSearch.McpServer.Services;

public class FileIndexingService
{
    private readonly ILogger<FileIndexingService> _logger;
    private readonly IConfiguration _configuration;
    private readonly ILuceneIndexService _luceneIndexService;
    private readonly IPathResolutionService _pathResolution;
    private readonly IIndexingMetricsService _metricsService;
    private readonly ICircuitBreakerService _circuitBreakerService;
    private readonly IBatchIndexingService _batchIndexingService;
    private readonly IMemoryPressureService _memoryPressureService;
    private readonly MemoryLimitsConfiguration _memoryLimits;
    private readonly HashSet<string> _supportedExtensions;
    private readonly HashSet<string> _excludedDirectories;

    public FileIndexingService(
        ILogger<FileIndexingService> logger, 
        IConfiguration configuration,
        ILuceneIndexService luceneIndexService,
        IPathResolutionService pathResolution,
        IIndexingMetricsService metricsService,
        ICircuitBreakerService circuitBreakerService,
        IBatchIndexingService batchIndexingService,
        IMemoryPressureService memoryPressureService,
        IOptions<MemoryLimitsConfiguration> memoryLimits)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _luceneIndexService = luceneIndexService ?? throw new ArgumentNullException(nameof(luceneIndexService));
        _pathResolution = pathResolution ?? throw new ArgumentNullException(nameof(pathResolution));
        _metricsService = metricsService ?? throw new ArgumentNullException(nameof(metricsService));
        _circuitBreakerService = circuitBreakerService ?? throw new ArgumentNullException(nameof(circuitBreakerService));
        _batchIndexingService = batchIndexingService ?? throw new ArgumentNullException(nameof(batchIndexingService));
        _memoryPressureService = memoryPressureService ?? throw new ArgumentNullException(nameof(memoryPressureService));
        _memoryLimits = memoryLimits?.Value ?? throw new ArgumentNullException(nameof(memoryLimits));
        
        // Initialize supported extensions from configuration or defaults
        var extensions = configuration.GetSection("Lucene:SupportedExtensions").Get<string[]>() 
            ?? new[] { 
                // Common text-based files (fallback if config missing)
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

    public async Task<int> IndexDirectoryAsync(string workspacePath, string directoryPath, CancellationToken cancellationToken = default)
    {
        using var operationTracker = _metricsService.StartOperation("IndexDirectory", workspacePath);
        
        var indexWriter = await _luceneIndexService.GetIndexWriterAsync(workspacePath, cancellationToken).ConfigureAwait(false);
        var indexedCount = 0;
        
        try
        {
            // Check for memory pressure before starting intensive operation
            if (_memoryPressureService.ShouldThrottleOperation("directory_indexing"))
            {
                _logger.LogWarning("Directory indexing throttled due to memory pressure");
                return 0;
            }
            
            // Configure backpressure limits based on system resources and memory pressure
            var baseConcurrency = Math.Min(Environment.ProcessorCount, _memoryLimits.MaxIndexingConcurrency);
            var maxConcurrency = _memoryPressureService.GetRecommendedConcurrency(baseConcurrency);
            var maxQueueSize = Math.Min(_memoryLimits.MaxIndexingQueueSize, maxConcurrency * 10); // 10x buffer for smooth operation
            
            var errors = new ConcurrentBag<(string file, Exception error)>();
            
            // Use bounded channel for proper backpressure control
            var channel = System.Threading.Channels.Channel.CreateBounded<string>(new System.Threading.Channels.BoundedChannelOptions(maxQueueSize)
            {
                FullMode = System.Threading.Channels.BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = true
            });
            
            // Producer task - enumerate files with backpressure
            var producerTask = Task.Run(async () =>
            {
                try
                {
                    await foreach (var filePath in GetFilesToIndexAsync(directoryPath, cancellationToken))
                    {
                        await channel.Writer.WriteAsync(filePath, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error enumerating files in directory: {Directory}", directoryPath);
                }
                finally
                {
                    channel.Writer.Complete();
                }
            }, cancellationToken);
            
            // Consumer task - process files with limited concurrency
            await Parallel.ForEachAsync(
                channel.Reader.ReadAllAsync(cancellationToken),
                new ParallelOptions 
                { 
                    MaxDegreeOfParallelism = maxConcurrency,
                    CancellationToken = cancellationToken 
                },
                async (filePath, ct) =>
                {
                    try
                    {
                        if (await IndexFileOptimizedAsync(indexWriter, filePath, workspacePath, ct).ConfigureAwait(false))
                        {
                            Interlocked.Increment(ref indexedCount);
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add((filePath, ex));
                        _logger.LogWarning(ex, "Failed to index file: {FilePath}", filePath);
                    }
                }).ConfigureAwait(false);
            
            // Wait for producer to complete
            await producerTask.ConfigureAwait(false);
            
            // Report any errors
            if (!errors.IsEmpty)
            {
                _logger.LogWarning("Failed to index {Count} files out of {Total}", errors.Count, indexedCount + errors.Count);
            }
            
            // Final commit
            await _luceneIndexService.CommitAsync(workspacePath, cancellationToken).ConfigureAwait(false);
            
            // Trigger memory cleanup if needed after intensive operation
            await _memoryPressureService.TriggerMemoryCleanupIfNeededAsync().ConfigureAwait(false);
            
            _logger.LogInformation("Indexed {Count} files from directory: {Directory}", indexedCount, directoryPath);
            return indexedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error indexing directory: {Directory}", directoryPath);
            throw;
        }
    }

    /// <summary>
    /// High-performance bulk directory indexing using batch commits for 10-100x performance improvement
    /// </summary>
    public async Task<int> IndexDirectoryBatchAsync(string workspacePath, string directoryPath, CancellationToken cancellationToken = default)
    {
        using var operationTracker = _metricsService.StartOperation("IndexDirectoryBatch", workspacePath);
        
        var indexedCount = 0;
        
        try
        {
            _logger.LogInformation("Starting batch indexing of directory: {Directory}", directoryPath);
            
            // Check for memory pressure before starting intensive operation
            if (_memoryPressureService.ShouldThrottleOperation("batch_indexing"))
            {
                _logger.LogWarning("Batch indexing throttled due to memory pressure");
                return 0;
            }
            
            // Configure backpressure limits based on system resources and memory pressure  
            var baseConcurrency = Math.Min(Environment.ProcessorCount, _memoryLimits.MaxIndexingConcurrency);
            var maxConcurrency = _memoryPressureService.GetRecommendedConcurrency(baseConcurrency);
            var maxQueueSize = Math.Min(_memoryLimits.MaxIndexingQueueSize, maxConcurrency * 10); // 10x buffer for smooth operation
            
            var errors = new ConcurrentBag<(string file, Exception error)>();
            
            // Use bounded channel for proper backpressure control
            var channel = System.Threading.Channels.Channel.CreateBounded<string>(new System.Threading.Channels.BoundedChannelOptions(maxQueueSize)
            {
                FullMode = System.Threading.Channels.BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = true
            });
            
            // Producer task - enumerate files with backpressure
            var producerTask = Task.Run(async () =>
            {
                try
                {
                    await foreach (var filePath in GetFilesToIndexAsync(directoryPath, cancellationToken))
                    {
                        await channel.Writer.WriteAsync(filePath, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error enumerating files in directory: {Directory}", directoryPath);
                }
                finally
                {
                    channel.Writer.Complete();
                }
            }, cancellationToken);
            
            // Consumer task - process files with limited concurrency using batch indexing
            await Parallel.ForEachAsync(
                channel.Reader.ReadAllAsync(cancellationToken),
                new ParallelOptions 
                { 
                    MaxDegreeOfParallelism = maxConcurrency,
                    CancellationToken = cancellationToken 
                },
                async (filePath, ct) =>
                {
                    try
                    {
                        if (await IndexFileForBatchAsync(filePath, workspacePath, ct).ConfigureAwait(false))
                        {
                            Interlocked.Increment(ref indexedCount);
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add((filePath, ex));
                        _logger.LogWarning(ex, "Failed to add file to batch: {FilePath}", filePath);
                    }
                }).ConfigureAwait(false);
            
            // Wait for producer to complete
            await producerTask.ConfigureAwait(false);
            
            // Flush any remaining batched documents
            await _batchIndexingService.FlushBatchAsync(workspacePath, cancellationToken).ConfigureAwait(false);
            
            // Report any errors
            if (!errors.IsEmpty)
            {
                _logger.LogWarning("Failed to batch index {Count} files out of {Total}", errors.Count, indexedCount + errors.Count);
            }
            
            // Trigger memory cleanup if needed after intensive operation
            await _memoryPressureService.TriggerMemoryCleanupIfNeededAsync().ConfigureAwait(false);
            
            _logger.LogInformation("Batch indexed {Count} files from directory: {Directory}", indexedCount, directoryPath);
            return indexedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error batch indexing directory: {Directory}", directoryPath);
            throw;
        }
    }

    private async IAsyncEnumerable<string> GetFilesToIndexAsync(string directoryPath, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var directoriesToProcess = new ConcurrentStack<string>();
        directoriesToProcess.Push(directoryPath);
        var processedCount = 0;
        const int yieldInterval = 100; // Yield control every 100 files to be responsive
        
        while (directoriesToProcess.TryPop(out var currentDir))
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            if (!SafeDirectoryExists(currentDir))
                continue;
                
            var dirName = GetSafeFileName(currentDir);
            if (!string.IsNullOrEmpty(dirName) && _excludedDirectories.Contains(dirName))
                continue;
                
            // Skip .codesearch directories
            if (IsUnderCodeSearchDirectory(currentDir))
                continue;
                
            // Yield files from current directory
            foreach (var file in SafeEnumerateFiles(currentDir))
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var extension = GetSafeFileExtension(file);
                if (_supportedExtensions.Contains(extension))
                {
                    yield return file;
                    
                    // Yield control periodically to maintain responsiveness
                    if (++processedCount % yieldInterval == 0)
                    {
                        await Task.Yield();
                    }
                }
            }
            
            // Add subdirectories to process
            foreach (var subDir in SafeEnumerateDirectories(currentDir))
            {
                var subDirName = GetSafeFileName(subDir);
                if (!_excludedDirectories.Contains(subDirName))
                {
                    directoriesToProcess.Push(subDir);
                }
            }
        }
    }
    
    // Keep the old synchronous method for backward compatibility
    private IEnumerable<string> GetFilesToIndex(string directoryPath)
    {
        var directoriesToProcess = new ConcurrentStack<string>();
        directoriesToProcess.Push(directoryPath);
        
        while (directoriesToProcess.TryPop(out var currentDir))
        {
            if (!SafeDirectoryExists(currentDir))
                continue;
                
            var dirName = GetSafeFileName(currentDir);
            if (!string.IsNullOrEmpty(dirName) && _excludedDirectories.Contains(dirName))
                continue;
                
            // Skip .codesearch directories
            if (IsUnderCodeSearchDirectory(currentDir))
                continue;
                
            // Yield files from current directory
            foreach (var file in SafeEnumerateFiles(currentDir))
            {
                var extension = GetSafeFileExtension(file);
                if (_supportedExtensions.Contains(extension))
                {
                    yield return file;
                }
            }
            
            // Add subdirectories to process
            foreach (var subDir in SafeEnumerateDirectories(currentDir))
            {
                var subDirName = GetSafeFileName(subDir);
                if (!_excludedDirectories.Contains(subDirName))
                {
                    directoriesToProcess.Push(subDir);
                }
            }
        }
    }
    

    public async Task<bool> IndexFileAsync(string workspacePath, string filePath, CancellationToken cancellationToken = default)
    {
        // Check memory pressure before indexing
        if (_memoryPressureService.ShouldThrottleOperation("file_indexing"))
        {
            _logger.LogDebug("File indexing throttled due to memory pressure: {FilePath}", filePath);
            return false;
        }
        
        try
        {
            return await _circuitBreakerService.ExecuteAsync(
                $"IndexFile:{Path.GetExtension(filePath)}", 
                async () =>
                {
                    var indexWriter = await _luceneIndexService.GetIndexWriterAsync(workspacePath, cancellationToken).ConfigureAwait(false);
                    var result = await IndexFileOptimizedAsync(indexWriter, filePath, workspacePath, cancellationToken);
                    
                    if (result)
                    {
                        await _luceneIndexService.CommitAsync(workspacePath, cancellationToken).ConfigureAwait(false);
                    }
                    
                    return result;
                }, 
                cancellationToken);
        }
        catch (CircuitBreakerOpenException ex)
        {
            _logger.LogWarning("File indexing skipped due to circuit breaker: {FilePath} - {Reason}", filePath, ex.Message);
            return false;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async Task<bool> IndexFileOptimizedAsync(
        IndexWriter indexWriter, 
        string filePath,
        string workspacePath, 
        CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        // Check if file exists before creating FileInfo to avoid exceptions
        if (!File.Exists(filePath))
        {
            _logger.LogWarning("File no longer exists, skipping indexing: {FilePath}", filePath);
            return false;
        }
        
        var fileInfo = new FileInfo(filePath);
        var success = false;
        string? error = null;
        
        try
        {
            if (fileInfo.Length > _memoryLimits.MaxFileSize)
            {
                error = $"File too large to index ({fileInfo.Length} bytes)";
                _logger.LogWarning("File too large to index: {FilePath} ({Size} bytes)", filePath, fileInfo.Length);
                return false;
            }

            // Read file content using memory-mapped files for large files
            string content;
            if (fileInfo.Length > _memoryLimits.LargeFileThreshold)
            {
                content = await ReadLargeFileAsync(filePath, cancellationToken);
            }
            else
            {
                // Use ArrayPool for small files
                content = await ReadSmallFileAsync(filePath, cancellationToken);
            }
            
            // Create document efficiently
            var doc = CreateDocument(filePath, fileInfo, content, workspacePath);

            // Update or add document
            indexWriter.UpdateDocument(new Term("id", filePath), doc);
            
            success = true;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            _logger.LogError(ex, "Error indexing file: {FilePath}", filePath);
            return false;
        }
        finally
        {
            stopwatch.Stop();
            _metricsService.RecordFileIndexed(filePath, fileInfo.Length, stopwatch.Elapsed, success, error);
        }
    }

    /// <summary>
    /// Index a file for batch processing - creates document and adds to batch without immediate commit
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async Task<bool> IndexFileForBatchAsync(string filePath, string workspacePath, CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var fileInfo = new FileInfo(filePath);
        var success = false;
        string? error = null;
        
        try
        {
            if (fileInfo.Length > _memoryLimits.MaxFileSize)
            {
                error = $"File too large to index ({fileInfo.Length} bytes)";
                _logger.LogWarning("File too large to batch index: {FilePath} ({Size} bytes)", filePath, fileInfo.Length);
                return false;
            }

            // Read file content using memory-mapped files for large files
            string content;
            if (fileInfo.Length > _memoryLimits.LargeFileThreshold)
            {
                content = await ReadLargeFileAsync(filePath, cancellationToken);
            }
            else
            {
                // Use ArrayPool for small files
                content = await ReadSmallFileAsync(filePath, cancellationToken);
            }
            
            // Create document efficiently
            var doc = CreateDocument(filePath, fileInfo, content, workspacePath);

            // Add to batch for efficient bulk processing
            await _batchIndexingService.AddDocumentAsync(workspacePath, doc, filePath, cancellationToken);
            
            success = true;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            _logger.LogError(ex, "Error preparing file for batch indexing: {FilePath}", filePath);
            return false;
        }
        finally
        {
            stopwatch.Stop();
            _metricsService.RecordFileIndexed(filePath, fileInfo.Length, stopwatch.Elapsed, success, error);
        }
    }
    
    private async Task<string> ReadLargeFileAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            using var mmf = MemoryMappedFile.CreateFromFile(filePath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
            using var accessor = mmf.CreateViewStream(0, 0, MemoryMappedFileAccess.Read);
            using var reader = new StreamReader(accessor, Encoding.UTF8);
            return await reader.ReadToEndAsync(cancellationToken);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Access denied when reading large file with memory mapping, falling back to stream: {FilePath}", filePath);
            return await ReadLargeFileWithStreamAsync(filePath, cancellationToken);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "IO error when reading large file with memory mapping, falling back to stream: {FilePath}", filePath);
            return await ReadLargeFileWithStreamAsync(filePath, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error when reading large file with memory mapping: {FilePath}", filePath);
            throw;
        }
    }
    
    private async Task<string> ReadLargeFileWithStreamAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 
                bufferSize: 65536, // 64KB buffer - optimal size for sequential file reading performance
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return await reader.ReadToEndAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read large file with stream fallback: {FilePath}", filePath);
            throw;
        }
    }
    
    private async Task<string> ReadSmallFileAsync(string filePath, CancellationToken cancellationToken)
    {
        var fileLength = (int)new FileInfo(filePath).Length;
        var buffer = ArrayPool<byte>.Shared.Rent(fileLength);
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, fileLength), cancellationToken);
            return Encoding.UTF8.GetString(buffer.AsSpan(0, bytesRead));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Document CreateDocument(string filePath, FileInfo fileInfo, string content, string workspacePath)
    {
        var extension = GetSafeFileExtension(filePath);
        var fileName = GetSafeFileName(filePath);
        var relativePath = GetSafeRelativePath(workspacePath, filePath);
        var directoryPath = GetSafeDirectoryName(filePath);
        var relativeDirectoryPath = GetSafeRelativePath(workspacePath, directoryPath);
        var directoryName = GetSafeFileName(directoryPath);
        
        var doc = new Document
        {
            // Stored fields
            new StringField("id", filePath, Field.Store.YES),
            new StringField("path", filePath, Field.Store.YES),
            new StringField("filename", fileName, Field.Store.YES),
            new StringField("filename_lower", fileName.ToLowerInvariant(), Field.Store.NO), // For case-insensitive wildcard search
            new StringField("extension", extension, Field.Store.YES),
            new Int64Field("size", fileInfo.Length, Field.Store.YES),
            new Int64Field("lastModified", fileInfo.LastWriteTimeUtc.Ticks, Field.Store.YES),
            new StringField("relativePath", relativePath, Field.Store.YES),
            new StringField("directory", directoryPath, Field.Store.YES),
            new StringField("relativeDirectory", relativeDirectoryPath, Field.Store.YES),
            new StringField("directoryName", directoryName, Field.Store.YES),
            
            // Indexed fields
            new TextField("content", content, Field.Store.YES), // Store content for MoreLikeThis
            new TextField("filename_text", fileName, Field.Store.NO),
            new TextField("directory_text", relativeDirectoryPath.Replace(Path.DirectorySeparatorChar, ' '), Field.Store.NO)
        };

        // Add language field if we can determine it
        var language = GetLanguageFromExtension(extension);
        if (string.IsNullOrEmpty(language) && string.IsNullOrEmpty(extension))
        {
            // Try filename-based detection for files without extensions
            language = GetLanguageFromFilename(fileName);
        }
        if (!string.IsNullOrEmpty(language))
        {
            doc.Add(new StringField("language", language, Field.Store.YES));
        }
        
        return doc;
    }

    public Task<bool> RemoveFileAsync(string workspacePath, string filePath, CancellationToken cancellationToken = default)
    {
        return DeleteFileAsync(workspacePath, filePath, cancellationToken);
    }

    public async Task<bool> ReindexFileAsync(string workspacePath, string filePath, CancellationToken cancellationToken = default)
    {
        // Reindexing is just a remove + add operation
        return await IndexFileAsync(workspacePath, filePath, cancellationToken);
    }
    
    [Obsolete("Use ReindexFileAsync instead. This method will be removed in a future version.")]
    public async Task<bool> UpdateFileAsync(string workspacePath, string filePath, CancellationToken cancellationToken = default)
    {
        return await ReindexFileAsync(workspacePath, filePath, cancellationToken);
    }

    public async Task<bool> DeleteFileAsync(string workspacePath, string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            var indexWriter = await _luceneIndexService.GetIndexWriterAsync(workspacePath, cancellationToken).ConfigureAwait(false);
            
            // Delete document by file path (using the "id" field)
            indexWriter.DeleteDocuments(new Term("id", filePath));
            
            // Commit the deletion
            await _luceneIndexService.CommitAsync(workspacePath, cancellationToken).ConfigureAwait(false);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting file from index: {FilePath}", filePath);
            return false;
        }
    }

    private static string GetLanguageFromExtension(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            // .NET & Microsoft
            ".cs" => "csharp",
            ".vb" => "vb.net",
            ".fs" or ".fsi" or ".fsx" => "fsharp",
            ".csproj" or ".vbproj" or ".fsproj" => "msbuild",
            ".sln" => "solution",
            ".config" => "xml",
            ".resx" => "xml",
            ".xaml" => "xaml",
            ".razor" => "razor",
            ".cshtml" or ".vbhtml" => "razor",
            
            // Web Technologies
            ".html" or ".htm" => "html",
            ".css" => "css",
            ".scss" or ".sass" => "scss",
            ".less" => "less",
            ".js" => "javascript",
            ".ts" => "typescript",
            ".jsx" => "javascript",
            ".tsx" => "typescript",
            ".vue" => "vue",
            ".svelte" => "svelte",
            ".php" => "php",
            ".asp" or ".aspx" => "asp",
            
            // Data & Configuration
            ".json" => "json",
            ".xml" => "xml",
            ".yaml" or ".yml" => "yaml",
            ".toml" => "toml",
            ".ini" or ".cfg" or ".conf" => "ini",
            ".properties" => "properties",
            ".env" => "dotenv",
            ".editorconfig" => "editorconfig",
            
            // Documentation & Text
            ".md" => "markdown",
            ".txt" => "text",
            ".rst" => "restructuredtext",
            ".adoc" => "asciidoc",
            ".tex" => "latex",
            ".rtf" => "rtf",
            ".log" => "log",
            ".readme" => "text",
            ".changelog" => "text",
            ".license" or ".authors" => "text",
            
            // Programming Languages
            ".py" or ".pyi" or ".pyx" => "python",
            ".go" => "go",
            ".rs" => "rust",
            ".java" => "java",
            ".kt" or ".kts" => "kotlin",
            ".scala" => "scala",
            ".clj" or ".cljs" or ".cljc" => "clojure",
            ".rb" => "ruby",
            ".pl" or ".pm" => "perl",
            ".cpp" or ".cc" or ".cxx" => "cpp",
            ".c" => "c",
            ".h" or ".hpp" or ".hh" or ".hxx" => "c",
            ".m" or ".mm" => "objective-c",
            ".swift" => "swift",
            ".dart" => "dart",
            ".lua" => "lua",
            ".r" => "r",
            ".jl" => "julia",
            
            // Functional & ML
            ".hs" or ".lhs" => "haskell",
            ".elm" => "elm",
            ".ml" or ".mli" => "ocaml",
            ".edn" => "edn",
            
            // Shell & Scripts
            ".sh" or ".bash" => "bash",
            ".zsh" => "zsh",
            ".fish" => "fish",
            ".ps1" or ".psm1" => "powershell",
            ".bat" or ".cmd" => "batch",
            ".awk" => "awk",
            ".sed" => "sed",
            
            // Database & Query
            ".sql" or ".psql" or ".mysql" or ".sqlite" => "sql",
            ".cypher" => "cypher",
            ".sparql" => "sparql",
            ".hql" => "hql",
            ".plsql" => "plsql",
            
            // Build & CI/CD
            ".dockerfile" or ".containerfile" => "dockerfile",
            ".makefile" => "makefile",
            ".cmake" => "cmake",
            ".gradle" => "gradle",
            ".maven" => "xml",
            ".ant" => "xml",
            ".sbt" => "scala",
            ".cabal" => "cabal",
            ".stack" => "yaml",
            
            // Templates & DSL
            ".j2" or ".jinja" => "jinja",
            ".mustache" => "mustache",
            ".handlebars" => "handlebars",
            ".liquid" => "liquid",
            ".erb" => "erb",
            ".haml" => "haml",
            ".slim" => "slim",
            ".pug" or ".jade" => "pug",
            
            // Special Purpose
            ".graphql" or ".gql" => "graphql",
            ".proto" => "protobuf",
            ".thrift" => "thrift",
            ".avro" => "avro",
            ".pem" or ".crt" or ".key" or ".cer" => "certificate",
            
            // Default case for unknown extensions
            _ => ""
        };
    }
    
    private static string GetLanguageFromFilename(string filename)
    {
        return filename.ToLowerInvariant() switch
        {
            "dockerfile" or "containerfile" => "dockerfile",
            "makefile" or "gnumakefile" => "makefile",
            "rakefile" => "ruby",
            "gemfile" => "ruby",
            "pipfile" => "toml",
            "poetry.lock" => "toml",
            "cargo.lock" => "toml",
            "package-lock.json" => "json",
            _ => ""
        };
    }

    // Safe path operation wrappers using PathResolutionService
    private bool SafeDirectoryExists(string path)
    {
        return _pathResolution.DirectoryExists(path);
    }

    private IEnumerable<string> SafeEnumerateFiles(string path)
    {
        return _pathResolution.EnumerateFiles(path);
    }

    private IEnumerable<string> SafeEnumerateDirectories(string path)
    {
        return _pathResolution.EnumerateDirectories(path);
    }

    private string GetSafeFileName(string path)
    {
        return _pathResolution.GetFileName(path);
    }

    private string GetSafeFileExtension(string path)
    {
        return _pathResolution.GetExtension(path);
    }

    private string GetSafeDirectoryName(string path)
    {
        return _pathResolution.GetDirectoryName(path);
    }

    private string GetSafeRelativePath(string relativeTo, string path)
    {
        return _pathResolution.GetRelativePath(relativeTo, path);
    }

    private bool IsUnderCodeSearchDirectory(string path)
    {
        try
        {
            var normalizedPath = _pathResolution.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var baseDir = _pathResolution.GetFullPath(_pathResolution.GetBasePath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return normalizedPath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // Fallback to simple string check if path normalization fails
            return path.Contains(PathConstants.BaseDirectoryName, StringComparison.OrdinalIgnoreCase);
        }
    }
}