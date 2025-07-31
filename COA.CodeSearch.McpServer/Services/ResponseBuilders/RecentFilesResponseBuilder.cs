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
/// Builds AI-optimized responses for recent files operations
/// </summary>
public class RecentFilesResponseBuilder : BaseResponseBuilder
{
    public RecentFilesResponseBuilder(ILogger<RecentFilesResponseBuilder> logger) 
        : base(logger)
    {
    }

    public override string ResponseType => "recent_files";

    /// <summary>
    /// Build AI-optimized response for recent files results
    /// </summary>
    public object BuildResponse(
        string workspacePath,
        string timeFrame,
        DateTime cutoffTime,
        List<RecentFileResult> results,
        double searchDurationMs,
        Dictionary<string, int> extensionCounts,
        long totalSize,
        ResponseMode mode,
        ProjectContext? projectContext = null)
    {
        var tokenBudget = mode == ResponseMode.Summary ? SummaryTokenBudget : FullTokenBudget;

        // Group by time buckets
        var now = DateTime.UtcNow;
        var timeBuckets = new
        {
            lastHour = results.Count(r => r.LastModified > now.AddHours(-1)),
            last24Hours = results.Count(r => r.LastModified > now.AddDays(-1)),
            lastWeek = results.Count(r => r.LastModified > now.AddDays(-7)),
            older = results.Count(r => r.LastModified <= now.AddDays(-7))
        };

        // Group by directory
        var directoryGroups = results
            .GroupBy(r => Path.GetDirectoryName(r.RelativePath) ?? "root")
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => new
            {
                directory = g.Key,
                fileCount = g.Count(),
                totalSize = g.Sum(r => r.FileSize),
                mostRecent = g.Max(r => r.LastModified)
            })
            .ToList();

        // Find modification patterns
        var modificationPatterns = AnalyzeModificationPatterns(results);

        // Generate insights
        var insights = GenerateInsights(new
        {
            workspacePath,
            timeFrame,
            cutoffTime,
            results,
            searchDurationMs,
            timeBuckets,
            directoryGroups,
            modificationPatterns,
            extensionCounts,
            totalSize,
            projectContext
        }, mode);

        // Generate actions
        var actions = GenerateActions(new
        {
            workspacePath,
            results,
            directoryGroups,
            extensionCounts,
            modificationPatterns
        }, tokenBudget);

        // Determine result limit based on mode
        var resultLimit = mode == ResponseMode.Summary ? 50 : 200;
        var displayResults = results.Take(resultLimit).ToList();

        return new
        {
            success = true,
            operation = "recent_files",
            query = new
            {
                timeFrame = timeFrame,
                cutoff = cutoffTime.ToString("yyyy-MM-dd HH:mm:ss"),
                workspace = Path.GetFileName(workspacePath)
            },
            summary = new
            {
                totalFound = results.Count,
                searchTime = $"{searchDurationMs:F1}ms",
                totalSize = totalSize,
                totalSizeFormatted = FormatFileSize(totalSize),
                avgFileSize = results.Any() ? FormatFileSize((long)(totalSize / results.Count)) : "0 B",
                distribution = new
                {
                    byTime = timeBuckets,
                    byExtension = extensionCounts
                }
            },
            analysis = new
            {
                patterns = insights.Take(3).ToList(),
                hotspots = new
                {
                    directories = directoryGroups.Select(d => (object)new
                    {
                        path = d.directory,
                        files = d.fileCount,
                        size = FormatFileSize(d.totalSize),
                        lastModified = FormatTimeAgo(d.mostRecent)
                    }).ToList()
                },
                activityPattern = modificationPatterns
            },
            results = displayResults.Select(r => new
            {
                file = r.FileName,
                path = r.RelativePath,
                modified = r.LastModified.ToString("yyyy-MM-dd HH:mm:ss"),
                modifiedAgo = FormatTimeAgo(r.LastModified),
                size = r.FileSize,
                sizeFormatted = FormatFileSize(r.FileSize),
                extension = r.Extension
            }).ToList(),
            resultsSummary = new
            {
                included = displayResults.Count,
                total = results.Count,
                hasMore = results.Count > displayResults.Count
            },
            insights = insights,
            actions = actions.Select(a => a is AIAction aiAction ? (object)new
            {
                id = aiAction.Id,
                description = aiAction.Description,
                command = aiAction.Command.Tool,
                parameters = aiAction.Command.Parameters,
                estimatedTokens = aiAction.EstimatedTokens,
                priority = aiAction.Priority.ToString().ToLowerInvariant()
            } : a),
            meta = new
            {
                mode = mode.ToString().ToLowerInvariant(),
                truncated = results.Count > displayResults.Count,
                tokens = EstimateResponseTokens(displayResults),
                format = "ai-optimized",
                indexed = true
            }
        };
    }

    protected override List<string> GenerateInsights(dynamic data, ResponseMode mode)
    {
        var insights = new List<string>();
        List<RecentFileResult> results = data.results;

        if (!results.Any())
        {
            insights.Add($"No files modified in the {data.timeFrame} timeframe");
            insights.Add("Try expanding the time range or checking workspace activity");
        }
        else
        {
            // Basic count insight
            insights.Add($"Found {results.Count} files modified in the last {data.timeFrame}");
            
            // Activity level insight
            if (data.timeBuckets.lastHour > 0)
            {
                insights.Add($"🔥 Active development: {data.timeBuckets.lastHour} files modified in the last hour");
            }
            else if (data.timeBuckets.last24Hours > 10)
            {
                insights.Add($"High activity: {data.timeBuckets.last24Hours} files changed today");
            }
            
            // Size insights
            insights.Add($"Total size of modified files: {FormatFileSize(data.totalSize)}");
            
            // Extension patterns
            var extensionCountsDict = (Dictionary<string, int>)data.extensionCounts;
            if (extensionCountsDict.Count == 1)
            {
                var ext = extensionCountsDict.First();
                insights.Add($"All modifications are {ext.Key} files");
            }
            else if (extensionCountsDict.Count > 1)
            {
                var topExt = extensionCountsDict.OrderByDescending(kv => kv.Value).First();
                insights.Add($"Most modified: {topExt.Key} files ({topExt.Value} files)");
            }
            
            // Directory concentration
            if (data.directoryGroups.Count > 0)
            {
                var topDir = data.directoryGroups[0];
                if (topDir.fileCount > results.Count * 0.5)
                {
                    insights.Add($"Heavy activity in {topDir.directory} directory");
                }
            }
            
            // Modification patterns
            if (data.modificationPatterns.burstActivity)
            {
                insights.Add("Burst activity detected - multiple files modified together");
            }
            
            if (data.modificationPatterns.workingHours)
            {
                insights.Add($"Most modifications during {data.modificationPatterns.peakHour}:00 hours");
            }
        }
        
        return insights;
    }

    protected override List<dynamic> GenerateActions(dynamic data, int tokenBudget)
    {
        var actions = new List<dynamic>();
        List<RecentFileResult> results = data.results;

        if (results.Any())
        {
            // View most recent file
            var mostRecent = results.First();
            actions.Add(new AIAction
            {
                Id = "view_recent",
                Description = "View most recently modified file",
                Command = new AICommand
                {
                    Tool = "read",
                    Parameters = new Dictionary<string, object>
                    {
                        ["file_path"] = Path.Combine(data.workspacePath, mostRecent.RelativePath)
                    }
                },
                EstimatedTokens = 1000,
                Priority = Priority.Recommended
            });
            
            // Search in recent files
            if (results.Count > 5)
            {
                actions.Add(new AIAction
                {
                    Id = "search_recent",
                    Description = "Search for TODO/FIXME in recent files",
                    Command = new AICommand
                    {
                        Tool = "text_search",
                        Parameters = new Dictionary<string, object>
                        {
                            ["query"] = "TODO|FIXME|BUG|HACK",
                            ["files"] = results.Take(20).Select(r => Path.Combine(data.workspacePath, r.RelativePath)).ToList()
                        }
                    },
                    EstimatedTokens = 1500,
                    Priority = Priority.Available
                });
            }
            
            // Analyze hot directory
            if (data.directoryGroups.Count > 0 && data.directoryGroups[0].fileCount > 5)
            {
                var hotDir = data.directoryGroups[0];
                actions.Add(new AIAction
                {
                    Id = "analyze_hot_directory",
                    Description = $"Analyze active directory: {hotDir.directory}",
                    Command = new AICommand
                    {
                        Tool = "directory_analysis",
                        Parameters = new Dictionary<string, object>
                        {
                            ["path"] = Path.Combine(data.workspacePath, hotDir.directory)
                        }
                    },
                    EstimatedTokens = 800,
                    Priority = Priority.Medium
                });
            }
            
            // Git status if many recent changes
            if (results.Count > 20)
            {
                actions.Add(new AIAction
                {
                    Id = "check_git_status",
                    Description = "Check git status for uncommitted changes",
                    Command = new AICommand
                    {
                        Tool = "git",
                        Parameters = new Dictionary<string, object>
                        {
                            ["command"] = "status",
                            ["workingDirectory"] = data.workspacePath
                        }
                    },
                    EstimatedTokens = 500,
                    Priority = Priority.Available
                });
            }
            
            // Store insight about activity
            if (data.modificationPatterns.burstActivity && results.Count > 10)
            {
                actions.Add(new AIAction
                {
                    Id = "store_activity_insight",
                    Description = "Remember this development session",
                    Command = new AICommand
                    {
                        Tool = "store_memory",
                        Parameters = new Dictionary<string, object>
                        {
                            ["memoryType"] = "WorkSession",
                            ["content"] = $"Active development session with {results.Count} files modified in {data.workspacePath}",
                            ["files"] = results.Take(10).Select(r => Path.Combine(data.workspacePath, r.RelativePath)).ToList()
                        }
                    },
                    EstimatedTokens = 200,
                    Priority = Priority.Low
                });
            }
        }
        else
        {
            // No recent files - suggest broader search
            actions.Add(new AIAction
            {
                Id = "expand_timeframe",
                Description = "Search last 7 days",
                Command = new AICommand
                {
                    Tool = "recent_files",
                    Parameters = new Dictionary<string, object>
                    {
                        ["workspacePath"] = data.workspacePath,
                        ["timeFrame"] = "7d"
                    }
                },
                EstimatedTokens = 500,
                Priority = Priority.Recommended
            });
        }
        
        return actions;
    }

    private dynamic AnalyzeModificationPatterns(List<RecentFileResult> results)
    {
        if (!results.Any())
        {
            return new { burstActivity = false, workingHours = false, peakHour = 0 };
        }

        // Check for burst activity (multiple files modified within short time)
        var sortedByTime = results.OrderBy(r => r.LastModified).ToList();
        var burstActivity = false;
        
        for (int i = 1; i < Math.Min(sortedByTime.Count, 10); i++)
        {
            var timeDiff = sortedByTime[i].LastModified - sortedByTime[i - 1].LastModified;
            if (timeDiff.TotalMinutes < 5)
            {
                burstActivity = true;
                break;
            }
        }

        // Find peak working hour
        var hourGroups = results
            .GroupBy(r => r.LastModified.Hour)
            .OrderByDescending(g => g.Count())
            .ToList();

        var peakHour = hourGroups.Any() ? hourGroups.First().Key : 0;
        var workingHours = hourGroups.Any() && hourGroups.First().Count() > results.Count * 0.3;

        return new
        {
            burstActivity,
            workingHours,
            peakHour
        };
    }

    private int EstimateResponseTokens(List<RecentFileResult> results)
    {
        // Base structure
        var baseTokens = 400;
        
        // Per result
        var perResultTokens = 35;
        
        // Analysis and insights
        var analysisTokens = 300;
        
        return baseTokens + (results.Count * perResultTokens) + analysisTokens;
    }
}