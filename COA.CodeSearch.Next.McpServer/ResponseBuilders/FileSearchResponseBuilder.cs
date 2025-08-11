using COA.Mcp.Framework.TokenOptimization.ResponseBuilders;
using COA.Mcp.Framework.TokenOptimization.Models;
using COA.Mcp.Framework.TokenOptimization;
using COA.Mcp.Framework.TokenOptimization.Storage;
using COA.Mcp.Framework.TokenOptimization.Reduction;
using COA.CodeSearch.Next.McpServer.Models;
using COA.CodeSearch.Next.McpServer.Tools;
using COA.Mcp.Framework.Models;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.Next.McpServer.ResponseBuilders;

/// <summary>
/// Response builder for file search operations with token-aware optimization.
/// </summary>
public class FileSearchResponseBuilder : BaseResponseBuilder<FileSearchResult, AIOptimizedResponse<Tools.FileSearchResult>>
{
    private readonly IResourceStorageService? _storageService;
    
    public FileSearchResponseBuilder(
        ILogger<FileSearchResponseBuilder>? logger = null,
        IResourceStorageService? storageService = null)
        : base(logger)
    {
        _storageService = storageService;
    }
    
    public override async Task<AIOptimizedResponse<Tools.FileSearchResult>> BuildResponseAsync(FileSearchResult data, ResponseContext context)
    {
        var startTime = DateTime.UtcNow;
        var tokenBudget = CalculateTokenBudget(context);
        
        _logger?.LogDebug("Building file search response with token budget: {Budget}, Mode: {Mode}", 
            tokenBudget, context.ResponseMode);
        
        // Allocate token budget
        var dataBudget = (int)(tokenBudget * 0.75);  // 75% for file paths
        var insightsBudget = (int)(tokenBudget * 0.15); // 15% for insights
        var actionsBudget = (int)(tokenBudget * 0.10);  // 10% for actions
        
        // Reduce file paths to fit budget
        var reducedFiles = ReduceFilePaths(data.Files, dataBudget, context.ResponseMode);
        var wasTruncated = reducedFiles.Count < data.Files.Count;
        
        // Store full results if truncated
        string? resourceUri = null;
        if (wasTruncated && context.StoreFullResults && _storageService != null)
        {
            try
            {
                var storageUri = await _storageService.StoreAsync(
                    data.Files,
                    new ResourceStorageOptions
                    {
                        Expiration = TimeSpan.FromHours(1),
                        Compress = true,
                        Category = "file-search-results",
                        Metadata = new Dictionary<string, string>
                        {
                            ["pattern"] = data.Pattern ?? "",
                            ["totalFiles"] = data.TotalFiles.ToString(),
                            ["tool"] = context.ToolName ?? "file_search"
                        }
                    });
                resourceUri = storageUri.ToString();
                _logger?.LogDebug("Stored full file list at: {Uri}", resourceUri);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to store full file list");
            }
        }
        
        // Generate insights and actions
        var insights = GenerateInsights(data, context.ResponseMode);
        var actions = GenerateActions(data, actionsBudget);
        
        // Build the response
        var response = new AIOptimizedResponse<Tools.FileSearchResult>
        {
            Success = true,
            Data = new AIResponseData<Tools.FileSearchResult>
            {
                Summary = BuildSummary(data, reducedFiles.Count, context.ResponseMode),
                Results = new Tools.FileSearchResult
                {
                    Files = reducedFiles.Select(f => new Tools.FileSearchMatch
                    {
                        FilePath = f.Path,
                        FileName = Path.GetFileName(f.Path),
                        Directory = Path.GetDirectoryName(f.Path) ?? "",
                        Extension = Path.GetExtension(f.Path)
                    }).ToList(),
                    TotalMatches = data.TotalFiles
                },
                Count = data.TotalFiles,
                ExtensionData = new Dictionary<string, object>
                {
                    ["totalFiles"] = data.TotalFiles,
                    ["pattern"] = data.Pattern ?? "",
                    ["searchPath"] = data.SearchPath ?? ""
                }
            },
            Insights = ReduceInsights(insights, insightsBudget),
            Actions = ReduceActions(actions, actionsBudget),
            Meta = CreateMetadata(startTime, wasTruncated, resourceUri)
        };
        
        // Operation name is handled automatically by the framework
        
        // Update token estimate
        response.Meta.TokenInfo.Estimated = TokenEstimator.EstimateObject(response);
        
        _logger?.LogInformation("Built file search response: {Files} of {Total} files, {Insights} insights, {Actions} actions, {Tokens} tokens",
            reducedFiles.Count, data.TotalFiles, response.Insights.Count, response.Actions.Count, response.Meta.TokenInfo.Estimated);
        
        return response;
    }
    
    protected override List<string> GenerateInsights(FileSearchResult data, string responseMode)
    {
        var insights = new List<string>();
        
        if (data.TotalFiles == 0)
        {
            insights.Add($"No files matching pattern '{data.Pattern}' found. Verify the pattern and search path.");
            return insights;
        }
        
        // File type distribution
        if (data.Files.Count > 0)
        {
            var extensions = data.Files
                .Select(f => Path.GetExtension(f.Path))
                .Where(ext => !string.IsNullOrEmpty(ext))
                .GroupBy(ext => ext)
                .OrderByDescending(g => g.Count())
                .Take(5)
                .ToList();
            
            if (extensions.Any())
            {
                var topTypes = string.Join(", ", extensions.Select(g => $"{g.Key} ({g.Count()})"));
                insights.Add($"File types found: {topTypes}");
            }
            
            // Directory structure insights
            var directories = data.Files
                .Select(f => Path.GetDirectoryName(f.Path) ?? "")
                .Distinct()
                .Count();
            
            if (directories > 1)
            {
                insights.Add($"Files spread across {directories} directories");
                
                // Find most common directory
                var topDir = data.Files
                    .Select(f => Path.GetDirectoryName(f.Path) ?? "")
                    .GroupBy(dir => dir)
                    .OrderByDescending(g => g.Count())
                    .First();
                
                if (topDir.Count() > data.Files.Count * 0.3)
                {
                    var dirName = Path.GetFileName(topDir.Key) ?? "root";
                    insights.Add($"Concentration in '{dirName}' ({topDir.Count()} files)");
                }
            }
            
            // Size insights (if available)
            if (data.Files.Any(f => f.Size > 0))
            {
                var totalSize = data.Files.Sum(f => f.Size);
                var avgSize = totalSize / data.Files.Count;
                
                insights.Add($"Total size: {FormatFileSize(totalSize)}, Average: {FormatFileSize(avgSize)}");
                
                var largeFiles = data.Files.Where(f => f.Size > 1_000_000).Count();
                if (largeFiles > 0)
                {
                    insights.Add($"{largeFiles} large file(s) (>1MB) found");
                }
            }
            
            // Recency insights
            if (responseMode == "full" && data.Files.Any(f => f.LastModified.HasValue))
            {
                var recentFiles = data.Files
                    .Where(f => f.LastModified.HasValue && f.LastModified.Value > DateTime.Now.AddDays(-7))
                    .Count();
                
                if (recentFiles > 0)
                {
                    insights.Add($"{recentFiles} file(s) modified in the last week");
                }
                
                var oldestFile = data.Files
                    .Where(f => f.LastModified.HasValue)
                    .OrderBy(f => f.LastModified)
                    .FirstOrDefault();
                
                if (oldestFile != null && oldestFile.LastModified < DateTime.Now.AddYears(-1))
                {
                    insights.Add("Contains files over a year old - may include legacy code");
                }
            }
        }
        
        return insights;
    }
    
    protected override List<COA.Mcp.Framework.Models.AIAction> GenerateActions(FileSearchResult data, int tokenBudget)
    {
        var actions = new List<COA.Mcp.Framework.Models.AIAction>();
        
        if (data.TotalFiles == 0)
        {
            actions.Add(new AIAction
            {
                Action = "adjust_pattern",
                Description = "Try a broader pattern like '*' or '**/*'",
                Priority = 100
            });
            
            actions.Add(new AIAction
            {
                Action = "check_path",
                Description = "Verify the search path exists and is accessible",
                Priority = 90
            });
        }
        else
        {
            // Search within results
            if (data.TotalFiles > 10)
            {
                actions.Add(new AIAction
                {
                    Action = ToolNames.TextSearch,
                    Description = "Search for content within these files",
                    Priority = 90
                });
            }
            
            // Filter by extension
            var extensions = data.Files
                .Select(f => Path.GetExtension(f.Path))
                .Where(ext => !string.IsNullOrEmpty(ext))
                .Distinct()
                .Take(3)
                .ToList();
            
            if (extensions.Count > 1)
            {
                foreach (var ext in extensions)
                {
                    actions.Add(new AIAction
                    {
                        Action = "filter_extension",
                        Description = $"Filter to only {ext} files",
                        Priority = 70
                    });
                }
            }
            
            // Analyze large files
            var largeFiles = data.Files
                .Where(f => f.Size > 1_000_000)
                .OrderByDescending(f => f.Size)
                .Take(3)
                .ToList();
            
            foreach (var file in largeFiles)
            {
                actions.Add(new AIAction
                {
                    Action = "analyze_large_file",
                    Description = $"Analyze {Path.GetFileName(file.Path)} ({FormatFileSize(file.Size)})",
                    Priority = 60
                });
            }
            
            // Recent activity
            if (data.Files.Any(f => f.LastModified.HasValue))
            {
                var mostRecent = data.Files
                    .Where(f => f.LastModified.HasValue)
                    .OrderByDescending(f => f.LastModified)
                    .First();
                
                actions.Add(new AIAction
                {
                    Action = "examine_recent",
                    Description = $"Examine recently modified: {Path.GetFileName(mostRecent.Path)}",
                    Priority = 80
                });
            }
        }
        
        return actions;
    }
    
    private List<FileInfo> ReduceFilePaths(List<FileInfo> files, int tokenBudget, string responseMode)
    {
        if (files.Count == 0)
            return files;
        
        // Estimate tokens per file
        var tokensPerFile = responseMode == "full" 
            ? 20  // Full path + metadata
            : 10; // Just filename
        
        var maxFiles = Math.Max(1, tokenBudget / tokensPerFile);
        
        if (files.Count <= maxFiles)
            return files;
        
        // Priority: recently modified files first, then by name
        var context = new ReductionContext
        {
            PriorityFunction = obj =>
            {
                if (obj is FileInfo file)
                {
                    var recencyScore = file.LastModified.HasValue
                        ? (DateTime.Now - file.LastModified.Value).TotalDays
                        : 365.0;
                    return 1.0 / (1.0 + recencyScore / 30.0); // Decay over 30 days
                }
                return 0;
            }
        };
        
        var result = _reductionEngine.Reduce(
            files,
            file => tokensPerFile,
            tokenBudget,
            "priority",
            context);
        
        return result.Items;
    }
    
    private string BuildSummary(FileSearchResult data, int includedCount, string responseMode)
    {
        if (data.TotalFiles == 0)
        {
            return $"No files found matching '{data.Pattern}'";
        }
        
        var summary = $"Found {data.TotalFiles} file{(data.TotalFiles != 1 ? "s" : "")}";
        
        if (includedCount < data.TotalFiles)
        {
            summary += $" (showing {includedCount})";
        }
        
        summary += $" matching '{data.Pattern}'";
        
        if (!string.IsNullOrEmpty(data.SearchPath))
        {
            summary += $" in {data.SearchPath}";
        }
        
        return summary;
    }
    
    private string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double size = bytes;
        
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        
        return $"{size:F1} {sizes[order]}";
    }
}

/// <summary>
/// File information for file search results.
/// </summary>
public class FileInfo
{
    public string Path { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime? LastModified { get; set; }
    public bool IsDirectory { get; set; }
}

/// <summary>
/// Result from file search operations.
/// </summary>
public class FileSearchResult
{
    public List<FileInfo> Files { get; set; } = new();
    public int TotalFiles { get; set; }
    public string? Pattern { get; set; }
    public string? SearchPath { get; set; }
}