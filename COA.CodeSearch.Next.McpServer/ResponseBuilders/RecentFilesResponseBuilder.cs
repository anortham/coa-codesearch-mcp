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
/// Response builder for recent files operations with token-aware optimization.
/// </summary>
public class RecentFilesResponseBuilder : BaseResponseBuilder<RecentFilesResult, AIOptimizedResponse<Tools.RecentFilesResult>>
{
    private readonly IResourceStorageService? _storageService;
    
    public RecentFilesResponseBuilder(
        ILogger<RecentFilesResponseBuilder>? logger = null,
        IResourceStorageService? storageService = null)
        : base(logger)
    {
        _storageService = storageService;
    }
    
    public override async Task<AIOptimizedResponse<Tools.RecentFilesResult>> BuildResponseAsync(RecentFilesResult data, ResponseContext context)
    {
        var startTime = DateTime.UtcNow;
        var tokenBudget = CalculateTokenBudget(context);
        
        _logger?.LogDebug("Building recent files response with token budget: {Budget}, Mode: {Mode}", 
            tokenBudget, context.ResponseMode);
        
        // Allocate token budget
        var dataBudget = (int)(tokenBudget * 0.70);  // 70% for file data
        var insightsBudget = (int)(tokenBudget * 0.20); // 20% for insights
        var actionsBudget = (int)(tokenBudget * 0.10);  // 10% for actions
        
        // Reduce file paths to fit budget
        var reducedFiles = ReduceRecentFiles(data.Files, dataBudget, context.ResponseMode);
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
                        Category = "recent-files-results",
                        Metadata = new Dictionary<string, string>
                        {
                            ["timeFrame"] = data.TimeFrameRequested,
                            ["totalFiles"] = data.TotalFiles.ToString(),
                            ["cutoffTime"] = data.CutoffTime.ToString("O"),
                            ["tool"] = context.ToolName ?? "recent_files"
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
        var response = new AIOptimizedResponse<Tools.RecentFilesResult>
        {
            Success = true,
            Data = new AIResponseData<Tools.RecentFilesResult>
            {
                Summary = BuildSummary(data, reducedFiles.Count, context.ResponseMode),
                Results = new Tools.RecentFilesResult
                {
                    Files = reducedFiles,
                    TimeFrameRequested = data.TimeFrameRequested,
                    CutoffTime = data.CutoffTime,
                    TotalFiles = data.TotalFiles
                },
                Count = data.TotalFiles,
                ExtensionData = new Dictionary<string, object>
                {
                    ["timeFrameRequested"] = data.TimeFrameRequested,
                    ["cutoffTime"] = data.CutoffTime.ToString("O"),
                    ["searchPath"] = data.SearchPath ?? "",
                    ["oldestFile"] = data.Files.Any() ? data.Files.Min(f => f.LastModified)?.ToString("O") ?? "" : "",
                    ["newestFile"] = data.Files.Any() ? data.Files.Max(f => f.LastModified)?.ToString("O") ?? "" : ""
                }
            },
            Insights = ReduceInsights(insights, insightsBudget),
            Actions = ReduceActions(actions, actionsBudget),
            Meta = CreateMetadata(startTime, wasTruncated, resourceUri)
        };
        
        // Operation name is handled automatically by the framework
        
        // Update token estimate
        response.Meta.TokenInfo!.Estimated = TokenEstimator.EstimateObject(response);
        
        _logger?.LogInformation("Built recent files response: {Files} of {Total} files, {Insights} insights, {Actions} actions, {Tokens} tokens",
            reducedFiles.Count, data.TotalFiles, response.Insights.Count, response.Actions.Count, response.Meta.TokenInfo.Estimated);
        
        return response;
    }
    
    protected override List<string> GenerateInsights(RecentFilesResult data, string responseMode)
    {
        var insights = new List<string>();
        
        if (data.TotalFiles == 0)
        {
            insights.Add($"No files modified in the last {data.TimeFrameRequested}. Try a longer time frame.");
            return insights;
        }
        
        // Time-based insights
        var timeAgo = DateTime.UtcNow - data.CutoffTime;
        insights.Add($"Found {data.TotalFiles} file{(data.TotalFiles != 1 ? "s" : "")} modified in the last {FormatTimeSpan(timeAgo)}");
        
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
                insights.Add($"File types: {topTypes}");
            }
            
            // Recent activity patterns
            var now = DateTime.UtcNow;
            var today = data.Files.Count(f => f.LastModified.HasValue && (now - f.LastModified.Value).TotalDays < 1);
            var yesterday = data.Files.Count(f => 
            {
                if (!f.LastModified.HasValue) return false;
                var days = (now - f.LastModified.Value).TotalDays;
                return days >= 1 && days < 2;
            });
            
            if (today > 0 || yesterday > 0)
            {
                var activityParts = new List<string>();
                if (today > 0) activityParts.Add($"{today} today");
                if (yesterday > 0) activityParts.Add($"{yesterday} yesterday");
                insights.Add($"Recent activity: {string.Join(", ", activityParts)}");
            }
            
            // Size insights
            if (data.Files.Any(f => f.Size > 0))
            {
                var totalSize = data.Files.Sum(f => f.Size);
                var avgSize = totalSize / data.Files.Count;
                
                insights.Add($"Total size: {FormatFileSize(totalSize)}, Average: {FormatFileSize(avgSize)}");
                
                var largeFiles = data.Files.Where(f => f.Size > 1_000_000).Count();
                if (largeFiles > 0)
                {
                    insights.Add($"{largeFiles} large file(s) (>1MB) recently modified");
                }
            }
            
            // Directory concentration
            var directories = data.Files
                .Select(f => Path.GetDirectoryName(f.Path) ?? "")
                .GroupBy(dir => dir)
                .OrderByDescending(g => g.Count())
                .ToList();
            
            if (directories.Count > 1)
            {
                var topDir = directories.First();
                if (topDir.Count() > data.Files.Count * 0.4)
                {
                    var dirName = Path.GetFileName(topDir.Key) ?? "root";
                    insights.Add($"Most activity in '{dirName}' ({topDir.Count()} files)");
                }
            }
        }
        
        // Full mode insights
        if (responseMode == "full" && data.Files.Count > 0)
        {
            var mostRecent = data.Files.OrderByDescending(f => f.LastModified).First();
            var timeAgoRecent = mostRecent.LastModified.HasValue 
                ? DateTime.UtcNow - mostRecent.LastModified.Value
                : TimeSpan.Zero;
            
            insights.Add($"Most recent: {Path.GetFileName(mostRecent.Path)} ({FormatTimeSpan(timeAgoRecent)} ago)");
            
            // Detect potential work sessions
            var recentBurst = data.Files.Count(f => f.LastModified.HasValue && (DateTime.UtcNow - f.LastModified.Value).TotalMinutes < 60);
            if (recentBurst > 3)
            {
                insights.Add($"{recentBurst} files modified in the last hour - active development session");
            }
        }
        
        return insights;
    }
    
    protected override List<COA.Mcp.Framework.Models.AIAction> GenerateActions(RecentFilesResult data, int tokenBudget)
    {
        var actions = new List<COA.Mcp.Framework.Models.AIAction>();
        
        if (data.TotalFiles == 0)
        {
            actions.Add(new AIAction
            {
                Action = "expand_timeframe",
                Description = "Try a longer time frame like '30d' or '1w'",
                Priority = 100
            });
            
            actions.Add(new AIAction
            {
                Action = ToolNames.TextSearch,
                Description = "Search for specific content in files",
                Priority = 90
            });
        }
        else
        {
            // Examine most recent file
            if (data.Files.Any())
            {
                var mostRecent = data.Files.OrderByDescending(f => f.LastModified).First();
                actions.Add(new AIAction
                {
                    Action = "examine_file",
                    Description = $"Examine most recent: {Path.GetFileName(mostRecent.Path)}",
                    Priority = 100
                });
            }
            
            // Search within recent files
            actions.Add(new AIAction
            {
                Action = ToolNames.TextSearch,
                Description = "Search for content within these recent files",
                Priority = 90
            });
            
            // Filter by file type
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
                        Action = "filter_by_extension",
                        Description = $"Filter to only {ext} files",
                        Priority = 70
                    });
                }
            }
            
            // Adjust time frame
            actions.Add(new AIAction
            {
                Action = "adjust_timeframe",
                Description = "Try different time frame (1h, 1d, 2w)",
                Priority = 60
            });
            
            // Directory focus
            var directories = data.Files
                .Select(f => Path.GetDirectoryName(f.Path) ?? "")
                .GroupBy(dir => dir)
                .OrderByDescending(g => g.Count())
                .Take(2)
                .ToList();
            
            foreach (var dir in directories.Where(d => !string.IsNullOrEmpty(d.Key)))
            {
                var dirName = Path.GetFileName(dir.Key) ?? "directory";
                actions.Add(new AIAction
                {
                    Action = "focus_directory",
                    Description = $"Focus on changes in {dirName} ({dir.Count()} files)",
                    Priority = 50
                });
            }
        }
        
        return actions;
    }
    
    private List<Tools.RecentFileInfo> ReduceRecentFiles(List<RecentFileInfo> files, int tokenBudget, string responseMode)
    {
        if (files.Count == 0)
            return new List<Tools.RecentFileInfo>();
        
        // Estimate tokens per file entry
        var tokensPerFile = responseMode == "full" 
            ? 25  // Full path + metadata + timestamps
            : 15; // Essential info only
        
        var maxFiles = Math.Max(5, tokenBudget / tokensPerFile);
        
        if (files.Count <= maxFiles)
        {
            return files.Select(ConvertToRecentFileInfo).ToList();
        }
        
        // Priority: most recent files first (they're already sorted)
        return files.Take(maxFiles).Select(ConvertToRecentFileInfo).ToList();
    }
    
    private Tools.RecentFileInfo ConvertToRecentFileInfo(RecentFileInfo file)
    {
        return new Tools.RecentFileInfo
        {
            FilePath = file.Path,
            FileName = Path.GetFileName(file.Path),
            Directory = Path.GetDirectoryName(file.Path) ?? "",
            Extension = Path.GetExtension(file.Path),
            LastModified = file.LastModified ?? DateTime.MinValue,
            SizeBytes = file.Size,
            ModifiedAgo = file.LastModified.HasValue 
                ? DateTime.UtcNow - file.LastModified.Value 
                : TimeSpan.Zero
        };
    }
    
    private string BuildSummary(RecentFilesResult data, int displayedFiles, string responseMode)
    {
        if (data.TotalFiles == 0)
        {
            return $"No files modified in the last {data.TimeFrameRequested}";
        }
        
        var summary = $"Found {data.TotalFiles} recent file{(data.TotalFiles != 1 ? "s" : "")}";
        
        if (displayedFiles < data.TotalFiles)
        {
            summary += $" (showing {displayedFiles})";
        }
        
        summary += $" modified in the last {data.TimeFrameRequested}";
        
        if (data.Files.Any())
        {
            var mostRecent = data.Files.OrderByDescending(f => f.LastModified).First();
            var timeAgo = mostRecent.LastModified.HasValue 
                ? DateTime.UtcNow - mostRecent.LastModified.Value 
                : TimeSpan.Zero;
            summary += $" - most recent: {FormatTimeSpan(timeAgo)} ago";
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
    
    private string FormatTimeSpan(TimeSpan timeSpan)
    {
        if (timeSpan.TotalMinutes < 60)
        {
            return $"{(int)timeSpan.TotalMinutes}min";
        }
        else if (timeSpan.TotalHours < 24)
        {
            return $"{(int)timeSpan.TotalHours}h";
        }
        else if (timeSpan.TotalDays < 7)
        {
            return $"{(int)timeSpan.TotalDays}d";
        }
        else
        {
            return $"{(int)(timeSpan.TotalDays / 7)}w";
        }
    }
}

/// <summary>
/// Result from recent files operations for response building.
/// </summary>
public class RecentFilesResult
{
    public List<RecentFileInfo> Files { get; set; } = new();
    public string TimeFrameRequested { get; set; } = string.Empty;
    public DateTime CutoffTime { get; set; }
    public int TotalFiles { get; set; }
    public string? SearchPath { get; set; }
}

/// <summary>
/// File information for recent files results.
/// </summary>
public class RecentFileInfo
{
    public string Path { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime? LastModified { get; set; }
    public bool IsDirectory { get; set; }
}