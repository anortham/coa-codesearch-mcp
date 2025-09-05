using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Services.Lucene;
using COA.CodeSearch.McpServer.ResponseBuilders;
using COA.Mcp.Framework;
using COA.Mcp.Framework.Base;
using COA.Mcp.Framework.TokenOptimization.Models;
using COA.Mcp.Framework.TokenOptimization.Storage;
using COA.Mcp.Framework.TokenOptimization;
using COA.Mcp.Framework.TokenOptimization.ResponseBuilders;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Util;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Line-level search tool that returns ALL occurrences of a pattern with structured results.
/// Faster than bash grep with AI-optimized context management and token limits.
/// </summary>
public class LineSearchTool : CodeSearchToolBase<LineSearchParams, AIOptimizedResponse<LineSearchResult>>
{
    private readonly ILuceneIndexService _indexService;
    private readonly LineAwareSearchService _lineSearchService;
    private readonly IPathResolutionService _pathResolutionService;
    private readonly QueryPreprocessor _queryPreprocessor;
    private readonly IResourceStorageService _storageService;
    private readonly LineSearchResponseBuilder _responseBuilder;
    private readonly ILogger<LineSearchTool> _logger;

    public LineSearchTool(
        IServiceProvider serviceProvider,
        ILuceneIndexService indexService,
        LineAwareSearchService lineSearchService,
        IPathResolutionService pathResolutionService,
        QueryPreprocessor queryPreprocessor,
        IResourceStorageService storageService,
        ILogger<LineSearchTool> logger) : base(serviceProvider)
    {
        _indexService = indexService;
        _lineSearchService = lineSearchService;
        _pathResolutionService = pathResolutionService;
        _queryPreprocessor = queryPreprocessor;
        _storageService = storageService;
        _responseBuilder = new LineSearchResponseBuilder(null, storageService);
        _logger = logger;
    }

    public override string Name => ToolNames.LineSearch;
    public override string Description => 
        "REPLACE grep/bash - Get ALL occurrences with line numbers. BETTER than Bash grep - returns structured JSON. " +
        "Perfect for: counting usages, refactoring prep, finding all instances. Use when you need EVERY match, not just examples.";
    public override ToolCategory Category => ToolCategory.Query;

    protected override async Task<AIOptimizedResponse<LineSearchResult>> ExecuteInternalAsync(LineSearchParams parameters, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            _logger.LogInformation("Starting line search for pattern: {Pattern}", parameters.Pattern);

            // Validate workspace path
            var normalizedPath = _pathResolutionService.GetFullPath(parameters.WorkspacePath);
            if (!_pathResolutionService.DirectoryExists(normalizedPath))
            {
                throw new DirectoryNotFoundException($"Workspace path not found: {parameters.WorkspacePath}");
            }

            // Ensure index exists
            if (!await _indexService.IndexExistsAsync(normalizedPath, cancellationToken))
            {
                throw new InvalidOperationException($"No search index found for workspace. Run index_workspace first for: {parameters.WorkspacePath}");
            }

            // Validate and preprocess query using the existing QueryPreprocessor
            var searchType = parameters.SearchType ?? "standard";
            if (!_queryPreprocessor.IsValidQuery(parameters.Pattern, searchType, out var errorMessage))
            {
                throw new ArgumentException($"Invalid query pattern: {errorMessage}");
            }

            // Build query with proper preprocessing for code patterns and special characters
            var analyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
            var query = _queryPreprocessor.BuildQuery(parameters.Pattern, searchType, parameters.CaseSensitive, analyzer);

            var searchResults = await _indexService.SearchAsync(normalizedPath, query, parameters.MaxTotalResults, cancellationToken);

            // Process results into line-centric format
            var fileResults = await ProcessSearchResults(searchResults, parameters);

            // Calculate statistics
            var totalLineMatches = fileResults.Sum(f => f.TotalMatches);
            var totalFilesSearched = searchResults.TotalHits; // From Lucene search
            var truncated = fileResults.Any(f => f.Matches.Count < f.TotalMatches) || 
                          fileResults.Count >= parameters.MaxTotalResults;

            stopwatch.Stop();

            var result = new LineSearchResult
            {
                Summary = GenerateSummary(fileResults, totalLineMatches, stopwatch.Elapsed),
                Files = fileResults,
                TotalFilesSearched = totalFilesSearched,
                TotalFilesWithMatches = fileResults.Count,
                TotalLineMatches = totalLineMatches,
                SearchTime = stopwatch.Elapsed,
                Query = parameters.Pattern,
                Truncated = truncated,
                Insights = GenerateInsights(fileResults, parameters)
            };

            _logger.LogInformation("Line search completed: {FileCount} files, {LineCount} lines, {Duration}ms",
                fileResults.Count, totalLineMatches, stopwatch.ElapsedMilliseconds);

            // Use response builder for optimization and resource storage
            var responseContext = new ResponseContext
            {
                TokenLimit = parameters.MaxTokens ?? 8000,
                ResponseMode = parameters.ResponseMode ?? "default",
                StoreFullResults = true,
                ToolName = Name
            };

            return await _responseBuilder.BuildResponseAsync(result, responseContext);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during line search for pattern: {Pattern}", parameters.Pattern);
            stopwatch.Stop();

            var errorResult = new LineSearchResult
            {
                Summary = $"Error: {ex.Message}",
                Files = new List<LineSearchFileResult>(),
                TotalFilesSearched = 0,
                TotalFilesWithMatches = 0,
                TotalLineMatches = 0,
                SearchTime = stopwatch.Elapsed,
                Query = parameters.Pattern,
                Truncated = false,
                Insights = new List<string> { $"Search failed: {ex.Message}" }
            };

            var responseContext = new ResponseContext
            {
                TokenLimit = parameters.MaxTokens ?? 8000,
                ResponseMode = parameters.ResponseMode ?? "default",
                StoreFullResults = false,
                ToolName = Name
            };

            return await _responseBuilder.BuildResponseAsync(errorResult, responseContext);
        }
    }

    private async Task<List<LineSearchFileResult>> ProcessSearchResults(
        SearchResult searchResults, 
        LineSearchParams parameters)
    {
        var fileResults = new List<LineSearchFileResult>();
        var totalResultsProcessed = 0;

        foreach (var hit in searchResults.Hits)
        {
            if (totalResultsProcessed >= parameters.MaxTotalResults)
                break;

            try
            {
                // Apply file pattern filter if specified
                if (!string.IsNullOrEmpty(parameters.FilePattern) && 
                    !MatchesFilePattern(hit.FilePath, parameters.FilePattern))
                {
                    continue;
                }

                // Get all line matches for this file (not just first occurrence)
                var lineMatches = await ExtractAllLineMatches(hit, parameters);
                
                if (lineMatches.Count == 0)
                    continue;

                // Apply per-file limit
                var limitedMatches = lineMatches.Take(parameters.MaxResultsPerFile).ToList();
                totalResultsProcessed += limitedMatches.Count;

                var fileResult = new LineSearchFileResult
                {
                    FilePath = hit.FilePath,
                    Matches = limitedMatches,
                    TotalMatches = lineMatches.Count, // Full count, not limited
                    LastModified = GetFileLastModified(hit.FilePath),
                    FileSize = GetFileSize(hit.FilePath)
                };

                fileResults.Add(fileResult);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error processing file {FilePath}", hit.FilePath);
            }
        }

        return fileResults;
    }

    private Task<List<LineMatch>> ExtractAllLineMatches(
        SearchHit hit, 
        LineSearchParams parameters)
    {
        var matches = new List<LineMatch>();

        try
        {
            // Get content from index, NOT from disk!
            string[]? lines = null;
            
            // Option 1: Try content field
            if (hit.Fields.TryGetValue("content", out var content) && !string.IsNullOrEmpty(content))
            {
                // Handle both Unix (\n) and Windows (\r\n) line endings
                lines = content.Replace("\r\n", "\n").Split('\n');
            }
            // Option 2: Try line_data field (while it still exists)
            else if (hit.Fields.TryGetValue("line_data", out var lineDataJson) && !string.IsNullOrEmpty(lineDataJson))
            {
                var lineData = LineData.DeserializeLineData(lineDataJson);
                if (lineData?.Lines != null)
                {
                    lines = lineData.Lines;
                }
            }
            
            if (lines == null)
            {
                // No indexed content - this should never happen
                _logger.LogError("No indexed content for {FilePath} - index may be corrupted", hit.FilePath);
                return Task.FromResult(matches);
            }

            var pattern = parameters.CaseSensitive ? parameters.Pattern : parameters.Pattern.ToLowerInvariant();

            // Find ALL matching lines (not just first occurrence)
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var searchLine = parameters.CaseSensitive ? line : line.ToLowerInvariant();

                if (ContainsPattern(searchLine, pattern, parameters.SearchType))
                {
                    var contextStart = Math.Max(0, i - parameters.ContextLines);
                    var contextEnd = Math.Min(lines.Length - 1, i + parameters.ContextLines);
                    
                    var contextLines = new List<string>();
                    for (int ctx = contextStart; ctx <= contextEnd; ctx++)
                    {
                        if (ctx != i) // Don't duplicate the match line
                            contextLines.Add(lines[ctx]);
                    }

                    var match = new LineMatch
                    {
                        FilePath = hit.FilePath,
                        LineNumber = i + 1, // 1-based line numbers
                        LineContent = line,
                        ContextLines = contextLines.Count > 0 ? contextLines.ToArray() : null,
                        StartLine = contextStart + 1,
                        EndLine = contextEnd + 1,
                        HighlightedFragments = ExtractHighlights(line, parameters.Pattern, parameters.CaseSensitive)
                    };

                    matches.Add(match);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error extracting line matches from {FilePath}", hit.FilePath);
        }

        return Task.FromResult(matches);
    }

    private bool ContainsPattern(string text, string pattern, string searchType)
    {
        return searchType.ToLowerInvariant() switch
        {
            "regex" => System.Text.RegularExpressions.Regex.IsMatch(text, pattern),
            "wildcard" => MatchesWildcard(text, pattern),
            "literal" => text.Contains(pattern, StringComparison.Ordinal),
            _ => text.Contains(pattern) // standard
        };
    }

    private bool MatchesWildcard(string text, string pattern)
    {
        // Simple wildcard matching (* and ?)
        var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(text, regex);
    }

    private bool MatchesFilePattern(string filePath, string pattern)
    {
        var fileName = Path.GetFileName(filePath);
        return MatchesWildcard(fileName, pattern);
    }

    private List<string> ExtractHighlights(string line, string pattern, bool caseSensitive)
    {
        var highlights = new List<string>();
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        
        var index = line.IndexOf(pattern, comparison);
        if (index >= 0)
        {
            // Extract context around the match
            var start = Math.Max(0, index - 10);
            var length = Math.Min(line.Length - start, pattern.Length + 20);
            highlights.Add(line.Substring(start, length));
        }

        return highlights;
    }

    private DateTime? GetFileLastModified(string filePath)
    {
        try
        {
            return File.GetLastWriteTime(filePath);
        }
        catch
        {
            return null;
        }
    }

    private long? GetFileSize(string filePath)
    {
        try
        {
            return new System.IO.FileInfo(filePath).Length;
        }
        catch
        {
            return null;
        }
    }

    private string GenerateSummary(List<LineSearchFileResult> fileResults, int totalLineMatches, TimeSpan searchTime)
    {
        var fileCount = fileResults.Count;
        var timeMs = (int)searchTime.TotalMilliseconds;

        if (fileCount == 0)
            return $"No matches found ({timeMs}ms)";

        return $"{totalLineMatches} lines in {fileCount} files ({timeMs}ms)";
    }

    private List<string> GenerateInsights(List<LineSearchFileResult> fileResults, LineSearchParams parameters)
    {
        var insights = new List<string>();

        if (fileResults.Count == 0)
        {
            insights.Add("No matches found - try broader search terms or check file patterns");
            return insights;
        }

        // File type distribution
        var extensions = fileResults
            .Select(f => Path.GetExtension(f.FilePath))
            .Where(ext => !string.IsNullOrEmpty(ext))
            .GroupBy(ext => ext)
            .OrderByDescending(g => g.Count())
            .Take(3)
            .Select(g => $"{g.Key}:{g.Count()}")
            .ToList();

        if (extensions.Any())
            insights.Add($"Types: {string.Join(", ", extensions)}");

        // High-frequency files
        var highFreqFiles = fileResults
            .Where(f => f.TotalMatches > 5)
            .OrderByDescending(f => f.TotalMatches)
            .Take(2)
            .Select(f => $"{Path.GetFileName(f.FilePath)} ({f.TotalMatches} matches)")
            .ToList();

        if (highFreqFiles.Any())
            insights.Add($"High frequency: {string.Join(", ", highFreqFiles)}");

        // Truncation warning
        if (fileResults.Any(f => f.Matches.Count < f.TotalMatches))
            insights.Add($"Some files truncated (limit: {parameters.MaxResultsPerFile} per file)");

        return insights;
    }
}