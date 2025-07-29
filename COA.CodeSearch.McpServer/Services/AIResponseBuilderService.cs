using COA.CodeSearch.McpServer.Infrastructure;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services.ResponseBuilders;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Service for building AI-optimized responses with contextual actions and token efficiency
/// Delegates to specialized response builders for each operation type
/// </summary>
public class AIResponseBuilderService
{
    private readonly ILogger<AIResponseBuilderService> _logger;
    private readonly IDetailRequestCache _detailCache;
    private readonly IResponseBuilderFactory _responseBuilderFactory;

    // Token budgets for different response modes
    private const int SummaryTokenBudget = 1500;
    private const int FullTokenBudget = 4000;
    private const int MaxActionTokens = 300;

    public AIResponseBuilderService(
        ILogger<AIResponseBuilderService> logger,
        IDetailRequestCache detailCache,
        IResponseBuilderFactory responseBuilderFactory)
    {
        _logger = logger;
        _detailCache = detailCache;
        _responseBuilderFactory = responseBuilderFactory;
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
        var builder = _responseBuilderFactory.GetBuilder<MemorySearchResponseBuilder>("memory_search");
        return builder.BuildResponse(searchResult, request, originalQuery, mode);
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
        var builder = _responseBuilderFactory.GetBuilder<TextSearchResponseBuilder>("text_search");
        return builder.BuildResponse(
            query, searchType, workspacePath, results, totalHits,
            filePattern, extensions, mode, projectContext,
            alternateHits, alternateExtensions);
    }

    /// <summary>
    /// Build AI-optimized response for text search results as JsonNode
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
        var response = BuildTextSearchResponse(
            query, searchType, workspacePath, results, totalHits,
            filePattern, extensions, mode, projectContext,
            alternateHits, alternateExtensions);
        
        var json = JsonSerializer.Serialize(response);
        return JsonNode.Parse(json) ?? new JsonObject();
    }

    /// <summary>
    /// Build AI-optimized response for file search results (legacy)
    /// </summary>
    public object BuildFileSearchResponse(
        string query,
        string searchType,
        string workspacePath,
        List<FileSearchResult> results,
        double searchDurationMs,
        ResponseMode mode,
        ProjectContext? projectContext)
    {
        var builder = _responseBuilderFactory.GetBuilder<FileSearchResponseBuilder>("file_search");
        return builder.BuildResponse(query, searchType, workspacePath, results, searchDurationMs, mode, projectContext);
    }

    /// <summary>
    /// Build AI-optimized response for directory search results
    /// </summary>
    public object BuildDirectorySearchResponse(
        string query,
        string searchType,
        string workspacePath,
        List<DirectorySearchResult> results,
        double searchDurationMs,
        bool groupByDirectory,
        ResponseMode mode,
        ProjectContext? projectContext)
    {
        var builder = _responseBuilderFactory.GetBuilder<DirectorySearchResponseBuilder>("directory_search");
        return builder.BuildResponse(query, searchType, workspacePath, results, searchDurationMs, mode, projectContext);
    }

    /// <summary>
    /// Build AI-optimized response for directory search results (overload for tools)
    /// </summary>
    public object BuildDirectorySearchResponse(
        string query,
        string searchType,
        string workspacePath,
        List<dynamic> results,
        double searchDurationMs,
        ResponseMode mode,
        bool groupByDirectory,
        int maxTokens)
    {
        // Convert dynamic results to DirectorySearchResult
        var typedResults = results.Select(r => new DirectorySearchResult
        {
            DirectoryName = System.IO.Path.GetFileName(r.path),
            RelativePath = r.path,
            FileCount = r.fileCount,
            Score = r.score ?? 1.0f
        }).ToList();

        var builder = _responseBuilderFactory.GetBuilder<DirectorySearchResponseBuilder>("directory_search");
        return builder.BuildResponse(query, searchType, workspacePath, typedResults, searchDurationMs, mode, null);
    }

    /// <summary>
    /// Build AI-optimized response for similar files results
    /// </summary>
    public object BuildSimilarFilesResponse(
        string sourcePath,
        string workspacePath,
        List<SimilarFileResult> results,
        double searchDurationMs,
        FileInfo sourceFileInfo,
        List<string>? topTerms,
        ResponseMode mode,
        ProjectContext? projectContext = null)
    {
        var builder = _responseBuilderFactory.GetBuilder<SimilarFilesResponseBuilder>("similar_files");
        return builder.BuildResponse(sourcePath, workspacePath, results, searchDurationMs, sourceFileInfo, topTerms, mode, projectContext);
    }

    /// <summary>
    /// Build AI-optimized response for similar files results (overload for tools with List<dynamic>)
    /// </summary>
    public object BuildSimilarFilesResponse(
        string sourcePath,
        string workspacePath,
        List<dynamic> results,
        double searchDurationMs,
        dynamic sourceFileInfo,
        List<string>? topTerms,
        ResponseMode mode,
        ProjectContext? projectContext = null)
    {
        // Convert List<dynamic> to List<SimilarFileResult>
        var typedResults = results.Select(r => new SimilarFileResult
        {
            FileName = r.filename,
            RelativePath = r.relativePath,
            Extension = r.extension,
            Score = r.similarity ?? 0.0f,
            MatchingTerms = 0, // Tool doesn't provide this
            FileSize = 0 // Tool doesn't provide this
        }).ToList();

        // Create FileInfo from dynamic
        var fileInfo = new FileInfo(sourceFileInfo.path);

        var builder = _responseBuilderFactory.GetBuilder<SimilarFilesResponseBuilder>("similar_files");
        return builder.BuildResponse(sourcePath, workspacePath, typedResults, searchDurationMs, fileInfo, topTerms, mode, projectContext);
    }

    /// <summary>
    /// Build AI-optimized response for recent files results
    /// </summary>
    public object BuildRecentFilesResponse(
        string workspacePath,
        string timeFrame,
        DateTime cutoffTime,
        List<RecentFileResult> results,
        double searchDurationMs,
        Dictionary<string, int> extensionCounts,
        long totalSize,
        ResponseMode mode,
        ProjectContext? projectContext)
    {
        var builder = _responseBuilderFactory.GetBuilder<RecentFilesResponseBuilder>("recent_files");
        return builder.BuildResponse(workspacePath, timeFrame, cutoffTime, results, searchDurationMs, extensionCounts, totalSize, mode, projectContext);
    }

    /// <summary>
    /// Build AI-optimized response for recent files results (overload for tools with List<dynamic>)
    /// </summary>
    public object BuildRecentFilesResponse(
        string workspacePath,
        string timeFrame,
        DateTime cutoffTime,
        List<dynamic> results,
        double searchDurationMs,
        Dictionary<string, int> extensionCounts,
        long totalSize,
        ResponseMode mode,
        ProjectContext? projectContext)
    {
        // Convert List<dynamic> to List<RecentFileResult>
        var typedResults = results.Select(r => new RecentFileResult
        {
            FileName = r.filename,
            RelativePath = r.relativePath,
            Extension = r.extension,
            FileSize = (long)r.size,
            LastModified = (DateTime)r.lastModified
        }).ToList();

        var builder = _responseBuilderFactory.GetBuilder<RecentFilesResponseBuilder>("recent_files");
        return builder.BuildResponse(workspacePath, timeFrame, cutoffTime, typedResults, searchDurationMs, extensionCounts, totalSize, mode, projectContext);
    }

    /// <summary>
    /// Build AI-optimized response for file size analysis results
    /// </summary>
    public object BuildFileSizeAnalysisResponse(
        string mode,
        string workspacePath,
        List<FileSizeResult> results,
        double searchDurationMs,
        FileSizeStatistics statistics,
        ResponseMode responseMode,
        ProjectContext? projectContext)
    {
        var builder = _responseBuilderFactory.GetBuilder<FileSizeAnalysisResponseBuilder>("file_size_analysis");
        return builder.BuildResponse(mode, workspacePath, results, searchDurationMs, statistics, responseMode, projectContext);
    }

    /// <summary>
    /// Build AI-optimized response for file size analysis results (overload for tools with List<object>)
    /// </summary>
    public object BuildFileSizeAnalysisResponse(
        string workspacePath,
        string mode,
        List<object> results,
        double searchDurationMs,
        long totalSize,
        Dictionary<string, int> sizeGroups,
        ResponseMode responseMode)
    {
        // Convert List<object> to List<FileSizeResult>
        var typedResults = results.Select(r =>
        {
            dynamic d = r;
            return new FileSizeResult
            {
                FileName = d.filename,
                RelativePath = d.relativePath,
                Extension = d.extension,
                FileSize = (long)d.size
            };
        }).ToList();

        // Calculate statistics from the results
        var statistics = new FileSizeStatistics
        {
            FileCount = typedResults.Count,
            TotalSize = totalSize,
            MinSize = typedResults.Any() ? typedResults.Min(r => r.FileSize) : 0,
            MaxSize = typedResults.Any() ? typedResults.Max(r => r.FileSize) : 0,
            AverageSize = typedResults.Any() ? (double)totalSize / typedResults.Count : 0,
            MedianSize = CalculateMedian(typedResults.Select(r => r.FileSize).ToList()),
            StandardDeviation = CalculateStandardDeviation(typedResults.Select(r => r.FileSize).ToList()),
            SizeDistribution = sizeGroups
        };

        var builder = _responseBuilderFactory.GetBuilder<FileSizeAnalysisResponseBuilder>("file_size_analysis");
        return builder.BuildResponse(mode, workspacePath, typedResults, searchDurationMs, statistics, responseMode, null);
    }

    /// <summary>
    /// Build AI-optimized response for file size distribution results
    /// </summary>
    public object BuildFileSizeDistributionResponse(
        string workspacePath,
        FileSizeStatistics statistics,
        double searchDurationMs,
        Dictionary<string, List<FileSizeResult>> buckets,
        ResponseMode responseMode,
        ProjectContext? projectContext)
    {
        var builder = _responseBuilderFactory.GetBuilder<FileSizeDistributionResponseBuilder>("file_size_distribution");
        return builder.BuildResponse(workspacePath, statistics, searchDurationMs, buckets, responseMode, projectContext);
    }

    /// <summary>
    /// Build AI-optimized response for file size distribution results (overload for tools)
    /// </summary>
    public object BuildFileSizeDistributionResponse(
        string workspacePath,
        double searchDurationMs,
        int totalFiles,
        long totalSize,
        Dictionary<string, (int count, long totalSize)> distribution,
        Dictionary<string, (int count, long totalSize)> extensionStats)
    {
        // Convert distribution to buckets format
        var buckets = new Dictionary<string, List<FileSizeResult>>();
        
        // Create statistics
        var statistics = new FileSizeStatistics
        {
            FileCount = totalFiles,
            TotalSize = totalSize,
            SizeDistribution = distribution.ToDictionary(kv => kv.Key, kv => kv.Value.count)
        };

        var builder = _responseBuilderFactory.GetBuilder<FileSizeDistributionResponseBuilder>("file_size_distribution");
        return builder.BuildResponse(workspacePath, statistics, searchDurationMs, buckets, ResponseMode.Full, null);
    }

    /// <summary>
    /// Build AI-optimized response for batch operations results
    /// </summary>
    public object BuildBatchOperationsResponse(
        List<BatchOperationSpec> operations,
        List<object> results,
        double totalDurationMs,
        Dictionary<string, double> operationTimings,
        ResponseMode mode)
    {
        var builder = _responseBuilderFactory.GetBuilder<BatchOperationsResponseBuilder>("batch_operations");
        return builder.BuildResponse(operations, results, totalDurationMs, operationTimings, mode);
    }

    /// <summary>
    /// Build AI-optimized response for batch operations results (overload for tools)
    /// </summary>
    public object BuildBatchOperationsResponse(
        BatchOperationRequest request,
        BatchOperationResult result,
        ResponseMode mode)
    {
        // Convert to expected format
        var operations = request.Operations.Select(op => new BatchOperationSpec
        {
            Operation = op.Operation,
            Parameters = op.Parameters
        }).ToList();

        var results = result.Operations?.Select(op => op.Result ?? new object()).ToList() ?? new List<object>();
        
        var operationTimings = new Dictionary<string, double>();
        if (result.Operations != null)
        {
            for (int i = 0; i < result.Operations.Count; i++)
            {
                operationTimings[$"op_{i}"] = result.Operations[i].Duration ?? 0;
            }
        }

        var builder = _responseBuilderFactory.GetBuilder<BatchOperationsResponseBuilder>("batch_operations");
        return builder.BuildResponse(operations, results, result.TotalExecutionTime, operationTimings, mode);
    }

    /// <summary>
    /// Generate cache key for responses
    /// </summary>
    private string GenerateCacheKey(string operation)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var hash = $"{operation}_{timestamp}";
        return $"cache_{hash.Substring(0, Math.Min(hash.Length, 32))}";
    }

    private double CalculateMedian(List<long> values)
    {
        if (!values.Any()) return 0;
        
        var sorted = values.OrderBy(v => v).ToList();
        var count = sorted.Count;
        
        if (count % 2 == 0)
        {
            return (sorted[count / 2 - 1] + sorted[count / 2]) / 2.0;
        }
        else
        {
            return sorted[count / 2];
        }
    }

    private double CalculateStandardDeviation(List<long> values)
    {
        if (!values.Any()) return 0;
        
        var avg = values.Average();
        var sumOfSquares = values.Sum(v => Math.Pow(v - avg, 2));
        return Math.Sqrt(sumOfSquares / values.Count);
    }
}