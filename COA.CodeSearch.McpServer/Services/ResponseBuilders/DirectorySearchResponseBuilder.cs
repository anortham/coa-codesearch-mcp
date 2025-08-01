using COA.CodeSearch.Contracts.Responses.DirectorySearch;
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
/// Builds AI-optimized responses for directory search operations
/// </summary>
public class DirectorySearchResponseBuilder : BaseResponseBuilder
{
    public DirectorySearchResponseBuilder(ILogger<DirectorySearchResponseBuilder> logger) 
        : base(logger)
    {
    }

    public override string ResponseType => "directory_search";

    /// <summary>
    /// Build AI-optimized response for directory search results
    /// </summary>
    public object BuildResponse(
        string query,
        string searchType,
        string workspacePath,
        List<DirectorySearchResult> results,
        double searchDurationMs,
        ResponseMode mode,
        ProjectContext? projectContext)
    {
        var tokenBudget = mode == ResponseMode.Summary ? SummaryTokenBudget : FullTokenBudget;

        // Group by parent directory
        var parentGroups = results
            .GroupBy(r => Path.GetDirectoryName(r.RelativePath) ?? "root")
            .OrderByDescending(g => g.Count())
            .Take(5)
            .ToDictionary(g => g.Key, g => g.Count());

        // Calculate depth statistics
        var depthStats = results
            .GroupBy(r => r.RelativePath.Count(c => c == Path.DirectorySeparatorChar))
            .ToDictionary(g => g.Key, g => g.Count());

        // Find directories with most files
        var directoriesWithFiles = results
            .Where(r => r.FileCount > 0)
            .OrderByDescending(r => r.FileCount)
            .Take(5)
            .Select(r => new DirectoryFileItem
            {
                path = r.RelativePath,
                fileCount = r.FileCount
            })
            .ToList();

        // Generate insights
        var insights = GenerateInsights(new
        {
            query,
            searchType,
            results,
            searchDurationMs,
            depthStats,
            directoriesWithFiles,
            projectContext
        }, mode);

        // Generate actions
        var actions = GenerateActions(new
        {
            query,
            searchType,
            results,
            workspacePath,
            directoriesWithFiles
        }, tokenBudget);

        // Determine result limit based on mode
        var resultLimit = mode == ResponseMode.Summary ? 30 : 100;
        var displayResults = results.Take(resultLimit).ToList();

        return new DirectorySearchResponse
        {
            success = true,
            operation = "directory_search",
            query = new DirectorySearchQuery
            {
                text = query,
                type = searchType,
                workspace = Path.GetFileName(workspacePath)
            },
            summary = new DirectorySearchSummary
            {
                totalFound = results.Count,
                searchTime = $"{searchDurationMs:F1}ms",
                performance = GetPerformanceRating(searchDurationMs),
                avgDepth = results.Any() ? results.Average(r => r.RelativePath.Count(c => c == Path.DirectorySeparatorChar)) : 0
            },
            analysis = new DirectorySearchAnalysis
            {
                patterns = insights.Take(3).ToList(),
                depthDistribution = depthStats,
                hotspots = new DirectorySearchHotspots
                {
                    byParent = parentGroups,
                    byFileCount = directoriesWithFiles
                }
            },
            results = displayResults.Select(r => new DirectorySearchResultItem
            {
                name = r.DirectoryName,
                path = r.RelativePath,
                fileCount = r.FileCount,
                depth = r.RelativePath.Count(c => c == Path.DirectorySeparatorChar),
                score = Math.Round(r.Score, 2)
            }).ToList(),
            resultsSummary = new DirectorySearchResultsSummary
            {
                included = displayResults.Count,
                total = results.Count,
                hasMore = results.Count > displayResults.Count
            },
            insights = insights,
            actions = actions.Select(a => a is AIAction aiAction ? new DirectorySearchAction
            {
                id = aiAction.Id,
                description = aiAction.Description,
                command = aiAction.Command.Tool,
                parameters = aiAction.Command.Parameters,
                priority = aiAction.Priority.ToString().ToLowerInvariant()
            } : a).ToList(),
            meta = new DirectorySearchMeta
            {
                mode = mode.ToString().ToLowerInvariant(),
                truncated = results.Count > displayResults.Count,
                tokens = EstimateResponseTokens(displayResults),
                format = "ai-optimized",
                cached = $"dirsearch_{Guid.NewGuid().ToString("N")[..16]}_{BitConverter.ToString(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(query + searchType))).Replace("-", "").ToLowerInvariant()}"
            }
        };
    }

    protected override List<string> GenerateInsights(dynamic data, ResponseMode mode)
    {
        var insights = new List<string>();
        List<DirectorySearchResult> results = data.results;

        if (!results.Any())
        {
            insights.Add($"No directories matching '{data.query}'");
            
            // Provide search type specific suggestions
            switch (data.searchType)
            {
                case "exact":
                    insights.Add("Try fuzzy or wildcard search for approximate matches");
                    break;
                case "fuzzy":
                    insights.Add("Check spelling or try broader patterns");
                    break;
                case "wildcard":
                    insights.Add("Verify wildcard pattern (* for any characters)");
                    break;
            }
        }
        else
        {
            // Basic count insight
            insights.Add($"Found {results.Count} directories in {data.searchDurationMs}ms");
            
            // Performance insight
            if (data.searchDurationMs < 10)
            {
                insights.Add("⚡ Excellent search performance");
            }
            
            // Structure insights
            if (data.depthStats.Count == 1)
            {
                insights.Add($"All directories at same depth level");
            }
            else if (data.depthStats.Count > 0)
            {
                var depthStatsDict = (Dictionary<int, int>)data.depthStats;
                var maxDepth = depthStatsDict.Keys.Max();
                insights.Add($"Directory tree spans {maxDepth + 1} levels");
            }
            
            // File concentration insights
            if (data.directoriesWithFiles.Count > 0)
            {
                var topDir = data.directoriesWithFiles[0];
                insights.Add($"Largest directory: {topDir.path} ({topDir.fileCount} files)");
            }
            
            // Pattern insights
            var commonPrefix = FindCommonPrefix(results.Select(r => r.RelativePath).ToList());
            if (!string.IsNullOrEmpty(commonPrefix) && commonPrefix.Length > 3)
            {
                insights.Add($"Common path prefix: {commonPrefix}");
            }
        }
        
        return insights;
    }

    protected override List<dynamic> GenerateActions(dynamic data, int tokenBudget)
    {
        var actions = new List<dynamic>();
        List<DirectorySearchResult> results = data.results;

        if (results.Any())
        {
            // Explore top directory
            var topResult = results.First();
            actions.Add(new AIAction
            {
                Id = "explore_directory",
                Description = "Explore top matching directory",
                Command = new AICommand
                {
                    Tool = "ls",
                    Parameters = new Dictionary<string, object>
                    {
                        ["path"] = Path.Combine(data.workspacePath, topResult.RelativePath)
                    }
                },
                EstimatedTokens = 200,
                Priority = Priority.Recommended
            });
            
            // Search for files in directories
            if (results.Count > 1)
            {
                actions.Add(new AIAction
                {
                    Id = "search_in_directories",
                    Description = "Search for files in these directories",
                    Command = new AICommand
                    {
                        Tool = "file_search",
                        Parameters = new Dictionary<string, object>
                        {
                            ["query"] = "*",
                            ["workspacePath"] = data.workspacePath,
                            ["filePattern"] = $"{results.First().RelativePath}/**/*"
                        }
                    },
                    EstimatedTokens = 500,
                    Priority = Priority.Available
                });
            }
            
            // For directories with files, suggest text search
            if (data.directoriesWithFiles.Count > 0)
            {
                var topFileDir = data.directoriesWithFiles[0];
                actions.Add(new AIAction
                {
                    Id = "search_text_in_directory",
                    Description = $"Search for text in {topFileDir.path}",
                    Command = new AICommand
                    {
                        Tool = "text_search",
                        Parameters = new Dictionary<string, object>
                        {
                            ["query"] = "TODO",
                            ["workspacePath"] = data.workspacePath,
                            ["filePattern"] = $"{topFileDir.path}/**/*"
                        }
                    },
                    EstimatedTokens = 1000,
                    Priority = Priority.Available
                });
            }
        }
        else
        {
            // No results - suggest alternative searches
            actions.Add(new AIAction
            {
                Id = "try_fuzzy",
                Description = "Try fuzzy search",
                Command = new AICommand
                {
                    Tool = "directory_search",
                    Parameters = new Dictionary<string, object>
                    {
                        ["query"] = $"{data.query}~",
                        ["searchType"] = "fuzzy",
                        ["workspacePath"] = data.workspacePath
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
        return "acceptable";
    }

    private int EstimateResponseTokens(List<DirectorySearchResult> results)
    {
        // Base structure
        var baseTokens = 300;
        
        // Per result
        var perResultTokens = 30;
        
        // Analysis and insights
        var analysisTokens = 200;
        
        return baseTokens + (results.Count * perResultTokens) + analysisTokens;
    }

    private string FindCommonPrefix(List<string> paths)
    {
        if (!paths.Any()) return "";
        if (paths.Count == 1) return "";
        
        var minLength = paths.Min(p => p.Length);
        var commonPrefix = "";
        
        for (int i = 0; i < minLength; i++)
        {
            var currentChar = paths[0][i];
            if (paths.All(p => p[i] == currentChar))
            {
                commonPrefix += currentChar;
            }
            else
            {
                break;
            }
        }
        
        // Trim to last directory separator
        var lastSeparator = commonPrefix.LastIndexOf(Path.DirectorySeparatorChar);
        if (lastSeparator > 0)
        {
            commonPrefix = commonPrefix.Substring(0, lastSeparator);
        }
        
        return commonPrefix;
    }
}