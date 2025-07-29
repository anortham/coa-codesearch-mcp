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
/// Builds AI-optimized responses for file search operations
/// </summary>
public class FileSearchResponseBuilder : BaseResponseBuilder
{
    public FileSearchResponseBuilder(ILogger<FileSearchResponseBuilder> logger) 
        : base(logger)
    {
    }

    public override string ResponseType => "file_search";

    /// <summary>
    /// Build AI-optimized response for file search results
    /// </summary>
    public object BuildResponse(
        string query,
        string searchType,
        string workspacePath,
        List<FileSearchResult> results,
        double searchDurationMs,
        ResponseMode mode,
        ProjectContext? projectContext)
    {
        var tokenBudget = mode == ResponseMode.Summary ? SummaryTokenBudget : FullTokenBudget;

        // Group by extension
        var extensionGroups = results
            .GroupBy(r => System.IO.Path.GetExtension(r.Path))
            .ToDictionary(g => g.Key, g => g.Count());

        // Group by language - not available in simple FileSearchResult
        var languageGroups = new Dictionary<string, int>();

        // Find directory hotspots
        var directoryHotspots = results
            .GroupBy(r => Path.GetDirectoryName(r.Path) ?? "root")
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => new
            {
                path = g.Key,
                count = g.Count()
            })
            .ToList();

        // Analyze match quality
        var matchQuality = new
        {
            exactMatches = results.Count(r => r.Score >= 0.9),
            partialMatches = results.Count(r => r.Score >= 0.5 && r.Score < 0.9),
            fuzzyMatches = results.Count(r => r.Score < 0.5),
            avgScore = results.Any() ? results.Average(r => r.Score) : 0.0
        };

        // Generate insights
        var insights = GenerateInsights(new
        {
            query,
            searchType,
            results,
            searchDurationMs,
            matchQuality,
            directoryHotspots,
            projectContext
        }, mode);

        // Generate actions
        var actions = GenerateActions(new
        {
            query,
            searchType,
            results,
            workspacePath,
            directoryHotspots,
            extensionGroups
        }, tokenBudget);

        // Determine result limit based on mode
        var resultLimit = mode == ResponseMode.Summary ? 20 : 100;
        var displayResults = results.Take(resultLimit).ToList();

        return new
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
                performance = GetPerformanceRating(searchDurationMs),
                distribution = new
                {
                    byExtension = extensionGroups,
                    byLanguage = languageGroups
                }
            },
            analysis = new
            {
                patterns = insights.Take(3).ToList(),
                matchQuality = matchQuality,
                hotspots = new
                {
                    directories = directoryHotspots
                }
            },
            results = displayResults.Select(r => new
            {
                file = Path.GetFileName(r.Path),
                path = r.Path,
                score = Math.Round(r.Score, 2)
            }).ToList(),
            resultsSummary = new
            {
                included = displayResults.Count,
                total = results.Count,
                hasMore = results.Count > displayResults.Count
            },
            insights = insights,
            actions = actions.Select(a => a is AIAction aiAction ? new
            {
                id = aiAction.Id,
                cmd = aiAction.Command.Parameters.ContainsKey("file") 
                    ? new { file = aiAction.Command.Parameters["file"] }
                    : (object)aiAction.Command.Parameters,
                tokens = aiAction.EstimatedTokens,
                priority = aiAction.Priority.ToString().ToLowerInvariant()
            } : a),
            meta = new
            {
                mode = mode.ToString().ToLowerInvariant(),
                truncated = results.Count > displayResults.Count,
                tokens = EstimateResponseTokens(displayResults),
                cached = $"filesearch_{Guid.NewGuid().ToString("N")[..16]}_{BitConverter.ToString(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(query + searchType))).Replace("-", "").ToLowerInvariant()}"
            }
        };
    }

    protected override List<string> GenerateInsights(dynamic data, ResponseMode mode)
    {
        var insights = new List<string>();
        List<FileSearchResult> results = data.results;

        if (!results.Any())
        {
            insights.Add($"No files matching '{data.query}'");
            
            // Provide search type specific suggestions
            switch (data.searchType)
            {
                case "exact":
                    insights.Add("Try fuzzy or wildcard search for approximate matches");
                    break;
                case "fuzzy":
                    insights.Add("Try adjusting the search term or using wildcards");
                    break;
                case "wildcard":
                    insights.Add("Check wildcard pattern syntax (* for any characters, ? for single)");
                    break;
                case "regex":
                    insights.Add("Verify regex pattern is correct");
                    break;
            }
        }
        else
        {
            // Basic count insight
            insights.Add($"Found {results.Count} files in {data.searchDurationMs}ms");
            
            // Performance insight
            if (data.searchDurationMs < 10)
            {
                insights.Add("⚡ Excellent search performance");
            }
            else if (data.searchDurationMs < 50)
            {
                insights.Add("✓ Good search performance");
            }
            
            // Pattern insights
            if (results.Count == 1)
            {
                insights.Add("Single match - precise search result");
            }
            else if (data.matchQuality.exactMatches > results.Count * 0.5)
            {
                insights.Add($"{data.matchQuality.exactMatches} exact matches found");
            }
            
            // Extension patterns
            var extensions = results.Select(r => Path.GetExtension(r.Path)).Distinct().ToList();
            if (extensions.Count == 1)
            {
                insights.Add($"All results are {extensions[0]} files");
            }
            else if (extensions.Count <= 3)
            {
                insights.Add($"Results span {extensions.Count} file types: {string.Join(", ", extensions)}");
            }
            
            // Directory concentration
            if (data.directoryHotspots.Count > 0 && data.directoryHotspots[0].count > results.Count * 0.5)
            {
                insights.Add($"Concentrated in {data.directoryHotspots[0].path} directory");
            }
        }
        
        return insights;
    }

    protected override List<dynamic> GenerateActions(dynamic data, int tokenBudget)
    {
        var actions = new List<dynamic>();
        List<FileSearchResult> results = data.results;

        if (results.Any())
        {
            // Open best match
            var bestMatch = results.First();
            actions.Add(new AIAction
            {
                Id = "open_file",
                Description = "Open best match",
                Command = new AICommand
                {
                    Tool = "open_file",
                    Parameters = new Dictionary<string, object>
                    {
                        ["file"] = bestMatch.Path
                    }
                },
                EstimatedTokens = 100,
                Priority = Priority.Recommended
            });
            
            // Search within found files
            if (results.Count > 1)
            {
                var filePaths = results.Select(r => r.Path).ToList();
                actions.Add(new AIAction
                {
                    Id = "search_in_files",
                    Description = "Search for text within these files",
                    Command = new AICommand
                    {
                        Tool = "text_search",
                        Parameters = new Dictionary<string, object>
                        {
                            ["operation"] = "text_search",
                            ["files"] = filePaths
                        }
                    },
                    EstimatedTokens = 1500,
                    Priority = Priority.Available
                });
            }
        }
        else
        {
            // No results - suggest alternative searches
            actions.Add(new AIAction
            {
                Id = "try_fuzzy_search",
                Description = "Try fuzzy search for approximate matches",
                Command = new AICommand
                {
                    Tool = "file_search",
                    Parameters = new Dictionary<string, object>
                    {
                        ["query"] = $"{data.query}~",
                        ["searchType"] = "fuzzy"
                    }
                },
                EstimatedTokens = 200,
                Priority = Priority.Recommended
            });
            
            actions.Add(new AIAction
            {
                Id = "try_wildcard_search",
                Description = "Try wildcard search",
                Command = new AICommand
                {
                    Tool = "file_search",
                    Parameters = new Dictionary<string, object>
                    {
                        ["query"] = $"*{data.query}*",
                        ["searchType"] = "wildcard"
                    }
                },
                EstimatedTokens = 200,
                Priority = Priority.Recommended
            });
        }
        
        return actions;
    }

    private string GetPerformanceRating(double durationMs)
    {
        if (durationMs < 10) return "excellent";
        if (durationMs < 50) return "fast";
        if (durationMs < 200) return "good";
        if (durationMs < 1000) return "acceptable";
        return "slow";
    }

    private int EstimateResponseTokens(List<FileSearchResult> results)
    {
        // Base structure
        var baseTokens = 300;
        
        // Per result
        var perResultTokens = 20;
        
        // Analysis and insights
        var analysisTokens = 200;
        
        return baseTokens + (results.Count * perResultTokens) + analysisTokens;
    }
}