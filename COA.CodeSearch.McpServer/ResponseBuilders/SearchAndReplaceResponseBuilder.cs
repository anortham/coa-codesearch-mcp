using COA.Mcp.Framework.TokenOptimization.ResponseBuilders;
using COA.Mcp.Framework.TokenOptimization.Models;
using COA.Mcp.Framework.TokenOptimization;
using COA.Mcp.Framework.TokenOptimization.Storage;
using COA.Mcp.Framework.TokenOptimization.Reduction;
using COA.CodeSearch.McpServer.Models;
using COA.Mcp.Framework.Models;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.ResponseBuilders;

/// <summary>
/// Response builder for search and replace operations with token-aware optimization and resource storage.
/// </summary>
public class SearchAndReplaceResponseBuilder : BaseResponseBuilder<SearchAndReplaceResult, AIOptimizedResponse<SearchAndReplaceResult>>
{
    private readonly IResourceStorageService? _storageService;
    
    public SearchAndReplaceResponseBuilder(
        ILogger<SearchAndReplaceResponseBuilder>? logger = null,
        IResourceStorageService? storageService = null)
        : base(logger)
    {
        _storageService = storageService;
    }
    
    public override async Task<AIOptimizedResponse<SearchAndReplaceResult>> BuildResponseAsync(SearchAndReplaceResult data, ResponseContext context)
    {
        var startTime = DateTime.UtcNow;
        var tokenBudget = CalculateTokenBudget(context);
        
        _logger?.LogDebug("Building search and replace response with token budget: {Budget}, Mode: {Mode}", 
            tokenBudget, context.ResponseMode);
        
        // Allocate token budget
        var dataBudget = (int)(tokenBudget * 0.7);  // 70% for data
        var insightsBudget = (int)(tokenBudget * 0.15); // 15% for insights
        var actionsBudget = (int)(tokenBudget * 0.15);  // 15% for actions
        
        // Reduce changes to fit budget
        var reducedChanges = ReduceChanges(data.Changes, dataBudget, context.ResponseMode);
        var wasTruncated = reducedChanges.Count < data.Changes.Count;
        
        // Store full results if truncated and storage is available
        string? resourceUri = null;
        if (wasTruncated && context.StoreFullResults && _storageService != null)
        {
            try
            {
                var storageUri = await _storageService.StoreAsync(
                    data.Changes,
                    new ResourceStorageOptions
                    {
                        Expiration = TimeSpan.FromHours(24),
                        Compress = true,
                        Category = "search-replace-results",
                        Metadata = new Dictionary<string, string>
                        {
                            ["searchPattern"] = data.SearchPattern ?? "",
                            ["replacePattern"] = data.ReplacePattern ?? "",
                            ["totalReplacements"] = data.TotalReplacements.ToString(),
                            ["totalFiles"] = data.TotalFiles.ToString(),
                            ["preview"] = data.Preview.ToString(),
                            ["tool"] = context.ToolName ?? "search_and_replace"
                        }
                    });
                resourceUri = storageUri.ToString();
                _logger?.LogDebug("Stored full search and replace results at: {Uri}", resourceUri);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to store full search and replace results");
            }
        }
        
        // Generate insights and actions
        var insights = ReduceInsights(GenerateInsights(data, context.ResponseMode), insightsBudget);
        var actions = ReduceActions(GenerateActions(data, actionsBudget), actionsBudget);
        
        // Update data with reduced results and recalculate summaries
        var reducedFileSummaries = RecalculateFileSummaries(reducedChanges);
        var optimizedData = new SearchAndReplaceResult
        {
            Summary = data.Summary,
            Preview = data.Preview,
            Changes = reducedChanges,
            FileSummaries = reducedFileSummaries,
            TotalFiles = data.TotalFiles, // Keep original count
            TotalReplacements = data.TotalReplacements, // Keep original count
            SearchTime = data.SearchTime,
            ApplyTime = data.ApplyTime,
            SearchPattern = data.SearchPattern ?? "",
            ReplacePattern = data.ReplacePattern ?? "",
            Truncated = wasTruncated,
            Insights = null // Will be handled by framework
        };
        
        var response = new AIOptimizedResponse<SearchAndReplaceResult>
        {
            Success = true,
            Data = new AIResponseData<SearchAndReplaceResult>
            {
                Summary = BuildSummary(data, reducedChanges.Count, context.ResponseMode),
                Results = optimizedData,
                Count = data.TotalReplacements,
                ExtensionData = new Dictionary<string, object>
                {
                    ["totalFiles"] = data.TotalFiles,
                    ["searchPattern"] = data.SearchPattern ?? "",
                    ["replacePattern"] = data.ReplacePattern ?? "",
                    ["preview"] = data.Preview,
                    ["searchTime"] = (int)data.SearchTime.TotalMilliseconds,
                    ["applyTime"] = data.ApplyTime?.TotalMilliseconds ?? 0
                }
            },
            Insights = ReduceInsights(insights, insightsBudget),
            Actions = ReduceActions(actions, actionsBudget),
            Meta = CreateMetadata(startTime, wasTruncated, resourceUri)
        };
        
        // Update token estimate
        response.Meta.TokenInfo!.Estimated = TokenEstimator.EstimateObject(response);
        
        _logger?.LogInformation("Built search and replace response: {Changes} of {Total} changes, {Files} files, {Mode}, {Tokens} tokens",
            reducedChanges.Count, data.TotalReplacements, data.TotalFiles, data.Preview ? "preview" : "applied", response.Meta.TokenInfo.Estimated);
        
        return response;
    }
    
    protected override List<string> GenerateInsights(SearchAndReplaceResult data, string responseMode)
    {
        var insights = new List<string>();
        
        if (data.TotalReplacements == 0)
        {
            insights.Add("No matches found - try different search patterns or file filters");
            return insights;
        }
        
        // Preview vs applied status
        var actionType = data.Preview ? "Preview" : "Applied";
        insights.Add($"{actionType}: {data.TotalReplacements} replacements in {data.TotalFiles} files");
        
        // File type distribution
        if (data.FileSummaries.Count > 1 && responseMode != "summary")
        {
            var fileExtensions = data.FileSummaries
                .Select(f => Path.GetExtension(f.FilePath))
                .Where(ext => !string.IsNullOrEmpty(ext))
                .GroupBy(ext => ext)
                .OrderByDescending(g => g.Count())
                .Take(3)
                .Select(g => $"{g.Key}:{g.Count()}")
                .ToList();
                
            if (fileExtensions.Any())
            {
                insights.Add($"File types: {string.Join(", ", fileExtensions)}");
            }
        }
        
        // High-change files
        if (responseMode != "summary")
        {
            var topFiles = data.FileSummaries
                .Where(f => f.ChangeCount > 3)
                .OrderByDescending(f => f.ChangeCount)
                .Take(3)
                .Select(f => $"{Path.GetFileName(f.FilePath)} ({f.ChangeCount} changes)")
                .ToList();
                
            if (topFiles.Any())
            {
                insights.Add($"High impact: {string.Join(", ", topFiles)}");
            }
        }
        
        // Performance insight
        if (data.SearchTime.TotalSeconds > 1)
        {
            insights.Add($"Search took {data.SearchTime.TotalSeconds:F1}s - consider file pattern filtering");
        }
        
        // Truncation notice
        if (data.Truncated)
        {
            insights.Add("Some changes truncated - use resourceUri for complete details");
        }
        
        return insights;
    }
    
    protected override List<AIAction> GenerateActions(SearchAndReplaceResult data, int tokenBudget)
    {
        var actions = new List<AIAction>();
        
        if (data.Changes.Any())
        {
            // Navigate to first change
            var firstChange = data.Changes.First();
            actions.Add(new AIAction
            {
                Action = "navigate_to_change",
                Description = $"Go to {Path.GetFileName(firstChange.FilePath)}:{firstChange.LineNumber}",
                Priority = 80
            });
            
            // Apply changes if in preview mode
            if (data.Preview && data.TotalReplacements > 0)
            {
                actions.Add(new AIAction
                {
                    Action = "apply_changes",
                    Description = $"Apply {data.TotalReplacements} replacements",
                    Priority = 90
                });
            }
            
            // Examine high-impact files
            var highImpactFile = data.FileSummaries.OrderByDescending(f => f.ChangeCount).First();
            if (highImpactFile.ChangeCount > 3)
            {
                actions.Add(new AIAction
                {
                    Action = "examine_file",
                    Description = $"Review {Path.GetFileName(highImpactFile.FilePath)} ({highImpactFile.ChangeCount} changes)",
                    Priority = 70
                });
            }
        }
        
        // Suggest testing if changes were applied
        if (!data.Preview && data.TotalReplacements > 0)
        {
            actions.Add(new AIAction
            {
                Action = "run_tests",
                Description = "Run tests to verify changes work correctly",
                Priority = 85
            });
        }
        
        // Suggest refinement for large result sets
        if (data.TotalReplacements > 50)
        {
            actions.Add(new AIAction
            {
                Action = "refine_scope",
                Description = "Use file patterns to limit scope of changes",
                Priority = 60
            });
        }
        
        return actions;
    }
    
    private List<ReplacementChange> ReduceChanges(List<ReplacementChange> changes, int tokenBudget, string responseMode)
    {
        if (changes.Count == 0)
            return changes;
        
        // Calculate mode-specific limits
        var maxChanges = responseMode switch
        {
            "summary" => 5,
            "full" => 20,
            _ => 10 // default
        };
        
        // Estimate tokens per change
        var sampleChange = changes.First();
        var tokensPerChange = EstimateChangeTokens(sampleChange, responseMode);
        
        // Calculate how many changes we can include based on budget
        var budgetBasedMaxChanges = Math.Max(1, tokenBudget / tokensPerChange);
        maxChanges = Math.Min(maxChanges, budgetBasedMaxChanges);
        
        // Take first N changes (preserving file order and line order)
        var reducedChanges = changes.Take(maxChanges).ToList();
        
        return CleanupChanges(reducedChanges, responseMode);
    }
    
    private List<ReplacementChange> CleanupChanges(List<ReplacementChange> changes, string responseMode)
    {
        return changes.Select(change => new ReplacementChange
        {
            FilePath = ShortenPath(change.FilePath),
            LineNumber = change.LineNumber,
            ColumnStart = change.ColumnStart,
            OriginalLength = change.OriginalLength,
            OriginalText = change.OriginalText,
            ReplacementText = change.ReplacementText,
            ModifiedLine = change.ModifiedLine,
            Applied = change.Applied,
            Error = change.Error,
            // Include context only in full mode to save tokens
            ContextBefore = responseMode == "full" ? change.ContextBefore : null,
            ContextAfter = responseMode == "full" ? change.ContextAfter : null
        }).ToList();
    }
    
    private List<FileChangeSummary> RecalculateFileSummaries(List<ReplacementChange> changes)
    {
        return changes
            .GroupBy(c => c.FilePath)
            .Select(g => new FileChangeSummary
            {
                FilePath = g.Key,
                ChangeCount = g.Count(),
                AllApplied = g.All(c => c.Applied == true)
            })
            .OrderByDescending(f => f.ChangeCount)
            .ToList();
    }
    
    private string ShortenPath(string fullPath)
    {
        try
        {
            var fileName = Path.GetFileName(fullPath);
            var directory = Path.GetFileName(Path.GetDirectoryName(fullPath));
            return string.IsNullOrEmpty(directory) ? fileName : $"{directory}/{fileName}";
        }
        catch
        {
            return Path.GetFileName(fullPath) ?? fullPath;
        }
    }
    
    private int EstimateChangeTokens(ReplacementChange change, string responseMode)
    {
        var tokens = TokenEstimator.EstimateString(change.FilePath);
        tokens += TokenEstimator.EstimateString(change.OriginalText);
        tokens += TokenEstimator.EstimateString(change.ReplacementText);
        tokens += TokenEstimator.EstimateString(change.ModifiedLine ?? "");
        
        // Add context tokens if in full mode
        if (responseMode == "full")
        {
            if (change.ContextBefore != null)
            {
                foreach (var line in change.ContextBefore)
                {
                    tokens += TokenEstimator.EstimateString(line);
                }
            }
            
            if (change.ContextAfter != null)
            {
                foreach (var line in change.ContextAfter)
                {
                    tokens += TokenEstimator.EstimateString(line);
                }
            }
        }
        
        // Add overhead for JSON structure
        tokens += 100; // Base structure overhead
        
        return Math.Max(tokens, 150); // Minimum estimate
    }
    
    private string BuildSummary(SearchAndReplaceResult data, int includedChangesCount, string responseMode)
    {
        if (data.TotalReplacements == 0)
            return "No matches found for replacement";
        
        var actionType = data.Preview ? "Preview" : "Applied";
        var summary = $"{actionType}: {data.TotalReplacements} replacements in {data.TotalFiles} files";
        
        if (includedChangesCount < data.TotalReplacements)
            summary += $" (showing {includedChangesCount})";
        
        // Include timing for significant operations
        if (responseMode != "summary" && data.SearchTime.TotalMilliseconds > 100)
        {
            summary += $" ({(int)data.SearchTime.TotalMilliseconds}ms";
            if (data.ApplyTime.HasValue)
                summary += $" + {(int)data.ApplyTime.Value.TotalMilliseconds}ms apply";
            summary += ")";
        }
        
        return summary;
    }
}