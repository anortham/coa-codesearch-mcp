using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Util;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// High-performance file size analysis using Lucene's indexed size field - find large files, analyze distributions, etc.
/// </summary>
public class FastFileSizeAnalysisTool : ITool
{
    public string ToolName => "fast_file_size_analysis";
    public string Description => "Analyze files by size and distribution";
    public ToolCategory Category => ToolCategory.Analysis;
    private readonly ILogger<FastFileSizeAnalysisTool> _logger;
    private readonly ILuceneIndexService _luceneIndexService;
    private readonly IFieldSelectorService _fieldSelectorService;
    private readonly IErrorRecoveryService _errorRecoveryService;
    private const LuceneVersion Version = LuceneVersion.LUCENE_48;

    public FastFileSizeAnalysisTool(
        ILogger<FastFileSizeAnalysisTool> logger,
        ILuceneIndexService luceneIndexService,
        IFieldSelectorService fieldSelectorService,
        IErrorRecoveryService errorRecoveryService)
    {
        _logger = logger;
        _luceneIndexService = luceneIndexService;
        _fieldSelectorService = fieldSelectorService;
        _errorRecoveryService = errorRecoveryService;
    }

    public async Task<object> ExecuteAsync(
        string workspacePath,
        string? mode = "largest",
        long? minSize = null,
        long? maxSize = null,
        string? filePattern = null,
        string[]? extensions = null,
        int maxResults = 50,
        bool includeAnalysis = true,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Fast file size analysis in {WorkspacePath}, Mode: {Mode}", 
                workspacePath, mode);

            // Validate inputs
            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                return UnifiedToolResponse<object>.CreateError(
                    ErrorCodes.VALIDATION_ERROR,
                    "Workspace path cannot be empty",
                    _errorRecoveryService.GetValidationErrorRecovery("workspacePath", "absolute directory path"));
            }

            // Get index searcher
            IndexSearcher searcher;
            try
            {
                searcher = await _luceneIndexService.GetIndexSearcherAsync(workspacePath, cancellationToken);
            }
            catch (DirectoryNotFoundException)
            {
                return UnifiedToolResponse<object>.CreateError(
                    ErrorCodes.INDEX_NOT_FOUND,
                    $"No search index exists for {workspacePath}",
                    _errorRecoveryService.GetIndexNotFoundRecovery(workspacePath));
            }
            
            // Build query based on mode
            Query query = mode?.ToLower() switch
            {
                "range" => BuildRangeQuery(minSize, maxSize),
                "largest" => BuildLargestQuery(),
                "smallest" => BuildSmallestQuery(minSize),
                "zero" => BuildZeroSizeQuery(),
                "distribution" => new MatchAllDocsQuery(),
                _ => BuildLargestQuery() // Default to largest
            };
            
            // Add filters if specified
            if (!string.IsNullOrWhiteSpace(filePattern) || extensions?.Length > 0)
            {
                var boolQuery = new BooleanQuery();
                boolQuery.Add(query, Occur.MUST);
                
                // Add file pattern filter
                if (!string.IsNullOrWhiteSpace(filePattern))
                {
                    var pathQuery = new WildcardQuery(new Term("relativePath", $"*{filePattern}*"));
                    boolQuery.Add(pathQuery, Occur.MUST);
                }
                
                // Add extension filters
                if (extensions?.Length > 0)
                {
                    var extensionQuery = new BooleanQuery();
                    foreach (var ext in extensions)
                    {
                        var normalizedExt = ext.StartsWith(".") ? ext : $".{ext}";
                        extensionQuery.Add(new TermQuery(new Term("extension", normalizedExt)), Occur.SHOULD);
                    }
                    boolQuery.Add(extensionQuery, Occur.MUST);
                }
                
                query = boolQuery;
            }

            // Execute search with appropriate sort
            var sort = mode?.ToLower() switch
            {
                "smallest" => new Sort(new SortField("size", SortFieldType.INT64, false)),
                "distribution" => null, // No sort needed for distribution
                _ => new Sort(new SortField("size", SortFieldType.INT64, true)) // Default to largest first
            };
            
            var startTime = DateTime.UtcNow;
            var searchMax = mode?.ToLower() == "distribution" ? 10000 : maxResults;
            var topDocs = sort != null ? 
                searcher.Search(query, searchMax, sort) : 
                searcher.Search(query, searchMax);
            var searchDuration = (DateTime.UtcNow - startTime).TotalMilliseconds;

            // Process results based on mode
            if (mode?.ToLower() == "distribution" && includeAnalysis)
            {
                return ProcessDistributionResults(searcher, topDocs, workspacePath, searchDuration);
            }
            
            // Process standard results
            var results = new List<object>();
            long totalSize = 0;
            var sizeGroups = new Dictionary<string, int>();
            
            foreach (var scoreDoc in topDocs.ScoreDocs.Take(maxResults))
            {
                // Use field selector to load only size analysis fields for better performance
                var doc = _fieldSelectorService.LoadDocument(searcher, scoreDoc.Doc, FieldSetType.SizeAnalysis);
                var size = long.Parse(doc.Get("size") ?? "0");
                totalSize += size;
                
                // Track size groups
                var sizeGroup = GetSizeGroup(size);
                sizeGroups[sizeGroup] = sizeGroups.GetValueOrDefault(sizeGroup, 0) + 1;
                
                results.Add(new
                {
                    path = doc.Get("path"),
                    filename = doc.Get("filename"),
                    relativePath = doc.Get("relativePath"),
                    extension = doc.Get("extension"),
                    size = size,
                    sizeFormatted = FormatFileSize(size),
                    sizeGroup = sizeGroup,
                    language = doc.Get("language") ?? ""
                });
            }

            _logger.LogInformation("Found {Count} files in {Duration}ms - high performance analysis!", 
                results.Count, searchDuration);

            var response = new Dictionary<string, object>
            {
                ["success"] = true,
                ["workspacePath"] = workspacePath,
                ["mode"] = mode ?? "largest",
                ["totalResults"] = results.Count,
                ["searchDurationMs"] = searchDuration,
                ["results"] = results,
                ["performance"] = searchDuration < 10 ? "excellent" : "very fast"
            };

            if (includeAnalysis && results.Count > 0)
            {
                response["analysis"] = new
                {
                    totalSize = totalSize,
                    totalSizeFormatted = FormatFileSize(totalSize),
                    averageSize = totalSize / results.Count,
                    averageSizeFormatted = FormatFileSize(totalSize / results.Count),
                    sizeDistribution = sizeGroups.OrderByDescending(kvp => kvp.Value).ToList(),
                    largestFile = results.FirstOrDefault(),
                    smallestFile = results.LastOrDefault()
                };
            }

            return response;
        }
        catch (CircuitBreakerOpenException cbEx)
        {
            _logger.LogWarning(cbEx, "Circuit breaker is open for file size analysis");
            return UnifiedToolResponse<object>.CreateError(
                ErrorCodes.CIRCUIT_BREAKER_OPEN,
                cbEx.Message,
                _errorRecoveryService.GetCircuitBreakerOpenRecovery(cbEx.OperationName));
        }
        catch (DirectoryNotFoundException dnfEx)
        {
            _logger.LogError(dnfEx, "Directory not found for file size analysis");
            return UnifiedToolResponse<object>.CreateError(
                ErrorCodes.DIRECTORY_NOT_FOUND,
                dnfEx.Message,
                _errorRecoveryService.GetDirectoryNotFoundRecovery(workspacePath));
        }
        catch (UnauthorizedAccessException uaEx)
        {
            _logger.LogError(uaEx, "Permission denied for file size analysis");
            return UnifiedToolResponse<object>.CreateError(
                ErrorCodes.PERMISSION_DENIED,
                $"Permission denied accessing {workspacePath}: {uaEx.Message}",
                null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in fast file size analysis");
            return UnifiedToolResponse<object>.CreateError(
                ErrorCodes.INTERNAL_ERROR,
                $"Analysis failed: {ex.Message}",
                null);
        }
    }

    private object ProcessDistributionResults(IndexSearcher searcher, TopDocs topDocs, string workspacePath, double searchDuration)
    {
        var distribution = new Dictionary<string, (int count, long totalSize)>();
        var extensionStats = new Dictionary<string, (int count, long totalSize)>();
        long overallTotalSize = 0;
        int totalFiles = 0;

        foreach (var scoreDoc in topDocs.ScoreDocs)
        {
            // Use field selector to load only size analysis fields for better performance
            var doc = _fieldSelectorService.LoadDocument(searcher, scoreDoc.Doc, FieldSetType.SizeAnalysis);
            var size = long.Parse(doc.Get("size") ?? "0");
            var extension = doc.Get("extension") ?? "unknown";
            
            overallTotalSize += size;
            totalFiles++;
            
            // Size group distribution
            var sizeGroup = GetSizeGroup(size);
            if (!distribution.ContainsKey(sizeGroup))
                distribution[sizeGroup] = (0, 0);
            var current = distribution[sizeGroup];
            distribution[sizeGroup] = (current.count + 1, current.totalSize + size);
            
            // Extension statistics
            if (!extensionStats.ContainsKey(extension))
                extensionStats[extension] = (0, 0);
            var extCurrent = extensionStats[extension];
            extensionStats[extension] = (extCurrent.count + 1, extCurrent.totalSize + size);
        }

        return new
        {
            success = true,
            workspacePath = workspacePath,
            mode = "distribution",
            searchDurationMs = searchDuration,
            performance = searchDuration < 50 ? "excellent" : "very fast",
            summary = new
            {
                totalFiles = totalFiles,
                totalSize = overallTotalSize,
                totalSizeFormatted = FormatFileSize(overallTotalSize),
                averageFileSize = totalFiles > 0 ? overallTotalSize / totalFiles : 0,
                averageFileSizeFormatted = totalFiles > 0 ? FormatFileSize(overallTotalSize / totalFiles) : "0 B"
            },
            sizeDistribution = distribution
                .Select(kvp => new
                {
                    sizeGroup = kvp.Key,
                    fileCount = kvp.Value.count,
                    totalSize = kvp.Value.totalSize,
                    totalSizeFormatted = FormatFileSize(kvp.Value.totalSize),
                    percentage = (kvp.Value.count * 100.0 / totalFiles).ToString("F1") + "%"
                })
                .OrderBy(x => GetSizeGroupOrder(x.sizeGroup))
                .ToList(),
            topExtensionsBySize = extensionStats
                .OrderByDescending(kvp => kvp.Value.totalSize)
                .Take(10)
                .Select(kvp => new
                {
                    extension = kvp.Key,
                    fileCount = kvp.Value.count,
                    totalSize = kvp.Value.totalSize,
                    totalSizeFormatted = FormatFileSize(kvp.Value.totalSize),
                    averageSize = kvp.Value.totalSize / kvp.Value.count,
                    averageSizeFormatted = FormatFileSize(kvp.Value.totalSize / kvp.Value.count)
                })
                .ToList()
        };
    }

    private Query BuildRangeQuery(long? minSize, long? maxSize)
    {
        var min = minSize ?? 0L;
        var max = maxSize ?? long.MaxValue;
        return NumericRangeQuery.NewInt64Range("size", min, max, true, true);
    }

    private Query BuildLargestQuery()
    {
        // Find files larger than 1KB to exclude tiny files
        return NumericRangeQuery.NewInt64Range("size", 1024L, long.MaxValue, true, true);
    }

    private Query BuildSmallestQuery(long? minSize)
    {
        // Find non-zero files by default, or files above minSize
        var min = minSize ?? 1L;
        return NumericRangeQuery.NewInt64Range("size", min, long.MaxValue, true, true);
    }

    private Query BuildZeroSizeQuery()
    {
        return new TermQuery(new Term("size", "0"));
    }

    private string GetSizeGroup(long bytes)
    {
        if (bytes == 0) return "Empty";
        if (bytes < 1024) return "< 1 KB";
        if (bytes < 10 * 1024) return "1-10 KB";
        if (bytes < 100 * 1024) return "10-100 KB";
        if (bytes < 1024 * 1024) return "100 KB - 1 MB";
        if (bytes < 10 * 1024 * 1024) return "1-10 MB";
        if (bytes < 100 * 1024 * 1024) return "10-100 MB";
        if (bytes < 1024L * 1024 * 1024) return "100 MB - 1 GB";
        return "> 1 GB";
    }

    private int GetSizeGroupOrder(string sizeGroup)
    {
        return sizeGroup switch
        {
            "Empty" => 0,
            "< 1 KB" => 1,
            "1-10 KB" => 2,
            "10-100 KB" => 3,
            "100 KB - 1 MB" => 4,
            "1-10 MB" => 5,
            "10-100 MB" => 6,
            "100 MB - 1 GB" => 7,
            "> 1 GB" => 8,
            _ => 9
        };
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
}