using COA.CodeSearch.McpServer.Services;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Util;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// High-performance tool to find recently modified files using Lucene's indexed lastModified field
/// </summary>
public class FastRecentFilesTool : ITool
{
    public string ToolName => "fast_recent_files";
    public string Description => "Find recently modified files using indexed timestamps";
    public ToolCategory Category => ToolCategory.Search;
    private readonly ILogger<FastRecentFilesTool> _logger;
    private readonly ILuceneIndexService _luceneIndexService;
    private readonly IFieldSelectorService _fieldSelectorService;
    private const LuceneVersion Version = LuceneVersion.LUCENE_48;

    public FastRecentFilesTool(
        ILogger<FastRecentFilesTool> logger,
        ILuceneIndexService luceneIndexService,
        IFieldSelectorService fieldSelectorService)
    {
        _logger = logger;
        _luceneIndexService = luceneIndexService;
        _fieldSelectorService = fieldSelectorService;
    }

    public async Task<object> ExecuteAsync(
        string workspacePath,
        string? timeFrame = "24h",
        string? filePattern = null,
        string[]? extensions = null,
        int maxResults = 50,
        bool includeSize = true,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Fast recent files search in {WorkspacePath}, TimeFrame: {TimeFrame}", 
                workspacePath, timeFrame);

            // Validate inputs
            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                return new
                {
                    success = false,
                    error = "Workspace path cannot be empty"
                };
            }

            // Parse time frame
            var cutoffTime = ParseTimeFrame(timeFrame ?? "24h");
            var cutoffTicks = cutoffTime.Ticks;

            // Get index searcher
            var searcher = await _luceneIndexService.GetIndexSearcherAsync(workspacePath, cancellationToken);
            
            // Build range query for lastModified field
            var rangeQuery = NumericRangeQuery.NewInt64Range("lastModified", cutoffTicks, long.MaxValue, true, true);
            
            // Build combined query if filters are specified
            Query finalQuery = rangeQuery;
            if (!string.IsNullOrWhiteSpace(filePattern) || extensions?.Length > 0)
            {
                var boolQuery = new BooleanQuery();
                boolQuery.Add(rangeQuery, Occur.MUST);
                
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
                
                finalQuery = boolQuery;
            }

            // Execute search with custom sort by lastModified descending
            var sort = new Sort(new SortField("lastModified", SortFieldType.INT64, true));
            var startTime = DateTime.UtcNow;
            var topDocs = searcher.Search(finalQuery, maxResults, sort);
            var searchDuration = (DateTime.UtcNow - startTime).TotalMilliseconds;

            // Process results
            var results = new List<object>();
            var totalSize = 0L;
            
            // Define fields needed for recent files analysis
            var recentFilesFields = new[] { "path", "filename", "relativePath", "extension", "lastModified", "size", "language" };
            
            foreach (var scoreDoc in topDocs.ScoreDocs)
            {
                var doc = _fieldSelectorService.LoadDocument(searcher, scoreDoc.Doc, recentFilesFields);
                var lastModifiedTicks = long.Parse(doc.Get("lastModified") ?? "0");
                var lastModified = new DateTime(lastModifiedTicks);
                var size = long.Parse(doc.Get("size") ?? "0");
                totalSize += size;
                
                var result = new
                {
                    path = doc.Get("path"),
                    filename = doc.Get("filename"),
                    relativePath = doc.Get("relativePath"),
                    extension = doc.Get("extension"),
                    lastModified = lastModified,
                    timeAgo = GetFriendlyTimeAgo(lastModified),
                    size = includeSize ? size : (long?)null,
                    sizeFormatted = includeSize ? FormatFileSize(size) : null,
                    language = doc.Get("language") ?? ""
                };
                
                results.Add(result);
            }

            _logger.LogInformation("Found {Count} recent files in {Duration}ms - high performance search!", 
                results.Count, searchDuration);

            return new
            {
                success = true,
                workspacePath = workspacePath,
                timeFrame = timeFrame,
                cutoffTime = cutoffTime,
                totalResults = results.Count,
                searchDurationMs = searchDuration,
                results = results,
                summary = new
                {
                    totalFiles = results.Count,
                    totalSize = includeSize ? totalSize : (long?)null,
                    totalSizeFormatted = includeSize ? FormatFileSize(totalSize) : null,
                    fileTypes = results.GroupBy(r => ((dynamic)r).extension)
                        .Select(g => new { extension = g.Key, count = g.Count() })
                        .OrderByDescending(x => x.count)
                        .ToList()
                },
                performance = searchDuration < 10 ? "excellent" : "very fast"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in fast recent files search");
            return new
            {
                success = false,
                error = $"Search failed: {ex.Message}"
            };
        }
    }

    private DateTime ParseTimeFrame(string timeFrame)
    {
        var now = DateTime.UtcNow;
        
        // Parse patterns like "24h", "7d", "1w", "30m"
        if (timeFrame.Length < 2)
            return now.AddDays(-1); // Default to 24 hours
            
        var numberPart = timeFrame.Substring(0, timeFrame.Length - 1);
        var unit = timeFrame[timeFrame.Length - 1];
        
        if (!int.TryParse(numberPart, out var number))
            return now.AddDays(-1); // Default to 24 hours
            
        return unit switch
        {
            'm' => now.AddMinutes(-number),
            'h' => now.AddHours(-number),
            'd' => now.AddDays(-number),
            'w' => now.AddDays(-number * 7),
            _ => now.AddDays(-1) // Default to 24 hours
        };
    }

    private string GetFriendlyTimeAgo(DateTime dateTime)
    {
        var timeSpan = DateTime.UtcNow - dateTime;
        
        if (timeSpan.TotalMinutes < 1)
            return "just now";
        if (timeSpan.TotalMinutes < 60)
            return $"{(int)timeSpan.TotalMinutes} minute{((int)timeSpan.TotalMinutes == 1 ? "" : "s")} ago";
        if (timeSpan.TotalHours < 24)
            return $"{(int)timeSpan.TotalHours} hour{((int)timeSpan.TotalHours == 1 ? "" : "s")} ago";
        if (timeSpan.TotalDays < 7)
            return $"{(int)timeSpan.TotalDays} day{((int)timeSpan.TotalDays == 1 ? "" : "s")} ago";
        if (timeSpan.TotalDays < 30)
            return $"{(int)(timeSpan.TotalDays / 7)} week{((int)(timeSpan.TotalDays / 7) == 1 ? "" : "s")} ago";
        
        return dateTime.ToString("yyyy-MM-dd");
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