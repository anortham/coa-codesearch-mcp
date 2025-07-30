using COA.CodeSearch.McpServer.Infrastructure;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace COA.CodeSearch.McpServer.Services.ResponseBuilders;

/// <summary>
/// Builds AI-optimized responses for batch operations
/// </summary>
public class BatchOperationsResponseBuilder : BaseResponseBuilder
{
    private readonly IDetailRequestCache? _detailCache;
    
    public BatchOperationsResponseBuilder(ILogger<BatchOperationsResponseBuilder> logger, IDetailRequestCache? detailCache) 
        : base(logger)
    {
        _detailCache = detailCache;
    }

    public override string ResponseType => "batch_operations";

    /// <summary>
    /// Build AI-optimized response for batch operations results
    /// </summary>
    public object BuildResponse(
        List<BatchOperationSpec> operations,
        List<object> results,
        double totalDurationMs,
        Dictionary<string, double> operationTimings,
        ResponseMode mode)
    {
        var tokenBudget = mode == ResponseMode.Summary ? SummaryTokenBudget : FullTokenBudget;

        // Analyze operation types
        var operationTypes = operations
            .GroupBy(op => op.Operation)
            .ToDictionary(g => g.Key, g => g.Count());

        // Find slowest operations
        var slowestOperations = operationTimings
            .OrderByDescending(kv => kv.Value)
            .Take(3)
            .Select(kv => new
            {
                operation = kv.Key,
                duration = $"{kv.Value:F1}ms"
            })
            .ToList();

        // Analyze results
        var resultAnalysis = AnalyzeResults(operations, results);

        // Generate insights
        var insights = GenerateInsights(new
        {
            operations,
            results,
            totalDurationMs,
            operationTypes,
            slowestOperations,
            resultAnalysis
        }, mode);

        // Generate actions
        var actions = GenerateActions(new
        {
            operations,
            results,
            resultAnalysis,
            mode
        }, tokenBudget);

        // Build operation summaries
        var operationSummaries = operations.Select((op, index) => new
        {
            index,
            operation = op.Operation,
            parameters = GetOperationSummary(op),
            success = index < results.Count,
            timing = operationTimings.ContainsKey($"op_{index}") ? $"{operationTimings[$"op_{index}"]:F1}ms" : "N/A"
        }).ToList();

        // Determine how many results to include based on mode and token budget
        var estimatedTokensPerResult = EstimateTokensPerResult(results);
        var maxResults = Math.Min(tokenBudget / estimatedTokensPerResult, results.Count);
        var includeResults = mode == ResponseMode.Full || results.Count <= 5;
        var displayResults = includeResults ? results.Take((int)maxResults).ToList() : new List<object>();

        // Store full results in cache if needed
        string? detailRequestToken = null;
        if (mode == ResponseMode.Summary && _detailCache != null && results.Count > displayResults.Count)
        {
            detailRequestToken = _detailCache.StoreDetailData(new 
            { 
                operations, 
                results, 
                operationSummaries,
                resultAnalysis 
            });
        }

        // Build unified response format matching other tools
        return new
        {
            success = true,
            operation = "batch_operations",
            query = new
            {
                operationCount = operations.Count,
                operationTypes = operationTypes,
                workspace = operations.FirstOrDefault()?.Parameters.ContainsKey("workspacePath") == true 
                    ? operations.First().Parameters["workspacePath"] : "multiple"
            },
            summary = new
            {
                totalOperations = operations.Count,
                completedOperations = results.Count,
                totalMatches = resultAnalysis.totalMatches,
                totalTime = $"{totalDurationMs:F1}ms",
                avgTimePerOperation = $"{totalDurationMs / operations.Count:F1}ms"
            },
            results = displayResults.Select<object, object>((r, i) => 
            {
                // Handle BatchOperationEntry objects
                if (r is BatchOperationEntry entry)
                {
                    var opIndex = entry.Index < operations.Count ? entry.Index : operations.Count - 1;
                    var operation = operations[opIndex];
                    var matchCount = entry.Result != null ? ExtractMatchCount(entry.Result, operation) : 0;
                    
                    return new
                    {
                        index = entry.Index,
                        operation = entry.OperationType ?? operation.Operation,
                        query = operation.Parameters.ContainsKey("query") ? operation.Parameters["query"] : null,
                        matches = matchCount,
                        summary = GetOperationSummary(operation),
                        success = entry.Success,
                        error = entry.Error,
                        result = entry.Result // Include the actual operation result
                    };
                }
                else
                {
                    // Fallback for raw results
                    var opIndex = i < operations.Count ? i : operations.Count - 1;
                    var operation = operations[opIndex];
                    var matchCount = ExtractMatchCount(r, operation);
                    
                    return (object)new
                    {
                        index = i,
                        operation = operation.Operation,
                        query = operation.Parameters.ContainsKey("query") ? operation.Parameters["query"] : null,
                        matches = matchCount,
                        summary = GetOperationSummary(operation),
                        result = r
                    };
                }
            }).ToList(),
            resultsSummary = new
            {
                included = displayResults.Count,
                total = results.Count,
                hasMore = results.Count > displayResults.Count
            },
            distribution = new
            {
                byOperation = operationTypes,
                commonFiles = resultAnalysis.commonFiles
            },
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
                mode = mode.ToString().ToLowerInvariant(),
                truncated = results.Count > displayResults.Count,
                tokens = EstimateResponseTokens(displayResults),
                detailRequestToken = detailRequestToken,
                // Include batch-specific metadata
                performance = new
                {
                    parallel = operations.Count > 1,
                    speedup = operations.Count > 1 ? $"{CalculateSpeedup(totalDurationMs, operationTimings):F1}x" : "N/A",
                    slowestOperations = slowestOperations
                },
                analysis = new
                {
                    effectiveness = CalculateEffectiveness(resultAnalysis),
                    highMatchOperations = resultAnalysis.highMatchOperations,
                    avgMatchesPerOperation = resultAnalysis.avgMatchesPerOperation
                }
            },
            resourceUri = $"codesearch-batch://batch_{Guid.NewGuid().ToString("N")[..8]}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}"
        };
    }

    protected override List<string> GenerateInsights(dynamic data, ResponseMode mode)
    {
        var insights = new List<string>();
        List<BatchOperationSpec> operations = data.operations;
        List<object> results = data.results;

        // Performance insight
        insights.Add($"Executed {operations.Count} operations in {data.totalDurationMs}ms");
        
        if (operations.Count > 1)
        {
            var avgTime = data.totalDurationMs / operations.Count;
            insights.Add($"Parallel execution saved ~{(operations.Count * avgTime - data.totalDurationMs):F0}ms");
        }

        // Operation mix insight
        var operationTypes = (Dictionary<string, int>)data.operationTypes;
        if (operationTypes.Count == 1)
        {
            var firstOperation = operationTypes.First();
            insights.Add($"All operations are {firstOperation.Key} searches");
        }
        else
        {
            insights.Add($"Mixed batch: {string.Join(", ", operationTypes.Select(kv => $"{kv.Value} {kv.Key}"))}");
        }

        // Result analysis insights
        if (data.resultAnalysis.totalMatches > 0)
        {
            insights.Add($"Found {data.resultAnalysis.totalMatches} total matches across all searches");
            
            if (data.resultAnalysis.highMatchOperations > 0)
            {
                insights.Add($"{data.resultAnalysis.highMatchOperations} operations found significant results");
            }
        }
        else
        {
            insights.Add("No matches found in any operation");
        }

        // Pattern insights
        if (data.resultAnalysis.commonFiles.Count > 0)
        {
            insights.Add($"Found {data.resultAnalysis.commonFiles.Count} files appearing in multiple results");
        }

        // Performance bottleneck
        if (data.slowestOperations.Count > 0)
        {
            var slowest = data.slowestOperations[0];
            insights.Add($"Slowest operation: {slowest.operation} ({slowest.duration})");
        }

        return insights;
    }

    protected override List<dynamic> GenerateActions(dynamic data, int tokenBudget)
    {
        var actions = new List<dynamic>();
        List<BatchOperationSpec> operations = data.operations;
        dynamic resultAnalysis = data.resultAnalysis;

        // If common files found, suggest focused analysis
        if (resultAnalysis.commonFiles.Count > 0)
        {
            var topFile = resultAnalysis.commonFiles[0];
            actions.Add(new AIAction
            {
                Id = "analyze_common_file",
                Command = new AICommand
                {
                    Tool = "Read",
                    Parameters = new Dictionary<string, object>
                    {
                        ["file_path"] = topFile.file
                    }
                },
                EstimatedTokens = 1000,
                Priority = Priority.Recommended
            });
        }

        // If no results, suggest broader search
        if (resultAnalysis.totalMatches == 0)
        {
            actions.Add(new AIAction
            {
                Id = "broaden_search",
                Command = new AICommand
                {
                    Tool = "batch_operations",
                    Parameters = new Dictionary<string, object>
                    {
                        ["operations"] = operations.Select(op => new
                        {
                            operation = op.Operation,
                            searchType = "fuzzy",
                            query = op.Parameters.ContainsKey("query") ? 
                                (object)$"{op.Parameters["query"]}~" : (object)op.Parameters
                        }).ToList()
                    }
                },
                EstimatedTokens = 2000,
                Priority = Priority.Medium
            });
        }

        // Suggest detail view if in summary mode
        if (data.mode == ResponseMode.Summary && resultAnalysis.totalMatches > 20)
        {
            actions.Add(new AIAction
            {
                Id = "view_full_results",
                Command = new AICommand
                {
                    Tool = "batch_operations",
                    Parameters = new Dictionary<string, object>
                    {
                        ["operations"] = operations,
                        ["responseMode"] = "full"
                    }
                },
                EstimatedTokens = Math.Min(resultAnalysis.totalMatches * 50, 10000),
                Priority = Priority.Available
            });
        }

        // If mixed operations, suggest focused batch
        var operationGroups = operations.GroupBy(op => op.Operation).ToList();
        if (operationGroups.Count > 2)
        {
            var topType = operationGroups.OrderByDescending(g => g.Count()).First();
            actions.Add(new AIAction
            {
                Id = "focus_operation_type",
                Command = new AICommand
                {
                    Tool = "batch_operations",
                    Parameters = new Dictionary<string, object>
                    {
                        ["operations"] = topType.ToList()
                    }
                },
                EstimatedTokens = 1500,
                Priority = Priority.Low
            });
        }

        return actions;
    }

    private dynamic AnalyzeResults(List<BatchOperationSpec> operations, List<object> results)
    {
        var totalMatches = 0;
        var highMatchOperations = 0;
        var fileOccurrences = new Dictionary<string, int>();

        for (int i = 0; i < results.Count; i++)
        {
            var result = results[i];
            
            // Determine the actual operation index
            int opIndex = i;
            object actualResult = result;
            
            if (result is BatchOperationEntry entry)
            {
                opIndex = entry.Index < operations.Count ? entry.Index : operations.Count - 1;
                actualResult = entry.Result ?? result;
            }
            
            var operation = operations[opIndex];
            
            // Extract match count based on operation type
            var matchCount = ExtractMatchCount(actualResult, operation);
            totalMatches += matchCount;
            
            if (matchCount > 10)
            {
                highMatchOperations++;
            }

            // Track file occurrences
            var files = ExtractFiles(actualResult, operation);
            foreach (var file in files)
            {
                fileOccurrences[file] = fileOccurrences.GetValueOrDefault(file, 0) + 1;
            }
        }

        var commonFiles = fileOccurrences
            .Where(kv => kv.Value > 1)
            .OrderByDescending(kv => kv.Value)
            .Take(5)
            .Select(kv => new { file = kv.Key, count = kv.Value })
            .ToList();

        return new
        {
            totalMatches,
            highMatchOperations,
            avgMatchesPerOperation = results.Count > 0 ? totalMatches / results.Count : 0,
            commonFiles
        };
    }

    private string GetOperationSummary(BatchOperationSpec operation)
    {
        var summary = operation.Operation;
        
        if (operation.Parameters.TryGetValue("query", out var query))
        {
            summary += $": '{query}'";
        }
        else if (operation.Parameters.TryGetValue("nameQuery", out var nameQuery))
        {
            summary += $": '{nameQuery}'";
        }
        else if (operation.Parameters.TryGetValue("sourcePath", out var sourcePath))
        {
            summary += $": {Path.GetFileName(sourcePath.ToString() ?? "")}";
        }

        return summary;
    }

    private dynamic GetResultSummary(object result, BatchOperationSpec operation)
    {
        var matchCount = ExtractMatchCount(result, operation);
        var resultType = result.GetType().GetProperty("operation")?.GetValue(result)?.ToString() ?? operation.Operation;
        
        return new
        {
            type = resultType,
            matches = matchCount,
            summary = $"{matchCount} results found"
        };
    }

    private int ExtractMatchCount(object result, BatchOperationSpec operation)
    {
        // Handle BatchOperationEntry
        if (result is BatchOperationEntry entry)
        {
            return entry.Result != null ? ExtractMatchCount(entry.Result, operation) : 0;
        }
        
        // Try to extract count from different result structures
        if (result is JsonElement json)
        {
            if (json.TryGetProperty("summary", out var summary))
            {
                if (summary.TryGetProperty("totalHits", out var totalHits))
                {
                    return totalHits.GetInt32();
                }
                else if (summary.TryGetProperty("totalFound", out var totalFound))
                {
                    return totalFound.GetInt32();
                }
            }
            else if (json.TryGetProperty("results", out var results) && 
                     results.ValueKind == JsonValueKind.Array)
            {
                return results.GetArrayLength();
            }
        }
        
        // Try reflection for dynamic objects
        var resultType = result.GetType();
        
        // Check for summary.totalHits pattern
        var summaryProp = resultType.GetProperty("summary");
        if (summaryProp != null)
        {
            var summaryValue = summaryProp.GetValue(result);
            if (summaryValue != null)
            {
                var summaryType = summaryValue.GetType();
                var totalHitsProp = summaryType.GetProperty("totalHits");
                if (totalHitsProp != null)
                {
                    var totalHitsValue = totalHitsProp.GetValue(summaryValue);
                    if (totalHitsValue != null)
                    {
                        return Convert.ToInt32(totalHitsValue);
                    }
                }
            }
        }
        
        var totalFoundProp = resultType.GetProperty("TotalFound");
        if (totalFoundProp != null)
        {
            return Convert.ToInt32(totalFoundProp.GetValue(result));
        }

        return 0;
    }

    private List<string> ExtractFiles(object result, BatchOperationSpec operation)
    {
        var files = new List<string>();
        
        // Handle BatchOperationEntry
        if (result is BatchOperationEntry entry)
        {
            return entry.Result != null ? ExtractFiles(entry.Result, operation) : files;
        }
        
        // Try to extract files from different result structures
        if (result is JsonElement json)
        {
            if (json.TryGetProperty("results", out var results) && 
                results.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in results.EnumerateArray())
                {
                    if (item.TryGetProperty("path", out var path))
                    {
                        files.Add(path.GetString() ?? "");
                    }
                    else if (item.TryGetProperty("file", out var file))
                    {
                        files.Add(file.GetString() ?? "");
                    }
                }
            }
        }
        
        return files.Distinct().ToList();
    }

    private double CalculateSpeedup(double totalDuration, Dictionary<string, double> timings)
    {
        if (timings.Count == 0) return 1.0;
        
        var sequentialTime = timings.Values.Sum();
        return sequentialTime / totalDuration;
    }

    private string CalculateEffectiveness(dynamic resultAnalysis)
    {
        if (resultAnalysis.totalMatches == 0) return "no_results";
        
        // Calculate total operations from the high match operations and average
        var totalOperations = resultAnalysis.avgMatchesPerOperation > 0 
            ? resultAnalysis.totalMatches / resultAnalysis.avgMatchesPerOperation 
            : 1;
            
        if (resultAnalysis.highMatchOperations > totalOperations * 0.5) return "highly_effective";
        if (resultAnalysis.avgMatchesPerOperation > 5) return "effective";
        return "limited_results";
    }

    private int EstimateTokensPerResult(List<object> results)
    {
        if (!results.Any()) return 100;
        
        // Sample first result to estimate size
        var firstResult = results.First();
        var resultJson = JsonSerializer.Serialize(firstResult);
        
        // Rough estimate: 1 token per 4 characters
        return Math.Max(100, resultJson.Length / 4);
    }

    private int EstimateResponseTokens(List<object> results)
    {
        // Base structure
        var baseTokens = 500;
        
        // Per result estimate
        var perResultTokens = EstimateTokensPerResult(results);
        
        // Analysis and insights
        var analysisTokens = 400;
        
        return baseTokens + (results.Count * perResultTokens) + analysisTokens;
    }
}