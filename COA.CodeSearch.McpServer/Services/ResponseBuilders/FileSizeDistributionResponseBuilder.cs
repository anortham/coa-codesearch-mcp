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
/// Builds AI-optimized responses for file size distribution analysis
/// </summary>
public class FileSizeDistributionResponseBuilder : BaseResponseBuilder
{
    public FileSizeDistributionResponseBuilder(ILogger<FileSizeDistributionResponseBuilder> logger) 
        : base(logger)
    {
    }

    public override string ResponseType => "file_size_distribution";

    /// <summary>
    /// Build AI-optimized response for file size distribution results
    /// </summary>
    public object BuildResponse(
        string workspacePath,
        FileSizeStatistics statistics,
        double searchDurationMs,
        Dictionary<string, List<FileSizeResult>> buckets,
        ResponseMode responseMode,
        ProjectContext? projectContext = null)
    {
        var tokenBudget = responseMode == ResponseMode.Summary ? SummaryTokenBudget : FullTokenBudget;

        // Calculate bucket summaries
        var bucketSummaries = buckets.ToDictionary(
            kv => kv.Key,
            kv => new
            {
                count = kv.Value.Count,
                totalSize = kv.Value.Sum(f => f.FileSize),
                avgSize = kv.Value.Any() ? kv.Value.Average(f => f.FileSize) : 0,
                examples = kv.Value.Take(3).Select(f => f.FileName).ToList()
            }
        );

        // Find dominant bucket
        var dominantBucket = bucketSummaries
            .OrderByDescending(kv => kv.Value.totalSize)
            .FirstOrDefault();

        // Calculate distribution metrics
        var distributionMetrics = CalculateDistributionMetrics(statistics, buckets);

        // Generate insights
        var insights = GenerateInsights(new
        {
            statistics,
            searchDurationMs,
            buckets,
            bucketSummaries,
            dominantBucket,
            distributionMetrics,
            projectContext
        }, responseMode);

        // Generate actions
        // Cast bucketSummaries to avoid dynamic lambda issue
        var bucketSummariesDict = bucketSummaries.ToDictionary(
            kv => kv.Key,
            kv => (object)kv.Value
        );
        
        var actions = GenerateActions(new
        {
            workspacePath,
            buckets,
            bucketSummaries = bucketSummariesDict,
            dominantBucket,
            statistics
        }, tokenBudget);

        return new
        {
            success = true,
            operation = "file_size_distribution",
            workspace = Path.GetFileName(workspacePath),
            summary = new
            {
                totalFiles = statistics.FileCount,
                totalSize = FormatFileSize(statistics.TotalSize),
                searchTime = $"{searchDurationMs:F1}ms",
                buckets = new
                {
                    tiny = new { range = "0-1KB", count = buckets.GetValueOrDefault("tiny", new List<FileSizeResult>()).Count },
                    small = new { range = "1KB-100KB", count = buckets.GetValueOrDefault("small", new List<FileSizeResult>()).Count },
                    medium = new { range = "100KB-1MB", count = buckets.GetValueOrDefault("medium", new List<FileSizeResult>()).Count },
                    large = new { range = "1MB-50MB", count = buckets.GetValueOrDefault("large", new List<FileSizeResult>()).Count },
                    huge = new { range = "50MB+", count = buckets.GetValueOrDefault("huge", new List<FileSizeResult>()).Count }
                }
            },
            statistics = new
            {
                mean = FormatFileSize((long)statistics.AverageSize),
                median = FormatFileSize((long)statistics.MedianSize),
                stdDev = FormatFileSize((long)statistics.StandardDeviation),
                min = FormatFileSize(statistics.MinSize),
                max = FormatFileSize(statistics.MaxSize),
                range = FormatFileSize(statistics.MaxSize - statistics.MinSize),
                skewness = distributionMetrics.skewness,
                kurtosis = distributionMetrics.kurtosis
            },
            distribution = bucketSummaries.Select(kv => (object)new
            {
                bucket = kv.Key,
                count = kv.Value.count,
                percentage = Math.Round((double)kv.Value.count / statistics.FileCount * 100, 2),
                totalSize = FormatFileSize(kv.Value.totalSize),
                sizePercentage = Math.Round((double)kv.Value.totalSize / statistics.TotalSize * 100, 2),
                avgSize = FormatFileSize((long)kv.Value.avgSize),
                examples = kv.Value.examples
            }).ToList(),
            analysis = new
            {
                patterns = insights.Take(3).ToList(),
                dominant = dominantBucket.Key != null ? new
                {
                    bucket = dominantBucket.Key,
                    impact = $"{Math.Round((double)dominantBucket.Value.totalSize / statistics.TotalSize * 100, 1)}% of total size"
                } : null,
                balance = distributionMetrics.balance,
                recommendation = GenerateDistributionRecommendation(distributionMetrics, bucketSummariesDict)
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
                mode = responseMode.ToString().ToLowerInvariant(),
                tokens = EstimateResponseTokens(buckets),
                format = "ai-optimized"
            }
        };
    }

    protected override List<string> GenerateInsights(dynamic data, ResponseMode mode)
    {
        var insights = new List<string>();
        FileSizeStatistics statistics = data.statistics;
        var buckets = (Dictionary<string, List<FileSizeResult>>)data.buckets;

        // Basic distribution insight
        insights.Add($"Analyzed {statistics.FileCount} files across 5 size categories");

        // Balance insight
        if (data.distributionMetrics.balance == "heavily_skewed")
        {
            insights.Add("⚠️ File size distribution is heavily skewed");
        }
        else if (data.distributionMetrics.balance == "well_balanced")
        {
            insights.Add("✓ File sizes are well distributed");
        }

        // Dominant bucket insight
        if (data.dominantBucket.Key != null)
        {
            var percentage = (double)data.dominantBucket.Value.totalSize / statistics.TotalSize * 100;
            insights.Add($"{data.dominantBucket.Key} files dominate with {percentage:F1}% of total size");
        }

        // Empty files insight
        var emptyCount = buckets.Values.SelectMany(b => b).Count(f => f.FileSize == 0);
        if (emptyCount > 0)
        {
            insights.Add($"Found {emptyCount} empty files");
        }

        // Large files insight
        var hugeFiles = buckets.GetValueOrDefault("huge", new List<FileSizeResult>());
        if (hugeFiles.Any())
        {
            var totalHugeSize = hugeFiles.Sum(f => f.FileSize);
            insights.Add($"{hugeFiles.Count} files exceed 50MB (total: {FormatFileSize(totalHugeSize)})");
        }

        // Median vs Mean insight
        if (statistics.MedianSize < statistics.AverageSize * 0.5)
        {
            insights.Add("Median much lower than mean - few large files skew the average");
        }

        // Extension concentration in buckets
        foreach (var bucket in buckets.Where(b => b.Value.Count > 10))
        {
            var topExtension = bucket.Value
                .GroupBy(f => f.Extension)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();
            
            if (topExtension != null && topExtension.Count() > bucket.Value.Count * 0.5)
            {
                insights.Add($"Most {bucket.Key} files are {topExtension.Key} files");
            }
        }

        return insights;
    }

    protected override List<dynamic> GenerateActions(dynamic data, int tokenBudget)
    {
        var actions = new List<dynamic>();
        var buckets = (Dictionary<string, List<FileSizeResult>>)data.buckets;
        FileSizeStatistics statistics = data.statistics;

        // Analyze huge files if present
        var hugeFiles = buckets.GetValueOrDefault("huge", new List<FileSizeResult>());
        if (hugeFiles.Any())
        {
            actions.Add(new AIAction
            {
                Id = "analyze_huge_files",
                Description = "Analyze files over 50MB",
                Command = new AICommand
                {
                    Tool = "file_size_analysis",
                    Parameters = new Dictionary<string, object>
                    {
                        ["workspacePath"] = data.workspacePath,
                        ["mode"] = "largest",
                        ["maxResults"] = 10
                    }
                },
                EstimatedTokens = 800,
                Priority = Priority.Recommended
            });

            // Storage optimization for huge files
            actions.Add(new AIAction
            {
                Id = "optimize_storage",
                Description = "Find storage optimization opportunities",
                Command = new AICommand
                {
                    Tool = "storage_optimization",
                    Parameters = new Dictionary<string, object>
                    {
                        ["targetFiles"] = hugeFiles.Take(5).Select(f => Path.Combine(data.workspacePath, f.RelativePath)).ToList()
                    }
                },
                EstimatedTokens = 1200,
                Priority = Priority.High
            });
        }

        // Check empty files
        var emptyFiles = buckets.Values.SelectMany(b => b).Where(f => f.FileSize == 0).ToList();
        if (emptyFiles.Count > 5)
        {
            actions.Add(new AIAction
            {
                Id = "review_empty_files",
                Description = $"Review {emptyFiles.Count} empty files",
                Command = new AICommand
                {
                    Tool = "file_size_analysis",
                    Parameters = new Dictionary<string, object>
                    {
                        ["workspacePath"] = data.workspacePath,
                        ["mode"] = "zero"
                    }
                },
                EstimatedTokens = 500,
                Priority = Priority.Medium
            });
        }

        // Analyze specific bucket if heavily skewed
        if (data.dominantBucket.Key != null && data.dominantBucket.Value.count > statistics.FileCount * 0.5)
        {
            var dominantBucketKey = (string)data.dominantBucket.Key;
            var workspacePath = (string)data.workspacePath;
            var dominantFiles = buckets[dominantBucketKey].Take(20).Select(f => Path.Combine(workspacePath, f.RelativePath)).ToList();
            
            actions.Add(new AIAction
            {
                Id = "analyze_dominant_bucket",
                Description = $"Deep dive into {dominantBucketKey} files",
                Command = new AICommand
                {
                    Tool = "bucket_analysis",
                    Parameters = new Dictionary<string, object>
                    {
                        ["bucket"] = dominantBucketKey,
                        ["files"] = dominantFiles
                    }
                },
                EstimatedTokens = 1000,
                Priority = Priority.Available
            });
        }

        // Compare with similar projects
        actions.Add(new AIAction
        {
            Id = "compare_distribution",
            Description = "Compare with typical project distributions",
            Command = new AICommand
            {
                Tool = "distribution_comparison",
                Parameters = new Dictionary<string, object>
                {
                    ["projectType"] = data.projectContext?.ProjectType ?? "unknown",
                    ["statistics"] = statistics
                }
            },
            EstimatedTokens = 600,
            Priority = Priority.Low
        });

        return actions;
    }

    private dynamic CalculateDistributionMetrics(FileSizeStatistics statistics, Dictionary<string, List<FileSizeResult>> buckets)
    {
        // Calculate skewness (simplified)
        var skewness = statistics.AverageSize > statistics.MedianSize ? "positive" : "negative";
        
        // Calculate balance
        var bucketCounts = buckets.Values.Select(b => b.Count).ToList();
        var maxCount = bucketCounts.Any() ? bucketCounts.Max() : 0;
        var totalCount = bucketCounts.Sum();
        
        string balance;
        if (maxCount > totalCount * 0.7)
            balance = "heavily_skewed";
        else if (maxCount > totalCount * 0.5)
            balance = "moderately_skewed";
        else
            balance = "well_balanced";

        // Simplified kurtosis (peakedness)
        var kurtosis = statistics.StandardDeviation < statistics.AverageSize * 0.5 ? "peaked" : "flat";

        return new
        {
            skewness,
            balance,
            kurtosis
        };
    }

    private string GenerateDistributionRecommendation(dynamic metrics, Dictionary<string, object> bucketSummaries)
    {
        if (metrics.balance == "heavily_skewed")
        {
            var dominant = bucketSummaries.OrderByDescending(kv => ((dynamic)kv.Value).totalSize).First();
            if (dominant.Key == "huge")
            {
                return "Consider archiving or compressing large files to improve repository size";
            }
            else if (dominant.Key == "tiny")
            {
                return "Many tiny files detected - consider consolidation where appropriate";
            }
        }
        else if (metrics.balance == "well_balanced")
        {
            return "File size distribution appears healthy and well-balanced";
        }

        return "Review file organization to optimize storage and performance";
    }

    private int EstimateResponseTokens(Dictionary<string, List<FileSizeResult>> buckets)
    {
        // Base structure
        var baseTokens = 600;
        
        // Per bucket summary
        var perBucketTokens = 100;
        
        // Analysis and insights
        var analysisTokens = 400;
        
        return baseTokens + (buckets.Count * perBucketTokens) + analysisTokens;
    }
}