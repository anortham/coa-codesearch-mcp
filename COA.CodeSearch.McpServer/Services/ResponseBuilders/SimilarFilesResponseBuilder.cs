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
/// Builds AI-optimized responses for similar files search operations
/// </summary>
public class SimilarFilesResponseBuilder : BaseResponseBuilder
{
    public SimilarFilesResponseBuilder(ILogger<SimilarFilesResponseBuilder> logger) 
        : base(logger)
    {
    }

    public override string ResponseType => "similar_files";

    /// <summary>
    /// Build AI-optimized response for similar files results
    /// </summary>
    public object BuildResponse(
        string sourceFilePath,
        string workspacePath,
        List<SimilarFileResult> results,
        double searchDurationMs,
        FileInfo sourceFileInfo,
        List<string>? topTerms,
        ResponseMode mode,
        ProjectContext? projectContext = null)
    {
        var tokenBudget = mode == ResponseMode.Summary ? SummaryTokenBudget : FullTokenBudget;

        // Group by similarity ranges
        var similarityRanges = new
        {
            veryHigh = results.Count(r => r.Score >= 0.8),
            high = results.Count(r => r.Score >= 0.6 && r.Score < 0.8),
            moderate = results.Count(r => r.Score >= 0.4 && r.Score < 0.6),
            low = results.Count(r => r.Score < 0.4)
        };

        // Group by extension
        var extensionGroups = results
            .GroupBy(r => r.Extension)
            .ToDictionary(g => g.Key, g => g.Count());

        // Find directory patterns
        var directoryPatterns = results
            .GroupBy(r => Path.GetDirectoryName(r.RelativePath) ?? "root")
            .OrderByDescending(g => g.Count())
            .Take(3)
            .Select(g => new
            {
                directory = g.Key,
                count = g.Count(),
                avgScore = g.Average(r => r.Score)
            })
            .ToList();

        // Generate insights
        var insights = GenerateInsights(new
        {
            sourceFilePath,
            results,
            searchDurationMs,
            similarityRanges,
            directoryPatterns,
            topTerms,
            projectContext
        }, mode);

        // Generate actions
        var actions = GenerateActions(new
        {
            sourceFilePath,
            workspacePath,
            results,
            directoryPatterns,
            extensionGroups
        }, tokenBudget);

        // Determine result limit based on mode
        var resultLimit = mode == ResponseMode.Summary ? 10 : 50;
        var displayResults = results.Take(resultLimit).ToList();

        return new
        {
            success = true,
            operation = "similar_files",
            source = new
            {
                file = Path.GetFileName(sourceFilePath),
                path = sourceFilePath,
                size = sourceFileInfo.Length,
                sizeFormatted = FormatFileSize(sourceFileInfo.Length)
            },
            summary = new
            {
                totalFound = results.Count,
                searchTime = $"{searchDurationMs:F1}ms",
                avgSimilarity = results.Any() ? results.Average(r => r.Score) : 0.0,
                similarityDistribution = similarityRanges
            },
            analysis = new
            {
                patterns = insights.Take(3).ToList(),
                topTerms = topTerms?.Take(10).ToList() ?? new List<string>(),
                directoryPatterns = directoryPatterns,
                extensionDistribution = extensionGroups
            },
            results = displayResults.Select(r => new
            {
                file = r.FileName,
                path = r.RelativePath,
                score = Math.Round(r.Score, 3),
                similarity = GetSimilarityDescription(r.Score),
                size = r.FileSize,
                sizeFormatted = FormatFileSize(r.FileSize),
                matchingTerms = r.MatchingTerms
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
                algorithm = "more-like-this"
            }
        };
    }

    protected override List<string> GenerateInsights(dynamic data, ResponseMode mode)
    {
        var insights = new List<string>();
        List<SimilarFileResult> results = data.results;

        if (!results.Any())
        {
            insights.Add($"No similar files found for {Path.GetFileName(data.sourceFilePath)}");
            insights.Add("This might indicate a unique file with no similar implementations");
            insights.Add("Consider adjusting similarity parameters or checking file content");
        }
        else
        {
            // Basic count insight
            insights.Add($"Found {results.Count} similar files in {data.searchDurationMs}ms");
            
            // Similarity distribution insight
            if (data.similarityRanges.veryHigh > 0)
            {
                insights.Add($"{data.similarityRanges.veryHigh} files are very similar (80%+ match)");
                
                if (data.similarityRanges.veryHigh > 1)
                {
                    insights.Add("⚠️ Potential code duplication detected");
                }
            }
            else if (data.similarityRanges.high > 0)
            {
                insights.Add($"{data.similarityRanges.high} files have high similarity (60-80% match)");
            }
            
            // Directory pattern insights
            if (data.directoryPatterns.Count > 0)
            {
                var topDir = data.directoryPatterns[0];
                if (topDir.count > results.Count * 0.5)
                {
                    insights.Add($"Most similar files are in {topDir.directory}");
                }
            }
            
            // Extension insights
            var sourceExt = Path.GetExtension(data.sourceFilePath);
            var sameExtCount = results.Count(r => r.Extension == sourceExt);
            if (sameExtCount == results.Count)
            {
                insights.Add($"All similar files are {sourceExt} files");
            }
            else if (sameExtCount == 0)
            {
                insights.Add("Similar content found in different file types");
            }
            
            // Top terms insight
            if (data.topTerms != null && data.topTerms.Count > 0)
            {
                List<string> terms = data.topTerms;
                var termList = string.Join(", ", terms.Take(5));
                insights.Add($"Key terms: {termList}");
            }
        }
        
        return insights;
    }

    protected override List<dynamic> GenerateActions(dynamic data, int tokenBudget)
    {
        var actions = new List<dynamic>();
        List<SimilarFileResult> results = data.results;

        if (results.Any())
        {
            // Compare with most similar file
            var mostSimilar = results.First();
            actions.Add(new AIAction
            {
                Id = "compare_files",
                Description = "Compare with most similar file",
                Command = new AICommand
                {
                    Tool = "diff",
                    Parameters = new Dictionary<string, object>
                    {
                        ["file1"] = data.sourceFilePath,
                        ["file2"] = Path.Combine(data.workspacePath, mostSimilar.RelativePath)
                    }
                },
                EstimatedTokens = 1500,
                Priority = Priority.Recommended
            });
            
            // Analyze for duplication if very similar files exist
            var verySimilar = results.Where(r => r.Score >= 0.8).ToList();
            if (verySimilar.Count > 1)
            {
                actions.Add(new AIAction
                {
                    Id = "analyze_duplication",
                    Description = "Analyze potential code duplication",
                    Command = new AICommand
                    {
                        Tool = "analyze_duplication",
                        Parameters = new Dictionary<string, object>
                        {
                            ["files"] = verySimilar.Select(r => Path.Combine(data.workspacePath, r.RelativePath)).ToList()
                        }
                    },
                    EstimatedTokens = 2000,
                    Priority = Priority.High
                });
            }
            
            // Search for text in similar files
            if (results.Count > 3)
            {
                actions.Add(new AIAction
                {
                    Id = "search_in_similar",
                    Description = "Search for patterns in similar files",
                    Command = new AICommand
                    {
                        Tool = "text_search",
                        Parameters = new Dictionary<string, object>
                        {
                            ["query"] = "TODO|FIXME|BUG",
                            ["files"] = results.Take(10).Select(r => Path.Combine(data.workspacePath, r.RelativePath)).ToList()
                        }
                    },
                    EstimatedTokens = 1000,
                    Priority = Priority.Available
                });
            }
            
            // Refactor suggestion for highly similar files
            if (verySimilar.Count >= 3)
            {
                actions.Add(new AIAction
                {
                    Id = "suggest_refactor",
                    Description = "Suggest refactoring to reduce duplication",
                    Command = new AICommand
                    {
                        Tool = "refactor_analysis",
                        Parameters = new Dictionary<string, object>
                        {
                            ["targetFiles"] = verySimilar.Select(r => Path.Combine(data.workspacePath, r.RelativePath)).ToList()
                        }
                    },
                    EstimatedTokens = 2500,
                    Priority = Priority.Medium
                });
            }
        }
        else
        {
            // No results - suggest alternatives
            actions.Add(new AIAction
            {
                Id = "broaden_search",
                Description = "Search with relaxed parameters",
                Command = new AICommand
                {
                    Tool = "similar_files",
                    Parameters = new Dictionary<string, object>
                    {
                        ["sourcePath"] = data.sourceFilePath,
                        ["workspacePath"] = data.workspacePath,
                        ["minDocFreq"] = 1,
                        ["minTermFreq"] = 1
                    }
                },
                EstimatedTokens = 500,
                Priority = Priority.Recommended
            });
        }
        
        return actions;
    }

    private string GetSimilarityDescription(float score)
    {
        if (score >= 0.9) return "nearly identical";
        if (score >= 0.8) return "very similar";
        if (score >= 0.7) return "highly similar";
        if (score >= 0.6) return "similar";
        if (score >= 0.5) return "moderately similar";
        if (score >= 0.4) return "somewhat similar";
        return "loosely similar";
    }

    private int EstimateResponseTokens(List<SimilarFileResult> results)
    {
        // Base structure
        var baseTokens = 400;
        
        // Per result (includes matching terms)
        var perResultTokens = 50;
        
        // Analysis and insights
        var analysisTokens = 300;
        
        return baseTokens + (results.Count * perResultTokens) + analysisTokens;
    }
}