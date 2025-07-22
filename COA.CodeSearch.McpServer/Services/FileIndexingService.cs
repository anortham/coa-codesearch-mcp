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
    private readonly HashSet<string> _supportedExtensions;
    private readonly HashSet<string> _excludedDirectories;
    private const int MaxFileSize = 10 * 1024 * 1024; // 10MB

    public FileIndexingService(
        ILogger<FileIndexingService> logger, 
        IConfiguration configuration,
        ILuceneIndexService luceneIndexService)
    {
        _logger = logger;
        _configuration = configuration;
        _luceneIndexService = luceneIndexService;
        
        // Initialize supported extensions from configuration or defaults
        var extensions = configuration.GetSection("Lucene:SupportedExtensions").Get<string[]>() 
            ?? new[] { ".cs", ".razor", ".cshtml", ".json", ".xml", ".md", ".txt", ".js", ".ts", ".jsx", ".tsx", ".css", ".scss", ".html", ".yml", ".yaml", ".csproj", ".sln" };
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
            // Get all files to index
            var files = GetFilesToIndex(directoryPath).ToList();
            _logger.LogInformation("Found {Count} files to index in {Directory}", files.Count, directoryPath);
            
            // Process files in parallel
            var semaphore = new SemaphoreSlim(Environment.ProcessorCount);
            var tasks = files.Select(async filePath =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    if (await IndexFileOptimizedAsync(indexWriter, filePath, workspacePath, cancellationToken))
                    {
                        Interlocked.Increment(ref indexedCount);
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            });
            
            await Task.WhenAll(tasks);
            
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
            if (!System.IO.Directory.Exists(currentDir))
                continue;
                
            var dirName = Path.GetFileName(currentDir);
            if (!string.IsNullOrEmpty(dirName) && _excludedDirectories.Contains(dirName))
                continue;
                
            // Yield files from current directory
            foreach (var file in System.IO.Directory.EnumerateFiles(currentDir))
            {
                var extension = Path.GetExtension(file);
                if (_supportedExtensions.Contains(extension))
                {
                    _logger.LogDebug("Found file to index: {File}", file);
                    yield return file;
                }
            }
            
            // Add subdirectories to process
            foreach (var subDir in System.IO.Directory.EnumerateDirectories(currentDir))
            {
                var subDirName = Path.GetFileName(subDir);
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
            if (fileInfo.Length > 1024 * 1024) // 1MB threshold
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
            
            _logger.LogDebug("Indexed file: {FilePath}", filePath);
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
        var extension = Path.GetExtension(filePath);
        var fileName = Path.GetFileName(filePath);
        var relativePath = Path.GetRelativePath(workspacePath, filePath);
        var directoryPath = Path.GetDirectoryName(filePath) ?? "";
        var relativeDirectoryPath = Path.GetRelativePath(workspacePath, directoryPath);
        var directoryName = Path.GetFileName(directoryPath) ?? "";
        
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

    public async Task<bool> RemoveFileAsync(string workspacePath, string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            var indexWriter = await _luceneIndexService.GetIndexWriterAsync(workspacePath, cancellationToken);
            indexWriter.DeleteDocuments(new Term("id", filePath));
            await _luceneIndexService.CommitAsync(workspacePath, cancellationToken);
            
            _logger.LogDebug("Removed file from index: {FilePath}", filePath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing file from index: {FilePath}", filePath);
            return false;
        }
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
            
            _logger.LogDebug("Deleted file from index: {FilePath}", filePath);
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
            _ => ""
        };
    }
}