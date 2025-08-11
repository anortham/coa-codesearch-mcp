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
/// Response builder for index operations with token-aware optimization.
/// </summary>
public class IndexResponseBuilder : BaseResponseBuilder<IndexResult, AIOptimizedResponse<Tools.IndexWorkspaceResult>>
{
    private readonly IResourceStorageService? _storageService;
    
    public IndexResponseBuilder(
        ILogger<IndexResponseBuilder>? logger = null,
        IResourceStorageService? storageService = null)
        : base(logger)
    {
        _storageService = storageService;
    }
    
    public override async Task<AIOptimizedResponse<Tools.IndexWorkspaceResult>> BuildResponseAsync(IndexResult data, ResponseContext context)
    {
        var startTime = DateTime.UtcNow;
        var tokenBudget = CalculateTokenBudget(context);
        
        _logger?.LogDebug("Building index response with token budget: {Budget}, Mode: {Mode}", 
            tokenBudget, context.ResponseMode);
        
        // Allocate token budget
        var dataBudget = (int)(tokenBudget * 0.6);  // 60% for data
        var insightsBudget = (int)(tokenBudget * 0.25); // 25% for insights
        var actionsBudget = (int)(tokenBudget * 0.15);  // 15% for actions
        
        // Reduce file list if needed
        var reducedFiles = data.IndexedFiles != null 
            ? ReduceFileList(data.IndexedFiles, dataBudget, context.ResponseMode)
            : new List<string>();
        var wasTruncated = data.IndexedFiles != null && reducedFiles.Count < data.IndexedFiles.Count;
        
        // Store full file list if truncated
        string? resourceUri = null;
        if (wasTruncated && context.StoreFullResults && _storageService != null && data.IndexedFiles != null)
        {
            try
            {
                var storageUri = await _storageService.StoreAsync(
                    data.IndexedFiles,
                    new ResourceStorageOptions
                    {
                        Expiration = TimeSpan.FromHours(1),
                        Compress = true,
                        Category = "index-results",
                        Metadata = new Dictionary<string, string>
                        {
                            ["workspacePath"] = data.WorkspacePath ?? "",
                            ["totalFiles"] = data.FilesIndexed.ToString(),
                            ["tool"] = context.ToolName ?? "index_workspace"
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
        var response = new AIOptimizedResponse<Tools.IndexWorkspaceResult>
        {
            Success = data.Success,
            Data = new AIResponseData<Tools.IndexWorkspaceResult>
            {
                Summary = BuildSummary(data, reducedFiles.Count, context.ResponseMode),
                Results = new Tools.IndexWorkspaceResult
                {
                    WorkspacePath = data.WorkspacePath ?? "",
                    WorkspaceHash = data.WorkspaceHash ?? "",
                    IndexPath = data.IndexPath,
                    IsNewIndex = data.IsNewIndex,
                    IndexedFileCount = data.FilesIndexed,
                    TotalFileCount = data.FilesIndexed + data.FilesSkipped,
                    Duration = TimeSpan.FromMilliseconds(data.IndexTimeMs)
                },
                Count = data.FilesIndexed,
                ExtensionData = new Dictionary<string, object>
                {
                    ["workspaceHash"] = data.WorkspaceHash ?? "",
                    ["indexPath"] = data.IndexPath ?? "",
                    ["watcherEnabled"] = data.WatcherEnabled,
                    ["statistics"] = data.Statistics
                }
            },
            Insights = ReduceInsights(insights, insightsBudget),
            Actions = ReduceActions(actions, actionsBudget),
            Meta = CreateMetadata(startTime, wasTruncated, resourceUri)
        };
        
        // Set operation name
        response.SetOperation(context.ToolName ?? "index_workspace");
        
        // Update token estimate
        response.Meta.TokenInfo.Estimated = TokenEstimator.EstimateObject(response);
        
        _logger?.LogInformation("Built index response: {Files} files indexed, {Insights} insights, {Actions} actions, {Tokens} tokens",
            data.FilesIndexed, response.Insights.Count, response.Actions.Count, response.Meta.TokenInfo.Estimated);
        
        return response;
    }
    
    protected override List<string> GenerateInsights(IndexResult data, string responseMode)
    {
        var insights = new List<string>();
        
        if (!data.Success)
        {
            insights.Add("Indexing failed. Check error details for resolution steps.");
            return insights;
        }
        
        // Index status
        if (data.IsNewIndex)
        {
            insights.Add($"Created new index for workspace: {Path.GetFileName(data.WorkspacePath ?? "")}");
        }
        else
        {
            insights.Add($"Updated existing index with {data.FilesIndexed} files");
        }
        
        // Performance insights
        if (data.IndexTimeMs > 0)
        {
            var filesPerSecond = data.FilesIndexed / (data.IndexTimeMs / 1000.0);
            insights.Add($"Indexing speed: {filesPerSecond:F1} files/second");
        }
        
        // Size insights
        if (data.TotalSizeBytes > 0)
        {
            var sizeMB = data.TotalSizeBytes / (1024.0 * 1024.0);
            insights.Add($"Total indexed content: {sizeMB:F1} MB");
        }
        
        // File type distribution
        if (data.Statistics?.FileTypeDistribution != null && data.Statistics.FileTypeDistribution.Any())
        {
            var topTypes = data.Statistics.FileTypeDistribution
                .OrderByDescending(kvp => kvp.Value)
                .Take(3)
                .Select(kvp => $"{kvp.Key} ({kvp.Value})")
                .ToList();
            
            if (topTypes.Any())
            {
                insights.Add($"Top file types: {string.Join(", ", topTypes)}");
            }
        }
        
        // Skipped files
        if (data.FilesSkipped > 0)
        {
            insights.Add($"{data.FilesSkipped} files skipped (binary/unsupported formats)");
        }
        
        // Watcher status
        if (data.WatcherEnabled)
        {
            insights.Add("File watcher enabled - index will auto-update on file changes");
        }
        
        // Full mode insights
        if (responseMode == "full")
        {
            if (data.Statistics != null)
            {
                if (data.Statistics.SegmentCount > 1)
                {
                    insights.Add($"Index has {data.Statistics.SegmentCount} segments - consider optimization");
                }
                
                if (data.Statistics.DeletedDocumentCount > 0)
                {
                    insights.Add($"{data.Statistics.DeletedDocumentCount} deleted documents in index - cleanup may improve performance");
                }
            }
        }
        
        return insights;
    }
    
    protected override List<AIAction> GenerateActions(IndexResult data, int tokenBudget)
    {
        var actions = new List<AIAction>();
        
        if (data.Success)
        {
            // Search the indexed content
            actions.Add(new AIAction
            {
                Action = ToolNames.TextSearch,
                Description = "Search for content in the indexed workspace",
                Priority = 100
            });
            
            // File search
            actions.Add(new AIAction
            {
                Action = ToolNames.FileSearch,
                Description = "Search for files by name pattern",
                Priority = 90
            });
            
            // Re-index if many files were skipped
            if (data.FilesSkipped > data.FilesIndexed * 0.5)
            {
                actions.Add(new AIAction
                {
                    Action = ToolNames.IndexWorkspace,
                    Description = "Re-index with different file type filters",
                    Priority = 70
                });
            }
            
            // Optimize if fragmented
            if (data.Statistics?.SegmentCount > 10)
            {
                actions.Add(new AIAction
                {
                    Action = "optimize_index",
                    Description = "Optimize index to improve search performance",
                    Priority = 60
                });
            }
        }
        else
        {
            // Retry indexing
            actions.Add(new AIAction
            {
                Action = ToolNames.IndexWorkspace,
                Description = "Retry indexing with corrected parameters",
                Priority = 100
            });
            
            // Check workspace
            actions.Add(new AIAction
            {
                Action = "verify_workspace",
                Description = "Verify workspace path exists and is accessible",
                Priority = 90
            });
        }
        
        return actions;
    }
    
    private List<string> ReduceFileList(List<string> files, int tokenBudget, string responseMode)
    {
        if (files.Count == 0)
            return files;
        
        // Estimate tokens per file path
        var avgPathLength = files.Average(f => f.Length);
        var tokensPerFile = (int)(avgPathLength / 3.5); // Rough estimate
        
        var maxFiles = Math.Max(10, tokenBudget / tokensPerFile);
        
        if (files.Count <= maxFiles)
            return files;
        
        // In summary mode, just show counts by directory
        if (responseMode == "summary")
        {
            var directoryCounts = files
                .GroupBy(f => Path.GetDirectoryName(f) ?? "")
                .Select(g => $"{g.Key}: {g.Count()} files")
                .Take(maxFiles / 2)
                .ToList();
            
            return directoryCounts;
        }
        
        // In full mode, show actual file paths but limited
        return files.Take(maxFiles).ToList();
    }
    
    private string BuildSummary(IndexResult data, int displayedFiles, string responseMode)
    {
        if (!data.Success)
        {
            return $"Failed to index workspace: {data.Error?.Message ?? "Unknown error"}";
        }
        
        var summary = data.IsNewIndex 
            ? $"Created new index with {data.FilesIndexed} files"
            : $"Updated index with {data.FilesIndexed} files";
        
        if (data.FilesSkipped > 0)
        {
            summary += $" ({data.FilesSkipped} skipped)";
        }
        
        if (data.IndexTimeMs > 0)
        {
            summary += $" in {data.IndexTimeMs}ms";
        }
        
        if (data.WatcherEnabled)
        {
            summary += " - watching for changes";
        }
        
        return summary;
    }
}

/// <summary>
/// Result from index operations
/// </summary>
public class IndexResult
{
    public bool Success { get; set; }
    public COA.Mcp.Framework.Models.ErrorInfo? Error { get; set; }
    public string? WorkspacePath { get; set; }
    public string? WorkspaceHash { get; set; }
    public string? IndexPath { get; set; }
    public bool IsNewIndex { get; set; }
    public int FilesIndexed { get; set; }
    public int FilesSkipped { get; set; }
    public long TotalSizeBytes { get; set; }
    public long IndexTimeMs { get; set; }
    public bool WatcherEnabled { get; set; }
    public List<string>? IndexedFiles { get; set; }
    public IndexStatistics? Statistics { get; set; }
}

/// <summary>
/// Index statistics
/// </summary>
public class IndexStatistics
{
    public int DocumentCount { get; set; }
    public int DeletedDocumentCount { get; set; }
    public int SegmentCount { get; set; }
    public long IndexSizeBytes { get; set; }
    public Dictionary<string, int> FileTypeDistribution { get; set; } = new();
}