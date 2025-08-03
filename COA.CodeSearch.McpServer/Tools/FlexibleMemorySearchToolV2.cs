using System.Text.Json;
using COA.CodeSearch.McpServer.Attributes;
using COA.CodeSearch.McpServer.Configuration;
using COA.CodeSearch.McpServer.Constants;
using COA.CodeSearch.McpServer.Infrastructure;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using COA.Mcp.Protocol;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// AI-optimized version of memory search with structured response format
/// </summary>
[McpServerToolType]
public class FlexibleMemorySearchToolV2 : ClaudeOptimizedToolBase
{
    public override string ToolName => "flexible_memory_search_v2";
    public override string Description => "AI-optimized memory search";
    public override ToolCategory Category => ToolCategory.Memory;
    private readonly FlexibleMemoryService _memoryService;
    private readonly IConfiguration _configuration;
    private readonly IContextAwarenessService _contextAwareness;
    private readonly AIResponseBuilderService _responseBuilder;
    private readonly MemoryLinkingTools _memoryLinking;

    public FlexibleMemorySearchToolV2(
        ILogger<FlexibleMemorySearchToolV2> logger,
        FlexibleMemoryService memoryService,
        IConfiguration configuration,
        IContextAwarenessService contextAwareness,
        AIResponseBuilderService responseBuilder,
        MemoryLinkingTools memoryLinking,
        IResponseSizeEstimator sizeEstimator,
        IResultTruncator truncator,
        IOptions<ResponseLimitOptions> options,
        IDetailRequestCache detailCache)
        : base(sizeEstimator, truncator, options, logger, detailCache)
    {
        _memoryService = memoryService;
        _configuration = configuration;
        _contextAwareness = contextAwareness;
        _responseBuilder = responseBuilder;
        _memoryLinking = memoryLinking;
    }

    [McpServerTool(Name = "search_memories")]
    [Description(@"Searches stored memories with intelligent query expansion.
Returns: Matching memories with scores, metadata, and relationships.
Prerequisites: None - searches existing memory database.
Error handling: Returns empty results if no matches found.
Use cases: Finding past decisions, reviewing technical debt, discovering patterns.
Features: Query expansion, context awareness, faceted filtering, smart ranking.")]
    public async Task<object> ExecuteAsync(FlexibleMemorySearchV2Params parameters)
    {
        if (parameters == null) throw new InvalidParametersException("Parameters are required");
        
        var mode = ResponseMode.Summary;
        if (!string.IsNullOrWhiteSpace(parameters.Mode))
        {
            mode = parameters.Mode.ToLowerInvariant() switch
            {
                "full" => ResponseMode.Full,
                "summary" => ResponseMode.Summary,
                _ => ResponseMode.Summary
            };
        }
        
        return await ExecuteAsync(
            parameters.Query,
            parameters.Types,
            parameters.DateRange,
            parameters.Facets,
            parameters.OrderBy,
            parameters.OrderDescending ?? true,
            parameters.MaxResults ?? 50,
            parameters.IncludeArchived ?? false,
            parameters.BoostRecent ?? false,
            parameters.BoostFrequent ?? false,
            // Removed EnableQueryExpansion - always use configured analyzer
            parameters.EnableContextAwareness ?? true,
            parameters.CurrentFile,
            parameters.RecentFiles,
            mode,
            false, // enableHighlighting - not exposed in params
            3, // maxFragments - not exposed in params
            100, // fragmentSize - not exposed in params
            null,
            CancellationToken.None);
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
        // Removed enableQueryExpansion - always use configured analyzer
        // Context awareness feature
        bool enableContextAwareness = true,
        string? currentFile = null,
        string[]? recentFiles = null,
        ResponseMode mode = ResponseMode.Summary,
        // Highlighting parameters
        bool enableHighlighting = false,
        int maxFragments = 3,
        int fragmentSize = 100,
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

            Logger.LogInformation("Memory search request: query={Query}, types={Types}, context={Context}", 
                query, types, enableContextAwareness);

            // Process query with context awareness
            var processedQuery = await ProcessIntelligentQueryAsync(query, enableContextAwareness, currentFile, recentFiles);
            
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
                BoostFrequent = boostFrequent,
                // Removed EnableQueryExpansion - always use configured analyzer
                EnableHighlighting = enableHighlighting,
                MaxFragments = maxFragments,
                FragmentSize = fragmentSize
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

            // Create AI-optimized response using response builder
            return _responseBuilder.BuildMemorySearchResponse(searchResult, request, query, mode);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in FlexibleMemorySearchV2");
            return CreateErrorResponse<object>(ex.Message);
        }
    }


    private async Task<object> HandleDetailRequestAsync(DetailRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrEmpty(request.DetailRequestToken))
            {
                return CreateErrorResponse<object>("Detail request token is required");
            }

            // Retrieve cached search data
            var cachedData = DetailCache?.GetDetailData<FlexibleMemorySearchResult>(request.DetailRequestToken);
            if (cachedData == null)
            {
                return CreateErrorResponse<object>("Invalid or expired detail request token. Please perform a new search.");
            }

            Logger.LogInformation("Processing detail request: level={Level}, targetItems={TargetCount}", 
                request.DetailLevelId, request.TargetItems?.Count ?? 0);

            return request.DetailLevelId switch
            {
                "full_content" => await GetFullContentDetailsAsync(cachedData, request),
                "memory_details" => await GetMemoryDetailsAsync(cachedData, request),
                "relationships" => await GetRelationshipDetailsAsync(cachedData, request),
                "file_analysis" => await GetFileAnalysisDetailsAsync(cachedData, request),
                _ => CreateErrorResponse<object>($"Unknown detail level: {request.DetailLevelId}. Available levels: full_content, memory_details, relationships, file_analysis")
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error processing detail request");
            return CreateErrorResponse<object>("Failed to process detail request: " + ex.Message);
        }
    }

    private Task<object> GetFullContentDetailsAsync(FlexibleMemorySearchResult cachedData, DetailRequest request)
    {
        var targetMemories = GetTargetMemories(cachedData, request.TargetItems);
        var maxResults = request.MaxResults ?? 10;

        var fullMemories = targetMemories.Take(maxResults).Select(memory => new
        {
            id = memory.Id,
            type = memory.Type,
            content = memory.Content, // Full content, not truncated
            created = memory.Created,
            modified = memory.Modified,
            files = memory.FilesInvolved,
            isShared = memory.IsShared,
            fields = memory.Fields,
            highlights = memory.Highlights
        }).ToList();

        return Task.FromResult<object>(new
        {
            success = true,
            detailLevel = "full_content",
            memories = fullMemories,
            metadata = new
            {
                totalResults = targetMemories.Count,
                returnedResults = fullMemories.Count,
                estimatedTokens = EstimateTokens(fullMemories)
            }
        });
    }

    private async Task<object> GetMemoryDetailsAsync(FlexibleMemorySearchResult cachedData, DetailRequest request)
    {
        var targetMemories = GetTargetMemories(cachedData, request.TargetItems);
        var maxResults = request.MaxResults ?? 5;

        var detailedMemories = new List<object>();
        
        foreach (var memory in targetMemories.Take(maxResults))
        {
            // Get related memories for each target memory
            var relatedMemoriesResult = await _memoryLinking.GetRelatedMemoriesAsync(memory.Id, maxDepth: 1);
            var relatedMemories = relatedMemoriesResult?.RelatedMemories;
            
            detailedMemories.Add(new
            {
                id = memory.Id,
                type = memory.Type,
                content = memory.Content,
                created = memory.Created,
                modified = memory.Modified,
                files = memory.FilesInvolved,
                isShared = memory.IsShared,
                fields = memory.Fields,
                highlights = memory.Highlights,
                relatedCount = relatedMemories?.Count ?? 0,
                relatedMemories = relatedMemories?.Take(3).Select(r => (object)new
                {
                    id = r.Memory.Id,
                    type = r.Memory.Type,
                    content = r.Memory.Content.Length > 100 ? r.Memory.Content.Substring(0, 100) + "..." : r.Memory.Content,
                    relationshipType = r.RelationshipType
                }).ToList() ?? new List<object>()
            });
        }

        return new
        {
            success = true,
            detailLevel = "memory_details",
            memories = detailedMemories,
            metadata = new
            {
                totalResults = targetMemories.Count,
                returnedResults = detailedMemories.Count,
                estimatedTokens = EstimateTokens(detailedMemories)
            }
        };
    }

    private async Task<object> GetRelationshipDetailsAsync(FlexibleMemorySearchResult cachedData, DetailRequest request)
    {
        var targetMemories = GetTargetMemories(cachedData, request.TargetItems);
        var maxResults = request.MaxResults ?? 3;

        var relationshipData = new List<object>();

        foreach (var memory in targetMemories.Take(maxResults))
        {
            var relatedMemoriesResult = await _memoryLinking.GetRelatedMemoriesAsync(memory.Id, maxDepth: 2);
            var relatedMemories = relatedMemoriesResult?.RelatedMemories;
            
            relationshipData.Add(new
            {
                sourceMemory = new
                {
                    id = memory.Id,
                    type = memory.Type,
                    content = memory.Content.Length > 150 ? memory.Content.Substring(0, 150) + "..." : memory.Content
                },
                relationships = relatedMemories?.Select(r => (object)new
                {
                    id = r.Memory.Id,
                    type = r.Memory.Type,
                    content = r.Memory.Content.Length > 100 ? r.Memory.Content.Substring(0, 100) + "..." : r.Memory.Content,
                    relationshipType = r.RelationshipType,
                    created = r.Memory.Created
                }).ToList() ?? new List<object>(),
                relationshipCount = relatedMemories?.Count ?? 0
            });
        }

        return new
        {
            success = true,
            detailLevel = "relationships",
            relationshipData,
            metadata = new
            {
                totalResults = targetMemories.Count,
                returnedResults = relationshipData.Count,
                estimatedTokens = EstimateTokens(relationshipData)
            }
        };
    }

    private Task<object> GetFileAnalysisDetailsAsync(FlexibleMemorySearchResult cachedData, DetailRequest request)
    {
        var targetMemories = GetTargetMemories(cachedData, request.TargetItems);

        // Analyze file references across all target memories
        var fileAnalysis = targetMemories
            .SelectMany(m => m.FilesInvolved.Select(f => new { file = f, memory = m }))
            .GroupBy(x => x.file)
            .Select(g => new
            {
                filePath = g.Key,
                fileName = Path.GetFileName(g.Key),
                referencesCount = g.Count(),
                memoryTypes = g.Select(x => x.memory.Type).Distinct().ToList(),
                memories = g.Select(x => new
                {
                    id = x.memory.Id,
                    type = x.memory.Type,
                    content = x.memory.Content.Length > 80 ? x.memory.Content.Substring(0, 80) + "..." : x.memory.Content,
                    created = x.memory.Created
                }).ToList()
            })
            .OrderByDescending(f => f.referencesCount)
            .Take(request.MaxResults ?? 10)
            .ToList();

        return Task.FromResult<object>(new
        {
            success = true,
            detailLevel = "file_analysis",
            fileAnalysis,
            summary = new
            {
                totalFiles = fileAnalysis.Count,
                totalReferences = fileAnalysis.Sum(f => f.referencesCount),
                mostReferencedFile = fileAnalysis.FirstOrDefault()?.fileName
            },
            metadata = new
            {
                totalResults = fileAnalysis.Count,
                returnedResults = fileAnalysis.Count,
                estimatedTokens = EstimateTokens(fileAnalysis)
            }
        });
    }

    private List<FlexibleMemoryEntry> GetTargetMemories(FlexibleMemorySearchResult cachedData, List<string>? targetItems)
    {
        if (targetItems == null || !targetItems.Any())
        {
            return cachedData.Memories;
        }

        // Filter by memory IDs if specified
        return cachedData.Memories.Where(m => targetItems.Contains(m.Id)).ToList();
    }

    private int EstimateTokens(object data)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(data);
            return Math.Max(50, json.Length / 4); // Rough estimation: 4 chars per token
        }
        catch
        {
            return 100; // Conservative fallback
        }
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
    /// Process query with context awareness
    /// </summary>
    private async Task<ProcessedQueryResult> ProcessIntelligentQueryAsync(
        string? originalQuery, 
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
            // Context Awareness
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

                // Track this search for future context
                // Note: ResultsFound will be updated after the actual search
                await _contextAwareness.TrackSearchQueryAsync(originalQuery, 0);
            }

            return result;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error processing query with context, falling back to original");
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
        public string[] ContextKeywords { get; set; } = Array.Empty<string>();
    }
}

/// <summary>
/// Parameters for FlexibleMemorySearchToolV2
/// </summary>
public class FlexibleMemorySearchV2Params
{
    [Description("Search query (* for all)")]
    public string? Query { get; set; }
    
    [Description("Filter by memory types")]
    public string[]? Types { get; set; }
    
    [Description("Relative time: 'last-week', 'last-month', 'last-7-days'")]
    public string? DateRange { get; set; }
    
    [Description("Field filters (e.g., {\"status\": \"pending\", \"priority\": \"high\"})")]
    public Dictionary<string, string>? Facets { get; set; }
    
    [Description("Sort field: 'created', 'modified', 'type', 'score', or custom field")]
    public string? OrderBy { get; set; }
    
    [Description("Sort order (default: true)")]
    public bool? OrderDescending { get; set; }
    
    [Description("Maximum results (default: 50)")]
    public int? MaxResults { get; set; }
    
    [Description("Include archived memories (default: false)")]
    public bool? IncludeArchived { get; set; }
    
    [Description("Boost recently created memories")]
    public bool? BoostRecent { get; set; }
    
    [Description("Boost frequently accessed memories")]
    public bool? BoostFrequent { get; set; }
    
    // Removed EnableQueryExpansion - always use configured analyzer for consistency
    
    [Description("Enable context-aware memory boosting based on current work")]
    public bool? EnableContextAwareness { get; set; }
    
    [Description("Path to current file being worked on (for context awareness)")]
    public string? CurrentFile { get; set; }
    
    [Description("Recently accessed files (for context awareness)")]
    public string[]? RecentFiles { get; set; }
    
    [Description("Response mode: 'summary' (default) or 'full'")]
    public string? Mode { get; set; }
}