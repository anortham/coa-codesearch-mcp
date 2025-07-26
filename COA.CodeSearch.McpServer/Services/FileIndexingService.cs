using Lucene.Net.Documents;
using Lucene.Net.Index;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
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
    private readonly HashSet<string> _supportedExtensions;
    private readonly HashSet<string> _excludedDirectories;
    private const int MaxFileSize = 10 * 1024 * 1024; // 10MB
    private const int LargeFileThreshold = 1024 * 1024; // 1MB

    public FileIndexingService(
        ILogger<FileIndexingService> logger, 
        IConfiguration configuration,
        ILuceneIndexService luceneIndexService,
        IPathResolutionService pathResolution)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _luceneIndexService = luceneIndexService ?? throw new ArgumentNullException(nameof(luceneIndexService));
        _pathResolution = pathResolution ?? throw new ArgumentNullException(nameof(pathResolution));
        
        // Initialize supported extensions from configuration or defaults
        var extensions = configuration.GetSection("Lucene:SupportedExtensions").Get<string[]>() 
            ?? new[] { ".cs", ".razor", ".cshtml", ".json", ".xml", ".md", ".txt", ".js", ".ts", ".jsx", ".tsx", ".css", ".scss", ".html", ".yml", ".yaml", ".csproj", ".sln", ".py", ".pyi" };
        _supportedExtensions = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);
        
        // Initialize excluded directories
        var excluded = configuration.GetSection("Lucene:ExcludedDirectories").Get<string[]>() 
            ?? PathConstants.DefaultExcludedDirectories;
        _excludedDirectories = new HashSet<string>(excluded, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<int> IndexDirectoryAsync(string workspacePath, string directoryPath, CancellationToken cancellationToken = default)
    {
        var indexWriter = await _luceneIndexService.GetIndexWriterAsync(workspacePath, cancellationToken);
        var indexedCount = 0;
        
        try
        {
            // Process files in parallel with streaming enumeration
            using var semaphore = new SemaphoreSlim(Environment.ProcessorCount);
            var errors = new ConcurrentBag<(string file, Exception error)>();
            await Parallel.ForEachAsync(
                GetFilesToIndex(directoryPath),
                new ParallelOptions 
                { 
                    MaxDegreeOfParallelism = Environment.ProcessorCount,
                    CancellationToken = cancellationToken 
                },
                async (filePath, ct) =>
                {
                    await semaphore.WaitAsync(ct);
                    try
                    {
                        if (await IndexFileOptimizedAsync(indexWriter, filePath, workspacePath, ct))
                        {
                            Interlocked.Increment(ref indexedCount);
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add((filePath, ex));
                        _logger.LogWarning(ex, "Failed to index file: {FilePath}", filePath);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });
            
            // Report any errors
            if (!errors.IsEmpty)
            {
                _logger.LogWarning("Failed to index {Count} files out of {Total}", errors.Count, indexedCount + errors.Count);
            }
            
            // Final commit
            await _luceneIndexService.CommitAsync(workspacePath, cancellationToken);
            
            _logger.LogInformation("Indexed {Count} files from directory: {Directory}", indexedCount, directoryPath);
            return indexedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error indexing directory: {Directory}", directoryPath);
            throw;
        }
    }

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
        var indexWriter = await _luceneIndexService.GetIndexWriterAsync(workspacePath, cancellationToken);
        var result = await IndexFileOptimizedAsync(indexWriter, filePath, workspacePath, cancellationToken);
        
        if (result)
        {
            await _luceneIndexService.CommitAsync(workspacePath, cancellationToken);
        }
        
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async Task<bool> IndexFileOptimizedAsync(
        IndexWriter indexWriter, 
        string filePath,
        string workspacePath, 
        CancellationToken cancellationToken)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length > MaxFileSize)
            {
                _logger.LogWarning("File too large to index: {FilePath} ({Size} bytes)", filePath, fileInfo.Length);
                return false;
            }

            // Read file content using memory-mapped files for large files
            string content;
            if (fileInfo.Length > LargeFileThreshold)
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
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error indexing file: {FilePath}", filePath);
            return false;
        }
    }
    
    private async Task<string> ReadLargeFileAsync(string filePath, CancellationToken cancellationToken)
    {
        using var mmf = MemoryMappedFile.CreateFromFile(filePath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        using var accessor = mmf.CreateViewStream(0, 0, MemoryMappedFileAccess.Read);
        using var reader = new StreamReader(accessor, Encoding.UTF8);
        return await reader.ReadToEndAsync(cancellationToken);
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
            new StringField("extension", extension, Field.Store.YES),
            new Int64Field("size", fileInfo.Length, Field.Store.YES),
            new Int64Field("lastModified", fileInfo.LastWriteTimeUtc.Ticks, Field.Store.YES),
            new StringField("relativePath", relativePath, Field.Store.YES),
            new StringField("directory", directoryPath, Field.Store.YES),
            new StringField("relativeDirectory", relativeDirectoryPath, Field.Store.YES),
            new StringField("directoryName", directoryName, Field.Store.YES),
            
            // Indexed fields
            new TextField("content", content, Field.Store.NO),
            new TextField("filename_text", fileName, Field.Store.NO),
            new TextField("directory_text", relativeDirectoryPath.Replace(Path.DirectorySeparatorChar, ' '), Field.Store.NO)
        };

        // Add language field if we can determine it
        var language = GetLanguageFromExtension(extension);
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

    public async Task<bool> UpdateFileAsync(string workspacePath, string filePath, CancellationToken cancellationToken = default)
    {
        // Update is just a remove + add operation
        return await IndexFileAsync(workspacePath, filePath, cancellationToken);
    }

    public async Task<bool> DeleteFileAsync(string workspacePath, string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            var indexWriter = await _luceneIndexService.GetIndexWriterAsync(workspacePath, cancellationToken);
            
            // Delete document by file path (using the "id" field)
            indexWriter.DeleteDocuments(new Term("id", filePath));
            
            // Commit the deletion
            await _luceneIndexService.CommitAsync(workspacePath, cancellationToken);
            
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
            ".cs" => "csharp",
            ".razor" => "razor",
            ".cshtml" => "razor",
            ".js" => "javascript",
            ".ts" => "typescript",
            ".jsx" => "javascript",
            ".tsx" => "typescript",
            ".json" => "json",
            ".xml" => "xml",
            ".html" => "html",
            ".css" => "css",
            ".scss" => "scss",
            ".yml" or ".yaml" => "yaml",
            ".md" => "markdown",
            ".csproj" => "msbuild",
            ".sln" => "solution",
            ".py" => "python",
            ".pyi" => "python",
            _ => ""
        };
    }

    // Safe path operation wrappers
    private bool SafeDirectoryExists(string path)
    {
        try
        {
            return Directory.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    private IEnumerable<string> SafeEnumerateFiles(string path)
    {
        try
        {
            return Directory.EnumerateFiles(path);
        }
        catch
        {
            return Enumerable.Empty<string>();
        }
    }

    private IEnumerable<string> SafeEnumerateDirectories(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path);
        }
        catch
        {
            return Enumerable.Empty<string>();
        }
    }

    private string GetSafeFileName(string path)
    {
        try
        {
            return Path.GetFileName(path) ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private string GetSafeFileExtension(string path)
    {
        try
        {
            return Path.GetExtension(path);
        }
        catch
        {
            return string.Empty;
        }
    }

    private string GetSafeDirectoryName(string path)
    {
        try
        {
            return Path.GetDirectoryName(path) ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private string GetSafeRelativePath(string relativeTo, string path)
    {
        try
        {
            return Path.GetRelativePath(relativeTo, path);
        }
        catch
        {
            return path;
        }
    }

    private bool IsUnderCodeSearchDirectory(string path)
    {
        try
        {
            var normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var baseDir = Path.GetFullPath(_pathResolution.GetBasePath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return normalizedPath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // Fallback to simple string check if path normalization fails
            return path.Contains(PathConstants.BaseDirectoryName, StringComparison.OrdinalIgnoreCase);
        }
    }
}