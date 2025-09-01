using COA.Mcp.Framework.TokenOptimization.ResponseBuilders;
using COA.Mcp.Framework.TokenOptimization.Models;
using COA.Mcp.Framework.TokenOptimization;
using COA.Mcp.Framework.TokenOptimization.Storage;
using COA.Mcp.Framework.TokenOptimization.Reduction;
using COA.CodeSearch.McpServer.Tools.Models;
using COA.Mcp.Framework.Models;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.ResponseBuilders;

/// <summary>
/// Response builder for symbol search operations with token-aware optimization.
/// </summary>
public class SymbolSearchResponseBuilder : BaseResponseBuilder<SymbolSearchResult, AIOptimizedResponse<SymbolSearchResult>>
{
    private readonly IResourceStorageService? _storageService;
    
    public SymbolSearchResponseBuilder(
        ILogger<SymbolSearchResponseBuilder>? logger = null,
        IResourceStorageService? storageService = null)
        : base(logger)
    {
        _storageService = storageService;
    }
    
    public override async Task<AIOptimizedResponse<SymbolSearchResult>> BuildResponseAsync(SymbolSearchResult data, ResponseContext context)
    {
        var startTime = DateTime.UtcNow;
        var tokenBudget = CalculateTokenBudget(context);
        
        _logger?.LogDebug("Building symbol search response with token budget: {Budget}, Mode: {Mode}", 
            tokenBudget, context.ResponseMode);
        
        // Allocate token budget
        var dataBudget = (int)(tokenBudget * 0.7);  // 70% for symbol data
        var insightsBudget = (int)(tokenBudget * 0.15); // 15% for insights
        var actionsBudget = (int)(tokenBudget * 0.15);  // 15% for actions
        
        // Reduce symbols to fit budget
        var reducedSymbols = ReduceSymbols(data.Symbols, dataBudget, context.ResponseMode);
        var wasTruncated = reducedSymbols.Count < data.Symbols.Count;
        
        // Store full results if truncated
        string? resourceUri = null;
        if (wasTruncated && context.StoreFullResults && _storageService != null)
        {
            try
            {
                var storageUri = await _storageService.StoreAsync(
                    data.Symbols,
                    new ResourceStorageOptions
                    {
                        Expiration = TimeSpan.FromHours(1),
                        Compress = true,
                        Category = "symbol-search-results",
                        Metadata = new Dictionary<string, string>
                        {
                            ["query"] = data.Query ?? "",
                            ["totalSymbols"] = data.TotalCount.ToString(),
                            ["tool"] = context.ToolName ?? "symbol_search"
                        }
                    });
                resourceUri = storageUri.ToString();
                _logger?.LogDebug("Stored full symbol list at: {Uri}", resourceUri);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to store full symbol list");
            }
        }
        
        // Generate insights and actions
        var insights = GenerateInsights(data, context.ResponseMode);
        var actions = GenerateActions(data, actionsBudget);
        
        // Build the response
        var response = new AIOptimizedResponse<SymbolSearchResult>
        {
            Success = true,
            Data = new AIResponseData<SymbolSearchResult>
            {
                Summary = BuildSummary(data, reducedSymbols.Count, context.ResponseMode),
                Results = new SymbolSearchResult
                {
                    Symbols = reducedSymbols,
                    TotalCount = data.TotalCount,
                    SearchTime = data.SearchTime,
                    Query = data.Query ?? ""
                },
                Count = reducedSymbols.Count,
                ExtensionData = new Dictionary<string, object>
                {
                    ["totalHits"] = data.TotalCount,
                    ["query"] = data.Query ?? "",
                    ["processingTime"] = (int)data.SearchTime.TotalMilliseconds
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
                    Estimated = EstimateTokens(reducedSymbols),
                    Limit = context.TokenLimit ?? 8000,
                    ReductionStrategy = wasTruncated ? "progressive" : null
                }
            }
        };
        
        return response;
    }
    
    protected override List<string> GenerateInsights(SymbolSearchResult data, string responseMode)
    {
        var insights = new List<string>();
        
        if (data.Symbols.Count == 0)
        {
            insights.Add("No exact matches found - try broader search with text_search");
        }
        else
        {
            insights.Add($"Found {data.TotalCount} symbols matching '{data.Query}'");
            
            // Language distribution
            var languages = data.Symbols.Where(s => s.Language != null).Select(s => s.Language!).Distinct();
            if (languages.Any())
                insights.Add($"Languages: {string.Join(", ", languages)}");
            
            // Type distribution
            var typeGroups = data.Symbols.GroupBy(s => s.Kind);
            if (typeGroups.Count() > 1)
            {
                var summary = string.Join(", ", typeGroups.Select(g => $"{g.Count()} {g.Key}(s)"));
                insights.Add($"Symbol types: {summary}");
            }
            
            // Inheritance insights
            var hasInheritance = data.Symbols.Any(s => !string.IsNullOrEmpty(s.BaseType));
            if (hasInheritance)
                insights.Add("Some types have inheritance relationships");
            
            var hasInterfaces = data.Symbols.Any(s => s.Interfaces?.Any() == true);
            if (hasInterfaces)
                insights.Add("Some types implement interfaces");
        }
        
        return insights;
    }
    
    protected override List<AIAction> GenerateActions(SymbolSearchResult data, int tokenBudget)
    {
        var actions = new List<AIAction>();
        
        if (data.Symbols.Count > 0)
        {
            var firstSymbol = data.Symbols.First();
            
            actions.Add(new AIAction
            {
                Action = "find_references",
                Description = $"Find all usages of '{firstSymbol.Name}'",
                Priority = 80
            });
            
            if (data.Symbols.Any(s => !string.IsNullOrEmpty(s.BaseType)))
            {
                actions.Add(new AIAction
                {
                    Action = "type_hierarchy",
                    Description = "View complete inheritance hierarchy",
                    Priority = 60
                });
            }
            
            actions.Add(new AIAction
            {
                Action = "goto_definition",
                Description = $"Jump to definition of '{firstSymbol.Name}'",
                Priority = 70
            });
        }
        else
        {
            actions.Add(new AIAction
            {
                Action = "text_search",
                Description = $"Try broader text search for '{data.Query}'",
                Priority = 100
            });
            
            actions.Add(new AIAction
            {
                Action = "file_search",
                Description = "Search for files containing the symbol name",
                Priority = 80
            });
        }
        
        return actions;
    }
    
    private List<SymbolDefinition> ReduceSymbols(List<SymbolDefinition> symbols, int tokenBudget, string responseMode)
    {
        if (!symbols.Any())
            return symbols;
        
        var reduced = new List<SymbolDefinition>();
        var estimatedTokens = 0;
        
        // Prioritize exact matches and higher scores
        var prioritized = symbols
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.Name.Length);
        
        foreach (var symbol in prioritized)
        {
            var symbolTokens = EstimateSymbolTokens(symbol, responseMode);
            
            if (estimatedTokens + symbolTokens > tokenBudget && reduced.Count > 0)
            {
                _logger?.LogDebug("Truncating symbols at {Count} to fit token budget", reduced.Count);
                break;
            }
            
            reduced.Add(symbol);
            estimatedTokens += symbolTokens;
        }
        
        return reduced;
    }
    
    private int EstimateSymbolTokens(SymbolDefinition symbol, string responseMode)
    {
        // Base tokens for essential fields
        var tokens = 50; // name, kind, file, line, column
        
        // Additional tokens for optional fields
        if (!string.IsNullOrEmpty(symbol.Signature))
            tokens += TokenEstimator.EstimateString(symbol.Signature);
        
        if (!string.IsNullOrEmpty(symbol.Snippet) && responseMode != "summary")
            tokens += TokenEstimator.EstimateString(symbol.Snippet);
        
        if (symbol.Modifiers?.Any() == true)
            tokens += symbol.Modifiers.Count * 5;
        
        if (symbol.Parameters?.Any() == true)
            tokens += symbol.Parameters.Count * 10;
        
        return tokens;
    }
    
    private int EstimateTokens(List<SymbolDefinition> symbols)
    {
        return symbols.Sum(s => EstimateSymbolTokens(s, "full"));
    }
    
    private string BuildSummary(SymbolSearchResult data, int includedCount, string responseMode)
    {
        if (data.Symbols.Count == 0)
            return $"No symbols found matching '{data.Query}'";
        
        if (data.Symbols.Count == 1)
        {
            var symbol = data.Symbols[0];
            return $"Found {symbol.Kind} '{symbol.Name}' at {Path.GetFileName(symbol.FilePath)}:{symbol.Line}";
        }
        
        if (includedCount < data.TotalCount)
        {
            return $"Found {data.TotalCount} symbols matching '{data.Query}' (showing {includedCount})";
        }
        
        return $"Found {data.TotalCount} symbols matching '{data.Query}'";
    }
}