using System.Text.Json;
using COA.CodeSearch.McpServer.Configuration;
using COA.CodeSearch.McpServer.Infrastructure;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// AI-optimized version of memory search with structured response format
/// </summary>
public class FlexibleMemorySearchToolV2 : ClaudeOptimizedToolBase
{
    private readonly FlexibleMemoryService _memoryService;
    private readonly IConfiguration _configuration;

    public FlexibleMemorySearchToolV2(
        ILogger<FlexibleMemorySearchToolV2> logger,
        FlexibleMemoryService memoryService,
        IConfiguration configuration,
        IResponseSizeEstimator sizeEstimator,
        IResultTruncator truncator,
        IOptions<ResponseLimitOptions> options,
        IDetailRequestCache detailCache)
        : base(sizeEstimator, truncator, options, logger, detailCache)
    {
        _memoryService = memoryService;
        _configuration = configuration;
    }

    public async Task<object> ExecuteAsync(
        string? query = null,
        string[]? types = null,
        string? dateRange = null,
        Dictionary<string, string>? facets = null,
        string? orderBy = null,
        bool orderDescending = true,
        int maxResults = 50,
        bool includeArchived = false,
        bool boostRecent = false,
        bool boostFrequent = false,
        ResponseMode mode = ResponseMode.Summary,
        DetailRequest? detailRequest = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Handle detail requests
            if (detailRequest != null && DetailCache != null)
            {
                return await HandleDetailRequestAsync(detailRequest, cancellationToken);
            }

            Logger.LogInformation("Memory search request: query={Query}, types={Types}", query, types);

            var request = new FlexibleMemorySearchRequest
            {
                Query = query ?? "*",
                Types = types,
                Facets = facets,
                OrderBy = orderBy,
                OrderDescending = orderDescending,
                MaxResults = maxResults,
                IncludeArchived = includeArchived,
                BoostRecent = boostRecent,
                BoostFrequent = boostFrequent
            };

            if (!string.IsNullOrEmpty(dateRange))
            {
                request.DateRange = new DateRangeFilter { RelativeTime = dateRange };
            }

            var searchResult = await _memoryService.SearchMemoriesAsync(request);

            // Create AI-optimized response
            return CreateAiOptimizedResponse(query, types, searchResult, mode);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in FlexibleMemorySearchV2");
            return CreateErrorResponse<object>(ex.Message);
        }
    }

    private object CreateAiOptimizedResponse(
        string? query,
        string[]? types,
        FlexibleMemorySearchResult result,
        ResponseMode mode)
    {
        // Analyze the results
        var analysis = AnalyzeMemoryResults(result);

        // Generate insights
        var insights = GenerateMemoryInsights(query, result, analysis);

        // Generate actions
        var actions = GenerateMemoryActions(query, result, analysis);

        // Prepare memories for response
        var memories = mode == ResponseMode.Full
            ? result.Memories.Select(m => new
            {
                id = m.Id,
                type = m.Type,
                content = m.Content.Length > 200 ? m.Content.Substring(0, 197) + "..." : m.Content,
                created = m.Created.ToString("yyyy-MM-dd HH:mm:ss"),
                modified = m.Modified.ToString("yyyy-MM-dd HH:mm:ss"),
                files = m.FilesInvolved.Length > 0 ? m.FilesInvolved : null,
                fields = m.Fields.Any() ? m.Fields : null,
                isShared = m.IsShared,
                accessCount = m.AccessCount
            }).ToList()
            : null;

        // Create response
        return new
        {
            success = true,
            operation = "memory_search",
            query = new
            {
                text = query ?? "*",
                types = types,
                totalRequested = result.Memories.Count
            },
            summary = new
            {
                totalFound = result.TotalFound,
                returned = result.Memories.Count,
                typeDistribution = analysis.TypeCounts.Take(5).ToDictionary(kv => kv.Key, kv => kv.Value),
                statusDistribution = analysis.StatusCounts.Take(5).ToDictionary(kv => kv.Key, kv => kv.Value),
                dateRange = analysis.DateRange
            },
            facets = result.FacetCounts.Any() ? result.FacetCounts : null,
            highlights = result.HighlightedTerms?.Any() == true ? result.HighlightedTerms : null,
            analysis = new
            {
                patterns = analysis.Patterns.Take(3).ToList(),
                hotspots = new
                {
                    byType = analysis.TypeCounts.Take(3).Select(kv => new { type = kv.Key, count = kv.Value }).ToList(),
                    byStatus = analysis.StatusCounts.Take(3).Select(kv => new { status = kv.Key, count = kv.Value }).ToList(),
                    recentlyAccessed = analysis.RecentlyAccessed.Take(3).Select(m => new
                    {
                        id = m.Id,
                        type = m.Type,
                        accessCount = m.AccessCount,
                        lastAccessed = m.LastAccessed?.ToString("yyyy-MM-dd HH:mm:ss")
                    }).ToList()
                },
                fileReferences = analysis.TopFiles.Take(5).Select(f => new { file = f.Key, references = f.Value }).ToList(),
                workingMemoryStatus = new
                {
                    active = analysis.ActiveWorkingMemories,
                    expiringSoon = analysis.ExpiringSoonMemories
                }
            },
            memories = memories,
            insights = insights,
            actions = actions,
            meta = new
            {
                mode = mode.ToString().ToLowerInvariant(),
                truncated = result.Memories.Count < result.TotalFound,
                tokens = EstimateResponseTokens(result),
                cached = $"memsearch_{Guid.NewGuid().ToString("N")[..8]}"
            }
        };
    }

    private MemoryAnalysis AnalyzeMemoryResults(FlexibleMemorySearchResult result)
    {
        var analysis = new MemoryAnalysis();
        var now = DateTime.UtcNow;

        foreach (var memory in result.Memories)
        {
            // Type distribution
            if (!analysis.TypeCounts.ContainsKey(memory.Type))
                analysis.TypeCounts[memory.Type] = 0;
            analysis.TypeCounts[memory.Type]++;

            // Status distribution
            var status = memory.GetField<string>("status") ?? "none";
            if (!analysis.StatusCounts.ContainsKey(status))
                analysis.StatusCounts[status] = 0;
            analysis.StatusCounts[status]++;

            // File references
            foreach (var file in memory.FilesInvolved)
            {
                if (!analysis.TopFiles.ContainsKey(file))
                    analysis.TopFiles[file] = 0;
                analysis.TopFiles[file]++;
            }

            // Working memory analysis
            if (memory.Type == "WorkingMemory" || memory.GetField<bool>("isWorkingMemory"))
            {
                analysis.ActiveWorkingMemories++;
                var expiresAt = memory.GetField<DateTime?>("expiresAt");
                if (expiresAt.HasValue && expiresAt.Value < now.AddHours(1))
                {
                    analysis.ExpiringSoonMemories++;
                }
            }

            // Recently accessed
            if (memory.LastAccessed.HasValue && memory.LastAccessed.Value > now.AddDays(-7))
            {
                analysis.RecentlyAccessed.Add(memory);
            }
        }

        // Date range
        if (result.Memories.Any())
        {
            analysis.DateRange = new
            {
                oldest = result.Memories.Min(m => m.Created).ToString("yyyy-MM-dd"),
                newest = result.Memories.Max(m => m.Created).ToString("yyyy-MM-dd")
            };
        }

        // Pattern detection
        if (analysis.TypeCounts.ContainsKey("TechnicalDebt") && analysis.TypeCounts["TechnicalDebt"] > 5)
        {
            analysis.Patterns.Add("High technical debt accumulation");
        }

        if (analysis.StatusCounts.ContainsKey("pending") && analysis.StatusCounts["pending"] > 10)
        {
            analysis.Patterns.Add("Many pending items require attention");
        }

        if (analysis.ActiveWorkingMemories > 5)
        {
            analysis.Patterns.Add($"{analysis.ActiveWorkingMemories} active working memories in session");
        }

        // Sort dictionaries by count
        analysis.TypeCounts = analysis.TypeCounts.OrderByDescending(kv => kv.Value)
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        analysis.StatusCounts = analysis.StatusCounts.OrderByDescending(kv => kv.Value)
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        analysis.TopFiles = analysis.TopFiles.OrderByDescending(kv => kv.Value)
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        analysis.RecentlyAccessed = analysis.RecentlyAccessed.OrderByDescending(m => m.AccessCount).ToList();

        return analysis;
    }

    private List<string> GenerateMemoryInsights(string? query, FlexibleMemorySearchResult result, MemoryAnalysis analysis)
    {
        var insights = new List<string>();

        // Search result insights
        if (result.TotalFound == 0)
        {
            insights.Add($"No memories found for query '{query}'");
            if (!string.IsNullOrEmpty(query) && query != "*")
            {
                insights.Add("Try broadening your search or using wildcards");
            }
        }
        else
        {
            insights.Add($"Found {result.TotalFound} memories across {analysis.TypeCounts.Count} types");
        }

        // Type distribution insights
        if (analysis.TypeCounts.Any())
        {
            var topType = analysis.TypeCounts.First();
            insights.Add($"Most common: {topType.Key} ({topType.Value} items, {topType.Value * 100 / result.TotalFound}%)");
        }

        // Status insights
        if (analysis.StatusCounts.ContainsKey("pending") && analysis.StatusCounts["pending"] > 0)
        {
            var pendingCount = analysis.StatusCounts["pending"];
            var pendingPercent = pendingCount * 100 / result.TotalFound;
            if (pendingPercent > 30)
            {
                insights.Add($"⚠️ {pendingCount} pending items ({pendingPercent}%) need attention");
            }
        }

        // Working memory insights
        if (analysis.ActiveWorkingMemories > 0)
        {
            insights.Add($"{analysis.ActiveWorkingMemories} active working memories");
            if (analysis.ExpiringSoonMemories > 0)
            {
                insights.Add($"⏰ {analysis.ExpiringSoonMemories} memories expiring soon");
            }
        }

        // File hotspot insights
        if (analysis.TopFiles.Any())
        {
            var topFile = analysis.TopFiles.First();
            if (topFile.Value > 3)
            {
                insights.Add($"File hotspot: {Path.GetFileName(topFile.Key)} ({topFile.Value} references)");
            }
        }

        // Pattern insights
        foreach (var pattern in analysis.Patterns.Take(2))
        {
            insights.Add(pattern);
        }

        // Date range insight
        if (analysis.DateRange != null)
        {
            insights.Add($"Memory span: {analysis.DateRange.oldest} to {analysis.DateRange.newest}");
        }

        return insights;
    }

    private List<object> GenerateMemoryActions(string? query, FlexibleMemorySearchResult result, MemoryAnalysis analysis)
    {
        var actions = new List<object>();

        // View specific memories
        if (result.Memories.Any())
        {
            var firstMemory = result.Memories.First();
            actions.Add(new
            {
                id = "view_memory",
                cmd = new { action = "flexible_get_memory", id = firstMemory.Id },
                tokens = 500,
                priority = "available"
            });
        }

        // Filter by type
        if (analysis.TypeCounts.Count > 1)
        {
            var topTypes = analysis.TypeCounts.Take(3).Select(kv => kv.Key).ToList();
            actions.Add(new
            {
                id = "filter_by_type",
                cmd = new { types = topTypes, query = query },
                tokens = 2000,
                priority = "recommended"
            });
        }

        // Address pending items
        if (analysis.StatusCounts.ContainsKey("pending") && analysis.StatusCounts["pending"] > 0)
        {
            actions.Add(new
            {
                id = "view_pending",
                cmd = new { facets = new { status = "pending" }, orderBy = "created" },
                tokens = 2500,
                priority = analysis.StatusCounts["pending"] > 5 ? "critical" : "recommended"
            });
        }

        // Create related memory
        if (!string.IsNullOrEmpty(query) && query != "*")
        {
            actions.Add(new
            {
                id = "create_related",
                cmd = new { type = "FollowUp", content = $"Related to search: {query}" },
                tokens = 300,
                priority = "available"
            });
        }

        // Archive old memories
        if (result.Memories.Any(m => (DateTime.UtcNow - m.Created).TotalDays > 90))
        {
            actions.Add(new
            {
                id = "archive_old",
                cmd = new { action = "flexible_archive_memories", daysOld = 90 },
                tokens = 1000,
                priority = "normal"
            });
        }

        // Export results
        if (result.TotalFound > 10)
        {
            actions.Add(new
            {
                id = "export_results",
                cmd = new { format = "markdown", includeFields = true },
                tokens = EstimateExportTokens(result),
                priority = "available"
            });
        }

        // Suggest summarization
        var oldMemoriesByType = result.Memories
            .Where(m => (DateTime.UtcNow - m.Created).TotalDays > 30)
            .GroupBy(m => m.Type)
            .Where(g => g.Count() > 10)
            .ToList();

        if (oldMemoriesByType.Any())
        {
            var typeToSummarize = oldMemoriesByType.First();
            actions.Add(new
            {
                id = "summarize_old",
                cmd = new 
                { 
                    action = "flexible_summarize_memories", 
                    type = typeToSummarize.Key, 
                    daysOld = 30,
                    count = typeToSummarize.Count()
                },
                tokens = 1500,
                priority = "normal"
            });
        }

        return actions;
    }

    private int EstimateResponseTokens(FlexibleMemorySearchResult result)
    {
        // Base tokens for structure
        var baseTokens = 200;
        
        // Per memory tokens (summary mode shows less)
        var perMemoryTokens = 50;
        
        // Facets and analysis
        var facetTokens = result.FacetCounts.Count * 20;
        
        return baseTokens + (result.Memories.Count * perMemoryTokens) + facetTokens;
    }

    private int EstimateExportTokens(FlexibleMemorySearchResult result)
    {
        // Estimate tokens for full export
        return result.Memories.Count * 150;
    }

    private Task<object> HandleDetailRequestAsync(DetailRequest request, CancellationToken cancellationToken)
    {
        // For now, return error as we don't have complex detail levels
        return Task.FromResult<object>(CreateErrorResponse<object>("Detail requests not implemented for memory search"));
    }

    protected override int GetTotalResults<T>(T data)
    {
        if (data is FlexibleMemorySearchResult searchResult)
        {
            return searchResult.TotalFound;
        }
        return 0;
    }

    private class MemoryAnalysis
    {
        public Dictionary<string, int> TypeCounts { get; set; } = new();
        public Dictionary<string, int> StatusCounts { get; set; } = new();
        public Dictionary<string, int> TopFiles { get; set; } = new();
        public List<FlexibleMemoryEntry> RecentlyAccessed { get; set; } = new();
        public List<string> Patterns { get; set; } = new();
        public int ActiveWorkingMemories { get; set; }
        public int ExpiringSoonMemories { get; set; }
        public dynamic? DateRange { get; set; }
    }
}