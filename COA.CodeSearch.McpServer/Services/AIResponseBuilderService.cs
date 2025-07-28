using COA.CodeSearch.McpServer.Models;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Service for building AI-optimized responses with contextual actions and token efficiency
/// </summary>
public class AIResponseBuilderService
{
    private readonly ILogger<AIResponseBuilderService> _logger;
    private readonly ITokenEstimationService _tokenEstimator;

    // Token budgets for different response modes
    private const int SummaryTokenBudget = 1500;
    private const int FullTokenBudget = 4000;
    private const int MaxActionTokens = 300;

    public AIResponseBuilderService(
        ILogger<AIResponseBuilderService> logger,
        ITokenEstimationService tokenEstimator)
    {
        _logger = logger;
        _tokenEstimator = tokenEstimator;
    }

    /// <summary>
    /// Build AI-optimized response for memory search results (backward compatible format)
    /// </summary>
    public object BuildMemorySearchResponse(
        FlexibleMemorySearchResult searchResult,
        FlexibleMemorySearchRequest request,
        string? originalQuery = null,
        ResponseMode mode = ResponseMode.Summary)
    {
        var tokenBudget = mode == ResponseMode.Summary ? SummaryTokenBudget : FullTokenBudget;
        
        // Build structured data efficiently
        var data = BuildMemorySearchData(searchResult, request, tokenBudget);

        // Generate contextual actions
        var actions = GenerateMemorySearchActions(searchResult, request, originalQuery);

        // Create insights
        var insights = GenerateMemorySearchInsights(searchResult, request);

        // Estimate tokens
        var estimatedTokens = EstimateMemoryResponseTokens(searchResult, data, actions, insights);

        // Create backward-compatible response format that includes AI optimizations
        return new
        {
            success = true,
            operation = "search_memories",
            query = new
            {
                text = originalQuery ?? "*",
                types = request.Types,
                totalRequested = searchResult.Memories.Count
            },
            summary = new
            {
                totalFound = searchResult.TotalFound,
                returned = searchResult.Memories.Count,
                typeDistribution = data.Distribution.GetValueOrDefault("type", new Dictionary<string, int>()),
                primaryType = data.Summary.PrimaryType
            },
            facets = searchResult.FacetCounts,
            // Backward-compatible analysis section
            analysis = new
            {
                patterns = insights.Take(3).ToList(),
                hotspots = new
                {
                    byType = data.Distribution.GetValueOrDefault("type", new Dictionary<string, int>())
                        .Take(3)
                        .Select(kv => new { type = kv.Key, count = kv.Value })
                        .ToList(),
                    byFile = data.Hotspots.Take(3).ToList()
                },
                fileReferences = data.Hotspots.Take(5).ToList()
            },
            // AI-optimized additions
            data = new
            {
                items = data.Items.Take(mode == ResponseMode.Full ? 20 : 10),
                hotspots = data.Hotspots.Take(5),
                distribution = data.Distribution
            },
            insights = insights,
            actions = actions.Select(a => new
            {
                id = a.Id,
                description = a.Description,
                command = a.Command.Tool,
                parameters = a.Command.Parameters,
                priority = a.Priority.ToString().ToLowerInvariant(),
                estimatedTokens = a.EstimatedTokens
            }),
            meta = new
            {
                mode = mode.ToString().ToLowerInvariant(),
                tokenBudget = tokenBudget,
                tokens = estimatedTokens, // Backward compatibility
                estimatedTokens = estimatedTokens,
                truncated = searchResult.Memories.Count < searchResult.TotalFound,
                format = "ai-optimized",
                cached = GenerateCacheKey("memory_search")
            }
        };
    }

    /// <summary>
    /// Build AI-optimized response for file search results
    /// </summary>
    public AIOptimizedResponse BuildFileSearchResponse(
        List<FileSearchResult> fileResults,
        string query,
        string workspacePath,
        ResponseMode mode = ResponseMode.Summary)
    {
        var tokenBudget = mode == ResponseMode.Summary ? SummaryTokenBudget : FullTokenBudget;
        
        var response = new AIOptimizedResponse
        {
            Meta = new AIResponseMeta
            {
                Mode = mode.ToString().ToLowerInvariant(),
                TokenBudget = tokenBudget,
                CacheKey = GenerateCacheKey("file_search")
            }
        };

        // Build structured data
        response.Data = BuildFileSearchData(fileResults, query, tokenBudget);

        // Generate contextual actions
        response.Actions = GenerateFileSearchActions(fileResults, query, workspacePath);

        // Create insights
        response.Insights = GenerateFileSearchInsights(fileResults, query);

        // Generate markdown display
        response.DisplayMarkdown = GenerateFileSearchMarkdown(fileResults, response.Data, response.Insights);

        // Calculate and optimize tokens
        response.Meta.EstimatedTokens = EstimateResponseTokens(response);
        if (response.Meta.EstimatedTokens > tokenBudget * 1.2)
        {
            response = OptimizeForTokenBudget(response, tokenBudget);
        }

        return response;
    }

    #region Memory Search Implementation

    private AIResponseData BuildMemorySearchData(
        FlexibleMemorySearchResult searchResult, 
        FlexibleMemorySearchRequest request, 
        int tokenBudget)
    {
        var data = new AIResponseData
        {
            Summary = new ResultSummary
            {
                TotalFound = searchResult.TotalFound,
                Returned = searchResult.Memories.Count,
                Truncated = searchResult.Memories.Count < searchResult.TotalFound,
                PrimaryType = GetPrimaryMemoryType(searchResult.Memories)
            }
        };

        // Calculate token budget for items
        var remainingTokens = tokenBudget - 500; // Reserve for structure
        var tokensPerItem = Math.Max(50, remainingTokens / Math.Max(1, searchResult.Memories.Count));

        // Add memory items within token budget
        var itemTokens = 0;
        foreach (var memory in searchResult.Memories)
        {
            var item = CreateMemoryItem(memory, tokensPerItem);
            var itemTokenCost = EstimateItemTokens(item);
            
            if (itemTokens + itemTokenCost > remainingTokens * 0.7) // Leave 30% for other data
                break;
                
            data.Items.Add(item);
            itemTokens += itemTokenCost;
        }

        // Add distribution analysis
        data.Distribution = CreateMemoryDistribution(searchResult.Memories);

        // Add hotspots
        data.Hotspots = CreateMemoryHotspots(searchResult.Memories).Cast<object>().ToList();

        return data;
    }

    private List<AIAction> GenerateMemorySearchActions(
        FlexibleMemorySearchResult searchResult,
        FlexibleMemorySearchRequest request,
        string? originalQuery)
    {
        var actions = new List<AIAction>();

        // View specific memory action for first result
        if (searchResult.Memories.Any())
        {
            var firstMemory = searchResult.Memories.First();
            actions.Add(new AIAction
            {
                Id = "view_memory",
                Description = $"View details of {firstMemory.Type}: {TruncateText(firstMemory.Content, 50)}",
                Command = new AIActionCommand
                {
                    Tool = "mcp__codesearch__get_memory",
                    Parameters = new Dictionary<string, object> { { "id", firstMemory.Id } }
                },
                EstimatedTokens = 150,
                Priority = ActionPriority.High,
                Context = ActionContext.Always
            });
        }

        // Explore related memories action
        if (searchResult.Memories.Count > 1)
        {
            actions.Add(new AIAction
            {
                Id = "explore_related",
                Description = "Explore relationships between these memories",
                Command = new AIActionCommand
                {
                    Tool = "mcp__codesearch__memory_graph_navigator",
                    Parameters = new Dictionary<string, object> { { "startPoint", originalQuery ?? "current search" } }
                },
                EstimatedTokens = 200,
                Priority = ActionPriority.Medium,
                Context = ActionContext.ManyResults
            });
        }

        // Refine search action if many results
        if (searchResult.TotalFound > 20)
        {
            var topType = GetPrimaryMemoryType(searchResult.Memories);
            if (!string.IsNullOrEmpty(topType))
            {
                actions.Add(new AIAction
                {
                    Id = "refine_by_type",
                    Description = $"Focus on {topType} memories only",
                    Command = new AIActionCommand
                    {
                        Tool = "mcp__codesearch__search_memories",
                        Parameters = new Dictionary<string, object> 
                        { 
                            { "query", originalQuery ?? "*" },
                            { "types", new[] { topType } }
                        }
                    },
                    EstimatedTokens = 180,
                    Priority = ActionPriority.Medium,
                    Context = ActionContext.ManyResults
                });
            }
        }

        // Create new memory action if no relevant results
        if (searchResult.TotalFound == 0 && !string.IsNullOrEmpty(originalQuery))
        {
            actions.Add(new AIAction
            {
                Id = "create_memory",
                Description = "Create a new memory about this topic",
                Command = new AIActionCommand
                {
                    Tool = "mcp__codesearch__store_memory",
                    Parameters = new Dictionary<string, object>
                    {
                        { "memoryType", "Question" },
                        { "content", $"Need to research: {originalQuery}" }
                    }
                },
                EstimatedTokens = 100,
                Priority = ActionPriority.High,
                Context = ActionContext.EmptyResults
            });
        }

        return actions.Take(4).ToList(); // Limit to 4 actions to control tokens
    }

    private List<string> GenerateMemorySearchInsights(
        FlexibleMemorySearchResult searchResult,
        FlexibleMemorySearchRequest request)
    {
        var insights = new List<string>();

        if (searchResult.TotalFound == 0)
        {
            insights.Add("No memories found - consider creating a new memory or trying a broader search");
        }
        else if (searchResult.TotalFound == 1)
        {
            insights.Add("Single memory found - may have related memories to explore");
        }
        else
        {
            var primaryType = GetPrimaryMemoryType(searchResult.Memories);
            if (!string.IsNullOrEmpty(primaryType))
            {
                var count = searchResult.Memories.Count(m => m.Type == primaryType);
                insights.Add($"Primary focus: {count} {primaryType} memories");
            }

            // Analyze temporal patterns
            var recentCount = searchResult.Memories.Count(m => m.Created > DateTime.UtcNow.AddDays(-7));
            if (recentCount > searchResult.Memories.Count * 0.5)
            {
                insights.Add("Many recent memories - active topic area");
            }

            // Analyze file references
            var filesReferenced = searchResult.Memories
                .SelectMany(m => m.FilesInvolved)
                .GroupBy(f => f)
                .OrderByDescending(g => g.Count())
                .Take(3)
                .ToList();

            if (filesReferenced.Any())
            {
                var topFile = filesReferenced.First();
                insights.Add($"Most referenced: {Path.GetFileName(topFile.Key)} ({topFile.Count()} memories)");
            }
        }

        return insights;
    }

    #endregion

    #region File Search Implementation

    private AIResponseData BuildFileSearchData(
        List<FileSearchResult> fileResults,
        string query,
        int tokenBudget)
    {
        var data = new AIResponseData
        {
            Summary = new ResultSummary
            {
                TotalFound = fileResults.Count,
                Returned = fileResults.Count,
                Truncated = false,
                PrimaryType = "file"
            }
        };

        // Calculate token budget for items
        var remainingTokens = tokenBudget - 300; // Reserve for structure
        var tokensPerItem = Math.Max(30, remainingTokens / Math.Max(1, fileResults.Count));

        // Add file items within token budget
        var itemTokens = 0;
        foreach (var file in fileResults.Take(20)) // Limit files
        {
            var item = CreateFileItem(file, tokensPerItem);
            var itemTokenCost = EstimateItemTokens(item);
            
            if (itemTokens + itemTokenCost > remainingTokens * 0.8)
                break;
                
            data.Items.Add(item);
            itemTokens += itemTokenCost;
        }

        // Add distribution by extension
        data.Distribution["extension"] = fileResults
            .GroupBy(f => Path.GetExtension(f.Path).ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.Count());

        // Add directory hotspots
        data.Hotspots = fileResults
            .GroupBy(f => Path.GetDirectoryName(f.Path))
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => new { directory = g.Key, count = g.Count() })
            .Cast<object>()
            .ToList();

        return data;
    }

    private List<AIAction> GenerateFileSearchActions(
        List<FileSearchResult> fileResults,
        string query,
        string workspacePath)
    {
        var actions = new List<AIAction>();

        // Read first file action
        if (fileResults.Any())
        {
            var firstFile = fileResults.First();
            actions.Add(new AIAction
            {
                Id = "read_file",
                Description = $"Read {Path.GetFileName(firstFile.Path)}",
                Command = new AIActionCommand
                {
                    Tool = "Read",
                    Parameters = new Dictionary<string, object> { { "file_path", firstFile.Path } }
                },
                EstimatedTokens = 200,
                Priority = ActionPriority.High,
                Context = ActionContext.Always
            });
        }

        // Search file contents action
        if (fileResults.Any())
        {
            actions.Add(new AIAction
            {
                Id = "search_content",
                Description = "Search content within these files",
                Command = new AIActionCommand
                {
                    Tool = "mcp__codesearch__text_search",
                    Parameters = new Dictionary<string, object>
                    {
                        { "workspacePath", workspacePath },
                        { "query", "TODO" }, // Example search
                        { "contextLines", 3 }
                    }
                },
                EstimatedTokens = 300,
                Priority = ActionPriority.Medium,
                Context = ActionContext.Exploration
            });
        }

        return actions;
    }

    private List<string> GenerateFileSearchInsights(List<FileSearchResult> fileResults, string query)
    {
        var insights = new List<string>();

        if (!fileResults.Any())
        {
            insights.Add($"No files found matching '{query}' - try a broader search or wildcard pattern");
            return insights;
        }

        // Analyze file types
        var extensions = fileResults
            .GroupBy(f => Path.GetExtension(f.Path).ToLowerInvariant())
            .OrderByDescending(g => g.Count())
            .Take(3)
            .ToList();

        if (extensions.Any())
        {
            var topExt = extensions.First();
            insights.Add($"Primary file type: {topExt.Key} ({topExt.Count()} files)");
        }

        // Analyze directory distribution
        var directories = fileResults
            .GroupBy(f => Path.GetDirectoryName(f.Path))
            .OrderByDescending(g => g.Count())
            .Take(2)
            .ToList();

        if (directories.Count > 1)
        {
            insights.Add($"Spread across {directories.Count} directories, most in {Path.GetFileName(directories.First().Key)}");
        }

        return insights;
    }

    #endregion

    #region Helper Methods

    private string GenerateMemorySearchMarkdown(
        FlexibleMemorySearchResult searchResult,
        AIResponseData data,
        List<string> insights)
    {
        var md = new StringBuilder();
        
        md.AppendLine($"## Found {searchResult.TotalFound} memories");
        md.AppendLine();

        if (insights.Any())
        {
            md.AppendLine("**Key Insights:**");
            foreach (var insight in insights.Take(3))
            {
                md.AppendLine($"- {insight}");
            }
            md.AppendLine();
        }

        if (data.Items.Any())
        {
            md.AppendLine($"**Top {data.Items.Count} Results:**");
            foreach (var item in data.Items.Take(5))
            {
                // Safely extract properties from dynamic item
                var itemDict = JsonSerializer.Deserialize<Dictionary<string, object>>(
                    JsonSerializer.Serialize(item));
                    
                if (itemDict != null)
                {
                    var type = itemDict.GetValueOrDefault("type", "Unknown").ToString();
                    var content = itemDict.GetValueOrDefault("content", "").ToString() ?? "";
                    md.AppendLine($"- **{type}**: {TruncateText(content, 100)}");
                }
            }
        }

        return md.ToString();
    }

    private string GenerateFileSearchMarkdown(
        List<FileSearchResult> fileResults,
        AIResponseData data,
        List<string> insights)
    {
        var md = new StringBuilder();
        
        md.AppendLine($"## Found {fileResults.Count} files");
        md.AppendLine();

        if (insights.Any())
        {
            md.AppendLine("**Analysis:**");
            foreach (var insight in insights)
            {
                md.AppendLine($"- {insight}");
            }
            md.AppendLine();
        }

        if (data.Items.Any())
        {
            md.AppendLine($"**Files:**");
            foreach (var item in data.Items.Take(10))
            {
                var itemDict = JsonSerializer.Deserialize<Dictionary<string, object>>(
                    JsonSerializer.Serialize(item));
                    
                if (itemDict != null)
                {
                    var path = itemDict.GetValueOrDefault("path", "").ToString();
                    md.AppendLine($"- {path}");
                }
            }
        }

        return md.ToString();
    }

    private object CreateMemoryItem(FlexibleMemoryEntry memory, int tokenBudget)
    {
        var contentLength = Math.Min(tokenBudget * 4, 300); // Rough char-to-token conversion
        var item = new
        {
            id = memory.Id,
            type = memory.Type,
            content = TruncateText(memory.Content, contentLength),
            created = memory.Created.ToString("yyyy-MM-dd"),
            files = memory.FilesInvolved.Take(3).ToArray(),
            isShared = memory.IsShared
        };

        // Add highlights if available
        if (memory.Highlights != null && memory.Highlights.Count > 0)
        {
            return new
            {
                id = item.id,
                type = item.type,
                content = item.content,
                created = item.created,
                files = item.files,
                isShared = item.isShared,
                highlights = memory.Highlights
            };
        }

        return item;
    }

    private object CreateFileItem(FileSearchResult file, int tokenBudget)
    {
        return new
        {
            path = file.Path,
            name = Path.GetFileName(file.Path),
            directory = Path.GetDirectoryName(file.Path),
            extension = Path.GetExtension(file.Path),
            score = file.Score
        };
    }

    private Dictionary<string, Dictionary<string, int>> CreateMemoryDistribution(List<FlexibleMemoryEntry> memories)
    {
        return new Dictionary<string, Dictionary<string, int>>
        {
            ["type"] = memories.GroupBy(m => m.Type).ToDictionary(g => g.Key, g => g.Count()),
            ["shared"] = memories.GroupBy(m => m.IsShared ? "shared" : "private").ToDictionary(g => g.Key, g => g.Count())
        };
    }

    private List<object> CreateMemoryHotspots(List<FlexibleMemoryEntry> memories)
    {
        return memories
            .SelectMany(m => m.FilesInvolved.Select(f => new { file = f, memory = m }))
            .GroupBy(x => x.file)
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => new { file = Path.GetFileName(g.Key), references = g.Count() })
            .Cast<object>()
            .ToList();
    }

    private string? GetPrimaryMemoryType(List<FlexibleMemoryEntry> memories)
    {
        return memories
            .GroupBy(m => m.Type)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault()?.Key;
    }

    private AIOptimizedResponse OptimizeForTokenBudget(AIOptimizedResponse response, int tokenBudget)
    {
        // Reduce items if over budget
        if (response.Meta.EstimatedTokens > tokenBudget)
        {
            var targetItems = Math.Max(1, response.Data.Items.Count / 2);
            response.Data.Items = response.Data.Items.Take(targetItems).ToList();
            response.Meta.AutoModeSwitch = true;
            response.Meta.EstimatedTokens = EstimateResponseTokens(response);
            
            _logger.LogDebug("Auto-optimized response: reduced to {Items} items, {Tokens} tokens", 
                targetItems, response.Meta.EstimatedTokens);
        }

        return response;
    }

    private int EstimateResponseTokens(AIOptimizedResponse response)
    {
        // Quick estimation based on structure
        var baseTokens = 100; // Structure overhead
        var dataTokens = response.Data.Items.Count * 50; // Rough per-item estimate
        var actionTokens = response.Actions.Sum(a => a.EstimatedTokens);
        var insightTokens = response.Insights.Sum(i => i.Length / 4); // Rough char-to-token
        var markdownTokens = (response.DisplayMarkdown?.Length ?? 0) / 4;

        return baseTokens + dataTokens + actionTokens + insightTokens + markdownTokens;
    }

    private int EstimateMemoryResponseTokens(
        FlexibleMemorySearchResult searchResult,
        AIResponseData data,
        List<AIAction> actions,
        List<string> insights)
    {
        // Quick estimation for backward-compatible response
        var baseTokens = 150; // Structure overhead
        var dataTokens = data.Items.Count * 40; // Per-item estimate
        var actionTokens = actions.Sum(a => a.EstimatedTokens);
        var insightTokens = insights.Sum(i => i.Length / 4); // Rough conversion
        var summaryTokens = 100; // Summary structure

        return baseTokens + dataTokens + actionTokens + insightTokens + summaryTokens;
    }

    private int EstimateItemTokens(object item)
    {
        var json = JsonSerializer.Serialize(item);
        return Math.Max(20, json.Length / 4); // Rough char-to-token conversion
    }

    private string TruncateText(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;
        
        return text.Substring(0, maxLength - 3) + "...";
    }

    private string GenerateCacheKey(string operation)
    {
        return $"{operation}_{DateTime.UtcNow.Ticks:X}_{Guid.NewGuid():N}";
    }

    #endregion
}

/// <summary>
/// Simple file search result for response building
/// </summary>
public class FileSearchResult
{
    public string Path { get; set; } = string.Empty;
    public double Score { get; set; }
}

/// <summary>
/// Token estimation service interface
/// </summary>
public interface ITokenEstimationService
{
    int EstimateTokens(string text);
    int EstimateTokens(object obj);
}

/// <summary>
/// Basic token estimation service implementation
/// </summary>
public class TokenEstimationService : ITokenEstimationService
{
    public int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        
        // Rough estimation: 1 token per 4 characters on average
        // This is approximate and could be improved with actual tokenizer
        return Math.Max(1, text.Length / 4);
    }

    public int EstimateTokens(object obj)
    {
        var json = JsonSerializer.Serialize(obj);
        return EstimateTokens(json);
    }
}