using COA.CodeSearch.McpServer.Services;
using Lucene.Net.Index;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Directory = System.IO.Directory;
using FSDirectory = Lucene.Net.Store.FSDirectory;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Tool to clean up incorrectly created memory index directories and migrate data to correct locations
/// </summary>
public class CleanupMemoryIndexesTool
{
    private readonly ILogger<CleanupMemoryIndexesTool> _logger;
    private readonly IPathResolutionService _pathResolution;
    
    public CleanupMemoryIndexesTool(ILogger<CleanupMemoryIndexesTool> logger, IPathResolutionService pathResolution)
    {
        _logger = logger;
        _pathResolution = pathResolution;
    }
    
    public async Task<CleanupResult> CleanupMemoryIndexesAsync(bool dryRun = true)
    {
        var result = new CleanupResult { DryRun = dryRun };
        
        try
        {
            var indexPath = _pathResolution.GetIndexRootPath();
            if (!Directory.Exists(indexPath))
            {
                result.Message = "No index directory found";
                return result;
            }
            
            // Find all incorrectly hashed memory directories
            var memoryPatterns = new[]
            {
                "project-memory_*",
                "local-memory_*", 
                "flexible-project-memory_*",
                "flexible-local-memory_*"
            };
            
            foreach (var pattern in memoryPatterns)
            {
                var matchingDirs = Directory.GetDirectories(indexPath, pattern);
                foreach (var dir in matchingDirs)
                {
                    var dirInfo = new DirectoryInfo(dir);
                    var dirName = dirInfo.Name;
                    
                    // Determine the correct target path
                    string targetPath;
                    if (dirName.StartsWith("flexible-project-memory") || dirName.StartsWith("project-memory"))
                    {
                        targetPath = _pathResolution.GetProjectMemoryPath();
                    }
                    else if (dirName.StartsWith("flexible-local-memory") || dirName.StartsWith("local-memory"))
                    {
                        targetPath = _pathResolution.GetLocalMemoryPath();
                    }
                    else
                    {
                        continue;
                    }
                    
                    var issue = new IndexIssue
                    {
                        IncorrectPath = dir,
                        CorrectPath = targetPath,
                        DirectoryName = dirName,
                        Size = GetDirectorySize(dirInfo),
                        FileCount = Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Length,
                        CreatedDate = dirInfo.CreationTimeUtc
                    };
                    
                    // Check if it has actual index data
                    issue.HasIndexData = File.Exists(Path.Combine(dir, "segments.gen"));
                    
                    if (issue.HasIndexData)
                    {
                        issue.DocumentCount = await GetDocumentCountAsync(dir);
                    }
                    
                    result.Issues.Add(issue);
                }
            }
            
            // Perform cleanup if not dry run
            if (!dryRun && result.Issues.Any())
            {
                foreach (var issue in result.Issues)
                {
                    try
                    {
                        if (issue.HasIndexData && issue.DocumentCount > 0)
                        {
                            // Migrate the data
                            _logger.LogInformation("Migrating {Count} documents from {Source} to {Target}", 
                                issue.DocumentCount, issue.DirectoryName, Path.GetFileName(issue.CorrectPath));
                            
                            await MigrateIndexDataAsync(issue.IncorrectPath, issue.CorrectPath);
                            result.MigratedCount++;
                        }
                        
                        // Delete the incorrect directory
                        _logger.LogInformation("Deleting incorrect directory: {Path}", issue.IncorrectPath);
                        Directory.Delete(issue.IncorrectPath, true);
                        result.DeletedCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to process {Path}", issue.IncorrectPath);
                        result.Errors.Add($"Failed to process {issue.DirectoryName}: {ex.Message}");
                    }
                }
            }
            
            // Build summary message
            if (result.Issues.Count == 0)
            {
                result.Message = "No incorrect memory index directories found. Everything looks good!";
            }
            else
            {
                var totalDocs = result.Issues.Sum(i => i.DocumentCount);
                var totalSize = result.Issues.Sum(i => i.Size);
                
                if (dryRun)
                {
                    result.Message = $"Found {result.Issues.Count} incorrect memory index directories with {totalDocs} total documents ({FormatBytes(totalSize)}). Run with dryRun=false to clean up.";
                }
                else
                {
                    result.Message = $"Cleaned up {result.DeletedCount} directories. Migrated {result.MigratedCount} indexes with data.";
                }
            }
            
            result.Success = true;
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during cleanup");
            result.Success = false;
            result.Message = $"Error: {ex.Message}";
            return result;
        }
    }
    
    private Task<int> GetDocumentCountAsync(string indexPath)
    {
        try
        {
            using var luceneDir = FSDirectory.Open(indexPath);
            using var reader = DirectoryReader.Open(luceneDir);
            return Task.FromResult(reader.NumDocs);
        }
        catch
        {
            return Task.FromResult(0);
        }
    }
    
    private Task MigrateIndexDataAsync(string sourcePath, string targetPath)
    {
        // If target already has data, we need to merge
        var targetHasData = Directory.Exists(targetPath) && File.Exists(Path.Combine(targetPath, "segments.gen"));
        
        if (!targetHasData)
        {
            // Simple case - just move the entire directory contents
            Directory.CreateDirectory(targetPath);
            foreach (var file in Directory.GetFiles(sourcePath))
            {
                var destFile = Path.Combine(targetPath, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }
        }
        else
        {
            // Complex case - need to merge indexes using Lucene
            _logger.LogWarning("Target index already exists at {Path}. Merging indexes...", targetPath);
            
            using var sourceDir = FSDirectory.Open(sourcePath);
            using var targetDir = FSDirectory.Open(targetPath);
            
            var analyzer = new Lucene.Net.Analysis.Standard.StandardAnalyzer(Lucene.Net.Util.LuceneVersion.LUCENE_48);
            var config = new IndexWriterConfig(Lucene.Net.Util.LuceneVersion.LUCENE_48, analyzer)
            {
                OpenMode = OpenMode.CREATE_OR_APPEND
            };
            
            using var writer = new IndexWriter(targetDir, config);
            writer.AddIndexes(sourceDir);
            writer.Commit();
        }
        
        return Task.CompletedTask;
    }
    
    private long GetDirectorySize(DirectoryInfo dir)
    {
        try
        {
            return dir.GetFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
        }
        catch
        {
            return 0;
        }
    }
    
    private string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}

public class CleanupResult
{
    public bool Success { get; set; }
    public bool DryRun { get; set; }
    public string Message { get; set; } = "";
    public List<IndexIssue> Issues { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public int DeletedCount { get; set; }
    public int MigratedCount { get; set; }
}

public class IndexIssue
{
    public string IncorrectPath { get; set; } = "";
    public string CorrectPath { get; set; } = "";
    public string DirectoryName { get; set; } = "";
    public long Size { get; set; }
    public int FileCount { get; set; }
    public int DocumentCount { get; set; }
    public bool HasIndexData { get; set; }
    public DateTime CreatedDate { get; set; }
}