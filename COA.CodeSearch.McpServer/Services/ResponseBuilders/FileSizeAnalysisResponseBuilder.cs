using COA.CodeSearch.Contracts.Responses.FileSizeAnalysis;
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
/// Builds AI-optimized responses for file size analysis operations
/// </summary>
public class FileSizeAnalysisResponseBuilder : BaseResponseBuilder
{
    public FileSizeAnalysisResponseBuilder(ILogger<FileSizeAnalysisResponseBuilder> logger) 
        : base(logger)
    {
    }

    public override string ResponseType => "file_size_analysis";

    /// <summary>
    /// Build AI-optimized response for file size analysis results
    /// </summary>
    public object BuildResponse(
        string mode,
        string workspacePath,
        List<FileSizeResult> results,
        double searchDurationMs,
        FileSizeStatistics statistics,
        ResponseMode responseMode,
        ProjectContext? projectContext = null)
    {
        var tokenBudget = responseMode == ResponseMode.Summary ? SummaryTokenBudget : FullTokenBudget;

        // Group by extension
        var extensionGroups = results
            .GroupBy(r => r.Extension)
            .ToDictionary(
                g => g.Key,
                g => new ExtensionGroupValue { count = g.Count(), totalSize = g.Sum(r => r.FileSize) }
            );

        // Group by directory
        var directoryGroups = results
            .GroupBy(r => Path.GetDirectoryName(r.RelativePath) ?? "root")
            .OrderByDescending(g => g.Sum(r => r.FileSize))
            .Take(5)
            .Select(g => new FileSizeDirectoryGroup
            {
                directory = g.Key,
                fileCount = g.Count(),
                totalSize = g.Sum(r => r.FileSize),
                avgSize = g.Average(r => r.FileSize)
            })
            .ToList();

        // Find outliers
        var outliers = FindSizeOutliers(results, statistics);

        // Generate insights
        var insights = GenerateInsights(new
        {
            mode,
            results,
            searchDurationMs,
            statistics,
            extensionGroups,
            directoryGroups,
            outliers,
            projectContext
        }, responseMode);

        // Generate actions
        var actions = GenerateActions(new
        {
            mode,
            workspacePath,
            results,
            outliers,
            extensionGroups,
            statistics
        }, tokenBudget);

        // Determine result limit based on response mode
        var resultLimit = responseMode == ResponseMode.Summary ? 50 : 200;
        var displayResults = results.Take(resultLimit).ToList();

        return new FileSizeAnalysisResponse
        {
            success = true,
            operation = "file_size_analysis",
            query = new FileSizeQuery
            {
                mode = mode,
                workspace = Path.GetFileName(workspacePath)
            },
            summary = new FileSizeSummary
            {
                totalFiles = results.Count,
                searchTime = $"{searchDurationMs:F1}ms",
                totalSize = statistics.TotalSize,
                totalSizeFormatted = FormatFileSize(statistics.TotalSize),
                avgSize = FormatFileSize((long)statistics.AverageSize),
                medianSize = FormatFileSize((long)statistics.MedianSize)
            },
            statistics = new FileSizeStatisticsInfo
            {
                min = FormatFileSize(statistics.MinSize),
                max = FormatFileSize(statistics.MaxSize),
                mean = FormatFileSize((long)statistics.AverageSize),
                median = FormatFileSize((long)statistics.MedianSize),
                stdDev = FormatFileSize((long)statistics.StandardDeviation),
                distribution = new SizeDistribution
                {
                    tiny = statistics.SizeDistribution.GetValueOrDefault("tiny", 0),
                    small = statistics.SizeDistribution.GetValueOrDefault("small", 0),
                    medium = statistics.SizeDistribution.GetValueOrDefault("medium", 0),
                    large = statistics.SizeDistribution.GetValueOrDefault("large", 0),
                    huge = statistics.SizeDistribution.GetValueOrDefault("huge", 0)
                }
            },
            analysis = new FileSizeAnalysis
            {
                patterns = insights.Take(3).ToList(),
                outliers = outliers.Cast<object>().ToList(),
                hotspots = new FileSizeHotspots
                {
                    byDirectory = directoryGroups.Select(d => new HotspotDirectory
                    {
                        path = d.directory,
                        files = d.fileCount,
                        totalSize = FormatFileSize(d.totalSize),
                        avgSize = FormatFileSize((long)d.avgSize)
                    }).ToList(),
                    byExtension = extensionGroups
                        .OrderByDescending(kv => kv.Value.totalSize)
                        .Take(5)
                        .Select(kv => new HotspotExtension
                        {
                            extension = kv.Key,
                            count = kv.Value.count,
                            totalSize = FormatFileSize(kv.Value.totalSize)
                        })
                        .ToList()
                }
            },
            results = displayResults.Select(r => new FileSizeResultItem
            {
                file = r.FileName,
                path = r.RelativePath,
                size = r.FileSize,
                sizeFormatted = FormatFileSize(r.FileSize),
                extension = r.Extension,
                percentOfTotal = Math.Round((double)r.FileSize / statistics.TotalSize * 100, 2)
            }).ToList(),
            resultsSummary = new FileSizeResultsSummary
            {
                included = displayResults.Count,
                total = results.Count,
                hasMore = results.Count > displayResults.Count
            },
            insights = insights,
            actions = actions.Select(a => a is AIAction aiAction ? (object)new FileSizeAction
            {
                id = aiAction.Id,
                description = aiAction.Description,
                command = aiAction.Command.Tool,
                parameters = aiAction.Command.Parameters,
                estimatedTokens = aiAction.EstimatedTokens,
                priority = aiAction.Priority.ToString().ToLowerInvariant()
            } : a).ToList(),
            meta = new FileSizeMeta
            {
                mode = responseMode.ToString().ToLowerInvariant(),
                analysisMode = mode,
                truncated = results.Count > displayResults.Count,
                tokens = EstimateResponseTokens(displayResults),
                format = "ai-optimized"
            }
        };
    }

    protected override List<string> GenerateInsights(dynamic data, ResponseMode mode)
    {
        var insights = new List<string>();
        List<FileSizeResult> results = data.results;
        FileSizeStatistics statistics = data.statistics;

        if (!results.Any())
        {
            insights.Add($"No files found for {data.mode} analysis");
            return insights;
        }

        // Basic summary
        insights.Add($"Analyzed {results.Count} files totaling {FormatFileSize(statistics.TotalSize)}");

        // Mode-specific insights
        switch (data.mode)
        {
            case "largest":
                if (results.Count > 0)
                {
                    var top = results.First();
                    var percentage = (double)top.FileSize / statistics.TotalSize * 100;
                    insights.Add($"Largest file is {percentage:F1}% of total size");
                    
                    if (top.FileSize > 100 * 1024 * 1024) // 100MB
                    {
                        insights.Add("⚠️ Very large file detected - consider optimization");
                    }
                }
                break;
                
            case "smallest":
                var nonEmpty = results.Count(r => r.FileSize > 0);
                insights.Add($"Found {nonEmpty} non-empty files");
                break;
                
            case "zero":
                insights.Add($"Found {results.Count} empty files");
                if (results.Count > 10)
                {
                    insights.Add("Consider cleaning up empty files");
                }
                break;
        }

        // Distribution insights
        if (statistics.SizeDistribution.GetValueOrDefault("huge", 0) > 0)
        {
            insights.Add($"{statistics.SizeDistribution["huge"]} files exceed 50MB");
        }

        // Outlier insights
        if (data.outliers.Count > 0)
        {
            insights.Add($"Found {data.outliers.Count} size outliers (>3 std dev from mean)");
        }

        // Extension insights
        if (data.extensionGroups.Count > 0)
        {
            // Cast results to proper type to avoid dynamic issues
            List<FileSizeResult> sizeResults = data.results;
            var firstExt = sizeResults
                .GroupBy(r => r.Extension)
                .Select(g => new { Extension = g.Key, TotalSize = g.Sum(r => r.FileSize) })
                .OrderByDescending(x => x.TotalSize)
                .First();
            var percentage = (double)firstExt.TotalSize / statistics.TotalSize * 100;
            insights.Add($"{firstExt.Extension} files account for {percentage:F1}% of total size");
        }

        // Directory concentration
        if (data.directoryGroups.Count > 0)
        {
            var topDir = data.directoryGroups[0];
            if (topDir.totalSize > statistics.TotalSize * 0.5)
            {
                insights.Add($"Over 50% of size concentrated in {topDir.directory}");
            }
        }

        return insights;
    }

    protected override List<dynamic> GenerateActions(dynamic data, int tokenBudget)
    {
        var actions = new List<dynamic>();
        List<FileSizeResult> results = data.results;
        FileSizeStatistics statistics = data.statistics;

        if (results.Any())
        {
            switch (data.mode)
            {
                case "largest":
                    // Analyze largest file
                    var largest = results.First();
                    actions.Add(new AIAction
                    {
                        Id = "analyze_largest",
                        Description = "Analyze largest file content",
                        Command = new AICommand
                        {
                            Tool = "file_analysis",
                            Parameters = new Dictionary<string, object>
                            {
                                ["file_path"] = Path.Combine(data.workspacePath, largest.RelativePath)
                            }
                        },
                        EstimatedTokens = 1500,
                        Priority = Priority.Recommended
                    });

                    // Compress suggestion for very large files
                    if (largest.FileSize > 50 * 1024 * 1024) // 50MB
                    {
                        actions.Add(new AIAction
                        {
                            Id = "suggest_compression",
                            Description = "Suggest compression strategies",
                            Command = new AICommand
                            {
                                Tool = "compression_analysis",
                                Parameters = new Dictionary<string, object>
                                {
                                    ["files"] = results.Take(5).Select(r => Path.Combine(data.workspacePath, r.RelativePath)).ToList()
                                }
                            },
                            EstimatedTokens = 800,
                            Priority = Priority.High
                        });
                    }
                    break;

                case "zero":
                    // Clean up empty files
                    if (results.Count > 5)
                    {
                        actions.Add(new AIAction
                        {
                            Id = "cleanup_empty",
                            Description = "Review and clean up empty files",
                            Command = new AICommand
                            {
                                Tool = "cleanup_review",
                                Parameters = new Dictionary<string, object>
                                {
                                    ["files"] = results.Select(r => Path.Combine(data.workspacePath, r.RelativePath)).ToList()
                                }
                            },
                            EstimatedTokens = 500,
                            Priority = Priority.Medium
                        });
                    }
                    break;
            }

            // Search for duplicates among large files
            if (data.mode == "largest" && results.Count > 3)
            {
                var similarSizes = results
                    .GroupBy(r => r.FileSize)
                    .Where(g => g.Count() > 1)
                    .Any();

                if (similarSizes)
                {
                    actions.Add(new AIAction
                    {
                        Id = "find_duplicates",
                        Description = "Check for duplicate large files",
                        Command = new AICommand
                        {
                            Tool = "duplicate_analysis",
                            Parameters = new Dictionary<string, object>
                            {
                                ["files"] = results.Take(10).Select(r => Path.Combine(data.workspacePath, r.RelativePath)).ToList()
                            }
                        },
                        EstimatedTokens = 1200,
                        Priority = Priority.Medium
                    });
                }
            }

            // Storage optimization
            if (statistics.TotalSize > 1024 * 1024 * 1024) // 1GB
            {
                actions.Add(new AIAction
                {
                    Id = "storage_optimization",
                    Description = "Analyze storage optimization opportunities",
                    Command = new AICommand
                    {
                        Tool = "storage_analysis",
                        Parameters = new Dictionary<string, object>
                        {
                            ["workspacePath"] = data.workspacePath,
                            ["focusExtensions"] = ((List<FileSizeResult>)data.results)
                                .GroupBy(r => r.Extension)
                                .Select(g => new { Extension = g.Key, TotalSize = g.Sum(r => r.FileSize) })
                                .OrderByDescending(x => x.TotalSize)
                                .Take(3)
                                .Select(x => x.Extension)
                                .ToList()
                        }
                    },
                    EstimatedTokens = 1000,
                    Priority = Priority.Available
                });
            }
        }
        else
        {
            // No results - suggest different analysis mode
            actions.Add(new AIAction
            {
                Id = "try_different_mode",
                Description = "Try different analysis mode",
                Command = new AICommand
                {
                    Tool = "file_size_analysis",
                    Parameters = new Dictionary<string, object>
                    {
                        ["workspacePath"] = data.workspacePath,
                        ["mode"] = data.mode == "largest" ? "distribution" : "largest"
                    }
                },
                EstimatedTokens = 500,
                Priority = Priority.Recommended
            });
        }

        return actions;
    }

    private List<SizeOutlier> FindSizeOutliers(List<FileSizeResult> results, FileSizeStatistics statistics)
    {
        if (!results.Any() || statistics.StandardDeviation == 0)
            return new List<SizeOutlier>();

        var outliers = new List<SizeOutlier>();
        var mean = statistics.AverageSize;
        var stdDev = statistics.StandardDeviation;

        foreach (var result in results)
        {
            var zScore = Math.Abs((result.FileSize - mean) / stdDev);
            if (zScore > 3) // More than 3 standard deviations
            {
                outliers.Add(new SizeOutlier
                {
                    file = result.FileName,
                    path = result.RelativePath,
                    size = FormatFileSize(result.FileSize),
                    zScore = Math.Round(zScore, 2)
                });
            }
        }

        return outliers.Take(5).ToList();
    }

    private int EstimateResponseTokens(List<FileSizeResult> results)
    {
        // Base structure
        var baseTokens = 500;
        
        // Per result
        var perResultTokens = 30;
        
        // Analysis and insights
        var analysisTokens = 400;
        
        return baseTokens + (results.Count * perResultTokens) + analysisTokens;
    }
}