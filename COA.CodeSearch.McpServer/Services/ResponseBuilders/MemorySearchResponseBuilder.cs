using COA.CodeSearch.McpServer.Infrastructure;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace COA.CodeSearch.McpServer.Services.ResponseBuilders;

/// <summary>
/// Builds AI-optimized responses for memory search operations
/// </summary>
public class MemorySearchResponseBuilder : BaseResponseBuilder
{
    private readonly IDetailRequestCache? _detailCache;
    
    public MemorySearchResponseBuilder(ILogger<MemorySearchResponseBuilder> logger, IDetailRequestCache? detailCache) 
        : base(logger)
    {
        _detailCache = detailCache;
    }

    public override string ResponseType => "memory_search";

    /// <summary>
    /// Build AI-optimized response for memory search results
    /// </summary>
    public object BuildResponse(
        FlexibleMemorySearchResult searchResult,
        FlexibleMemorySearchRequest request,
        string? originalQuery = null,
        ResponseMode mode = ResponseMode.Summary)
    {
        var tokenBudget = mode == ResponseMode.Summary ? SummaryTokenBudget : FullTokenBudget;
        
        // Build structured data efficiently
        var data = BuildMemorySearchData(searchResult, request, tokenBudget, mode);

        // Generate contextual actions
        var actions = GenerateActions(new
        {
            searchResult,
            request,
            originalQuery
        }, tokenBudget);

        // Create insights
        var insights = GenerateInsights(new
        {
            searchResult,
            request
        }, mode);

        // Estimate tokens
        var estimatedTokens = EstimateMemoryResponseTokens(searchResult, data, actions, insights);

        // Store data in cache for detail requests
        string? detailRequestToken = null;
        List<DetailLevel>? availableDetailLevels = null;
        
        if (mode == ResponseMode.Summary && _detailCache != null)
        {
            detailRequestToken = _detailCache.StoreDetailData(searchResult);
            availableDetailLevels = CreateMemoryDetailLevels(searchResult);
        }

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
            actions = actions.Select(a => a is AIAction aiAction ? new
            {
                id = aiAction.Id,
                description = aiAction.Description,
                command = aiAction.Command.Tool,
                parameters = aiAction.Command.Parameters,
                priority = aiAction.Priority.ToString().ToLowerInvariant(),
                estimatedTokens = aiAction.EstimatedTokens
            } : a),
            meta = new
            {
                mode = mode.ToString().ToLowerInvariant(),
                tokenBudget = tokenBudget,
                tokens = estimatedTokens, // Backward compatibility
                estimatedTokens = estimatedTokens,
                truncated = searchResult.Memories.Count < searchResult.TotalFound,
                format = "ai-optimized",
                cached = GenerateCacheKey("memory_search"),
                detailRequestToken = detailRequestToken,
                availableDetailLevels = availableDetailLevels
            }
        };
    }

    protected override List<string> GenerateInsights(dynamic data, ResponseMode mode)
    {
        var insights = new List<string>();
        FlexibleMemorySearchResult searchResult = data.searchResult;
        FlexibleMemorySearchRequest request = data.request;

        // Basic results insight
        if (searchResult.TotalFound == 0)
        {
            insights.Add("No memories found matching criteria");
            insights.Add("Try broader search terms or remove filters");
        }
        else
        {
            insights.Add($"Found {searchResult.TotalFound} memories");
            
            // Type distribution insights
            var typeGroups = searchResult.Memories
                .GroupBy(m => m.Type)
                .OrderByDescending(g => g.Count())
                .ToList();
            
            if (typeGroups.Count == 1)
            {
                insights.Add($"All memories are type: {typeGroups[0].Key}");
            }
            else if (typeGroups.Count > 1)
            {
                var topType = typeGroups[0];
                insights.Add($"Most common type: {topType.Key} ({topType.Count()} memories)");
            }
            
            // Recency insights
            var recentMemories = searchResult.Memories
                .Where(m => m.Created > DateTime.UtcNow.AddDays(-7))
                .Count();
            
            if (recentMemories > 0)
            {
                insights.Add($"{recentMemories} memories created in the last week");
            }
            
            // File association insights
            var filesInvolved = searchResult.Memories
                .Where(m => m.FilesInvolved?.Any() == true)
                .SelectMany(m => m.FilesInvolved!)
                .Distinct()
                .Count();
            
            if (filesInvolved > 0)
            {
                insights.Add($"Memories reference {filesInvolved} distinct files");
            }
            
            // Session vs shared insights
            var sessionMemories = searchResult.Memories.Count(m => !m.IsShared);
            var sharedMemories = searchResult.Memories.Count(m => m.IsShared);
            
            if (sessionMemories > 0 && sharedMemories > 0)
            {
                insights.Add($"Mix of shared ({sharedMemories}) and session ({sessionMemories}) memories");
            }
        }
        
        return insights;
    }

    protected override List<dynamic> GenerateActions(dynamic data, int tokenBudget)
    {
        var actions = new List<dynamic>();
        FlexibleMemorySearchResult searchResult = data.searchResult;
        string? originalQuery = data.originalQuery;

        if (searchResult.Memories.Any())
        {
            // Explore relationships
            actions.Add(new AIAction
            {
                Id = "explore_relationships",
                Description = "Explore memory relationships",
                Command = new AICommand
                {
                    Tool = "memory_graph_navigator",
                    Parameters = new Dictionary<string, object>
                    {
                        ["startPoint"] = searchResult.Memories.First().Id,
                        ["depth"] = 2
                    }
                },
                EstimatedTokens = 800,
                Priority = Priority.Medium
            });
            
            // Filter by type if multiple types
            var typeGroups = searchResult.Memories.GroupBy(m => m.Type).ToList();
            if (typeGroups.Count > 1)
            {
                var topType = typeGroups.OrderByDescending(g => g.Count()).First();
                actions.Add(new AIAction
                {
                    Id = "filter_by_type",
                    Description = $"Filter to {topType.Key} memories only",
                    Command = new AICommand
                    {
                        Tool = "search_memories",
                        Parameters = new Dictionary<string, object>
                        {
                            ["query"] = originalQuery ?? "*",
                            ["types"] = new[] { topType.Key }
                        }
                    },
                    EstimatedTokens = 500,
                    Priority = Priority.Low
                });
            }
            
            // Recent memories action
            var hasRecentMemories = searchResult.Memories.Any(m => m.Created > DateTime.UtcNow.AddDays(-1));
            if (hasRecentMemories)
            {
                actions.Add(new AIAction
                {
                    Id = "recent_only",
                    Description = "Show only today's memories",
                    Command = new AICommand
                    {
                        Tool = "search_memories",
                        Parameters = new Dictionary<string, object>
                        {
                            ["query"] = originalQuery ?? "*",
                            ["dateRange"] = "last-24-hours"
                        }
                    },
                    EstimatedTokens = 300,
                    Priority = Priority.Low
                });
            }
        }
        else
        {
            // No results - suggest creating memory
            actions.Add(new AIAction
            {
                Id = "create_memory",
                Description = "Create a new memory",
                Command = new AICommand
                {
                    Tool = "store_memory",
                    Parameters = new Dictionary<string, object>
                    {
                        ["memoryType"] = "WorkSession",
                        ["content"] = "Enter memory content here"
                    }
                },
                EstimatedTokens = 100,
                Priority = Priority.Medium
            });
        }
        
        return actions;
    }

    private MemorySearchData BuildMemorySearchData(
        FlexibleMemorySearchResult searchResult, 
        FlexibleMemorySearchRequest request,
        int tokenBudget,
        ResponseMode mode)
    {
        var data = new MemorySearchData();
        
        // Build items with token awareness
        var itemTokenEstimate = 100; // Rough estimate per memory item
        var maxItems = Math.Min(tokenBudget / itemTokenEstimate, searchResult.Memories.Count);
        
        data.Items = searchResult.Memories
            .Take(maxItems)
            .Select(m => new MemorySummaryItem
            {
                Id = m.Id,
                Type = m.Type,
                Content = mode == ResponseMode.Full ? m.Content : TruncateContent(m.Content, 200),
                Created = m.Created,
                IsShared = m.IsShared,
                Score = 1.0f, // TODO: Add score support to FlexibleMemoryEntry
                Files = m.FilesInvolved?.Take(3).ToList() ?? new List<string>()
            })
            .ToList();
        
        // Build distribution
        data.Distribution = new Dictionary<string, Dictionary<string, int>>
        {
            ["type"] = searchResult.Memories
                .GroupBy(m => m.Type)
                .ToDictionary(g => g.Key, g => g.Count()),
            ["shared"] = searchResult.Memories
                .GroupBy(m => m.IsShared ? "shared" : "session")
                .ToDictionary(g => g.Key, g => g.Count())
        };
        
        // Build hotspots (files with most memories)
        data.Hotspots = searchResult.Memories
            .Where(m => m.FilesInvolved?.Any() == true)
            .SelectMany(m => m.FilesInvolved!)
            .GroupBy(f => f)
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => new FileHotspot
            {
                File = Path.GetFileName(g.Key),
                Path = g.Key,
                MemoryCount = g.Count()
            })
            .ToList();
        
        // Build summary
        data.Summary = new MemorySearchSummary
        {
            TotalFound = searchResult.TotalFound,
            Returned = searchResult.Memories.Count,
            PrimaryType = searchResult.Memories
                .GroupBy(m => m.Type)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault()?.Key ?? "Unknown"
        };
        
        return data;
    }

    private string TruncateContent(string content, int maxLength)
    {
        if (content.Length <= maxLength) return content;
        return content.Substring(0, maxLength - 3) + "...";
    }

    private int EstimateMemoryResponseTokens(
        FlexibleMemorySearchResult searchResult,
        MemorySearchData data,
        List<dynamic> actions,
        List<string> insights)
    {
        // Base structure
        var baseTokens = 300;
        
        // Memory items
        var itemTokens = data.Items.Count * 100;
        
        // Actions and insights
        var actionTokens = actions.Count * 50;
        var insightTokens = insights.Count * 20;
        
        // Distribution and analysis
        var analysisTokens = 200;
        
        return baseTokens + itemTokens + actionTokens + insightTokens + analysisTokens;
    }

    private List<DetailLevel> CreateMemoryDetailLevels(FlexibleMemorySearchResult searchResult)
    {
        return new List<DetailLevel>
        {
            new DetailLevel
            {
                Id = "next10",
                Description = "Next 10 memories",
                EstimatedTokens = 1000
            },
            new DetailLevel
            {
                Id = "full",
                Description = $"All {searchResult.TotalFound} memories",
                EstimatedTokens = searchResult.TotalFound * 100
            },
            new DetailLevel
            {
                Id = "byType",
                Description = "Grouped by memory type",
                EstimatedTokens = 1500
            }
        };
    }

    private string GenerateCacheKey(string operation)
    {
        return $"{operation}_{Guid.NewGuid().ToString("N")[..8]}";
    }
}

// Supporting types
public class MemorySearchData
{
    public List<MemorySummaryItem> Items { get; set; } = new();
    public Dictionary<string, Dictionary<string, int>> Distribution { get; set; } = new();
    public List<FileHotspot> Hotspots { get; set; } = new();
    public MemorySearchSummary Summary { get; set; } = new();
}

public class MemorySummaryItem
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string Content { get; set; } = "";
    public DateTime Created { get; set; }
    public bool IsShared { get; set; }
    public float Score { get; set; }
    public List<string> Files { get; set; } = new();
}

public class FileHotspot
{
    public string File { get; set; } = "";
    public string Path { get; set; } = "";
    public int MemoryCount { get; set; }
}

public class MemorySearchSummary
{
    public int TotalFound { get; set; }
    public int Returned { get; set; }
    public string PrimaryType { get; set; } = "";
}