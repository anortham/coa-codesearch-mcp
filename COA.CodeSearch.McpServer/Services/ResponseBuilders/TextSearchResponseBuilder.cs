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
/// Builds AI-optimized responses for text search operations
/// </summary>
public class TextSearchResponseBuilder : BaseResponseBuilder
{
    private readonly IDetailRequestCache? _detailCache;
    
    public TextSearchResponseBuilder(ILogger<TextSearchResponseBuilder> logger, IDetailRequestCache? detailCache) 
        : base(logger)
    {
        _detailCache = detailCache;
    }

    public override string ResponseType => "text_search";

    /// <summary>
    /// Build AI-optimized response for text search results
    /// </summary>
    public object BuildResponse(
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
        var insights = GenerateInsights(new
        {
            query,
            searchType,
            workspacePath,
            results,
            totalHits,
            filePattern,
            extensions,
            projectContext,
            alternateHits,
            alternateExtensions
        }, mode);

        // Generate actions
        var actions = GenerateActions(new
        {
            query,
            searchType,
            workspacePath,
            results,
            totalHits,
            hotspots,
            byExtension,
            mode
        }, tokenBudget);

        // Determine how many results to include inline based on token budget, mode, and context
        var hasContext = results.Any(r => r.Context?.Any() == true);
        var maxInlineResults = hasContext ? 5 : 10; // Fewer results when including context
        var includeResults = mode == ResponseMode.Full || results.Count <= maxInlineResults;
        var inlineResults = includeResults ? results : results.Take(maxInlineResults).ToList();
        
        // Pre-estimate response size and apply hard safety limit
        var preEstimatedTokens = EstimateResponseTokens(inlineResults) + 500; // Add overhead for metadata
        var safetyLimitApplied = false;
        if (preEstimatedTokens > 5000)
        {
            Logger.LogWarning("Pre-estimated response ({Tokens} tokens) exceeds safety threshold. Forcing minimal results.", preEstimatedTokens);
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
                tokens = EstimateResponseTokens(inlineResults),
                cached = $"txt_{Guid.NewGuid().ToString("N")[..8]}",
                safetyLimitApplied = safetyLimitApplied,
                originalEstimatedTokens = safetyLimitApplied ? preEstimatedTokens : (int?)null,
                detailRequestToken = detailRequestToken
            },
            resourceUri = $"codesearch-search://search_{Guid.NewGuid().ToString("N")[..8]}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}"
        };

        return response;
    }

    protected override List<string> GenerateInsights(dynamic data, ResponseMode mode)
    {
        var insights = new List<string>();

        // Basic result insights
        if (data.totalHits == 0)
        {
            insights.Add($"No matches found for '{data.query}'");
            
            // Check if alternate search would find results
            if (data.alternateHits > 0 && data.alternateExtensions != null)
            {
                var topExtensions = ((Dictionary<string, int>)data.alternateExtensions)
                    .OrderByDescending(kvp => kvp.Value)
                    .Take(5)
                    .Select(kvp => $"{kvp.Key} ({kvp.Value})")
                    .ToList();
                    
                insights.Add($"Found {data.alternateHits} matches in other file types: {string.Join(", ", topExtensions)}");
                insights.Add($"💡 TIP: Remove filePattern/extensions to search ALL file types");
                insights.Add($"🔍 Try: text_search --query \"{data.query}\" --workspacePath \"{data.workspacePath}\"");
            }
            
            // Add search type specific tips
            switch (data.searchType)
            {
                case "standard":
                    insights.Add("Try wildcard search with '*' or fuzzy search with '~'");
                    break;
                case "fuzzy":
                    insights.Add("Try broader fuzzy search or wildcard patterns");
                    break;
                case "regex":
                    insights.Add("Check regex syntax or try simpler patterns");
                    break;
            }
        }
        else
        {
            List<TextSearchResult> results = data.results;
            
            // Summary mode - provide high-level insights
            if (mode == ResponseMode.Summary && data.totalHits > 10)
            {
                insights.Add($"Showing {results.Count} of {data.totalHits} total matches");
                
                // File concentration insights
                var filesWithMultipleMatches = results
                    .GroupBy(r => r.RelativePath)
                    .Count(g => g.Count() > 1);
                
                if (filesWithMultipleMatches > 0)
                {
                    var avgMatchesPerFile = (double)results.Count / results.Select(r => r.RelativePath).Distinct().Count();
                    insights.Add($"Average {avgMatchesPerFile:F1} matches per file - some files have high concentration");
                }
            }
            else
            {
                // Full mode or small result set
                if (results.Count == 1)
                {
                    insights.Add("Found exactly 1 match");
                }
                else if (results.Count <= 5)
                {
                    insights.Add($"Found {results.Count} matches in {results.Select(r => r.FilePath).Distinct().Count()} files");
                }
            }
            
            // Extension insights if pattern was specified
            if (data.extensions != null && data.extensions.Length > 0)
            {
                var extensionList = string.Join(", ", data.extensions);
                insights.Add($"Results filtered by extensions: {extensionList}");
            }
            else if (data.filePattern != null)
            {
                insights.Add($"Results filtered by pattern: {data.filePattern}");
            }
            
            // Project context insights
            if (data.projectContext != null)
            {
                var context = (ProjectContext)data.projectContext;
                if (!string.IsNullOrEmpty(context.ProjectType))
                {
                    insights.Add($"Searching in {context.ProjectType} project");
                }
            }
        }
        
        return insights;
    }

    protected override List<dynamic> GenerateActions(dynamic data, int tokenBudget)
    {
        var actions = new List<dynamic>();

        if (data.totalHits == 0)
        {
            // No results - suggest alternative searches
            actions.Add(new AIAction
            {
                Id = "try_wildcard",
                Description = "Try wildcard search",
                Command = new AICommand
                {
                    Tool = "text_search",
                    Parameters = new Dictionary<string, object>
                    {
                        ["query"] = $"*{data.query}*",
                        ["searchType"] = "wildcard"
                    }
                },
                EstimatedTokens = 2000,
                Priority = Priority.Low
            });
            
            actions.Add(new AIAction
            {
                Id = "try_fuzzy",
                Description = "Try fuzzy search",
                Command = new AICommand
                {
                    Tool = "text_search",
                    Parameters = new Dictionary<string, object>
                    {
                        ["query"] = $"{data.query}~",
                        ["searchType"] = "fuzzy"
                    }
                },
                EstimatedTokens = 2000,
                Priority = Priority.Low
            });
        }
        else
        {
            List<TextSearchResult> results = data.results;
            List<TextSearchHotspot> hotspots = data.hotspots;
            
            // Suggest exploring hotspot files
            if (hotspots.Any())
            {
                var topHotspot = hotspots.First();
                actions.Add(new AIAction
                {
                    Id = "explore_hotspot",
                    Description = $"Explore file with most matches",
                    Command = new AICommand
                    {
                        Tool = "open_file",
                        Parameters = new Dictionary<string, object>
                        {
                            ["file_path"] = Path.Combine(data.workspacePath, topHotspot.File)
                        }
                    },
                    EstimatedTokens = 1000,
                    Priority = Priority.Medium
                });
            }
            
            // For wildcards, suggest more specific search
            if (data.searchType == "wildcard" && data.query.Contains("*"))
            {
                actions.Add(new AIAction
                {
                    Id = "try_specific",
                    Description = "Try more specific search",
                    Command = new AICommand
                    {
                        Tool = "text_search",
                        Parameters = new Dictionary<string, object>
                        {
                            ["query"] = data.query.Replace("*", ""),
                            ["searchType"] = "standard"
                        }
                    },
                    EstimatedTokens = 1500,
                    Priority = Priority.Low
                });
            }
            
            // Suggest full details if in summary mode
            if (data.mode == ResponseMode.Summary && data.totalHits > results.Count)
            {
                actions.Add(new AIAction
                {
                    Id = "full_details",
                    Description = "Get all results",
                    Command = new AICommand
                    {
                        Tool = "text_search",
                        Parameters = new Dictionary<string, object>
                        {
                            ["query"] = data.query,
                            ["responseMode"] = "full"
                        }
                    },
                    EstimatedTokens = Math.Min((int)data.totalHits * 50, 5000),
                    Priority = Priority.Low
                });
            }
        }
        
        return actions;
    }

    private int EstimateResponseTokens(List<TextSearchResult> results)
    {
        // Base tokens for structure
        var baseTokens = 200;
        
        // Per-result tokens
        var perResultTokens = 30; // Basic result info
        if (results.Any(r => r.Context?.Any() == true))
        {
            perResultTokens += 50; // Additional for context lines
        }
        
        // Additional for analysis
        var analysisTokens = 200;
        
        return baseTokens + (results.Count * perResultTokens) + analysisTokens;
    }
}