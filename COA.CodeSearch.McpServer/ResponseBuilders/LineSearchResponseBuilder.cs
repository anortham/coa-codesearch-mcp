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
/// Response builder for line search operations with token-aware optimization and resource storage.
/// </summary>
public class LineSearchResponseBuilder : BaseResponseBuilder<LineSearchResult, AIOptimizedResponse<LineSearchResult>>
{
    private readonly IResourceStorageService? _storageService;
    
    public LineSearchResponseBuilder(
        ILogger<LineSearchResponseBuilder>? logger = null,
        IResourceStorageService? storageService = null)
        : base(logger)
    {
        _storageService = storageService;
    }
    
    public override async Task<AIOptimizedResponse<LineSearchResult>> BuildResponseAsync(LineSearchResult data, ResponseContext context)
    {
        var startTime = DateTime.UtcNow;
        var tokenBudget = CalculateTokenBudget(context);
        
        _logger?.LogDebug("Building line search response with token budget: {Budget}, Mode: {Mode}", 
            tokenBudget, context.ResponseMode);
        
        // Allocate token budget
        var dataBudget = (int)(tokenBudget * 0.7);  // 70% for data
        var insightsBudget = (int)(tokenBudget * 0.15); // 15% for insights
        var actionsBudget = (int)(tokenBudget * 0.15);  // 15% for actions
        
        // Reduce search results to fit budget
        var reducedFiles = ReduceLineSearchResults(data.Files, dataBudget, context.ResponseMode);
        var wasTruncated = reducedFiles.Count < data.Files.Count || 
                          reducedFiles.Any(f => f.Matches.Count < f.TotalMatches);
        
        // Store full results if truncated and storage is available
        string? resourceUri = null;
        if (wasTruncated && context.StoreFullResults && _storageService != null)
        {
            try
            {
                var storageUri = await _storageService.StoreAsync(
                    data.Files,
                    new ResourceStorageOptions
                    {
                        Expiration = TimeSpan.FromHours(24),
                        Compress = true,
                        Category = "line-search-results",
                        Metadata = new Dictionary<string, string>
                        {
                            ["query"] = data.Query ?? "",
                            ["totalLineMatches"] = data.TotalLineMatches.ToString(),
                            ["totalFilesWithMatches"] = data.TotalFilesWithMatches.ToString(),
                            ["tool"] = context.ToolName ?? "line_search"
                        }
                    });
                resourceUri = storageUri.ToString();
                _logger?.LogDebug("Stored full line search results at: {Uri}", resourceUri);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to store full line search results");
            }
        }
        
        // Generate insights and actions
        var insights = ReduceInsights(GenerateInsights(data, context.ResponseMode), insightsBudget);
        var actions = ReduceActions(GenerateActions(data, actionsBudget), actionsBudget);
        
        // Update data with reduced results
        var optimizedData = new LineSearchResult
        {
            Summary = data.Summary,
            Files = reducedFiles,
            TotalFilesSearched = data.TotalFilesSearched,
            TotalFilesWithMatches = data.TotalFilesWithMatches,
            TotalLineMatches = data.TotalLineMatches, // Keep original count
            SearchTime = data.SearchTime,
            Query = data.Query ?? "",
            Truncated = wasTruncated,
            Insights = null // Will be handled by framework
        };
        
        var response = new AIOptimizedResponse<LineSearchResult>
        {
            Success = true,
            Data = new AIResponseData<LineSearchResult>
            {
                Summary = BuildSummary(data, reducedFiles.Count, context.ResponseMode),
                Results = optimizedData,
                Count = data.TotalFilesWithMatches,
                ExtensionData = new Dictionary<string, object>
                {
                    ["totalLineMatches"] = data.TotalLineMatches,
                    ["query"] = data.Query ?? "",
                    ["processingTime"] = (int)data.SearchTime.TotalMilliseconds
                }
            },
            Insights = ReduceInsights(insights, insightsBudget),
            Actions = ReduceActions(actions, actionsBudget),
            Meta = CreateMetadata(startTime, wasTruncated, resourceUri)
        };
        
        // Update token estimate
        response.Meta.TokenInfo!.Estimated = TokenEstimator.EstimateObject(response);
        
        _logger?.LogInformation("Built line search response: {Files} of {Total} files, {Lines} lines, {Insights} insights, {Actions} actions, {Tokens} tokens",
            reducedFiles.Count, data.TotalFilesWithMatches, reducedFiles.Sum(f => f.Matches.Count), insights.Count, actions.Count, response.Meta.TokenInfo.Estimated);
        
        return response;
    }
    
    protected override List<string> GenerateInsights(LineSearchResult data, string responseMode)
    {
        var insights = new List<string>();
        
        if (data.TotalLineMatches == 0)
        {
            insights.Add("No matches found - try broader search terms or check file patterns");
            return insights;
        }
        
        // Basic statistics
        if (data.Files.Count > 1)
        {
            var fileExtensions = data.Files
                .Select(f => Path.GetExtension(f.FilePath))
                .Where(ext => !string.IsNullOrEmpty(ext))
                .GroupBy(ext => ext)
                .OrderByDescending(g => g.Count())
                .Take(3)
                .Select(g => $"{g.Key}:{g.Count()}")
                .ToList();
                
            if (fileExtensions.Any())
            {
                insights.Add($"Types: {string.Join(", ", fileExtensions)}");
            }
        }
        
        // High-frequency files
        if (responseMode != "summary")
        {
            var topFiles = data.Files
                .Where(f => f.TotalMatches > 5)
                .OrderByDescending(f => f.TotalMatches)
                .Take(3)
                .Select(f => $"{Path.GetFileName(f.FilePath)} ({f.TotalMatches} matches)")
                .ToList();
                
            if (topFiles.Any())
            {
                insights.Add($"High frequency: {string.Join(", ", topFiles)}");
            }
        }
        
        // Truncation notice
        if (data.Truncated)
        {
            insights.Add("Some results truncated - use resourceUri for complete data");
        }
        
        return insights;
    }
    
    protected override List<AIAction> GenerateActions(LineSearchResult data, int tokenBudget)
    {
        var actions = new List<AIAction>();
        
        if (data.Files.Any())
        {
            // Navigate to first result
            var firstFile = data.Files.First();
            var firstMatch = firstFile.Matches.FirstOrDefault();
            if (firstMatch != null)
            {
                actions.Add(new AIAction
                {
                    Action = "navigate_to_line",
                    Description = $"Go to {Path.GetFileName(firstFile.FilePath)}:{firstMatch.LineNumber}",
                    Priority = 80
                });
            }
            
            // Explore high-frequency files
            var highFreqFile = data.Files.OrderByDescending(f => f.TotalMatches).First();
            if (highFreqFile.TotalMatches > 3)
            {
                actions.Add(new AIAction
                {
                    Action = "explore_file",
                    Description = $"Examine {Path.GetFileName(highFreqFile.FilePath)} ({highFreqFile.TotalMatches} matches)",
                    Priority = 70
                });
            }
        }
        
        // Suggest refinement for large result sets
        if (data.TotalLineMatches > 50)
        {
            actions.Add(new AIAction
            {
                Action = "refine_search",
                Description = "Add file pattern filter to narrow results",
                Priority = 60
            });
        }
        
        return actions;
    }
    
    private List<LineSearchFileResult> ReduceLineSearchResults(List<LineSearchFileResult> files, int tokenBudget, string responseMode)
    {
        if (files.Count == 0)
            return files;
        
        // Calculate mode-specific limits
        var (maxFiles, maxLinesPerFile) = responseMode switch
        {
            "summary" => (3, 3),
            "full" => (10, 10),
            _ => (5, 5) // default
        };
        
        // Estimate tokens per file result
        var sampleFile = files.First();
        var tokensPerFile = EstimateFileTokens(sampleFile, responseMode);
        
        // Calculate how many files we can include
        var budgetBasedMaxFiles = Math.Max(1, tokenBudget / tokensPerFile);
        maxFiles = Math.Min(maxFiles, budgetBasedMaxFiles);
        
        // Take top files by match count
        var reducedFiles = files
            .OrderByDescending(f => f.TotalMatches)
            .Take(maxFiles)
            .Select(f => ReduceFileResult(f, maxLinesPerFile))
            .ToList();
        
        return CleanupFileResults(reducedFiles);
    }
    
    private LineSearchFileResult ReduceFileResult(LineSearchFileResult file, int maxLines)
    {
        // Take first N matches (they're usually most relevant)
        var reducedMatches = file.Matches.Take(maxLines).ToList();
        
        return new LineSearchFileResult
        {
            FilePath = ShortenPath(file.FilePath),
            Matches = reducedMatches,
            TotalMatches = file.TotalMatches, // Keep original count
            LastModified = file.LastModified,
            FileSize = file.FileSize
        };
    }
    
    private List<LineSearchFileResult> CleanupFileResults(List<LineSearchFileResult> files)
    {
        return files.Select(file => new LineSearchFileResult
        {
            FilePath = file.FilePath,
            Matches = file.Matches.Select(match => new LineMatch
            {
                LineNumber = match.LineNumber,
                LineContent = match.LineContent,
                ContextLines = match.ContextLines?.Length > 0 ? match.ContextLines : null,
                StartLine = match.StartLine,
                EndLine = match.EndLine,
                HighlightedFragments = ShouldIncludeHighlights(match) ? match.HighlightedFragments : null
            }).ToList(),
            TotalMatches = file.TotalMatches,
            LastModified = file.LastModified,
            FileSize = file.FileSize
        }).ToList();
    }
    
    private bool ShouldIncludeHighlights(LineMatch match)
    {
        // Skip highlights if they're identical to the full line content
        if (match.HighlightedFragments?.Count == 1 && 
            match.HighlightedFragments[0] == match.LineContent)
        {
            return false;
        }
        
        return match.HighlightedFragments?.Count > 0;
    }
    
    private string ShortenPath(string fullPath)
    {
        // Convert to relative path or use just filename + parent directory
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
    
    private int EstimateFileTokens(LineSearchFileResult file, string responseMode)
    {
        var tokens = TokenEstimator.EstimateString(file.FilePath);
        
        foreach (var match in file.Matches.Take(5)) // Sample first 5 matches
        {
            tokens += TokenEstimator.EstimateString(match.LineContent);
            
            if (responseMode == "full" && match.ContextLines != null)
            {
                foreach (var contextLine in match.ContextLines)
                {
                    tokens += TokenEstimator.EstimateString(contextLine);
                }
            }
        }
        
        // Add overhead for JSON structure
        tokens += 50; // Base structure overhead
        
        return Math.Max(tokens, 100); // Minimum estimate
    }
    
    private string BuildSummary(LineSearchResult data, int includedFilesCount, string responseMode)
    {
        if (data.TotalLineMatches == 0)
            return "No matches found";
        
        // Ultra-lean summary
        var summary = $"{data.TotalLineMatches} lines in {data.TotalFilesWithMatches} files";
        
        if (includedFilesCount < data.TotalFilesWithMatches)
            summary += $" (top {includedFilesCount})";
        
        // Skip timing in summary mode to save tokens
        if (responseMode != "summary" && data.SearchTime.TotalMilliseconds > 100)
            summary += $" ({(int)data.SearchTime.TotalMilliseconds}ms)";
        
        return summary;
    }
}