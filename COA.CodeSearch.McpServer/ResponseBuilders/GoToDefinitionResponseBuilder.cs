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
/// Response builder for go-to-definition operations with token-aware optimization.
/// </summary>
public class GoToDefinitionResponseBuilder : BaseResponseBuilder<SymbolDefinition?, AIOptimizedResponse<SymbolDefinition>>
{
    private readonly IResourceStorageService? _storageService;
    
    public GoToDefinitionResponseBuilder(
        ILogger<GoToDefinitionResponseBuilder>? logger = null,
        IResourceStorageService? storageService = null)
        : base(logger)
    {
        _storageService = storageService;
    }
    
    public override async Task<AIOptimizedResponse<SymbolDefinition>> BuildResponseAsync(SymbolDefinition? data, ResponseContext context)
    {
        var startTime = DateTime.UtcNow;
        var tokenBudget = CalculateTokenBudget(context);
        
        _logger?.LogDebug("Building go-to-definition response with token budget: {Budget}, Mode: {Mode}", 
            tokenBudget, context.ResponseMode);
        
        // For go-to-definition, we typically have a single result or none
        if (data == null)
        {
            return BuildNoDefinitionResponse(context);
        }
        
        // Allocate token budget
        var dataBudget = (int)(tokenBudget * 0.75);  // 75% for definition data
        var insightsBudget = (int)(tokenBudget * 0.15); // 15% for insights
        var actionsBudget = (int)(tokenBudget * 0.10);  // 10% for actions
        
        // Truncate snippet if needed to fit budget
        var processedDefinition = ProcessDefinition(data, dataBudget, context.ResponseMode);
        
        // Generate insights and actions
        var insights = GenerateInsights(data, context.ResponseMode);
        var actions = GenerateActions(data, actionsBudget);
        
        // Build the response
        var response = new AIOptimizedResponse<SymbolDefinition>
        {
            Success = true,
            Data = new AIResponseData<SymbolDefinition>
            {
                Summary = BuildSummary(data),
                Results = processedDefinition,
                Count = 1,
                ExtensionData = new Dictionary<string, object>
                {
                    ["totalHits"] = 1,
                    ["query"] = data.Name,
                    ["location"] = $"{data.FilePath}:{data.Line}:{data.Column}"
                }
            },
            Insights = insights,
            Actions = actions,
            ResponseMeta = new AIResponseMeta
            {
                ExecutionTime = $"{(DateTime.UtcNow - startTime).TotalMilliseconds:F0}ms",
                Truncated = false,
                TokenInfo = new TokenInfo
                {
                    Estimated = EstimateDefinitionTokens(processedDefinition),
                    Limit = context.TokenLimit ?? 8000,
                    ReductionStrategy = null
                }
            }
        };
        
        return response;
    }
    
    protected override List<string> GenerateInsights(SymbolDefinition? data, string responseMode)
    {
        var insights = new List<string>();
        
        if (data == null)
        {
            insights.Add("Symbol not found in indexed type definitions");
            insights.Add("Try using symbol_search for broader search");
            insights.Add("Ensure the workspace is fully indexed");
        }
        else
        {
            insights.Add($"Definition location: {data.FilePath}:{data.Line}:{data.Column}");
            insights.Add($"Symbol type: {data.Kind}");
            
            if (!string.IsNullOrEmpty(data.Language))
                insights.Add($"Language: {data.Language}");
            
            if (!string.IsNullOrEmpty(data.BaseType))
                insights.Add($"Inherits from: {data.BaseType}");
            
            if (data.Interfaces?.Any() == true)
                insights.Add($"Implements: {string.Join(", ", data.Interfaces)}");
            
            if (data.Modifiers?.Any() == true)
                insights.Add($"Modifiers: {string.Join(" ", data.Modifiers)}");
            
            if (!string.IsNullOrEmpty(data.ReturnType))
                insights.Add($"Returns: {data.ReturnType}");
            
            if (data.Parameters?.Any() == true)
                insights.Add($"Parameters: {data.Parameters.Count}");
        }
        
        return insights;
    }
    
    protected override List<AIAction> GenerateActions(SymbolDefinition? data, int tokenBudget)
    {
        var actions = new List<AIAction>();
        
        if (data != null)
        {
            actions.Add(new AIAction
            {
                Action = "find_references",
                Description = $"Find all references to '{data.Name}'",
                Priority = 100
            });
            
            actions.Add(new AIAction
            {
                Action = "symbol_search",
                Description = $"Search for related symbols",
                Priority = 80
            });
            
            if (!string.IsNullOrEmpty(data.BaseType))
            {
                actions.Add(new AIAction
                {
                    Action = "goto_definition",
                    Description = $"Go to definition of base type '{data.BaseType}'",
                    Priority = 70
                });
            }
            
            if (data.Interfaces?.Any() == true)
            {
                actions.Add(new AIAction
                {
                    Action = "goto_definition",
                    Description = $"Go to interface definitions",
                    Priority = 60
                });
            }
        }
        else
        {
            actions.Add(new AIAction
            {
                Action = "symbol_search",
                Description = "Try broader symbol search",
                Priority = 100
            });
            
            actions.Add(new AIAction
            {
                Action = "text_search",
                Description = "Search in file content",
                Priority = 90
            });
            
            actions.Add(new AIAction
            {
                Action = "index_workspace",
                Description = "Re-index the workspace",
                Priority = 50
            });
        }
        
        return actions;
    }
    
    private AIOptimizedResponse<SymbolDefinition> BuildNoDefinitionResponse(ResponseContext context)
    {
        // Extract the symbol name from the context's CustomMetadata
        var query = context.CustomMetadata?.ContainsKey("symbolName") == true 
            ? context.CustomMetadata["symbolName"]?.ToString() ?? "unknown symbol"
            : "unknown symbol";
            
        return new AIOptimizedResponse<SymbolDefinition>
        {
            Success = true,
            Data = new AIResponseData<SymbolDefinition>
            {
                Summary = $"No definition found for '{query}'",
                Results = null,
                Count = 0,
                ExtensionData = new Dictionary<string, object>
                {
                    ["totalHits"] = 0,
                    ["query"] = query ?? "",
                    ["processingTime"] = 0
                }
            },
            Insights = new List<string>
            {
                "Symbol not found in indexed type definitions",
                "Try using symbol_search for broader search",
                "Ensure the workspace is fully indexed"
            },
            Actions = new List<AIAction>
            {
                new AIAction
                {
                    Action = "symbol_search",
                    Description = $"Try broader symbol search for '{query}'",
                    Priority = 100
                },
                new AIAction
                {
                    Action = "text_search",
                    Description = $"Search in file content for '{query}'",
                    Priority = 90
                }
            },
            ResponseMeta = new AIResponseMeta
            {
                ExecutionTime = "0ms",
                Truncated = false
            }
        };
    }
    
    private SymbolDefinition ProcessDefinition(SymbolDefinition definition, int tokenBudget, string responseMode)
    {
        // Clone the definition to avoid modifying the original
        var processed = new SymbolDefinition
        {
            Name = definition.Name,
            Kind = definition.Kind,
            Signature = definition.Signature,
            FilePath = definition.FilePath,
            Line = definition.Line,
            Column = definition.Column,
            Language = definition.Language,
            Modifiers = definition.Modifiers,
            BaseType = definition.BaseType,
            Interfaces = definition.Interfaces,
            ContainingType = definition.ContainingType,
            ReturnType = definition.ReturnType,
            Parameters = definition.Parameters,
            ReferenceCount = definition.ReferenceCount,
            Score = definition.Score,
            Snippet = definition.Snippet
        };
        
        // In summary mode, remove the snippet to save tokens
        if (responseMode == "summary")
        {
            processed.Snippet = null;
        }
        // Truncate snippet if it's too large
        else if (!string.IsNullOrEmpty(processed.Snippet))
        {
            var snippetTokens = TokenEstimator.EstimateString(processed.Snippet);
            if (snippetTokens > tokenBudget * 0.5) // Don't let snippet take more than half the budget
            {
                var lines = processed.Snippet.Split('\n');
                if (lines.Length > 10)
                {
                    processed.Snippet = string.Join('\n', lines.Take(10)) + "\n... (truncated)";
                }
            }
        }
        
        return processed;
    }
    
    private int EstimateDefinitionTokens(SymbolDefinition? definition)
    {
        if (definition == null)
            return 50;
        
        // Base tokens for essential fields
        var tokens = 100; // name, kind, file, line, column, etc.
        
        // Additional tokens for optional fields
        if (!string.IsNullOrEmpty(definition.Signature))
            tokens += TokenEstimator.EstimateString(definition.Signature);
        
        if (!string.IsNullOrEmpty(definition.Snippet))
            tokens += TokenEstimator.EstimateString(definition.Snippet);
        
        if (definition.Modifiers?.Any() == true)
            tokens += definition.Modifiers.Count * 5;
        
        if (definition.Parameters?.Any() == true)
            tokens += definition.Parameters.Count * 10;
        
        if (definition.Interfaces?.Any() == true)
            tokens += definition.Interfaces.Count * 10;
        
        return tokens;
    }
    
    private string BuildSummary(SymbolDefinition definition)
    {
        return $"Found {definition.Kind} '{definition.Name}' at {Path.GetFileName(definition.FilePath)}:{definition.Line}";
    }
}