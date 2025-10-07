using COA.CodeSearch.McpServer.Tools.Models;
using COA.Mcp.Framework.Models;
using COA.Mcp.Framework.TokenOptimization;
using COA.Mcp.Framework.TokenOptimization.Models;
using COA.Mcp.Framework.TokenOptimization.ResponseBuilders;
using COA.Mcp.Framework.TokenOptimization.Storage;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.ResponseBuilders;

/// <summary>
/// Response builder for smart refactoring operations with token-aware optimization
/// </summary>
public class SmartRefactorResponseBuilder : BaseResponseBuilder<SmartRefactorResult, AIOptimizedResponse<SmartRefactorResult>>
{
    private readonly IResourceStorageService? _storageService;

    public SmartRefactorResponseBuilder(
        ILogger<SmartRefactorResponseBuilder>? logger = null,
        IResourceStorageService? storageService = null)
        : base(logger)
    {
        _storageService = storageService;
    }

    public override async Task<AIOptimizedResponse<SmartRefactorResult>> BuildResponseAsync(
        SmartRefactorResult data,
        ResponseContext context)
    {
        var startTime = DateTime.UtcNow;
        var tokenBudget = CalculateTokenBudget(context);

        _logger?.LogDebug("Building smart refactor response with token budget: {Budget}", tokenBudget);

        // Allocate token budget
        var dataBudget = (int)(tokenBudget * 0.60);  // 60% for refactor data
        var insightsBudget = (int)(tokenBudget * 0.20); // 20% for insights
        var actionsBudget = (int)(tokenBudget * 0.20);  // 20% for next actions

        // Reduce changes if needed
        var reducedChanges = ReduceChanges(data.Changes, dataBudget);
        var wasTruncated = reducedChanges.Count < data.Changes.Count;

        // Store full results if truncated
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
                        Category = "smart-refactor-results",
                        Metadata = new Dictionary<string, string>
                        {
                            ["operation"] = data.Operation,
                            ["filesModified"] = data.FilesModified.Count.ToString(),
                            ["changesCount"] = data.ChangesCount.ToString(),
                            ["dryRun"] = data.DryRun.ToString()
                        }
                    });
                resourceUri = storageUri.ToString();
                _logger?.LogDebug("Stored full refactor results at: {Uri}", resourceUri);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to store full refactor results");
            }
        }

        // Generate insights and actions
        var insights = GenerateInsights(data, "adaptive");
        var actions = GenerateActions(data, actionsBudget);

        // Build the response with reduced changes
        var reducedResult = new SmartRefactorResult
        {
            Success = data.Success,
            Operation = data.Operation,
            DryRun = data.DryRun,
            FilesModified = data.FilesModified,
            ChangesCount = data.ChangesCount,
            Changes = reducedChanges,
            Errors = data.Errors,
            NextActions = data.NextActions,
            Duration = data.Duration
        };

        var response = new AIOptimizedResponse<SmartRefactorResult>
        {
            Success = data.Success,
            Data = new AIResponseData<SmartRefactorResult>
            {
                Summary = BuildSummary(data),
                Results = reducedResult,
                Count = data.ChangesCount,
                ExtensionData = new Dictionary<string, object>
                {
                    ["operation"] = data.Operation,
                    ["dryRun"] = data.DryRun,
                    ["filesModified"] = data.FilesModified.Count,
                    ["changesCount"] = data.ChangesCount,
                    ["errors"] = data.Errors.Count,
                    ["durationMs"] = (int)data.Duration.TotalMilliseconds
                }
            },
            Insights = insights,
            Actions = actions,
            ResponseMeta = new AIResponseMeta
            {
                ExecutionTime = $"{(DateTime.UtcNow - startTime).TotalMilliseconds:F0}ms",
                Truncated = wasTruncated,
                ResourceUri = resourceUri,
                TokenInfo = new TokenInfo
                {
                    Estimated = EstimateTokens(reducedResult),
                    Limit = context.TokenLimit ?? 8000,
                    ReductionStrategy = wasTruncated ? "progressive" : null
                }
            }
        };

        return response;
    }

    protected override List<string> GenerateInsights(SmartRefactorResult data, string responseMode)
    {
        var insights = new List<string>();

        if (data.Success)
        {
            if (data.DryRun)
            {
                insights.Add($"🔍 DRY RUN: Would modify {data.FilesModified.Count} files with {data.ChangesCount} total changes");
            }
            else
            {
                insights.Add($"✅ Successfully modified {data.FilesModified.Count} files with {data.ChangesCount} total changes");
            }

            if (data.Duration.TotalMilliseconds > 0)
            {
                insights.Add($"⚡ Completed in {data.Duration.TotalMilliseconds:F0}ms");
            }
        }
        else
        {
            insights.Add($"❌ Operation failed: {string.Join(", ", data.Errors.Take(3))}");
        }

        // Add specific insights based on operation
        if (data.Operation == "rename_symbol" && data.FilesModified.Any())
        {
            insights.Add($"📝 Renamed symbol across {data.FilesModified.Count} files");
        }

        if (data.Errors.Any())
        {
            insights.Add($"⚠️ {data.Errors.Count} errors occurred during operation");
        }

        return insights;
    }

    protected override List<AIAction> GenerateActions(SmartRefactorResult data, int tokenBudget)
    {
        var actions = new List<AIAction>();

        if (data.Success)
        {
            if (data.DryRun)
            {
                actions.Add(new AIAction
                {
                    Action = "apply_changes",
                    Description = "Set dry_run=false to apply the refactoring",
                    Priority = 100
                });
            }
            else
            {
                actions.Add(new AIAction
                {
                    Action = "verify_changes",
                    Description = "Run tests to verify the refactoring is correct",
                    Priority = 95
                });

                actions.Add(new AIAction
                {
                    Action = "review_diff",
                    Description = "Review git diff to inspect all changes",
                    Priority = 90
                });
            }

            if (data.Operation == "rename_symbol")
            {
                actions.Add(new AIAction
                {
                    Action = "find_references",
                    Description = "Verify all references were renamed correctly",
                    Priority = 85
                });
            }
        }
        else
        {
            actions.Add(new AIAction
            {
                Action = "review_errors",
                Description = "Check error messages and fix issues",
                Priority = 100
            });
        }

        return actions;
    }

    private List<FileRefactorChange> ReduceChanges(List<FileRefactorChange> changes, int tokenBudget)
    {
        if (!changes.Any())
            return changes;

        var reduced = new List<FileRefactorChange>();
        var estimatedTokens = 0;

        foreach (var change in changes.OrderByDescending(c => c.ReplacementCount))
        {
            var changeTokens = EstimateChangeTokens(change);

            if (estimatedTokens + changeTokens > tokenBudget && reduced.Count > 0)
            {
                _logger?.LogDebug("Truncating changes at {Count} to fit token budget", reduced.Count);
                break;
            }

            reduced.Add(change);
            estimatedTokens += changeTokens;
        }

        return reduced;
    }

    private int EstimateChangeTokens(FileRefactorChange change)
    {
        var tokens = 40; // Base tokens for file path and counts

        if (!string.IsNullOrEmpty(change.ChangePreview))
        {
            tokens += TokenEstimator.EstimateString(change.ChangePreview);
        }

        tokens += change.Lines.Count * 3; // ~3 tokens per line number

        return tokens;
    }

    private int EstimateTokens(SmartRefactorResult result)
    {
        return result.Changes.Sum(c => EstimateChangeTokens(c)) + 200; // +200 for metadata
    }

    private string BuildSummary(SmartRefactorResult data)
    {
        if (!data.Success)
        {
            return $"Smart refactor failed: {data.Operation}";
        }

        if (data.DryRun)
        {
            return $"Preview: {data.Operation} would modify {data.FilesModified.Count} files ({data.ChangesCount} changes)";
        }

        return $"Completed: {data.Operation} modified {data.FilesModified.Count} files ({data.ChangesCount} changes)";
    }
}
