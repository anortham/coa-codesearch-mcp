using COA.Mcp.Framework.TokenOptimization.ResponseBuilders;
using COA.Mcp.Framework.TokenOptimization.Models;
using COA.Mcp.Framework.TokenOptimization;
using COA.Mcp.Framework.TokenOptimization.Storage;
using COA.Mcp.Framework.TokenOptimization.Reduction;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services.Lucene;
using COA.CodeSearch.McpServer.Tools;
using COA.Mcp.Framework.Models;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.ResponseBuilders;

/// <summary>
/// Response builder for find references operations with type-aware optimization.
/// Preserves type_info field for semantic reference classification.
/// </summary>
public class FindReferencesResponseBuilder : BaseResponseBuilder<SearchResult, AIOptimizedResponse<SearchResult>>
{
    private readonly IResourceStorageService? _storageService;
    
    public FindReferencesResponseBuilder(
        ILogger<FindReferencesResponseBuilder>? logger = null,
        IResourceStorageService? storageService = null)
        : base(logger)
    {
        _storageService = storageService;
    }
    
    public override async Task<AIOptimizedResponse<SearchResult>> BuildResponseAsync(SearchResult data, ResponseContext context)
    {
        var startTime = DateTime.UtcNow;
        var tokenBudget = CalculateTokenBudget(context);
        
        _logger?.LogDebug("Building find references response with token budget: {Budget}, Mode: {Mode}", 
            tokenBudget, context.ResponseMode);
        // Adaptive token budget allocation based on result count and mode
        var (dataBudget, insightsBudget, actionsBudget) = CalculateAdaptiveBudget(tokenBudget, data.TotalHits, context.ResponseMode);
        
        _logger?.LogDebug("Adaptive token optimization: Data={DataBudget}, Insights={InsightsBudget}, Actions={ActionsBudget}, Total={TotalBudget}, ResultCount={ResultCount}", 
            dataBudget, insightsBudget, actionsBudget, tokenBudget, data.TotalHits);
        
        // Reduce search hits to fit budget - preserve type info for classification
        var reducedHits = ReduceSearchHitsPreservingTypeInfo(data.Hits, dataBudget, context.ResponseMode);
        var wasTruncated = reducedHits.Count < data.Hits.Count;
        
        // Store full results if truncated and storage is available
        string? resourceUri = null;
        if (wasTruncated && context.StoreFullResults && _storageService != null)
        {
            try
            {
                var storageUri = await _storageService.StoreAsync(
                    data.Hits,
                    new ResourceStorageOptions
                    {
                        Expiration = TimeSpan.FromHours(24),
                        Compress = true,
                        Category = "find-references-results",
                        Metadata = new Dictionary<string, string>
                        {
                            ["query"] = data.Query ?? "",
                            ["totalHits"] = data.TotalHits.ToString(),
                            ["tool"] = context.ToolName ?? "find_references"
                        }
                    });
                resourceUri = storageUri.ToString();
                _logger?.LogDebug("Stored full results at: {Uri}", resourceUri);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to store full search results");
            }
        }
        
        // Generate insights and actions
        var insights = GenerateInsights(data, context.ResponseMode);
        var actions = GenerateActions(data, actionsBudget);
        
        // Build the response
        // Create a copy of the data for AI response with reduced hits
        var aiData = new SearchResult
        {
            TotalHits = data.TotalHits,
            Hits = reducedHits,
            SearchTime = data.SearchTime,
            Query = data.Query
            // ProcessingTimeMs is a computed property, no need to set it
        };
        
        var response = new AIOptimizedResponse<SearchResult>
        {
            Success = true,
            Data = new AIResponseData<SearchResult>
            {
                Summary = BuildSummary(data, reducedHits.Count, context.ResponseMode),
                Results = aiData, // Pass the copy with reduced hits for AI
                Count = data.TotalHits,
                ExtensionData = new Dictionary<string, object>
                {
                    ["totalHits"] = data.TotalHits,
                    ["query"] = data.Query ?? "",
                    ["processingTime"] = data.ProcessingTimeMs
                }
            },
            Insights = ReduceInsights(insights, insightsBudget),
            Actions = ReduceActions(actions, actionsBudget),
            Meta = CreateMetadata(startTime, wasTruncated, resourceUri)
        };
        
        // Update token estimate
        response.Meta.TokenInfo!.Estimated = TokenEstimator.EstimateObject(response);
        
        _logger?.LogInformation("Built find references response: {Hits} of {Total} hits, {Insights} insights, {Actions} actions, {Tokens} tokens (Budget: {Budget}, Truncated: {Truncated}, Stored: {HasResource})",
            reducedHits.Count, data.TotalHits, response.Insights.Count, response.Actions.Count, 
            response.Meta.TokenInfo.Estimated, tokenBudget, wasTruncated, resourceUri != null);
        
        return response;
    }
    
    /// <summary>
    /// Override to use TokenLimit from context instead of default budget
    /// </summary>
    protected override int CalculateTokenBudget(ResponseContext context)
    {
        return context.TokenLimit ?? 8000; // Use TokenLimit from context or default to 8000
    }
    /// <summary>
    /// Calculates adaptive token budget allocation based on result count and response mode.
    /// Optimizes allocation for find references scenarios.
    /// </summary>
    private (int dataBudget, int insightsBudget, int actionsBudget) CalculateAdaptiveBudget(int totalBudget, int resultCount, string responseMode)
    {
        // Base allocation percentages
        double dataPercent = 0.70;
        double insightsPercent = 0.15;
        double actionsPercent = 0.15;
        
        // Adjust based on result count
        if (resultCount == 0)
        {
            // No results: more budget for insights and actions to guide user
            dataPercent = 0.30;
            insightsPercent = 0.35;
            actionsPercent = 0.35;
        }
        else if (resultCount == 1)
        {
            // Single result: less need for insights, more for data
            dataPercent = 0.80;
            insightsPercent = 0.10;
            actionsPercent = 0.10;
        }
        else if (resultCount <= 5)
        {
            // Few results: balanced approach
            dataPercent = 0.75;
            insightsPercent = 0.12;
            actionsPercent = 0.13;
        }
        else if (resultCount <= 20)
        {
            // Moderate results: standard allocation
            // Keep defaults
        }
        else if (resultCount <= 100)
        {
            // Many results: more insights for classification, less actions
            dataPercent = 0.68;
            insightsPercent = 0.20;
            actionsPercent = 0.12;
        }
        else
        {
            // Very many results: heavy on insights for analysis
            dataPercent = 0.65;
            insightsPercent = 0.25;
            actionsPercent = 0.10;
        }
        
        // Adjust for response mode
        if (responseMode == "summary")
        {
            // Summary mode: reduce data slightly, increase insights
            dataPercent -= 0.05;
            insightsPercent += 0.05;
        }
        
        return (
            dataBudget: (int)(totalBudget * dataPercent),
            insightsBudget: (int)(totalBudget * insightsPercent),
            actionsBudget: (int)(totalBudget * actionsPercent)
        );
    }
    
    
    protected override List<string> GenerateInsights(SearchResult data, string responseMode)
    {
        var insights = new List<string>();
        
        if (data.TotalHits == 0)
        {
            insights.Add("No references found");
            return insights;
        }
        
        // Reference type analysis
        if (data.Hits.Count > 0 && responseMode != "summary")
        {
            var referenceTypes = data.Hits
                .Where(h => h.Fields?.ContainsKey("referenceType") == true)
                .Select(h => h.Fields["referenceType"])
                .Where(rt => !string.IsNullOrEmpty(rt))
                .GroupBy(rt => rt)
                .OrderByDescending(g => g.Count())
                .Take(3)
                .ToList();
            
            if (referenceTypes.Any())
            {
                var typesSummary = string.Join(", ", referenceTypes.Select(g => $"{g.Count()} {g.Key}"));
                insights.Add($"Reference types: {typesSummary}");
            }
            
            // File distribution
            var fileCount = data.Hits.Select(h => h.FilePath).Distinct().Count();
            if (fileCount > 1)
                insights.Add($"References found across {fileCount} files");
        }
        
        // High reference count warning
        if (data.TotalHits > 100)
        {
            insights.Add($"High reference count ({data.TotalHits}). Consider narrowing search before refactoring.");
        }
        
        return insights;
    }
    
    protected override List<COA.Mcp.Framework.Models.AIAction> GenerateActions(SearchResult data, int tokenBudget)
    {
        var actions = new List<COA.Mcp.Framework.Models.AIAction>();
        
        if (data.TotalHits == 0)
        {
            actions.Add(new AIAction
            {
                Action = "symbol_search",
                Description = "Search for symbol definitions instead",
                Priority = 100
            });
        }
        else
        {
            // Reference refactoring actions
            if (data.TotalHits <= 50) // Safe refactoring threshold
            {
                actions.Add(new AIAction
                {
                    Action = "safe_refactor",
                    Description = $"Refactor all {data.TotalHits} references safely",
                    Priority = 90
                });
            }
            else
            {
                actions.Add(new AIAction
                {
                    Action = "careful_refactor",
                    Description = $"Review {data.TotalHits} references before refactoring",
                    Priority = 80
                });
            }
            
            // Symbol definition lookup
            if (!string.IsNullOrEmpty(data.Query))
            {
                actions.Add(new AIAction
                {
                    Action = "goto_definition",
                    Description = $"View definition of '{data.Query}'",
                    Priority = 70
                });
            }
            
            // File grouping for large result sets
            if (data.TotalHits > 20)
            {
                actions.Add(new AIAction
                {
                    Action = "group_by_file",
                    Description = "Group references by file for easier review",
                    Priority = 60
                });
            }
        }
        
        return actions;
    }
    
    /// <summary>
    /// Reduces search hits while preserving type_info and other fields needed for reference classification
    /// </summary>
    private List<SearchHit> ReduceSearchHitsPreservingTypeInfo(List<SearchHit> hits, int tokenBudget, string responseMode)
    {
        if (hits.Count == 0)
            return hits;
        
        // Estimate tokens per hit
        var sampleHit = hits.First();
        var tokensPerHit = EstimateHitTokensWithTypeInfo(sampleHit, responseMode);
        
        // Calculate how many hits we can include
        var maxHits = Math.Max(1, tokenBudget / tokensPerHit);
        
            if (hits.Count <= maxHits)
                return CleanupHitsPreservingTypeInfo(hits); // Clean up even if we're not reducing
            
            // Use progressive reduction with score-based priority
            var context = new ReductionContext
            {
                PriorityFunction = obj => obj is SearchHit hit ? hit.Score : 0
            };
            
            var result = _reductionEngine.Reduce(
                hits,
                hit => EstimateHitTokensWithTypeInfo(hit, responseMode),
                tokenBudget,
                "priority",
                context);
            
            return CleanupHitsPreservingTypeInfo(result.Items);
        }
        
        /// <summary>
        /// Actually caps type_info content to reduce tokens
        /// </summary>
        private string CapTypeInfoContent(string typeInfoJson)
        {
            if (string.IsNullOrEmpty(typeInfoJson))
                return typeInfoJson;
            
            var baseEstimate = TokenEstimator.EstimateString(typeInfoJson);
            
            // If already small enough, return as-is
            if (baseEstimate <= 60)
                return typeInfoJson;
            
            // Try to parse and truncate JSON content intelligently
            try
            {
                // For large type info, create a summarized version
                if (typeInfoJson.StartsWith("{") && typeInfoJson.Contains("\"methods\""))
                {
                    // Extract just the essential info: type, name, and first few methods/properties
                    var lines = typeInfoJson.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    var essential = lines.Take(3).ToList(); // Type, name, and maybe namespace
                    essential.Add("\"...truncated for size\"");
                    essential.Add("}");
                    return string.Join("\n", essential);
                }
                
                // Fallback: truncate to reasonable length while keeping valid JSON structure
                var truncated = typeInfoJson.Length > 200 ? typeInfoJson.Substring(0, 197) + "...}" : typeInfoJson;
                return truncated;
            }
            catch
            {
                // If parsing fails, just truncate the string
                return typeInfoJson.Length > 200 ? typeInfoJson.Substring(0, 197) + "..." : typeInfoJson;
            }
        }
    /// <summary>
    /// Cleans up hits while preserving fields needed for type-aware reference classification
    /// </summary>
    private List<SearchHit> CleanupHitsPreservingTypeInfo(List<SearchHit> hits)
    {
        return hits.Select(hit => {
            // Preserve fields needed for reference classification
            var preservedFields = new Dictionary<string, string>();
            
            // Essential fields for navigation
            if (hit.Fields.ContainsKey("size"))
                preservedFields["size"] = hit.Fields["size"];
            
            // Type-aware fields for reference classification
            if (hit.Fields.ContainsKey("type_info"))
                preservedFields["type_info"] = CapTypeInfoContent(hit.Fields["type_info"]); // Actually cap the content
            
            if (hit.Fields.ContainsKey("language"))
                preservedFields["language"] = hit.Fields["language"];
            
            if (hit.Fields.ContainsKey("referenceType"))
                preservedFields["referenceType"] = hit.Fields["referenceType"];
            
            // Round score to 2 decimal places
            hit.Score = (float)Math.Round(hit.Score, 2);
            
            // Clear empty collections
            if (hit.HighlightedFragments?.Count == 0)
                hit.HighlightedFragments = null;
            
            // Return clean hit structure with preserved type information
            return new SearchHit
            {
                FilePath = hit.FilePath,
                Score = hit.Score,
                Fields = preservedFields, // Preserve type-aware fields
                HighlightedFragments = hit.HighlightedFragments,
                LastModified = hit.LastModified,
                LineNumber = hit.LineNumber, // PRESERVE: Essential for VS Code navigation
                ContextLines = hit.ContextLines, // PRESERVE: Context for AI analysis
                StartLine = hit.StartLine, // PRESERVE: Context bounds
                EndLine = hit.EndLine, // PRESERVE: Context bounds
                Snippet = hit.Snippet // PRESERVE: Original snippet for context
            };
        }).ToList();
    }
    
    private int EstimateHitTokensWithTypeInfo(SearchHit hit, string responseMode)
    {
        var tokens = TokenEstimator.EstimateString(hit.FilePath);
        
        if (responseMode == "full")
        {
            // In full mode, include snippet and fields (including type_info)
            tokens += TokenEstimator.EstimateString(hit.Snippet ?? "");
            tokens += EstimateFieldTokensWithTypeInfo(hit.Fields);
            tokens += 20; // Metadata overhead
        }
        else
        {
            // Summary mode - minimal tokens
            tokens += 15; // Basic metadata
            if (hit.Fields?.ContainsKey("referenceType") == true)
                tokens += 10; // Reference type classification
        }
        
        if (hit.ContextLines?.Any() == true)
            tokens += hit.ContextLines.Sum(line => TokenEstimator.EstimateString(line) / 2); // Context has less weight
        
        return tokens;
    }
    
    /// <summary>
    /// Optimized field token estimation with smart type_info handling
    /// </summary>
    private int EstimateFieldTokensWithTypeInfo(Dictionary<string, string>? fields)
    {
        if (fields == null || !fields.Any())
            return 0;
        
        var tokens = 0;
        foreach (var field in fields)
        {
            // Smart type_info handling - compress large type info
            if (field.Key == "type_info")
            {
                tokens += EstimateTypeInfoTokens(field.Value);
            }
            else
            {
                tokens += TokenEstimator.EstimateString(field.Value);
            }
        }
        
        return tokens;
    }
    
    /// <summary>
    /// Intelligently estimates tokens for type_info field with compression strategies
    /// </summary>
    private int EstimateTypeInfoTokens(string typeInfoJson)
    {
        if (string.IsNullOrEmpty(typeInfoJson))
            return 0;
        
        var baseEstimate = TokenEstimator.EstimateString(typeInfoJson);
        
        // Cap type_info tokens based on size - larger info gets more aggressive capping
        if (baseEstimate > 100)
            return Math.Min(baseEstimate, 40); // Large type_info: cap at 40 tokens
        else if (baseEstimate > 60)
            return Math.Min(baseEstimate, 50); // Medium type_info: cap at 50 tokens
        else
            return Math.Min(baseEstimate, 60); // Small type_info: cap at 60 tokens
    }
    
    private string BuildSummary(SearchResult data, int includedCount, string responseMode)
    {
        var query = !string.IsNullOrEmpty(data.Query) ? data.Query : "symbol";
        
        if (data.TotalHits == 0)
            return $"No references found for '{query}'";
        
        if (data.TotalHits == 1)
            return $"Found 1 reference to '{query}'";
        
        if (includedCount < data.TotalHits)
            return $"Found {data.TotalHits} references to '{query}' (showing {includedCount})";
        
        return $"Found {data.TotalHits} references to '{query}'";
    }
}

