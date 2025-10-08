using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Services.Lucene;
using COA.CodeSearch.McpServer.Services.Analysis;
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
    private readonly SmartQueryPreprocessor _queryProcessor;
    private readonly CodeAnalyzer _codeAnalyzer;
    private readonly IResourceStorageService _storageService;
    private readonly LineSearchResponseBuilder _responseBuilder;
    private readonly ILogger<LineSearchTool> _logger;

    /// <summary>
    /// Initializes a new instance of the LineSearchTool with required dependencies.
    /// </summary>
    /// <param name="serviceProvider">Service provider for dependency resolution</param>
    /// <param name="indexService">Lucene index service for search operations</param>
    /// <param name="lineSearchService">Line-aware search service</param>
    /// <param name="pathResolutionService">Path resolution service</param>
    /// <param name="queryProcessor">Smart query preprocessing service</param>
    /// <param name="codeAnalyzer">Code analysis service</param>
    /// <param name="storageService">Resource storage service</param>
    /// <param name="logger">Logger instance</param>
    public LineSearchTool(
        IServiceProvider serviceProvider,
        ILuceneIndexService indexService,
        LineAwareSearchService lineSearchService,
        IPathResolutionService pathResolutionService,
        SmartQueryPreprocessor queryProcessor,
        CodeAnalyzer codeAnalyzer,
        IResourceStorageService storageService,
        ILogger<LineSearchTool> logger) : base(serviceProvider, logger)
    {
        _indexService = indexService;
        _lineSearchService = lineSearchService;
        _pathResolutionService = pathResolutionService;
        _queryProcessor = queryProcessor;
        _codeAnalyzer = codeAnalyzer;
        _storageService = storageService;
        _responseBuilder = new LineSearchResponseBuilder(null, storageService);
        _logger = logger;
    }

    /// <summary>
    /// Gets the tool name identifier.
    /// </summary>
    public override string Name => ToolNames.LineSearch;

    /// <summary>
    /// Gets the tool description explaining its purpose and usage scenarios.
    /// </summary>
    public override string Description =>
        "REPLACE grep/bash - Get ALL occurrences with line numbers. BETTER than Bash grep - returns structured JSON. " +
        "Perfect for: counting usages, refactoring prep, finding all instances. Use when you need EVERY match, not just examples.";

    /// <summary>
    /// Gets the tool category for classification purposes.
    /// </summary>
    public override ToolCategory Category => ToolCategory.Query;

    /// <summary>
    /// Executes the line search operation to find all occurrences of a pattern.
    /// </summary>
    /// <param name="parameters">Line search parameters including pattern and search options</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>Line search results with all matching lines and their context</returns>
    protected override async Task<AIOptimizedResponse<LineSearchResult>> ExecuteInternalAsync(LineSearchParams parameters, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            _logger.LogInformation("Starting line search for pattern: {Pattern}", parameters.Pattern);

            // Use provided workspace path or default to current workspace
            var workspacePath = string.IsNullOrWhiteSpace(parameters.WorkspacePath)
                ? _pathResolutionService.GetPrimaryWorkspacePath()
                : parameters.WorkspacePath;

            var normalizedPath = _pathResolutionService.GetFullPath(workspacePath);
            if (!_pathResolutionService.DirectoryExists(normalizedPath))
            {
                throw new DirectoryNotFoundException($"Workspace path not found: {workspacePath}");
            }

            // Ensure index exists
            if (!await _indexService.IndexExistsAsync(normalizedPath, cancellationToken))
            {
                throw new InvalidOperationException($"No search index found for workspace. Run index_workspace first for: {workspacePath}");
            }

            // Use SmartQueryPreprocessor for multi-field search optimization
            var searchMode = DetermineSearchMode(parameters.SearchType ?? "standard");
            var queryResult = _queryProcessor.Process(parameters.Pattern, searchMode);
            
            _logger.LogInformation("Line search: {Pattern} -> Field: {Field}, Query: {Query}, Reason: {Reason}", 
                parameters.Pattern, queryResult.TargetField, queryResult.ProcessedQuery, queryResult.Reason);
            
            // Build query using the processed query and target field
            // Use CodeAnalyzer to match the analyzer used during indexing
            var parser = new QueryParser(LuceneVersion.LUCENE_48, queryResult.TargetField, _codeAnalyzer);
            var query = parser.Parse(queryResult.ProcessedQuery);

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
            // Get content from index - Lucene already found the pattern exists!
            if (!hit.Fields.TryGetValue("content", out var content) || string.IsNullOrEmpty(content))
            {
                _logger.LogError("No indexed content for {FilePath} - index may be corrupted", hit.FilePath);
                return Task.FromResult(matches);
            }

            // OPTIMIZATION: Trust Lucene - it found the pattern, so we just need to locate WHERE
            matches = FindMatchingLinesEfficiently(content, parameters, hit.FilePath);
            
            _logger.LogTrace("Efficiently extracted {Count} line matches from {FilePath} using optimized search", 
                matches.Count, hit.FilePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error extracting line matches from {FilePath}", hit.FilePath);
        }

        return Task.FromResult(matches);
    }

    /// <summary>
    /// Efficiently finds matching lines by trusting Lucene's search results and using targeted line extraction.
    /// This replaces the inefficient approach of splitting entire files and doing manual pattern matching.
    /// </summary>
    private List<LineMatch> FindMatchingLinesEfficiently(string content, LineSearchParams parameters, string filePath)
    {
        var matches = new List<LineMatch>();
        
        // Prepare pattern for efficient searching
        var pattern = parameters.CaseSensitive ? parameters.Pattern : parameters.Pattern.ToLowerInvariant();
        var searchContent = parameters.CaseSensitive ? content : content.ToLowerInvariant();
        
        // Find all occurrences of the pattern in content
        var patternMatches = FindPatternOccurrences(searchContent, pattern, parameters.SearchType);
        
        if (patternMatches.Count == 0)
        {
            _logger.LogDebug("Pattern '{Pattern}' not found in content for {FilePath} - possible Lucene/search mode mismatch", 
                parameters.Pattern, filePath);
            return matches;
        }

        // Convert content to lines only once, and only when we know we have matches
        var lines = content.Replace("\r\n", "\n").Split('\n');
        
        // For each pattern occurrence, find which line it's on
        var processedLines = new HashSet<int>(); // Avoid duplicate line matches
        
        foreach (var occurrence in patternMatches)
        {
            var lineNumber = GetLineNumberFromPosition(content, occurrence.Position);
            
            // Skip if we've already processed this line
            if (processedLines.Contains(lineNumber))
                continue;
                
            processedLines.Add(lineNumber);
            
            var lineIndex = lineNumber - 1; // Convert to 0-based
            if (lineIndex < 0 || lineIndex >= lines.Length)
                continue;
                
            // Extract context lines efficiently
            var contextStart = Math.Max(0, lineIndex - parameters.ContextLines);
            var contextEnd = Math.Min(lines.Length - 1, lineIndex + parameters.ContextLines);
            
            var contextLines = new List<string>();
            for (int ctx = contextStart; ctx <= contextEnd; ctx++)
            {
                if (ctx != lineIndex) // Don't duplicate the match line
                    contextLines.Add(lines[ctx]);
            }

            var match = new LineMatch
            {
                FilePath = filePath,
                LineNumber = lineNumber,
                LineContent = lines[lineIndex],
                ContextLines = contextLines.Count > 0 ? contextLines.ToArray() : null,
                StartLine = contextStart + 1,
                EndLine = contextEnd + 1,
                HighlightedFragments = ExtractHighlights(lines[lineIndex], parameters.Pattern, parameters.CaseSensitive)
            };

            matches.Add(match);
        }
        
        return matches.OrderBy(m => m.LineNumber).ToList();
    }
    
    /// <summary>
    /// Efficiently finds all occurrences of a pattern in content without splitting into lines first.
    /// </summary>
    private List<(int Position, int Length)> FindPatternOccurrences(string content, string pattern, string searchType)
    {
        var occurrences = new List<(int, int)>();
        var comparison = StringComparison.Ordinal;
        
        switch (searchType.ToLowerInvariant())
        {
            case "regex":
                try
                {
                    var regex = new System.Text.RegularExpressions.Regex(pattern);
                    var matches = regex.Matches(content);
                    occurrences.AddRange(matches.Cast<System.Text.RegularExpressions.Match>()
                        .Select(m => (m.Index, m.Length)));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Invalid regex pattern '{Pattern}', falling back to literal search", pattern);
                    goto case "literal";
                }
                break;
                
            case "wildcard":
                // Convert wildcard to regex and use regex search
                var wildcardRegex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
                    .Replace("\\*", ".*")
                    .Replace("\\?", ".") + "$";
                try
                {
                    var regex = new System.Text.RegularExpressions.Regex(wildcardRegex);
                    // For wildcards, we need to check line by line since it's a line-matching pattern
                    var lines = content.Split('\n');
                    int position = 0;
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (regex.IsMatch(lines[i]))
                        {
                            occurrences.Add((position, lines[i].Length));
                        }
                        position += lines[i].Length + 1; // +1 for newline
                    }
                }
                catch
                {
                    goto case "literal";
                }
                break;
                
            case "literal":
            default:
                // Efficient string searching for literal patterns
                int index = 0;
                while ((index = content.IndexOf(pattern, index, comparison)) != -1)
                {
                    occurrences.Add((index, pattern.Length));
                    index += pattern.Length;
                }
                break;
        }
        
        return occurrences;
    }
    
    /// <summary>
    /// Efficiently determines which line number a character position belongs to.
    /// </summary>
    private int GetLineNumberFromPosition(string content, int position)
    {
        if (position < 0 || position >= content.Length)
            return 1;
            
        // Count newlines before this position
        int lineNumber = 1;
        for (int i = 0; i < position && i < content.Length; i++)
        {
            if (content[i] == '\n')
                lineNumber++;
        }
        
        return lineNumber;
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

    private SearchMode DetermineSearchMode(string searchType)
    {
        return searchType.ToLowerInvariant() switch
        {
            "literal" => SearchMode.Pattern, // Pattern-preserving search for special characters
            "regex" => SearchMode.Pattern,   // Regex patterns work well with pattern field
            "wildcard" => SearchMode.Standard,
            "code" => SearchMode.Symbol,     // Code searches map to symbol search
            _ => SearchMode.Pattern // Line search should default to pattern for exact matching
        };
    }
}