using COA.Mcp.Framework.TokenOptimization.ResponseBuilders;
using COA.Mcp.Framework.TokenOptimization.Storage;
using COA.Mcp.Framework.TokenOptimization.Models;
using COA.Mcp.Framework.TokenOptimization;
using COA.Mcp.Framework.Models;
using COA.CodeSearch.McpServer.Tools.Results;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.ResponseBuilders;

/// <summary>
/// Response builder for directory search results with token optimization
/// </summary>
public class DirectorySearchResponseBuilder : BaseResponseBuilder<DirectorySearchResult, AIOptimizedResponse<DirectorySearchResult>>
{
    private readonly IResourceStorageService? _storageService;
    
    public DirectorySearchResponseBuilder(
        ILogger<DirectorySearchResponseBuilder>? logger = null,
        IResourceStorageService? storageService = null)
        : base(logger)
    {
        _storageService = storageService;
    }
    
    public override async Task<AIOptimizedResponse<DirectorySearchResult>> BuildResponseAsync(
        DirectorySearchResult data, 
        ResponseContext context)
    {
        var startTime = DateTime.UtcNow;
        var tokenBudget = CalculateTokenBudget(context);
        
        _logger?.LogDebug("Building directory search response with token budget: {Budget}, Mode: {Mode}", 
            tokenBudget, context.ResponseMode);
        
        // Allocate token budget
        var dataBudget = (int)(tokenBudget * 0.7);  // 70% for data
        var insightsBudget = (int)(tokenBudget * 0.15); // 15% for insights
        var actionsBudget = (int)(tokenBudget * 0.15);  // 15% for actions
        
        // Reduce directories to fit budget
        var reducedDirectories = ReduceDirectories(data.Directories, dataBudget, context.ResponseMode);
        var wasTruncated = reducedDirectories.Count < data.Directories.Count;
        
        // Store full results if truncated
        string? resourceUri = null;
        if (wasTruncated && context.StoreFullResults && _storageService != null)
        {
            try
            {
                var storageUri = await _storageService.StoreAsync(
                    data.Directories,
                    new ResourceStorageOptions
                    {
                        Expiration = TimeSpan.FromHours(24),
                        Compress = true,
                        Category = "directory-search-results",
                        Metadata = new Dictionary<string, string>
                        {
                            ["pattern"] = data.Pattern ?? "",
                            ["totalMatches"] = data.TotalMatches.ToString(),
                            ["workspace"] = data.WorkspacePath ?? ""
                        }
                    });
                resourceUri = storageUri.ToString();
                _logger?.LogDebug("Stored full results at: {Uri}", resourceUri);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to store full directory search results");
            }
        }
        
        // Generate insights and actions
        var insights = GenerateInsights(data, context.ResponseMode);
        var actions = GenerateActions(data, actionsBudget);
        
        // Build the response
        var response = new AIOptimizedResponse<DirectorySearchResult>
        {
            Success = true,
            Data = new AIResponseData<DirectorySearchResult>
            {
                Summary = BuildSummary(data, reducedDirectories.Count, context.ResponseMode),
                Results = new DirectorySearchResult
                {
                    Success = data.Success,
                    Directories = reducedDirectories,
                    TotalMatches = data.TotalMatches,
                    Pattern = data.Pattern ?? string.Empty,
                    WorkspacePath = data.WorkspacePath ?? string.Empty,
                    IncludedSubdirectories = data.IncludedSubdirectories,
                    SearchTimeMs = data.SearchTimeMs
                },
                Count = data.TotalMatches,
                ExtensionData = new Dictionary<string, object>
                {
                    ["totalMatches"] = data.TotalMatches,
                    ["pattern"] = data.Pattern ?? "",
                    ["searchTimeMs"] = data.SearchTimeMs
                }
            },
            Insights = ReduceInsights(insights, insightsBudget),
            Actions = ReduceActions(actions, actionsBudget),
            Meta = CreateMetadata(startTime, wasTruncated, resourceUri)
        };
        
        // Update token estimate
        response.Meta.TokenInfo!.Estimated = TokenEstimator.EstimateObject(response);
        
        _logger?.LogInformation("Built directory search response: {Dirs} of {Total} directories, {Insights} insights, {Actions} actions, {Tokens} tokens",
            reducedDirectories.Count, data.TotalMatches, response.Insights.Count, response.Actions.Count, response.Meta.TokenInfo.Estimated);
        
        return response;
    }
    
    protected override List<string> GenerateInsights(DirectorySearchResult data, string responseMode)
    {
        var insights = new List<string>();
        
        if (data.TotalMatches == 0)
        {
            insights.Add($"No directories found matching pattern '{data.Pattern}'. Consider using a broader pattern or checking the workspace path.");
            return insights;
        }
        
        if (data.TotalMatches > data.Directories.Count)
        {
            insights.Add($"Showing {data.Directories.Count} of {data.TotalMatches} matching directories.");
        }
        
        // Analyze directory structure
        if (data.Directories.Any())
        {
            var maxDepth = data.Directories.Max(d => d.Depth);
            var avgFileCount = data.Directories.Average(d => d.FileCount);
            
            if (maxDepth > 5)
            {
                insights.Add($"Deep directory structure detected (max depth: {maxDepth} levels).");
            }
            
            if (avgFileCount > 50)
            {
                insights.Add($"Large directories found with average of {avgFileCount:F0} files per directory.");
            }
            
            var hiddenDirs = data.Directories.Count(d => d.IsHidden);
            if (hiddenDirs > 0)
            {
                insights.Add($"Found {hiddenDirs} hidden directories (starting with '.').");
            }
            
            // Group by depth
            var depthGroups = data.Directories.GroupBy(d => d.Depth).OrderBy(g => g.Key);
            if (depthGroups.Count() > 1)
            {
                var depthDistribution = string.Join(", ", depthGroups.Select(g => $"Level {g.Key}: {g.Count()}"));
                insights.Add($"Directory distribution by depth: {depthDistribution}");
            }
        }
        
        return insights;
    }
    
    protected override List<AIAction> GenerateActions(DirectorySearchResult data, int maxActions)
    {
        var actions = new List<AIAction>();
        
        if (data.TotalMatches == 0 && !string.IsNullOrEmpty(data.Pattern))
        {
            actions.Add(new AIAction
            {
                Action = "broaden_search",
                Description = "Try a less specific pattern",
                Rationale = "No results found with current pattern",
                Parameters = new Dictionary<string, object>
                {
                    ["pattern"] = "*" + data.Pattern.TrimStart('*')
                },
                Priority = 100
            });
        }
        else if (data.TotalMatches > 100)
        {
            actions.Add(new AIAction
            {
                Action = "refine_search",
                Description = "Use a more specific pattern to narrow results",
                Rationale = $"Found {data.TotalMatches} directories, which may be too many to review",
                Priority = 80
            });
        }
        
        if (!data.IncludedSubdirectories && data.TotalMatches < 10)
        {
            actions.Add(new AIAction
            {
                Action = "include_subdirectories",
                Description = "Search in subdirectories for more results",
                Rationale = "Current search only includes top-level directories",
                Parameters = new Dictionary<string, object>
                {
                    ["includeSubdirectories"] = true
                },
                Priority = 90
            });
        }
        
        // Suggest searching for files in found directories
        if (data.Directories.Any())
        {
            var topDir = data.Directories.First();
            actions.Add(new AIAction
            {
                Action = "search_files_in_directory",
                Description = $"Search for files in '{topDir.Name}'",
                Rationale = "Explore the contents of matching directories",
                Parameters = new Dictionary<string, object>
                {
                    ["directory"] = topDir.Path
                },
                Priority = 70
            });
        }
        
        return actions.Take(maxActions).ToList();
    }
    
    private List<DirectoryMatch> ReduceDirectories(List<DirectoryMatch> directories, int tokenBudget, string responseMode)
    {
        if (!directories.Any())
            return directories;
        
        var tokensPerDir = EstimateTokensPerDirectory();
        var maxDirs = Math.Max(1, tokenBudget / tokensPerDir);
        
        // In summary mode, show fewer items
        if (responseMode == "summary")
        {
            maxDirs = Math.Min(10, maxDirs);
        }
        
        return directories.Take(maxDirs).ToList();
    }
    
    private string BuildSummary(DirectorySearchResult data, int shownCount, string responseMode)
    {
        if (data.TotalMatches == 0)
        {
            return $"No directories found matching pattern '{data.Pattern}' in {data.WorkspacePath}";
        }
        
        var summary = $"Found {data.TotalMatches} directories matching '{data.Pattern}'";
        
        if (shownCount < data.TotalMatches)
        {
            summary += $" (showing {shownCount})";
        }
        
        if (data.SearchTimeMs > 0)
        {
            summary += $" in {data.SearchTimeMs}ms";
        }
        
        return summary;
    }
    
    private int EstimateTokensPerDirectory()
    {
        // Each directory entry with all fields is roughly 50-100 tokens
        return 75;
    }
}