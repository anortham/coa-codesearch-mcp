using COA.Mcp.Framework.TokenOptimization.ResponseBuilders;
using COA.Mcp.Framework.TokenOptimization.Models;
using COA.Mcp.Framework.TokenOptimization.Storage;
using COA.Mcp.Framework.TokenOptimization;
using COA.Mcp.Framework.Models;
using COA.CodeSearch.McpServer.Tools;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.ResponseBuilders;

/// <summary>
/// Response builder for similar files search results with progressive disclosure
/// </summary>
public class SimilarFilesResponseBuilder : BaseResponseBuilder<SimilarFilesSearchResult, AIOptimizedResponse<SimilarFilesResult>>
{
    private const int SUMMARY_FILE_LIMIT = 5;
    private const int FULL_FILE_LIMIT = 20;
    private readonly IResourceStorageService? _storageService;
    
    public SimilarFilesResponseBuilder(
        ILogger<SimilarFilesResponseBuilder>? logger,
        IResourceStorageService? storageService = null)
        : base(logger)
    {
        _storageService = storageService;
    }
    
    public override async Task<AIOptimizedResponse<SimilarFilesResult>> BuildResponseAsync(
        SimilarFilesSearchResult data,
        ResponseContext context)
    {
        var startTime = DateTime.UtcNow;
        var tokenBudget = CalculateTokenBudget(context);
        
        _logger?.LogDebug("Building similar files response with token budget: {Budget}, Mode: {Mode}", 
            tokenBudget, context.ResponseMode);
        
        // Determine how many files to include based on response mode
        var fileLimit = context.ResponseMode?.ToLowerInvariant() switch
        {
            "summary" => SUMMARY_FILE_LIMIT,
            "full" => FULL_FILE_LIMIT,
            _ => Math.Min(data.SimilarFiles.Count, FULL_FILE_LIMIT)
        };
        
        var reducedFiles = data.SimilarFiles.Take(fileLimit).ToList();
        var wasTruncated = reducedFiles.Count < data.SimilarFiles.Count;
        
        // Store full results if truncated and storage is available
        string? resourceUri = null;
        if (wasTruncated && context.StoreFullResults && _storageService != null)
        {
            try
            {
                var storageUri = await _storageService.StoreAsync(
                    data.SimilarFiles,
                    new ResourceStorageOptions
                    {
                        Expiration = TimeSpan.FromHours(1),
                        Compress = true,
                        Category = "similar-files",
                        Metadata = new Dictionary<string, string>
                        {
                            ["queryFile"] = data.QueryFile,
                            ["totalMatches"] = data.TotalMatches.ToString(),
                            ["tool"] = "similar_files"
                        }
                    });
                resourceUri = storageUri.ToString();
                _logger?.LogDebug("Stored full results at: {Uri}", resourceUri);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to store full similar files results");
            }
        }
        
        // Generate insights and actions
        var insights = GenerateInsights(data, context.ResponseMode ?? "adaptive");
        var actions = GenerateActions(data, tokenBudget / 10); // Use 10% of budget for actions
        
        // Build the result
        var result = new SimilarFilesResult
        {
            Success = data.Success,
            QueryFile = data.QueryFile,
            TotalMatches = data.TotalMatches,
            Files = reducedFiles
        };
        
        if (wasTruncated)
        {
            result.Message = $"Showing top {fileLimit} of {data.TotalMatches} similar files.";
        }
        
        // Build the response
        var response = new AIOptimizedResponse<SimilarFilesResult>
        {
            Success = true,
            Data = new AIResponseData<SimilarFilesResult>
            {
                Summary = BuildSummary(data, reducedFiles.Count),
                Results = result,
                Count = data.TotalMatches,
                ExtensionData = new Dictionary<string, object>
                {
                    ["queryFile"] = data.QueryFile,
                    ["totalMatches"] = data.TotalMatches,
                    ["searchTimeMs"] = data.SearchTimeMs
                }
            },
            Insights = insights,
            Actions = actions,
            Meta = CreateMetadata(startTime, wasTruncated, resourceUri)
        };
        
        return response;
    }
    
    private string BuildSummary(SimilarFilesSearchResult data, int displayedCount)
    {
        if (data.TotalMatches == 0)
            return "No similar files found.";
        
        var summary = $"Found {data.TotalMatches} files similar to {Path.GetFileName(data.QueryFile)}.";
        if (displayedCount < data.TotalMatches)
            summary += $" Showing top {displayedCount} matches.";
        
        return summary;
    }
    
    protected override List<string> GenerateInsights(SimilarFilesSearchResult data, string responseMode)
    {
        var insights = new List<string>();
        
        if (data.TotalMatches == 0)
        {
            insights.Add("No similar files found - this file appears to be unique");
            insights.Add("Consider if this file should be refactored to share common patterns");
        }
        else if (data.TotalMatches == 1)
        {
            insights.Add("Found one similar file - potential for code consolidation");
        }
        else if (data.TotalMatches > 10)
        {
            insights.Add($"Found {data.TotalMatches} similar files - significant code duplication detected");
            insights.Add("Consider extracting common functionality into shared components");
        }
        
        // Analyze similarity scores
        if (data.SimilarFiles.Any())
        {
            var avgScore = data.SimilarFiles.Average(f => f.Score);
            if (avgScore > 0.7f)
            {
                insights.Add("High similarity scores indicate potential duplicate code");
            }
            else if (avgScore < 0.3f)
            {
                insights.Add("Low similarity scores - files share minimal common patterns");
            }
            
            // Check for same extension patterns
            var queryExt = Path.GetExtension(data.QueryFile);
            var sameExtCount = data.SimilarFiles.Count(f => f.Extension == queryExt);
            if (sameExtCount == data.SimilarFiles.Count)
            {
                insights.Add($"All similar files are {queryExt} files - good language consistency");
            }
            else if (sameExtCount == 0)
            {
                insights.Add("Similar patterns found across different file types");
            }
        }
        
        return insights;
    }
    
    protected override List<AIAction> GenerateActions(SimilarFilesSearchResult data, int tokenBudget)
    {
        var actions = new List<AIAction>();
        
        if (data.TotalMatches > 5)
        {
            actions.Add(new AIAction
            {
                Action = "analyze_duplication",
                Description = "Analyze code duplication patterns across similar files",
                Priority = 90
            });
            
            actions.Add(new AIAction
            {
                Action = "refactor_common",
                Description = "Extract common functionality into shared modules",
                Priority = 80
            });
        }
        
        if (data.SimilarFiles.Any(f => f.Score > 0.8f))
        {
            actions.Add(new AIAction
            {
                Action = "review_duplicates",
                Description = "Review highly similar files for potential consolidation",
                Priority = 95
            });
        }
        
        if (data.TotalMatches > 0)
        {
            actions.Add(new AIAction
            {
                Action = "compare_files",
                Description = "Compare the most similar files to understand patterns",
                Priority = 70
            });
        }
        
        return actions;
    }
}