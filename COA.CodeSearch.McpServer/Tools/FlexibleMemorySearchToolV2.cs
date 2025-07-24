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
    public override string ToolName => "flexible_memory_search_v2";
    public override string Description => "AI-optimized memory search";
    public override ToolCategory Category => ToolCategory.Memory;
    private readonly FlexibleMemoryService _memoryService;
    private readonly IConfiguration _configuration;
    private readonly IQueryExpansionService _queryExpansion;
    private readonly IContextAwarenessService _contextAwareness;

    public FlexibleMemorySearchToolV2(
        ILogger<FlexibleMemorySearchToolV2> logger,
        FlexibleMemoryService memoryService,
        IConfiguration configuration,
        IQueryExpansionService queryExpansion,
        IContextAwarenessService contextAwareness,
        IResponseSizeEstimator sizeEstimator,
        IResultTruncator truncator,
        IOptions<ResponseLimitOptions> options,
        IDetailRequestCache detailCache)
        : base(sizeEstimator, truncator, options, logger, detailCache)
    {
        _memoryService = memoryService;
        _configuration = configuration;
        _queryExpansion = queryExpansion;
        _contextAwareness = contextAwareness;
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
        // New intelligent features
        bool enableQueryExpansion = true,
        bool enableContextAwareness = true,
        string? currentFile = null,
        string[]? recentFiles = null,
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

            Logger.LogInformation("Memory search request: query={Query}, types={Types}, expansion={Expansion}, context={Context}", 
                query, types, enableQueryExpansion, enableContextAwareness);

            // Process query with intelligence
            var processedQuery = await ProcessIntelligentQueryAsync(query, enableQueryExpansion, enableContextAwareness, currentFile, recentFiles);
            
            var request = new FlexibleMemorySearchRequest
            {
                Query = processedQuery.FinalQuery,
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

            // Update search tracking with actual results
            if (enableContextAwareness && !string.IsNullOrEmpty(query))
            {
                await _contextAwareness.TrackSearchQueryAsync(query, searchResult.TotalFound);
            }

            // Create AI-optimized response with intelligence info
            return CreateAiOptimizedResponse(query, types, searchResult, processedQuery, mode);
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
        ProcessedQueryResult processedQuery,
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
                original = processedQuery.OriginalQuery,
                expanded = processedQuery.ExpandedTerms.Length > 0 ? processedQuery.ExpandedTerms : null,
                contextKeywords = processedQuery.ContextKeywords.Length > 0 ? processedQuery.ContextKeywords : null,
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

    /// <summary>
    /// Process query with intelligent expansion and context awareness
    /// </summary>
    private async Task<ProcessedQueryResult> ProcessIntelligentQueryAsync(
        string? originalQuery, 
        bool enableExpansion, 
        bool enableContext, 
        string? currentFile, 
        string[]? recentFiles)
    {
        var result = new ProcessedQueryResult
        {
            OriginalQuery = originalQuery ?? "*",
            FinalQuery = originalQuery ?? "*"
        };

        // Skip processing for wildcard queries
        if (string.IsNullOrWhiteSpace(originalQuery) || originalQuery == "*")
        {
            return result;
        }

        try
        {
            // Step 1: Query Expansion
            if (enableExpansion)
            {
                var expandedQuery = await _queryExpansion.ExpandQueryAsync(originalQuery);
                result.ExpandedTerms = expandedQuery.WeightedTerms.Keys.ToArray();
                result.FinalQuery = expandedQuery.ExpandedLuceneQuery;
                
                Logger.LogDebug("Query expanded from '{Original}' to '{Final}' with {TermCount} terms", 
                    originalQuery, result.FinalQuery, expandedQuery.WeightedTerms.Count);
            }

            // Step 2: Context Awareness
            if (enableContext)
            {
                // Update context tracking
                if (!string.IsNullOrEmpty(currentFile))
                {
                    await _contextAwareness.UpdateCurrentFileAsync(currentFile);
                }

                if (recentFiles != null)
                {
                    foreach (var file in recentFiles)
                    {
                        await _contextAwareness.TrackFileAccessAsync(file);
                    }
                }

                // Get current context
                var context = await _contextAwareness.GetCurrentContextAsync();
                result.ContextKeywords = context.ContextKeywords;

                // Apply context boosts (this would integrate with Lucene scoring)
                if (result.ExpandedTerms.Length > 0)
                {
                    var contextBoosts = _contextAwareness.GetContextBoosts(context, result.ExpandedTerms);
                    result.ContextBoosts = contextBoosts;
                    
                    Logger.LogDebug("Applied context boosts to {TermCount} terms, max boost: {MaxBoost:F2}", 
                        contextBoosts.Count, contextBoosts.Values.DefaultIfEmpty(1.0f).Max());
                }
            }

            // Track this search for future context
            // Note: ResultsFound will be updated after the actual search
            await _contextAwareness.TrackSearchQueryAsync(originalQuery, 0);

            return result;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error processing intelligent query, falling back to original");
            return result; // Return with original query on error
        }
    }

    /// <summary>
    /// Result of intelligent query processing
    /// </summary>
    private class ProcessedQueryResult
    {
        public string OriginalQuery { get; set; } = string.Empty;
        public string FinalQuery { get; set; } = string.Empty;
        public string[] ExpandedTerms { get; set; } = Array.Empty<string>();
        public string[] ContextKeywords { get; set; } = Array.Empty<string>();
        public Dictionary<string, float> ContextBoosts { get; set; } = new();
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