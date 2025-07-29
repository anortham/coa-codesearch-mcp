using COA.CodeSearch.McpServer.Infrastructure;
using COA.CodeSearch.McpServer.Models;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Service for building AI-optimized responses with contextual actions and token efficiency
/// </summary>
public class AIResponseBuilderService
{
    private readonly ILogger<AIResponseBuilderService> _logger;
    private readonly ITokenEstimationService _tokenEstimator;
    private readonly IDetailRequestCache _detailCache;

    // Token budgets for different response modes
    private const int SummaryTokenBudget = 1500;
    private const int FullTokenBudget = 4000;
    private const int MaxActionTokens = 300;

    public AIResponseBuilderService(
        ILogger<AIResponseBuilderService> logger,
        ITokenEstimationService tokenEstimator,
        IDetailRequestCache detailCache)
    {
        _logger = logger;
        _tokenEstimator = tokenEstimator;
        _detailCache = detailCache;
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
                cached = GenerateCacheKey("memory_search"),
                detailRequestToken = detailRequestToken,
                availableDetailLevels = availableDetailLevels
            }
        };
    }

    /// <summary>
    /// Build AI-optimized response for text search results
    /// </summary>
    public object BuildTextSearchResponse(
        string query,
        string searchType,
        string workspacePath,
        List<TextSearchResult> results,
        long totalHits,
        string? filePattern,
        string[]? extensions,
        ResponseMode mode,
        ProjectContext? projectContext,
        long? alternateHits,
        Dictionary<string, int>? alternateExtensions)
    {
        var tokenBudget = mode == ResponseMode.Summary ? SummaryTokenBudget : FullTokenBudget;

        // Group by extension
        var byExtension = results
            .GroupBy(r => r.Extension)
            .ToDictionary(
                g => g.Key,
                g => new { count = g.Count(), files = g.Select(r => r.FileName).Distinct().Count() }
            );

        // Group by directory
        var byDirectory = results
            .GroupBy(r => Path.GetDirectoryName(r.RelativePath) ?? "root")
            .OrderByDescending(g => g.Count())
            .Take(5)
            .ToDictionary(
                g => g.Key,
                g => g.Count()
            );

        // Find hotspot files
        var hotspots = results
            .GroupBy(r => r.RelativePath)
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => new TextSearchHotspot
            { 
                File = g.Key, 
                Matches = g.Count(),
                Lines = g.SelectMany(r => r.Context?.Where(c => c.IsMatch).Select(c => c.LineNumber) ?? Enumerable.Empty<int>()).Distinct().Count()
            })
            .ToList();

        // Generate insights
        var insights = GenerateTextSearchInsights(query, searchType, workspacePath, results, totalHits, filePattern, extensions, projectContext, alternateHits, alternateExtensions);

        // Generate actions
        var actions = GenerateTextSearchActions(query, searchType, results, totalHits, hotspots, 
            byExtension.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value), mode);

        // Determine how many results to include inline based on token budget, mode, and context
        var hasContext = results.Any(r => r.Context?.Any() == true);
        var maxInlineResults = hasContext ? 5 : 10; // Fewer results when including context
        var includeResults = mode == ResponseMode.Full || results.Count <= maxInlineResults;
        var inlineResults = includeResults ? results : results.Take(maxInlineResults).ToList();
        
        // Pre-estimate response size and apply hard safety limit
        var preEstimatedTokens = EstimateTextSearchResponseTokens(inlineResults) + 500; // Add overhead for metadata
        var safetyLimitApplied = false;
        if (preEstimatedTokens > 5000)
        {
            _logger.LogWarning("Pre-estimated response ({Tokens} tokens) exceeds safety threshold. Forcing minimal results.", preEstimatedTokens);
            // Force minimal results to ensure we stay under limit
            inlineResults = results.Take(3).ToList();
            // Remove context from these results to save even more tokens
            foreach (var result in inlineResults)
            {
                result.Context = null;
            }
            safetyLimitApplied = true;
            // Add a warning to insights
            insights.Insert(0, $"⚠️ Response size limit applied ({preEstimatedTokens} tokens). Showing 3 results without context.");
        }
        
        // Store data in cache for detail requests if available
        string? detailRequestToken = null;
        if (mode == ResponseMode.Summary && _detailCache != null && results.Count > inlineResults.Count)
        {
            detailRequestToken = _detailCache.StoreDetailData(new { results, query, summary = new { totalHits }, distribution = new { byExtension, byDirectory }, hotspots });
        }

        // Create response object with hybrid approach
        var response = new
        {
            success = true,
            operation = "text_search",
            query = new
            {
                text = query,
                type = searchType,
                filePattern = filePattern,
                extensions = extensions,
                workspace = workspacePath
            },
            summary = new
            {
                totalHits = totalHits,
                returnedResults = results.Count,
                filesMatched = results.Select(r => r.FilePath).Distinct().Count(),
                truncated = totalHits > results.Count
            },
            results = inlineResults.Select(r => new
            {
                file = r.FileName,
                path = r.RelativePath,
                score = Math.Round(r.Score, 2),
                context = r.Context?.Any() == true ? r.Context.Select(c => new
                {
                    line = c.LineNumber,
                    content = c.Content,
                    match = c.IsMatch
                }).ToList() : null
            }).ToList(),
            resultsSummary = new
            {
                included = inlineResults.Count,
                total = results.Count,
                hasMore = results.Count > inlineResults.Count
            },
            distribution = new
            {
                byExtension = byExtension,
                byDirectory = byDirectory
            },
            hotspots = hotspots.Select(h => new { file = h.File, matches = h.Matches, lines = h.Lines }).ToList(),
            insights = insights,
            actions = actions.Select(a => a is AIAction aiAction ? new
            {
                id = aiAction.Id,
                cmd = aiAction.Command.Parameters,
                tokens = aiAction.EstimatedTokens,
                priority = aiAction.Priority.ToString().ToLowerInvariant()
            } : a),
            meta = new
            {
                mode = safetyLimitApplied ? "safety-limited" : mode.ToString().ToLowerInvariant(),
                indexed = true,
                tokens = EstimateTextSearchResponseTokens(inlineResults),
                cached = $"txt_{Guid.NewGuid().ToString("N")[..8]}",
                safetyLimitApplied = safetyLimitApplied,
                originalEstimatedTokens = safetyLimitApplied ? preEstimatedTokens : (int?)null,
                detailRequestToken = detailRequestToken
            }
        };

        return response;
    }

    /// <summary>
    /// Build AI-optimized response for text search results using JsonNode (POC)
    /// </summary>
    public JsonNode BuildTextSearchResponseAsJsonNode(
        string query,
        string searchType,
        string workspacePath,
        List<TextSearchResult> results,
        long totalHits,
        string? filePattern,
        string[]? extensions,
        ResponseMode mode,
        ProjectContext? projectContext,
        long? alternateHits,
        Dictionary<string, int>? alternateExtensions)
    {
        var tokenBudget = mode == ResponseMode.Summary ? SummaryTokenBudget : FullTokenBudget;

        // Create response using JsonNode
        var response = new JsonObject
        {
            ["success"] = true,
            ["operation"] = "text_search"
        };

        // Build query object
        var queryObj = new JsonObject
        {
            ["text"] = query,
            ["type"] = searchType,
            ["workspace"] = workspacePath
        };
        if (filePattern != null) queryObj["filePattern"] = filePattern;
        if (extensions != null) queryObj["extensions"] = JsonValue.Create(extensions);
        response["query"] = queryObj;

        // Build summary
        var summary = new JsonObject
        {
            ["totalHits"] = totalHits,
            ["returnedResults"] = results.Count,
            ["filesMatched"] = results.Select(r => r.FilePath).Distinct().Count(),
            ["truncated"] = totalHits > results.Count
        };
        response["summary"] = summary;

        // Group by extension
        var byExtension = results
            .GroupBy(r => r.Extension)
            .ToDictionary(
                g => g.Key,
                g => new { count = g.Count(), files = g.Select(r => r.FileName).Distinct().Count() }
            );

        // Group by directory
        var byDirectory = results
            .GroupBy(r => Path.GetDirectoryName(r.RelativePath) ?? "root")
            .OrderByDescending(g => g.Count())
            .Take(5)
            .ToDictionary(
                g => g.Key,
                g => g.Count()
            );

        // Build distribution
        var distribution = new JsonObject();
        
        // Extension distribution
        var extDistribution = new JsonObject();
        foreach (var (ext, data) in byExtension)
        {
            extDistribution[ext] = new JsonObject
            {
                ["count"] = data.count,
                ["files"] = data.files
            };
        }
        distribution["byExtension"] = extDistribution;
        
        // Directory distribution
        var dirDistribution = new JsonObject();
        foreach (var (dir, count) in byDirectory)
        {
            dirDistribution[dir] = count;
        }
        distribution["byDirectory"] = dirDistribution;
        
        response["distribution"] = distribution;

        // Find hotspot files
        var hotspots = results
            .GroupBy(r => r.RelativePath)
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => new
            { 
                file = g.Key, 
                matches = g.Count(),
                lines = g.SelectMany(r => r.Context?.Where(c => c.IsMatch).Select(c => c.LineNumber) ?? Enumerable.Empty<int>()).Distinct().Count()
            });

        // Build hotspots array
        var hotspotsArray = new JsonArray();
        foreach (var hotspot in hotspots)
        {
            hotspotsArray.Add(new JsonObject
            {
                ["file"] = hotspot.file,
                ["matches"] = hotspot.matches,
                ["lines"] = hotspot.lines
            });
        }
        response["hotspots"] = hotspotsArray;

        // Generate insights
        var insights = GenerateTextSearchInsights(query, searchType, workspacePath, results, totalHits, filePattern, extensions, projectContext, alternateHits, alternateExtensions);
        var insightsArray = new JsonArray();
        foreach (var insight in insights)
        {
            insightsArray.Add(insight);
        }
        response["insights"] = insightsArray;

        // Generate actions
        var actions = GenerateTextSearchActions(query, searchType, results, totalHits, 
            hotspots.Select(h => new TextSearchHotspot { File = h.file, Matches = h.matches, Lines = h.lines }).ToList(),
            byExtension.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value), mode);

        // Build actions array
        var actionsArray = new JsonArray();
        foreach (var action in actions)
        {
            if (action is AIAction aiAction)
            {
                actionsArray.Add(new JsonObject
                {
                    ["id"] = aiAction.Id,
                    ["cmd"] = JsonNode.Parse(JsonSerializer.Serialize(aiAction.Command.Parameters)),
                    ["tokens"] = aiAction.EstimatedTokens,
                    ["priority"] = aiAction.Priority.ToString().ToLowerInvariant()
                });
            }
            else
            {
                // Handle legacy action format
                actionsArray.Add(JsonNode.Parse(JsonSerializer.Serialize(action)));
            }
        }
        response["actions"] = actionsArray;

        // Determine how many results to include inline
        var hasContext = results.Any(r => r.Context?.Any() == true);
        var maxInlineResults = hasContext ? 5 : 10;
        var includeResults = mode == ResponseMode.Full || results.Count <= maxInlineResults;
        var inlineResults = includeResults ? results : results.Take(maxInlineResults).ToList();
        
        // Pre-estimate response size and apply safety limit
        var preEstimatedTokens = EstimateTextSearchResponseTokens(inlineResults) + 500;
        var safetyLimitApplied = false;
        if (preEstimatedTokens > 5000)
        {
            _logger.LogWarning("Pre-estimated response ({Tokens} tokens) exceeds safety threshold. Forcing minimal results.", preEstimatedTokens);
            inlineResults = results.Take(3).ToList();
            foreach (var result in inlineResults)
            {
                result.Context = null;
            }
            safetyLimitApplied = true;
            insightsArray.Insert(0, JsonValue.Create($"⚠️ Response size limit applied ({preEstimatedTokens} tokens). Showing 3 results without context."));
        }

        // Build results array
        var resultsArray = new JsonArray();
        foreach (var result in inlineResults)
        {
            var resultObj = new JsonObject
            {
                ["file"] = result.FileName,
                ["path"] = result.RelativePath,
                ["score"] = Math.Round(result.Score, 2)
            };
            
            // Add context if available
            if (result.Context?.Any() == true)
            {
                var contextArray = new JsonArray();
                foreach (var ctx in result.Context)
                {
                    contextArray.Add(new JsonObject
                    {
                        ["line"] = ctx.LineNumber,
                        ["content"] = ctx.Content,
                        ["match"] = ctx.IsMatch
                    });
                }
                resultObj["context"] = contextArray;
            }
            
            resultsArray.Add(resultObj);
        }
        response["results"] = resultsArray;

        // Results summary
        response["resultsSummary"] = new JsonObject
        {
            ["included"] = inlineResults.Count,
            ["total"] = results.Count,
            ["hasMore"] = results.Count > inlineResults.Count
        };

        // Meta information
        response["meta"] = new JsonObject
        {
            ["mode"] = safetyLimitApplied ? "safety-limited" : mode.ToString().ToLowerInvariant(),
            ["indexed"] = true,
            ["tokens"] = EstimateTextSearchResponseTokens(inlineResults),
            ["cached"] = $"txt_{Guid.NewGuid().ToString("N")[..8]}",
            ["safetyLimitApplied"] = safetyLimitApplied
        };
        
        if (safetyLimitApplied)
        {
            response["meta"]["originalEstimatedTokens"] = preEstimatedTokens;
        }

        // Store detail request token if applicable
        if (mode == ResponseMode.Summary && _detailCache != null && results.Count > inlineResults.Count)
        {
            var detailData = new
            {
                results,
                query = response["query"],
                summary = response["summary"],
                distribution = response["distribution"],
                hotspots = response["hotspots"]
            };
            var detailRequestToken = _detailCache.StoreDetailData(detailData);
            response["meta"]["detailRequestToken"] = detailRequestToken;
        }

        return response;
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

    private List<DetailLevel> CreateMemoryDetailLevels(FlexibleMemorySearchResult searchResult)
    {
        var levels = new List<DetailLevel>();

        // Full content detail level
        levels.Add(new DetailLevel
        {
            Id = "full_content",
            Name = "Full Content",
            Description = "Complete memory content without truncation",
            EstimatedTokens = searchResult.Memories.Sum(m => Math.Max(50, m.Content.Length / 4)),
            IsActive = false
        });

        // Memory details with relationships
        levels.Add(new DetailLevel
        {
            Id = "memory_details",
            Name = "Memory Details",
            Description = "Full memory details including related memories and custom fields",
            EstimatedTokens = searchResult.Memories.Count * 200, // Rough estimate
            IsActive = false
        });

        // Relationship analysis
        if (searchResult.Memories.Count > 1)
        {
            levels.Add(new DetailLevel
            {
                Id = "relationships",
                Name = "Relationship Analysis",
                Description = "Deep analysis of relationships between memories",
                EstimatedTokens = searchResult.Memories.Count * 150,
                IsActive = false
            });
        }

        // File analysis if memories have file references
        var totalFiles = searchResult.Memories.SelectMany(m => m.FilesInvolved).Distinct().Count();
        if (totalFiles > 0)
        {
            levels.Add(new DetailLevel
            {
                Id = "file_analysis",
                Name = "File Analysis",
                Description = $"Analysis of {totalFiles} referenced files across memories",
                EstimatedTokens = totalFiles * 100,
                IsActive = false
            });
        }

        return levels;
    }

    /// <summary>
    /// Build AI-optimized response for file search results
    /// </summary>
    public object BuildFileSearchResponse(
        string query,
        string? searchType,
        string workspacePath,
        List<FileSearchResult> results,
        double searchDurationMs,
        Dictionary<string, int> extensionCounts,
        Dictionary<string, int> directoryCounts,
        Dictionary<string, int> languageCounts,
        ResponseMode mode)
    {
        var tokenBudget = mode == ResponseMode.Summary ? SummaryTokenBudget : FullTokenBudget;

        // Generate insights
        var insights = GenerateFileSearchInsights(results, query, searchDurationMs);

        // Find hotspots (directories with high concentration)
        var hotspots = directoryCounts
            .Where(kv => kv.Value >= 1)
            .OrderByDescending(kv => kv.Value)
            .Take(3)
            .Select(kv => new { path = kv.Key, count = kv.Value })
            .ToList();

        // Generate actions
        var actions = GenerateFileSearchActions(query, searchType, results, extensionCounts, directoryCounts);

        // Prepare results based on mode
        var resultsToInclude = mode == ResponseMode.Full 
            ? results.Select(r => new
            {
                path = r.Path,
                filename = Path.GetFileName(r.Path),
                relativePath = GetRelativePath(r.Path, workspacePath),
                extension = Path.GetExtension(r.Path),
                score = Math.Round(r.Score, 3)
            }).ToList<object>()
            : results.Take(10).Select(r => new
            {
                file = Path.GetFileName(r.Path),
                path = GetRelativePath(r.Path, workspacePath),
                score = Math.Round(r.Score, 2)
            }).ToList<object>();

        // Create the response
        var response = new
        {
            success = true,
            operation = "file_search",
            query = new
            {
                text = query,
                type = searchType,
                workspace = Path.GetFileName(workspacePath)
            },
            summary = new
            {
                totalFound = results.Count,
                searchTime = $"{searchDurationMs:F1}ms",
                performance = searchDurationMs < 10 ? "excellent" : searchDurationMs < 50 ? "fast" : "normal",
                distribution = new
                {
                    byExtension = extensionCounts
                        .OrderByDescending(kv => kv.Value)
                        .Take(5)
                        .ToDictionary(kv => kv.Key, kv => kv.Value),
                    byLanguage = languageCounts
                        .OrderByDescending(kv => kv.Value)
                        .Take(3)
                        .ToDictionary(kv => kv.Key, kv => kv.Value)
                }
            },
            analysis = new
            {
                patterns = AnalyzeFileSearchPatterns(results, extensionCounts, directoryCounts).Take(3).ToList(),
                matchQuality = AnalyzeMatchQuality(query, results),
                hotspots = new
                {
                    directories = hotspots
                }
            },
            results = resultsToInclude,
            resultsSummary = new
            {
                included = resultsToInclude.Count,
                total = results.Count,
                hasMore = results.Count > resultsToInclude.Count
            },
            insights = insights,
            actions = actions,
            meta = new
            {
                mode = mode.ToString().ToLowerInvariant(),
                truncated = false,
                tokens = EstimateFileSearchResponseTokens(results),
                cached = GenerateCacheKey("filesearch")
            }
        };

        return response;
    }

    private List<string> GenerateFileSearchInsights(List<FileSearchResult> results, string query, double searchDurationMs)
    {
        var insights = new List<string>();

        // Basic result insight
        if (results.Count == 0)
        {
            insights.Add($"No files matching '{query}'");
            insights.Add("Try fuzzy or wildcard search for approximate matches");
        }
        else
        {
            insights.Add($"Found {results.Count} files in {searchDurationMs:F0}ms");
        }

        // Performance insight
        if (searchDurationMs < 10)
        {
            insights.Add("⚡ Excellent search performance");
        }

        // Ensure we always have at least one insight
        if (insights.Count == 0)
        {
            insights.Add($"Found {results.Count} files matching '{query}'");
        }

        return insights;
    }

    private List<object> GenerateFileSearchActions(
        string query,
        string? searchType,
        List<FileSearchResult> results,
        Dictionary<string, int> extensionCounts,
        Dictionary<string, int> directoryCounts)
    {
        var actions = new List<object>();

        // Open file action
        if (results.Any())
        {
            var topResult = results.OrderByDescending(r => r.Score).First();
            actions.Add(new
            {
                id = "open_file",
                cmd = new { file = topResult.Path },
                tokens = 100,
                priority = "recommended"
            });
        }

        // Search refinement actions
        if (results.Count > 20)
        {
            // Filter by extension
            var topExt = extensionCounts.OrderByDescending(kv => kv.Value).First();
            actions.Add(new
            {
                id = "filter_by_type",
                cmd = new { query = query, filter = $"*.{topExt.Key}" },
                tokens = 500,
                priority = "recommended"
            });

            // Search in specific directory
            if (directoryCounts.Any(kv => kv.Value > 3))
            {
                var topDir = directoryCounts.OrderByDescending(kv => kv.Value).First();
                actions.Add(new
                {
                    id = "search_in_directory",
                    cmd = new { query = query, path = topDir.Key },
                    tokens = 300,
                    priority = "available"
                });
            }
        }

        // Alternative search suggestions
        if (results.Count == 0)
        {
            actions.Add(new
            {
                id = "try_fuzzy_search",
                cmd = new { query = $"{query}~", searchType = "fuzzy" },
                tokens = 200,
                priority = "recommended"
            });

            actions.Add(new
            {
                id = "try_wildcard_search",
                cmd = new { query = $"*{query}*", searchType = "wildcard" },
                tokens = 200,
                priority = "recommended"
            });
        }

        // Content search in found files
        if (results.Count > 0 && results.Count < 20)
        {
            actions.Add(new
            {
                id = "search_in_files",
                cmd = new
                {
                    operation = "text_search",
                    files = results.Take(10).Select(r => r.Path).ToList()
                },
                tokens = 1500,
                priority = "available"
            });
        }

        // Ensure we always have at least one action
        if (actions.Count == 0)
        {
            if (results.Count > 0)
            {
                actions.Add(new
                {
                    id = "explore_results",
                    cmd = new { expand = "details" },
                    tokens = 1000,
                    priority = "available"
                });
            }
            else
            {
                actions.Add(new
                {
                    id = "broaden_search",
                    cmd = new { query = $"*{query}*", searchType = "wildcard" },
                    tokens = 1500,
                    priority = "recommended"
                });
            }
        }

        return actions;
    }

    private List<string> AnalyzeFileSearchPatterns(
        List<FileSearchResult> results,
        Dictionary<string, int> extensionCounts,
        Dictionary<string, int> directoryCounts)
    {
        var patterns = new List<string>();

        if (results.Count == 0)
        {
            patterns.Add("No matches found - check spelling or use fuzzy search");
        }
        else if (results.Count == 1)
        {
            patterns.Add("Single match - precise search result");
        }
        else if (results.Count >= 40)
        {
            patterns.Add("Many matches - consider refining search");
        }

        // Extension patterns
        if (extensionCounts.Count == 1)
        {
            patterns.Add($"All results are {extensionCounts.First().Key} files");
        }
        else if (extensionCounts.Any(kv => kv.Value > results.Count * 0.7))
        {
            var dominant = extensionCounts.OrderByDescending(kv => kv.Value).First();
            patterns.Add($"Predominantly {dominant.Key} files ({dominant.Value * 100 / results.Count}%)");
        }

        // Directory concentration
        if (directoryCounts.Any(kv => kv.Value > results.Count * 0.5))
        {
            var concentrated = directoryCounts.OrderByDescending(kv => kv.Value).First();
            patterns.Add($"Concentrated in {concentrated.Key} directory");
        }

        return patterns;
    }

    private object AnalyzeMatchQuality(string query, List<FileSearchResult> results)
    {
        var exactMatches = 0;
        var partialMatches = 0;
        var fuzzyMatches = 0;
        var totalScore = 0f;

        foreach (var result in results)
        {
            var filename = Path.GetFileName(result.Path).ToLower();
            var queryLower = query.ToLower();

            if (filename == queryLower)
                exactMatches++;
            else if (filename.Contains(queryLower))
                partialMatches++;
            else
                fuzzyMatches++;

            totalScore += (float)result.Score;
        }

        return new
        {
            exactMatches = exactMatches,
            partialMatches = partialMatches,
            fuzzyMatches = fuzzyMatches,
            avgScore = results.Any() ? totalScore / results.Count : 0
        };
    }

    private string GetRelativePath(string fullPath, string workspacePath)
    {
        if (fullPath.StartsWith(workspacePath, StringComparison.OrdinalIgnoreCase))
        {
            var relativePath = fullPath.Substring(workspacePath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return relativePath.Replace(Path.DirectorySeparatorChar, '/');
        }
        return fullPath;
    }

    private int EstimateFileSearchResponseTokens(List<FileSearchResult> results)
    {
        // Base tokens for structure
        var baseTokens = 200;
        
        // Per result tokens
        var perResultTokens = 30;
        
        // Additional for statistics
        var statsTokens = 100;
        
        return baseTokens + (results.Count * perResultTokens) + statsTokens;
    }

    #endregion

    #region Directory Search Implementation

    /// <summary>
    /// Build AI-optimized response for directory search results
    /// </summary>
    public object BuildDirectorySearchResponse(
        string query,
        string? searchType,
        string workspacePath,
        List<dynamic> results,
        double searchDurationMs,
        ResponseMode mode,
        bool groupByDirectory,
        long totalHits)
    {
        var tokenBudget = mode == ResponseMode.Summary ? SummaryTokenBudget : FullTokenBudget;

        // Generate insights
        var insights = GenerateDirectorySearchInsights(results, query, searchDurationMs, totalHits);

        // Extract distribution data from results if grouped
        Dictionary<string, int> extensionCounts = new Dictionary<string, int>();
        Dictionary<string, int> depthDistribution = new Dictionary<string, int>();
        List<object> hotspots = new List<object>();

        if (groupByDirectory && results.Any())
        {
            foreach (var result in results)
            {
                // Count by depth
                int depth = result.depth;
                var depthKey = $"level_{depth}";
                if (!depthDistribution.ContainsKey(depthKey))
                    depthDistribution[depthKey] = 0;
                depthDistribution[depthKey]++;

                // Extract file types if available
                if (result.fileTypes != null)
                {
                    foreach (string ext in result.fileTypes)
                    {
                        if (!extensionCounts.ContainsKey(ext))
                            extensionCounts[ext] = 0;
                        extensionCounts[ext]++;
                    }
                }

                // Find hotspots (directories with many files)
                if (result.fileCount > 10)
                {
                    hotspots.Add(new
                    {
                        path = result.relativePath,
                        fileCount = result.fileCount,
                        fileTypes = result.fileTypes
                    });
                }
            }

            // Sort hotspots by file count
            hotspots = hotspots
                .OrderByDescending(h => ((dynamic)h).fileCount)
                .Take(5)
                .ToList();
        }

        // Generate actions
        var actions = GenerateDirectorySearchActions(query, searchType, results, groupByDirectory, mode);

        // Prepare results based on mode
        var resultsToInclude = mode == ResponseMode.Full 
            ? results
            : results.Take(20).ToList();

        // Create the response
        var response = new
        {
            success = true,
            operation = "directory_search",
            query = new
            {
                text = query,
                type = searchType ?? "standard",
                workspace = Path.GetFileName(workspacePath),
                grouped = groupByDirectory
            },
            summary = new
            {
                totalFound = totalHits,
                returnedDirectories = results.Count,
                searchTime = $"{searchDurationMs:F1}ms",
                performance = searchDurationMs < 20 ? "excellent" : searchDurationMs < 50 ? "fast" : "normal",
                averageDepth = results.Any() ? results.Average(r => (double)r.depth) : 0
            },
            analysis = new
            {
                patterns = AnalyzeDirectorySearchPatterns(results, extensionCounts, depthDistribution).Take(3).ToList(),
                distribution = new
                {
                    byDepth = depthDistribution,
                    byFileTypes = extensionCounts
                        .OrderByDescending(kv => kv.Value)
                        .Take(5)
                        .ToDictionary(kv => kv.Key, kv => kv.Value)
                },
                hotspots = new
                {
                    largeFolders = hotspots
                }
            },
            results = resultsToInclude,
            resultsSummary = new
            {
                included = resultsToInclude.Count,
                total = results.Count,
                hasMore = results.Count > resultsToInclude.Count
            },
            insights = insights,
            actions = actions,
            meta = new
            {
                mode = mode.ToString().ToLowerInvariant(),
                truncated = false,
                tokens = EstimateDirectorySearchResponseTokens(results),
                cached = GenerateCacheKey("dirsearch")
            }
        };

        return response;
    }

    private List<string> GenerateDirectorySearchInsights(List<dynamic> results, string query, double searchDurationMs, long totalHits)
    {
        var insights = new List<string>();

        // Basic result insight
        if (results.Count == 0)
        {
            insights.Add($"No directories matching '{query}'");
            insights.Add("Try fuzzy or wildcard search for approximate matches");
        }
        else
        {
            insights.Add($"Found {results.Count} directories in {searchDurationMs:F0}ms");
        }

        // Performance insight
        if (searchDurationMs < 20)
        {
            insights.Add("⚡ Excellent search performance");
        }

        // Structure insights
        if (results.Any())
        {
            var avgDepth = results.Average(r => (double)r.depth);
            if (avgDepth > 3)
            {
                insights.Add($"Deep directory structure detected (avg depth: {avgDepth:F1})");
            }

            // Check for common patterns
            var hasTestDirs = results.Any(r => ((string)r.directoryName).Contains("test", StringComparison.OrdinalIgnoreCase));
            var hasSrcDirs = results.Any(r => ((string)r.directoryName).Contains("src", StringComparison.OrdinalIgnoreCase));
            
            if (hasTestDirs && hasSrcDirs)
            {
                insights.Add("Project follows standard src/test structure");
            }
        }

        return insights;
    }

    private List<object> GenerateDirectorySearchActions(
        string query,
        string? searchType,
        List<dynamic> results,
        bool groupByDirectory,
        ResponseMode mode)
    {
        var actions = new List<object>();

        // Navigate to directory action
        if (results.Any())
        {
            var topResult = results.First();
            actions.Add(new
            {
                id = "explore_directory",
                cmd = new { operation = "ls", path = topResult.path },
                tokens = 200,
                priority = "recommended"
            });

            // Search within directory
            actions.Add(new
            {
                id = "search_in_directory",
                cmd = new
                {
                    operation = "text_search",
                    workspacePath = topResult.path,
                    query = "*"
                },
                tokens = 1500,
                priority = "available"
            });
        }

        // Alternative search suggestions
        if (results.Count == 0)
        {
            if (searchType != "fuzzy")
            {
                actions.Add(new
                {
                    id = "try_fuzzy_search",
                    cmd = new { query = $"{query}~", searchType = "fuzzy" },
                    tokens = 200,
                    priority = "recommended"
                });
            }

            if (searchType != "wildcard")
            {
                actions.Add(new
                {
                    id = "try_wildcard_search",
                    cmd = new { query = $"*{query}*", searchType = "wildcard" },
                    tokens = 200,
                    priority = "recommended"
                });
            }
        }

        // Group/ungroup toggle
        if (results.Count > 0)
        {
            actions.Add(new
            {
                id = groupByDirectory ? "show_files" : "group_directories",
                cmd = new
                {
                    query = query,
                    groupByDirectory = !groupByDirectory
                },
                tokens = 500,
                priority = "available"
            });
        }

        return actions;
    }

    private List<string> AnalyzeDirectorySearchPatterns(
        List<dynamic> results,
        Dictionary<string, int> extensionCounts,
        Dictionary<string, int> depthDistribution)
    {
        var patterns = new List<string>();

        if (results.Count == 0)
        {
            patterns.Add("No matches found - check spelling or use fuzzy search");
        }
        else if (results.Count == 1)
        {
            patterns.Add("Single directory match - precise search result");
        }
        else if (results.Count >= 20)
        {
            patterns.Add("Many directory matches - consider refining search");
        }

        // Depth patterns
        if (depthDistribution.Any())
        {
            var mostCommonDepth = depthDistribution.OrderByDescending(kv => kv.Value).First();
            patterns.Add($"Most directories at {mostCommonDepth.Key.Replace("level_", "depth ")}");
        }

        // File type patterns
        if (extensionCounts.Count > 0)
        {
            var dominantType = extensionCounts.OrderByDescending(kv => kv.Value).First();
            patterns.Add($"Directories contain mostly {dominantType.Key} files");
        }

        return patterns;
    }

    private int EstimateDirectorySearchResponseTokens(List<dynamic> results)
    {
        // Base tokens for structure
        var baseTokens = 200;
        
        // Per result tokens (directories tend to have more metadata)
        var perResultTokens = 40;
        
        // Additional for statistics
        var statsTokens = 150;
        
        return baseTokens + (results.Count * perResultTokens) + statsTokens;
    }

    #endregion

    #region Similar Files Implementation

    /// <summary>
    /// Build AI-optimized response for similar files search results
    /// </summary>
    public object BuildSimilarFilesResponse(
        string sourceFilePath,
        string workspacePath,
        List<dynamic> results,
        double searchDurationMs,
        dynamic sourceFileInfo,
        List<string> topTerms,
        ResponseMode mode)
    {
        var tokenBudget = mode == ResponseMode.Summary ? SummaryTokenBudget : FullTokenBudget;

        // Generate insights
        var insights = GenerateSimilarFilesInsights(results, sourceFilePath, searchDurationMs);

        // Analyze similarity distribution
        var similarityGroups = new Dictionary<string, List<dynamic>>();
        foreach (var result in results)
        {
            float similarity = result.similarity ?? 0f;
            var groupKey = similarity >= 0.8f ? "high" :
                          similarity >= 0.6f ? "medium" :
                          similarity >= 0.4f ? "moderate" : "low";
            
            if (!similarityGroups.ContainsKey(groupKey))
                similarityGroups[groupKey] = new List<dynamic>();
            
            similarityGroups[groupKey].Add(result);
        }

        // Find patterns in similar files
        var extensionDistribution = results
            .GroupBy(r => (string)r.extension)
            .ToDictionary(g => g.Key, g => g.Count());

        var languageDistribution = results
            .Where(r => !string.IsNullOrEmpty((string)r.language))
            .GroupBy(r => (string)r.language)
            .ToDictionary(g => g.Key, g => g.Count());

        // Generate actions
        var actions = GenerateSimilarFilesActions(sourceFilePath, results, similarityGroups);

        // Prepare results based on mode
        var resultsToInclude = mode == ResponseMode.Full 
            ? results
            : results.Take(10).ToList();

        // Create the response
        var response = new
        {
            success = true,
            operation = "similar_files",
            query = new
            {
                sourceFile = sourceFilePath,
                workspace = Path.GetFileName(workspacePath)
            },
            sourceFile = sourceFileInfo,
            summary = new
            {
                totalFound = results.Count,
                searchTime = $"{searchDurationMs:F1}ms",
                performance = searchDurationMs < 50 ? "excellent" : searchDurationMs < 100 ? "fast" : "normal",
                similarityDistribution = similarityGroups.ToDictionary(
                    g => g.Key,
                    g => new { count = g.Value.Count, percentage = $"{(g.Value.Count * 100.0 / results.Count):F0}%" }
                )
            },
            analysis = new
            {
                patterns = AnalyzeSimilarityPatterns(results, extensionDistribution, languageDistribution).Take(3).ToList(),
                topTerms = topTerms,
                distribution = new
                {
                    byExtension = extensionDistribution,
                    byLanguage = languageDistribution,
                    bySimilarity = similarityGroups.ToDictionary(g => g.Key, g => g.Value.Count)
                }
            },
            results = resultsToInclude,
            resultsSummary = new
            {
                included = resultsToInclude.Count,
                total = results.Count,
                hasMore = results.Count > resultsToInclude.Count
            },
            insights = insights,
            actions = actions,
            meta = new
            {
                mode = mode.ToString().ToLowerInvariant(),
                truncated = false,
                tokens = EstimateSimilarFilesResponseTokens(results),
                cached = GenerateCacheKey("similar")
            }
        };

        return response;
    }

    private List<string> GenerateSimilarFilesInsights(List<dynamic> results, string sourceFilePath, double searchDurationMs)
    {
        var insights = new List<string>();

        // Basic result insight
        if (results.Count == 0)
        {
            insights.Add("No similar files found - source file may have unique content");
            insights.Add("Try adjusting similarity parameters for broader matches");
        }
        else
        {
            insights.Add($"Found {results.Count} similar files in {searchDurationMs:F0}ms");
            
            // Similarity level insights
            var highSimilarity = results.Count(r => (float)(r.similarity ?? 0f) >= 0.8f);
            if (highSimilarity > 0)
            {
                insights.Add($"{highSimilarity} files have high similarity (80%+) - possible duplicates");
            }
            
            // Extension pattern insights
            var sourceExt = Path.GetExtension(sourceFilePath);
            var sameExtCount = results.Count(r => r.extension == sourceExt);
            if (sameExtCount == results.Count)
            {
                insights.Add("All similar files share the same file type");
            }
            else if (sameExtCount == 0)
            {
                insights.Add("Similar content found in different file types - cross-language patterns");
            }
        }

        // Performance insight
        if (searchDurationMs < 50)
        {
            insights.Add("⚡ Excellent similarity analysis performance");
        }

        return insights;
    }

    private List<object> GenerateSimilarFilesActions(
        string sourceFilePath,
        List<dynamic> results,
        Dictionary<string, List<dynamic>> similarityGroups)
    {
        var actions = new List<object>();

        if (results.Any())
        {
            // View most similar file
            var mostSimilar = results.First();
            actions.Add(new
            {
                id = "view_most_similar",
                cmd = new { file = mostSimilar.path },
                tokens = 1000,
                priority = "recommended"
            });

            // Compare with source
            if (results.Count > 0 && mostSimilar.similarity >= 0.7f)
            {
                actions.Add(new
                {
                    id = "compare_files",
                    cmd = new
                    {
                        file1 = sourceFilePath,
                        file2 = mostSimilar.path,
                        tool = "diff"
                    },
                    tokens = 1500,
                    priority = "recommended"
                });
            }

            // Search for duplicates
            if (similarityGroups.ContainsKey("high") && similarityGroups["high"].Count > 1)
            {
                actions.Add(new
                {
                    id = "find_duplicates",
                    cmd = new
                    {
                        operation = "similar_files",
                        minSimilarity = 0.9f,
                        sourcePath = sourceFilePath
                    },
                    tokens = 500,
                    priority = "available"
                });
            }

            // Analyze pattern across similar files
            if (results.Count >= 3)
            {
                actions.Add(new
                {
                    id = "analyze_pattern",
                    cmd = new
                    {
                        operation = "batch_operations",
                        operations = results.Take(5).Select(r => new
                        {
                            operation = "text_search",
                            query = "TODO|FIXME|HACK",
                            files = new[] { r.path }
                        }).ToList()
                    },
                    tokens = 2000,
                    priority = "available"
                });
            }
        }
        else
        {
            // Suggest parameter adjustments
            actions.Add(new
            {
                id = "adjust_parameters",
                cmd = new
                {
                    operation = "similar_files",
                    sourcePath = sourceFilePath,
                    minTermFreq = 1,
                    minDocFreq = 1,
                    minWordLength = 3
                },
                tokens = 1000,
                priority = "recommended"
            });
        }

        return actions;
    }

    private List<string> AnalyzeSimilarityPatterns(
        List<dynamic> results,
        Dictionary<string, int> extensionDistribution,
        Dictionary<string, int> languageDistribution)
    {
        var patterns = new List<string>();

        if (results.Count == 0)
        {
            patterns.Add("No similar files found - content appears unique");
        }
        else
        {
            // Similarity concentration
            var avgSimilarity = results.Average(r => (float)(r.similarity ?? 0f));
            if (avgSimilarity > 0.8f)
            {
                patterns.Add("High similarity concentration - likely code duplication");
            }
            else if (avgSimilarity > 0.6f)
            {
                patterns.Add("Moderate similarity - shared patterns or templates");
            }
            else
            {
                patterns.Add("Low similarity - loose content relationships");
            }

            // File type patterns
            if (extensionDistribution.Count == 1)
            {
                patterns.Add($"All similar files are {extensionDistribution.First().Key} files");
            }
            else if (extensionDistribution.Count > 3)
            {
                patterns.Add("Similar patterns found across multiple file types");
            }

            // Language patterns
            if (languageDistribution.Count > 1)
            {
                var topLang = languageDistribution.OrderByDescending(kv => kv.Value).First();
                patterns.Add($"Cross-language similarity, primarily {topLang.Key}");
            }
        }

        return patterns;
    }

    private int EstimateSimilarFilesResponseTokens(List<dynamic> results)
    {
        // Base tokens for structure
        var baseTokens = 250;
        
        // Per result tokens (similar files include more metadata)
        var perResultTokens = 40;
        
        // Additional for analysis
        var analysisTokens = 150;
        
        return baseTokens + (results.Count * perResultTokens) + analysisTokens;
    }

    #endregion

    #region Recent Files Implementation

    /// <summary>
    /// Build AI-optimized response for recent files search results
    /// </summary>
    public object BuildRecentFilesResponse(
        string workspacePath,
        string timeFrame,
        DateTime cutoffTime,
        List<dynamic> results,
        double searchDurationMs,
        Dictionary<string, int> extensionCounts,
        long totalSize,
        ResponseMode mode)
    {
        var tokenBudget = mode == ResponseMode.Summary ? SummaryTokenBudget : FullTokenBudget;

        // Generate insights
        var insights = GenerateRecentFilesInsights(results, timeFrame, searchDurationMs, extensionCounts);

        // Analyze temporal patterns
        var temporalGroups = new Dictionary<string, List<dynamic>>();
        var now = DateTime.UtcNow;
        
        foreach (var result in results)
        {
            DateTime lastModified = result.lastModified;
            var age = now - lastModified;
            
            var groupKey = age.TotalHours < 1 ? "last_hour" :
                          age.TotalHours < 24 ? "today" :
                          age.TotalDays < 7 ? "this_week" :
                          age.TotalDays < 30 ? "this_month" : "older";
            
            if (!temporalGroups.ContainsKey(groupKey))
                temporalGroups[groupKey] = new List<dynamic>();
            
            temporalGroups[groupKey].Add(result);
        }

        // Find activity patterns
        var activityPatterns = AnalyzeActivityPatterns(results, temporalGroups);

        // Generate actions
        var actions = GenerateRecentFilesActions(workspacePath, timeFrame, results, extensionCounts, temporalGroups);

        // Prepare results based on mode
        var resultsToInclude = mode == ResponseMode.Full 
            ? results
            : results.Take(20).ToList();

        // Create the response
        var response = new
        {
            success = true,
            operation = "recent_files",
            query = new
            {
                workspace = Path.GetFileName(workspacePath),
                timeFrame = timeFrame,
                cutoffTime = cutoffTime
            },
            summary = new
            {
                totalFound = results.Count,
                totalSize = totalSize,
                totalSizeFormatted = FormatFileSize(totalSize),
                searchTime = $"{searchDurationMs:F1}ms",
                performance = searchDurationMs < 10 ? "excellent" : searchDurationMs < 50 ? "fast" : "normal",
                fileTypes = extensionCounts
                    .Select(kvp => new { extension = kvp.Key, count = kvp.Value })
                    .OrderByDescending(x => x.count)
                    .ToList()
            },
            analysis = new
            {
                patterns = activityPatterns.Take(3).ToList(),
                temporalDistribution = temporalGroups.ToDictionary(
                    g => g.Key,
                    g => new { count = g.Value.Count, percentage = $"{(g.Value.Count * 100.0 / results.Count):F0}%" }
                ),
                hotspots = FindHotspotDirectories(results).Take(5).ToList()
            },
            results = resultsToInclude,
            resultsSummary = new
            {
                included = resultsToInclude.Count,
                total = results.Count,
                hasMore = results.Count > resultsToInclude.Count
            },
            insights = insights,
            actions = actions,
            meta = new
            {
                mode = mode.ToString().ToLowerInvariant(),
                truncated = false,
                tokens = EstimateRecentFilesResponseTokens(results),
                cached = GenerateCacheKey("recent")
            }
        };

        return response;
    }

    private List<string> GenerateRecentFilesInsights(
        List<dynamic> results, 
        string timeFrame, 
        double searchDurationMs,
        Dictionary<string, int> extensionCounts)
    {
        var insights = new List<string>();

        // Basic result insight
        if (results.Count == 0)
        {
            insights.Add($"No files modified in the last {timeFrame}");
            insights.Add("Try extending the time frame or checking a different directory");
        }
        else
        {
            insights.Add($"Found {results.Count} files modified in the last {timeFrame}");
            
            // Activity level insight
            if (results.Count > 50)
            {
                insights.Add("High activity detected - many recent changes");
            }
            else if (results.Count < 5)
            {
                insights.Add("Low activity - few recent modifications");
            }
            
            // File type insights
            if (extensionCounts.Count > 0)
            {
                var topType = extensionCounts.OrderByDescending(kv => kv.Value).First();
                if (topType.Value > results.Count * 0.5)
                {
                    insights.Add($"Mostly {topType.Key} files modified ({topType.Value} files)");
                }
            }
            
            // Most recent file
            if (results.Any())
            {
                var mostRecent = results.First();
                insights.Add($"Most recent: {mostRecent.filename} ({mostRecent.timeAgo})");
            }
        }

        // Performance insight
        if (searchDurationMs < 10)
        {
            insights.Add("⚡ Excellent timestamp-based search performance");
        }

        return insights;
    }

    private List<object> GenerateRecentFilesActions(
        string workspacePath,
        string timeFrame,
        List<dynamic> results,
        Dictionary<string, int> extensionCounts,
        Dictionary<string, List<dynamic>> temporalGroups)
    {
        var actions = new List<object>();

        if (results.Any())
        {
            // View most recent file
            var mostRecent = results.First();
            actions.Add(new
            {
                id = "view_most_recent",
                cmd = new { file = mostRecent.path },
                tokens = 1000,
                priority = "recommended"
            });

            // Search in recent files
            if (results.Count > 1 && results.Count <= 20)
            {
                actions.Add(new
                {
                    id = "search_in_recent",
                    cmd = new
                    {
                        operation = "text_search",
                        query = "TODO|FIXME|HACK",
                        files = results.Take(10).Select(r => r.path).ToList()
                    },
                    tokens = 1500,
                    priority = "available"
                });
            }

            // Filter by most active file type
            if (extensionCounts.Count > 1)
            {
                var topExt = extensionCounts.OrderByDescending(kv => kv.Value).First();
                actions.Add(new
                {
                    id = "filter_by_type",
                    cmd = new
                    {
                        operation = "recent_files",
                        workspacePath = workspacePath,
                        timeFrame = timeFrame,
                        extensions = new[] { topExt.Key }
                    },
                    tokens = 500,
                    priority = "available"
                });
            }

            // Narrow time window if many results
            if (results.Count > 50 && temporalGroups.ContainsKey("today"))
            {
                actions.Add(new
                {
                    id = "today_only",
                    cmd = new
                    {
                        operation = "recent_files",
                        workspacePath = workspacePath,
                        timeFrame = "24h"
                    },
                    tokens = 300,
                    priority = "recommended"
                });
            }
        }
        else
        {
            // Suggest broader time frame
            var broaderTimeFrame = timeFrame switch
            {
                "1h" => "24h",
                "24h" => "7d",
                "7d" => "30d",
                _ => "7d"
            };
            
            actions.Add(new
            {
                id = "broaden_timeframe",
                cmd = new
                {
                    operation = "recent_files",
                    workspacePath = workspacePath,
                    timeFrame = broaderTimeFrame
                },
                tokens = 1000,
                priority = "recommended"
            });
        }

        // Git status integration hint
        actions.Add(new
        {
            id = "check_git_status",
            cmd = new
            {
                operation = "bash",
                command = "git status --short"
            },
            tokens = 200,
            priority = "available"
        });

        return actions;
    }

    private List<string> AnalyzeActivityPatterns(
        List<dynamic> results,
        Dictionary<string, List<dynamic>> temporalGroups)
    {
        var patterns = new List<string>();

        if (results.Count == 0)
        {
            patterns.Add("No recent file activity detected");
        }
        else
        {
            // Temporal concentration
            if (temporalGroups.ContainsKey("last_hour") && temporalGroups["last_hour"].Count > results.Count * 0.5)
            {
                patterns.Add("Burst of activity in the last hour");
            }
            else if (temporalGroups.ContainsKey("today") && temporalGroups["today"].Count > results.Count * 0.7)
            {
                patterns.Add("Most activity concentrated today");
            }
            
            // File type patterns
            var extensionGroups = results.GroupBy(r => (string)r.extension).ToList();
            if (extensionGroups.Count == 1)
            {
                patterns.Add($"All modifications in {extensionGroups.First().Key} files");
            }
            else if (extensionGroups.Count > 5)
            {
                patterns.Add("Diverse file types modified - broad changes");
            }
            
            // Directory patterns
            var directoryGroups = results
                .GroupBy(r => Path.GetDirectoryName((string)r.relativePath))
                .OrderByDescending(g => g.Count())
                .ToList();
                
            if (directoryGroups.Any() && directoryGroups.First().Count() > results.Count * 0.5)
            {
                patterns.Add($"Concentrated activity in {directoryGroups.First().Key}");
            }
        }

        return patterns;
    }

    private List<object> FindHotspotDirectories(List<dynamic> results)
    {
        return results
            .GroupBy(r => Path.GetDirectoryName((string)r.relativePath) ?? "root")
            .OrderByDescending(g => g.Count())
            .Select(g => new
            {
                directory = g.Key,
                fileCount = g.Count(),
                mostRecent = g.OrderByDescending(r => (DateTime)r.lastModified).First().timeAgo
            })
            .Cast<object>()
            .ToList();
    }

    private string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    private int EstimateRecentFilesResponseTokens(List<dynamic> results)
    {
        // Base tokens for structure
        var baseTokens = 250;
        
        // Per result tokens (recent files include timestamps)
        var perResultTokens = 45;
        
        // Additional for analysis
        var analysisTokens = 150;
        
        return baseTokens + (results.Count * perResultTokens) + analysisTokens;
    }

    #endregion

    #region Text Search Implementation

    private List<string> GenerateTextSearchInsights(
        string query,
        string searchType,
        string workspacePath,
        List<TextSearchResult> results,
        long totalHits,
        string? filePattern,
        string[]? extensions,
        ProjectContext? projectContext,
        long? alternateHits,
        Dictionary<string, int>? alternateExtensions)
    {
        var insights = new List<string>();

        // Basic result insights
        if (totalHits == 0)
        {
            insights.Add($"No matches found for '{query}'");
            
            // Check if alternate search would find results
            if (alternateHits > 0 && alternateExtensions != null)
            {
                var topExtensions = alternateExtensions
                    .OrderByDescending(kvp => kvp.Value)
                    .Take(5)
                    .Select(kvp => $"{kvp.Key} ({kvp.Value})")
                    .ToList();
                    
                insights.Add($"Found {alternateHits} matches in other file types: {string.Join(", ", topExtensions)}");
                insights.Add($"💡 TIP: Remove filePattern/extensions to search ALL file types");
                insights.Add($"🔍 Try: text_search --query \"{query}\" --workspacePath \"{workspacePath}\"");
                
                // Project-aware suggestions
                if (projectContext?.Technologies?.Contains("blazor", StringComparer.OrdinalIgnoreCase) == true)
                {
                    if (filePattern == "*.cs" || extensions?.Contains(".cs") == true)
                    {
                        insights.Add("🎯 Blazor project detected - UI components are in .razor files!");
                        insights.Add($"🔍 Try: text_search --query \"{query}\" --extensions .cs,.razor --workspacePath \"{workspacePath}\"");
                    }
                }
                else if (projectContext?.Technologies?.Contains("aspnet", StringComparer.OrdinalIgnoreCase) == true)
                {
                    if (filePattern == "*.cs" || extensions?.Contains(".cs") == true)
                    {
                        insights.Add("🎯 ASP.NET project detected - views are in .cshtml files!");
                        insights.Add($"🔍 Try: text_search --query \"{query}\" --extensions .cs,.cshtml --workspacePath \"{workspacePath}\"");
                    }
                }
            }
            else
            {
                // Original suggestions when no alternate results
                if (searchType == "standard" && !query.Contains("*"))
                {
                    insights.Add("Try wildcard search with '*' or fuzzy search with '~'");
                }
                if (extensions?.Length > 0)
                {
                    insights.Add($"Search limited to: {string.Join(", ", extensions)}");
                }
                if (!string.IsNullOrEmpty(filePattern))
                {
                    insights.Add($"Results filtered by pattern: {filePattern}");
                }
            }
        }
        else if (totalHits > results.Count)
        {
            insights.Add($"Showing {results.Count} of {totalHits} total matches");
            if (totalHits > 100)
            {
                insights.Add("Consider refining search or using file patterns");
            }
        }

        // File type insights
        var extensionGroups = results.GroupBy(r => r.Extension).OrderByDescending(g => g.Count()).ToList();
        if (extensionGroups.Count > 1)
        {
            var topTypes = string.Join(", ", extensionGroups.Take(3).Select(g => $"{g.Key} ({g.Count()})"));
            insights.Add($"Most matches in: {topTypes}");
        }

        // Concentration insights
        var filesWithMatches = results.Select(r => r.FilePath).Distinct().Count();
        if (filesWithMatches > 0 && totalHits > filesWithMatches * 2)
        {
            var avgMatchesPerFile = totalHits / filesWithMatches;
            insights.Add($"Average {avgMatchesPerFile:F1} matches per file - some files have high concentration");
        }

        // Search type insights
        if (searchType == "fuzzy" && results.Any())
        {
            insights.Add("Fuzzy search found approximate matches");
        }
        else if (searchType == "phrase")
        {
            insights.Add("Exact phrase search - results contain the full phrase");
        }

        // Pattern insights
        if (!string.IsNullOrEmpty(filePattern))
        {
            insights.Add($"Results filtered by pattern: {filePattern}");
        }

        // Ensure we always have at least one insight
        if (insights.Count == 0)
        {
            if (totalHits > 0)
            {
                insights.Add($"Found {totalHits} matches for '{query}' in {filesWithMatches} files");
                if (extensionGroups.Any())
                {
                    insights.Add($"Search matched files of type: {string.Join(", ", extensionGroups.Select(g => g.Key))}");
                }
            }
            else
            {
                insights.Add($"No matches found for '{query}'");
            }
        }

        return insights;
    }

    private List<object> GenerateTextSearchActions(
        string query,
        string searchType,
        List<TextSearchResult> results,
        long totalHits,
        List<TextSearchHotspot> hotspots,
        Dictionary<string, object> byExtension,
        ResponseMode mode)
    {
        var actions = new List<object>();

        // Refine search actions
        if (totalHits > 100)
        {
            if (byExtension.Count > 1)
            {
                var topExt = byExtension.OrderByDescending(kvp => ((dynamic)kvp.Value).count).First();
                dynamic topExtValue = topExt.Value;
                actions.Add(new AIAction
                {
                    Id = "filter_by_type",
                    Description = $"Filter results to {topExt.Key} files only",
                    Command = new AIActionCommand
                    {
                        Tool = "mcp__codesearch__text_search",
                        Parameters = new Dictionary<string, object>
                        {
                            { "query", query },
                            { "extensions", new[] { topExt.Key } }
                        }
                    },
                    EstimatedTokens = Math.Min(2000, topExtValue.count * 50),
                    Priority = ActionPriority.High,
                    Context = ActionContext.ManyResults
                });
            }

            actions.Add(new AIAction
            {
                Id = "narrow_search",
                Description = "Narrow search with more specific terms",
                Command = new AIActionCommand
                {
                    Tool = "mcp__codesearch__text_search",
                    Parameters = new Dictionary<string, object>
                    {
                        { "query", $"\"{query}\" AND specific_term" },
                        { "searchType", "standard" }
                    }
                },
                EstimatedTokens = 1500,
                Priority = ActionPriority.Medium,
                Context = ActionContext.ManyResults
            });
        }

        // Context actions
        if (hotspots.Any() && results.Any(r => r.Context == null))
        {
            actions.Add(new AIAction
            {
                Id = "add_context",
                Description = "Show results with surrounding context",
                Command = new AIActionCommand
                {
                    Tool = "mcp__codesearch__text_search",
                    Parameters = new Dictionary<string, object>
                    {
                        { "query", query },
                        { "contextLines", 3 }
                    }
                },
                EstimatedTokens = EstimateContextTokens(results.Take(20).ToList(), 3),
                Priority = ActionPriority.High,
                Context = ActionContext.Always
            });
        }

        // Explore hotspots
        if (hotspots.Any())
        {
            var topHotspot = hotspots.First();
            actions.Add(new AIAction
            {
                Id = "explore_hotspot",
                Description = $"Read {topHotspot.File} with {topHotspot.Matches} matches",
                Command = new AIActionCommand
                {
                    Tool = "Read",
                    Parameters = new Dictionary<string, object>
                    {
                        { "file_path", topHotspot.File }
                    }
                },
                EstimatedTokens = 1000,
                Priority = ActionPriority.Medium,
                Context = ActionContext.ManyResults
            });
        }

        // Alternative search types
        if (searchType == "standard" && !query.Contains("*"))
        {
            actions.Add(new AIAction
            {
                Id = "try_wildcard",
                Description = "Try wildcard search for broader results",
                Command = new AIActionCommand
                {
                    Tool = "mcp__codesearch__text_search",
                    Parameters = new Dictionary<string, object>
                    {
                        { "query", $"*{query}*" },
                        { "searchType", "wildcard" }
                    }
                },
                EstimatedTokens = 2000,
                Priority = ActionPriority.Low,
                Context = ActionContext.EmptyResults
            });

            actions.Add(new AIAction
            {
                Id = "try_fuzzy",
                Description = "Try fuzzy search for approximate matches",
                Command = new AIActionCommand
                {
                    Tool = "mcp__codesearch__text_search",
                    Parameters = new Dictionary<string, object>
                    {
                        { "query", query.TrimEnd('~') + "~" },
                        { "searchType", "fuzzy" }
                    }
                },
                EstimatedTokens = 2000,
                Priority = ActionPriority.Low,
                Context = ActionContext.EmptyResults
            });
        }

        // Full details action
        if (mode == ResponseMode.Summary && results.Count < 100)
        {
            actions.Add(new AIAction
            {
                Id = "full_details",
                Description = "Get full details for all results",
                Command = new AIActionCommand
                {
                    Tool = "mcp__codesearch__text_search",
                    Parameters = new Dictionary<string, object>
                    {
                        { "query", query },
                        { "responseMode", "full" }
                    }
                },
                EstimatedTokens = EstimateFullTextSearchResponseTokens(results),
                Priority = ActionPriority.Low,
                Context = ActionContext.Exploration
            });
        }

        return actions.Cast<object>().ToList();
    }

    private int EstimateTextSearchResponseTokens(List<TextSearchResult> results)
    {
        // Estimate ~100 tokens per result without context, ~200 with context
        var hasContext = results.Any(r => r.Context != null);
        var tokensPerResult = hasContext ? 200 : 100;
        return Math.Min(25000, results.Count * tokensPerResult);
    }

    private int EstimateContextTokens(List<TextSearchResult> results, int contextLines)
    {
        // Estimate tokens for adding context to results
        var avgLinesPerResult = contextLines * 2 + 1; // Before + match + after
        var avgCharsPerLine = 80;
        var charsPerResult = avgLinesPerResult * avgCharsPerLine;
        var tokensPerResult = charsPerResult / 4; // Rough char-to-token conversion
        return results.Count * tokensPerResult;
    }

    private int EstimateFullTextSearchResponseTokens(List<TextSearchResult> results)
    {
        // Estimate for full response mode
        return Math.Min(25000, results.Count * 250); // More tokens per result in full mode
    }

    #endregion

    #region Batch Operations Response Building

    /// <summary>
    /// Build AI-optimized response for batch operations results
    /// </summary>
    public object BuildBatchOperationsResponse(
        BatchOperationRequest request,
        BatchOperationResult result,
        ResponseMode mode = ResponseMode.Summary)
    {
        var tokenBudget = mode == ResponseMode.Summary ? SummaryTokenBudget : FullTokenBudget;
        
        // Analyze batch results
        var analysis = AnalyzeBatchResults(result);
        
        // Generate insights
        var insights = GenerateBatchInsights(analysis);
        
        // Generate actions
        var actions = GenerateBatchActions(analysis);
        
        // Prepare operation results based on mode
        var operationResults = mode == ResponseMode.Full 
            ? PrepareFullBatchResults(result.Operations)
            : PrepareSummaryBatchResults(result.Operations, analysis);
        
        // Calculate estimated tokens
        var estimatedTokens = EstimateBatchResponseTokens(analysis, operationResults);
        
        // Store in cache for detail requests if summary mode
        string? detailRequestToken = null;
        List<DetailLevel>? availableDetailLevels = null;
        
        if (mode == ResponseMode.Summary && _detailCache != null && estimatedTokens > tokenBudget)
        {
            detailRequestToken = _detailCache.StoreDetailData(result);
            availableDetailLevels = CreateBatchDetailLevels(result);
        }
        
        return new
        {
            success = true,
            operation = "batch_operations",
            batch = new
            {
                totalOperations = analysis.TotalOperations,
                successCount = analysis.SuccessCount,
                failureCount = analysis.FailureCount,
                operationTypes = analysis.OperationTypeCounts
            },
            summary = new
            {
                executionTime = analysis.EstimatedExecutionTime,
                successRate = analysis.SuccessRate,
                topOperations = analysis.OperationTypeCounts
                    .OrderByDescending(kv => kv.Value)
                    .Take(3)
                    .Select(kv => new { type = kv.Key, count = kv.Value })
                    .ToList(),
                errorSummary = analysis.ErrorSummary.Any() ? analysis.ErrorSummary : null
            },
            analysis = new
            {
                patterns = analysis.Patterns.Take(3).ToList(),
                hotspots = analysis.FileReferences.Any() ? new
                {
                    byFile = analysis.FileReferences
                        .OrderByDescending(kv => kv.Value)
                        .Take(5)
                        .Select(kv => new { file = kv.Key, operations = kv.Value })
                        .ToList()
                } : null,
                correlations = analysis.Correlations.Take(3).ToList()
            },
            results = operationResults,
            insights = insights,
            actions = actions.Select(a => new
            {
                id = a.Id,
                description = a.Description,
                command = a.Command.Tool,
                parameters = a.Command.Parameters,
                tokens = a.EstimatedTokens,
                priority = a.Priority.ToString().ToLowerInvariant()
            }),
            meta = new
            {
                mode = mode.ToString().ToLowerInvariant(),
                avgTokensPerOp = analysis.AverageTokensPerOperation,
                totalTokens = estimatedTokens,
                cached = GenerateCacheKey("batch_operations"),
                detailRequestToken = detailRequestToken,
                availableDetailLevels = availableDetailLevels
            }
        };
    }

    private BatchAnalysis AnalyzeBatchResults(BatchOperationResult result)
    {
        var analysis = new BatchAnalysis
        {
            TotalOperations = result.Operations.Count,
            SuccessCount = result.Operations.Count(op => op.Success),
            FailureCount = result.Operations.Count(op => !op.Success)
        };
        
        // Count operation types
        foreach (var op in result.Operations)
        {
            if (!analysis.OperationTypeCounts.ContainsKey(op.OperationType))
                analysis.OperationTypeCounts[op.OperationType] = 0;
            analysis.OperationTypeCounts[op.OperationType]++;
            
            // Track errors
            if (!op.Success && !string.IsNullOrEmpty(op.Error))
            {
                if (!analysis.ErrorSummary.ContainsKey(op.Error))
                    analysis.ErrorSummary[op.Error] = 0;
                analysis.ErrorSummary[op.Error]++;
            }
            
            // Extract file references
            ExtractFileReferences(op, analysis);
        }
        
        // Calculate metrics
        analysis.SuccessRate = analysis.TotalOperations > 0
            ? (double)analysis.SuccessCount / analysis.TotalOperations
            : 0.0;
        
        analysis.EstimatedExecutionTime = EstimateExecutionTime(analysis.OperationTypeCounts);
        analysis.AverageTokensPerOperation = 150; // Simplified estimate
        
        // Detect patterns
        DetectBatchPatterns(result, analysis);
        
        // Find correlations
        FindBatchCorrelations(analysis);
        
        return analysis;
    }
    
    private void ExtractFileReferences(BatchOperationEntry operation, BatchAnalysis analysis)
    {
        if (!operation.Success || operation.Result == null)
            return;
        
        // Try to extract file references from the result
        if (operation.Result is JsonElement jsonResult)
        {
            ExtractFilesFromJson(jsonResult, analysis);
        }
        else
        {
            // For non-JSON results, try reflection or dynamic
            dynamic d = operation.Result;
            try
            {
                if (d.files != null)
                {
                    foreach (var file in d.files)
                    {
                        string? filePath = file is string s ? s : file?.path?.ToString();
                        if (!string.IsNullOrEmpty(filePath))
                        {
                            if (!analysis.FileReferences.ContainsKey(filePath))
                                analysis.FileReferences[filePath] = 0;
                            analysis.FileReferences[filePath]++;
                        }
                    }
                }
            }
            catch { /* Ignore extraction errors */ }
        }
    }
    
    private void ExtractFilesFromJson(JsonElement element, BatchAnalysis analysis)
    {
        if (element.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
        {
            foreach (var file in files.EnumerateArray())
            {
                string? filePath = null;
                if (file.ValueKind == JsonValueKind.String)
                {
                    filePath = file.GetString();
                }
                else if (file.ValueKind == JsonValueKind.Object && file.TryGetProperty("path", out var path))
                {
                    filePath = path.GetString();
                }
                
                if (!string.IsNullOrEmpty(filePath))
                {
                    if (!analysis.FileReferences.ContainsKey(filePath))
                        analysis.FileReferences[filePath] = 0;
                    analysis.FileReferences[filePath]++;
                }
            }
        }
    }
    
    private void DetectBatchPatterns(BatchOperationResult result, BatchAnalysis analysis)
    {
        // Pattern: Multiple search operations
        var searchOps = analysis.OperationTypeCounts.Where(kv =>
            kv.Key.Contains("search", StringComparison.OrdinalIgnoreCase)).Sum(kv => kv.Value);
        if (searchOps > 2)
        {
            analysis.Patterns.Add($"Comprehensive search strategy: {searchOps} search operations");
        }
        
        // Pattern: File analysis workflow
        var fileAnalysisOps = analysis.OperationTypeCounts.Where(kv =>
            kv.Key.Contains("file", StringComparison.OrdinalIgnoreCase) ||
            kv.Key.Contains("recent", StringComparison.OrdinalIgnoreCase) ||
            kv.Key.Contains("similar", StringComparison.OrdinalIgnoreCase)).Sum(kv => kv.Value);
        if (fileAnalysisOps > 1)
        {
            analysis.Patterns.Add("File discovery and analysis workflow");
        }
        
        // Pattern: Size and structure analysis
        if (analysis.OperationTypeCounts.ContainsKey("file_size_analysis") &&
            analysis.OperationTypeCounts.ContainsKey("directory_search"))
        {
            analysis.Patterns.Add("Project structure analysis pattern");
        }
    }
    
    private void FindBatchCorrelations(BatchAnalysis analysis)
    {
        if (analysis.FileReferences.Count > 5 && analysis.OperationTypeCounts.Count > 2)
        {
            analysis.Correlations.Add("Multiple search types across many files - comprehensive discovery");
        }
        
        if (analysis.FailureCount > 0 && analysis.FailureCount < analysis.TotalOperations / 2)
        {
            analysis.Correlations.Add($"Partial failures ({analysis.FailureCount}/{analysis.TotalOperations}) - some search targets may be invalid");
        }
        
        if (analysis.TotalResultItems > 100)
        {
            analysis.Correlations.Add("High result volume - consider refining search criteria");
        }
    }
    
    private string EstimateExecutionTime(Dictionary<string, int> operationTypeCounts)
    {
        // Rough estimates in milliseconds
        var timeEstimates = new Dictionary<string, int>
        {
            ["text_search"] = 50,
            ["file_search"] = 30,
            ["recent_files"] = 25,
            ["file_size_analysis"] = 40,
            ["similar_files"] = 100,
            ["directory_search"] = 20
        };
        
        var totalMs = operationTypeCounts.Sum(kv =>
            timeEstimates.GetValueOrDefault(kv.Key, 50) * kv.Value);
        
        if (totalMs < 1000)
            return $"{totalMs}ms";
        else if (totalMs < 60000)
            return $"{totalMs / 1000.0:F1}s";
        else
            return $"{totalMs / 60000.0:F1}m";
    }
    
    private List<object> PrepareSummaryBatchResults(List<BatchOperationEntry> operations, BatchAnalysis analysis)
    {
        var summaryResults = new List<object>();
        var resultsByType = operations.GroupBy(op => op.OperationType);
        
        foreach (var group in resultsByType)
        {
            var successCount = group.Count(op => op.Success);
            var failureCount = group.Count() - successCount;
            
            summaryResults.Add(new
            {
                operation = group.Key,
                count = group.Count(),
                success = successCount,
                failures = failureCount,
                examples = group.Take(2).Select(op => new
                {
                    success = op.Success,
                    summary = CreateOperationSummary(op)
                }).ToList()
            });
        }
        
        return summaryResults;
    }
    
    private object CreateOperationSummary(BatchOperationEntry operation)
    {
        if (!operation.Success)
        {
            return new { error = operation.Error };
        }
        
        // Create operation-specific summaries based on type
        return operation.OperationType switch
        {
            "text_search" => new { matches = ExtractCount(operation.Result, "totalMatches"), files = ExtractCount(operation.Result, "files") },
            "file_search" => new { found = ExtractCount(operation.Result, "files") },
            "recent_files" => new { found = ExtractCount(operation.Result, "files") },
            "file_size_analysis" => new { analyzed = ExtractCount(operation.Result, "totalFiles") },
            "similar_files" => new { found = ExtractCount(operation.Result, "similarFiles") },
            "directory_search" => new { found = ExtractCount(operation.Result, "directories") },
            _ => new { hasResult = true }
        };
    }
    
    private int ExtractCount(object? result, string propertyName)
    {
        if (result == null) return 0;
        
        try
        {
            if (result is JsonElement json)
            {
                if (json.TryGetProperty(propertyName, out var prop))
                {
                    if (prop.ValueKind == JsonValueKind.Number)
                        return prop.GetInt32();
                    if (prop.ValueKind == JsonValueKind.Array)
                        return prop.GetArrayLength();
                }
            }
            else
            {
                // Use dynamic for non-JSON results
                dynamic d = result;
                var value = d.GetType().GetProperty(propertyName)?.GetValue(d);
                if (value is int count) return count;
                if (value is IEnumerable<object> list) return list.Count();
            }
        }
        catch { /* Ignore extraction errors */ }
        
        return 0;
    }
    
    private List<object> PrepareFullBatchResults(List<BatchOperationEntry> operations)
    {
        return operations.Select(op => new
        {
            operation = op.OperationType,
            success = op.Success,
            result = op.Success ? op.Result : null,
            error = !op.Success ? op.Error : null
        }).Cast<object>().ToList();
    }
    
    private List<string> GenerateBatchInsights(BatchAnalysis analysis)
    {
        var insights = new List<string>();
        
        // Success rate insight
        if (analysis.SuccessRate < 1.0)
        {
            insights.Add($"{analysis.FailureCount} operations failed ({(1 - analysis.SuccessRate) * 100:F0}%)");
            if (analysis.ErrorSummary.Any())
            {
                var topError = analysis.ErrorSummary.OrderByDescending(kv => kv.Value).First();
                insights.Add($"Most common error: {topError.Key}");
            }
        }
        else
        {
            insights.Add($"All {analysis.TotalOperations} operations completed successfully");
        }
        
        // Operation distribution insight
        if (analysis.OperationTypeCounts.Count > 1)
        {
            var topOp = analysis.OperationTypeCounts.OrderByDescending(kv => kv.Value).First();
            insights.Add($"Primary focus: {topOp.Key} ({topOp.Value} operations)");
        }
        
        // File hotspot insight
        if (analysis.FileReferences.Any())
        {
            var fileCount = analysis.FileReferences.Count;
            var totalFileOps = analysis.FileReferences.Sum(kv => kv.Value);
            insights.Add($"Discovered {fileCount} files across {totalFileOps} operations");
        }
        
        // Pattern insights
        foreach (var pattern in analysis.Patterns.Take(2))
        {
            insights.Add(pattern);
        }
        
        // Performance insight
        insights.Add($"Estimated execution time: {analysis.EstimatedExecutionTime}");
        
        return insights;
    }
    
    private List<AIAction> GenerateBatchActions(BatchAnalysis analysis)
    {
        var actions = new List<AIAction>();
        
        // Retry failed operations
        if (analysis.FailureCount > 0)
        {
            actions.Add(new AIAction
            {
                Id = "retry_failures",
                Description = "Retry failed operations",
                Command = new AIActionCommand
                {
                    Tool = "batch_operations",
                    Parameters = new Dictionary<string, object>
                    {
                        { "filterBy", "failed" },
                        { "maxRetries", 1 }
                    }
                },
                EstimatedTokens = analysis.FailureCount * 200,
                Priority = ActionPriority.High,
                Context = ActionContext.Always
            });
        }
        
        // Expand search scope
        if (analysis.TotalResultItems < 10 && analysis.OperationTypeCounts.ContainsKey("text_search"))
        {
            actions.Add(new AIAction
            {
                Id = "expand_search",
                Description = "Broaden search criteria",
                Command = new AIActionCommand
                {
                    Tool = "batch_operations",
                    Parameters = new Dictionary<string, object>
                    {
                        { "broaderTerms", true },
                        { "includeComments", true }
                    }
                },
                EstimatedTokens = 1500,
                Priority = ActionPriority.Medium,
                Context = ActionContext.ManyResults
            });
        }
        
        // Analyze discovered files
        if (analysis.FileReferences.Count > 5)
        {
            actions.Add(new AIAction
            {
                Id = "analyze_files",
                Description = "Analyze discovered files",
                Command = new AIActionCommand
                {
                    Tool = "batch_operations",
                    Parameters = new Dictionary<string, object>
                    {
                        { "operations", new[] { "file_size_analysis", "similar_files" } }
                    }
                },
                EstimatedTokens = 2000,
                Priority = ActionPriority.Low,
                Context = ActionContext.Exploration
            });
        }
        
        return actions;
    }
    
    private int EstimateBatchResponseTokens(BatchAnalysis analysis, List<object> results)
    {
        // Base tokens for structure
        var baseTokens = 300;
        
        // Per operation tokens
        var perOpTokens = 80;
        
        // Additional tokens for file references
        var fileTokens = analysis.FileReferences.Count * 15;
        
        // Results tokens
        var resultsTokens = _tokenEstimator.EstimateTokens(results);
        
        return baseTokens + (analysis.TotalOperations * perOpTokens) + fileTokens + resultsTokens;
    }
    
    private List<DetailLevel> CreateBatchDetailLevels(BatchOperationResult result)
    {
        return new List<DetailLevel>
        {
            new DetailLevel
            {
                Id = "full_results",
                Name = "Full Results",
                Description = "Complete results for all batch operations",
                EstimatedTokens = result.Operations.Count * 250,
                IsActive = false
            },
            new DetailLevel
            {
                Id = "failed_operations",
                Name = "Failed Operations Details",
                Description = "Detailed information about failed operations",
                EstimatedTokens = result.Operations.Count(op => !op.Success) * 150,
                IsActive = false
            }
        };
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
/// Text search result for response building
/// </summary>
public class TextSearchResult
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public float Score { get; set; }
    public List<TextSearchContextLine>? Context { get; set; }
}

/// <summary>
/// Context line for text search results
/// </summary>
public class TextSearchContextLine
{
    public int LineNumber { get; set; }
    public string Content { get; set; } = string.Empty;
    public bool IsMatch { get; set; }
}

/// <summary>
/// Hotspot info for text search results
/// </summary>
public class TextSearchHotspot
{
    public string File { get; set; } = string.Empty;
    public int Matches { get; set; }
    public int Lines { get; set; }
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

#region Batch Operations Support Classes

/// <summary>
/// Request data for batch operations
/// </summary>
public class BatchOperationRequest
{
    public List<BatchOperationDefinition> Operations { get; set; } = new();
    public string? DefaultWorkspacePath { get; set; }
}

/// <summary>
/// Definition of a single operation in a batch
/// </summary>
public class BatchOperationDefinition
{
    public string OperationType { get; set; } = string.Empty;
    public Dictionary<string, object> Parameters { get; set; } = new();
}

/// <summary>
/// Result of batch operations execution
/// </summary>
public class BatchOperationResult
{
    public List<BatchOperationEntry> Operations { get; set; } = new();
    public TimeSpan TotalExecutionTime { get; set; }
}

/// <summary>
/// Individual operation result in a batch
/// </summary>
public class BatchOperationEntry
{
    public string OperationType { get; set; } = string.Empty;
    public bool Success { get; set; }
    public object? Result { get; set; }
    public string? Error { get; set; }
    public int Index { get; set; }
}

/// <summary>
/// Analysis data for batch operations
/// </summary>
public class BatchAnalysis
{
    public int TotalOperations { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public double SuccessRate { get; set; }
    public string EstimatedExecutionTime { get; set; } = "0ms";
    public int TotalResultItems { get; set; }
    public int AverageTokensPerOperation { get; set; }
    
    public Dictionary<string, int> OperationTypeCounts { get; set; } = new();
    public Dictionary<string, int> ErrorSummary { get; set; } = new();
    public Dictionary<string, int> FileReferences { get; set; } = new();
    public List<string> Patterns { get; set; } = new();
    public List<string> Correlations { get; set; } = new();
}

#endregion