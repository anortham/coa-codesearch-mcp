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
using System.Text.RegularExpressions;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Util;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Search and replace tool that consolidates the common search-read-edit workflow.
/// Finds all occurrences and can apply replacements in a single operation.
/// </summary>
public class SearchAndReplaceTool : CodeSearchToolBase<SearchAndReplaceParams, AIOptimizedResponse<SearchAndReplaceResult>>
{
    private readonly ILuceneIndexService _indexService;
    private readonly LineAwareSearchService _lineSearchService;
    private readonly IPathResolutionService _pathResolutionService;
    private readonly QueryPreprocessor _queryPreprocessor;
    private readonly IResourceStorageService _storageService;
        private readonly AdvancedPatternMatcher _patternMatcher;
    private readonly ILogger<SearchAndReplaceTool> _logger;

    public SearchAndReplaceTool(
        IServiceProvider serviceProvider,
        ILuceneIndexService indexService,
        LineAwareSearchService lineSearchService,
        IPathResolutionService pathResolutionService,
        QueryPreprocessor queryPreprocessor,
        IResourceStorageService storageService,
                AdvancedPatternMatcher patternMatcher,
        ILogger<SearchAndReplaceTool> logger) : base(serviceProvider)
    {
        _indexService = indexService;
        _lineSearchService = lineSearchService;
        _pathResolutionService = pathResolutionService;
        _queryPreprocessor = queryPreprocessor;
                _patternMatcher = patternMatcher;
        _storageService = storageService;
        _logger = logger;
    }

    public override string Name => ToolNames.SearchAndReplace;
    public override string Description => 
        "BULK updates across files - Replace patterns everywhere at once. SAFER than manual edits - preview mode by default. " +
        "Perfect for: renaming, refactoring, fixing patterns. Consolidates search→read→edit workflow.";
    public override ToolCategory Category => ToolCategory.Query;

    protected override async Task<AIOptimizedResponse<SearchAndReplaceResult>> ExecuteInternalAsync(
        SearchAndReplaceParams parameters, 
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            // Validate parameters
            if (string.IsNullOrWhiteSpace(parameters.SearchPattern))
                throw new ArgumentException("Search pattern cannot be empty");
            
            if (string.IsNullOrWhiteSpace(parameters.ReplacePattern))
                throw new ArgumentException("Replace pattern cannot be empty");

            // Validate search type
            var validTypes = new[] { "standard", "literal", "regex", "code" };
            if (!validTypes.Contains(parameters.SearchType.ToLowerInvariant()))
                throw new ArgumentException($"Invalid search type. Must be one of: {string.Join(", ", validTypes)}");

            // Validate regex if using regex search
            if (parameters.SearchType.ToLowerInvariant() == "regex")
            {
                try
                {
                    _ = new Regex(parameters.SearchPattern);
                }
                catch (ArgumentException ex)
                {
                    throw new ArgumentException($"Invalid regex pattern: {ex.Message}");
                }
            }

            // Resolve workspace path
            if (string.IsNullOrEmpty(parameters.WorkspacePath))
            {
                throw new ArgumentException("WorkspacePath is required for search and replace operations");
            }
            
            var normalizedPath = Path.GetFullPath(parameters.WorkspacePath);

            // Ensure index exists
            if (!await _indexService.IndexExistsAsync(normalizedPath, cancellationToken))
            {
                throw new InvalidOperationException($"No search index found for workspace. Run index_workspace first for: {parameters.WorkspacePath}");
            }

            // Phase 1: Find all matches using line-aware search
            var matches = await FindAllMatches(parameters, normalizedPath, cancellationToken);

            if (!matches.Any())
            {
                return await BuildNoMatchesResponse(parameters, stopwatch.Elapsed);
            }

            // Phase 2: Build replacement changes
            var changes = await BuildReplacementChanges(matches, parameters, cancellationToken);

            // Phase 3: Apply changes if not preview mode
            TimeSpan? applyTime = null;
            if (!parameters.Preview)
            {
                var applyStopwatch = Stopwatch.StartNew();
                await ApplyChangesToFiles(changes, cancellationToken);
                applyStopwatch.Stop();
                applyTime = applyStopwatch.Elapsed;
            }

            stopwatch.Stop();

            // Build result
            var result = BuildResult(changes, parameters, stopwatch.Elapsed, applyTime);

            // Use response builder for optimization
            var responseContext = new ResponseContext
            {
                TokenLimit = parameters.MaxTokens ?? 8000,
                ResponseMode = parameters.ResponseMode ?? "default",
                StoreFullResults = changes.Count > 50,
                ToolName = Name
            };

            // Create response builder and build response
            var responseBuilder = new SearchAndReplaceResponseBuilder(null, _storageService);
            return await responseBuilder.BuildResponseAsync(result, responseContext);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during search and replace for pattern: {Pattern}", parameters.SearchPattern);
            stopwatch.Stop();

            var errorResult = new SearchAndReplaceResult
            {
                Summary = $"Error: {ex.Message}",
                Preview = parameters.Preview,
                Changes = new List<ReplacementChange>(),
                FileSummaries = new List<FileChangeSummary>(),
                TotalFiles = 0,
                TotalReplacements = 0,
                SearchTime = stopwatch.Elapsed,
                SearchPattern = parameters.SearchPattern,
                ReplacePattern = parameters.ReplacePattern,
                Truncated = false,
                Insights = new List<string> { $"Operation failed: {ex.Message}" }
            };

            var errorContext = new ResponseContext
            {
                TokenLimit = parameters.MaxTokens ?? 8000,
                ResponseMode = parameters.ResponseMode ?? "default",
                StoreFullResults = false,
                ToolName = Name
            };

            var responseBuilder = new SearchAndReplaceResponseBuilder(null, _storageService);
            return await responseBuilder.BuildResponseAsync(errorResult, errorContext);
        }
    }

    private async Task<List<LineMatch>> FindAllMatches(
        SearchAndReplaceParams parameters,
        string workspacePath,
        CancellationToken cancellationToken)
    {
        // Build Lucene query - automatically use literal search for patterns with curly braces
        var analyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
        var searchType = parameters.SearchType;
        
        // Auto-detect code patterns with curly braces and force literal search
        if ((searchType == "standard" || string.IsNullOrEmpty(searchType)) && 
            (parameters.SearchPattern.Contains('{') || parameters.SearchPattern.Contains('}')))
        {
            searchType = "literal";
            _logger.LogDebug("Auto-detected curly braces in search pattern, using literal search type");
        }
        
        var query = _queryPreprocessor.BuildQuery(parameters.SearchPattern, searchType, parameters.CaseSensitive, analyzer);

        // Search for files containing the pattern
        var searchResults = await _indexService.SearchAsync(workspacePath, query, parameters.MaxMatches * 2, cancellationToken);

        var allMatches = new List<LineMatch>();
        var processedCount = 0;

        foreach (var hit in searchResults.Hits)
        {
            // Apply file pattern filter if specified
            if (!string.IsNullOrEmpty(parameters.FilePattern))
            {
                var fileName = Path.GetFileName(hit.FilePath);
                if (!IsFilePatternMatch(fileName, parameters.FilePattern))
                    continue;
            }

            // Extract line matches from this file
            var lineMatches = await ExtractLineMatches(hit, parameters);
            allMatches.AddRange(lineMatches);

            processedCount += lineMatches.Count;
            if (processedCount >= parameters.MaxMatches)
            {
                allMatches = allMatches.Take(parameters.MaxMatches).ToList();
                break;
            }
        }

        return allMatches;
    }

    private bool IsFilePatternMatch(string fileName, string pattern)
    {
        // Simple glob pattern matching
        if (pattern.Contains("*"))
        {
            var regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
            return Regex.IsMatch(fileName, regexPattern, RegexOptions.IgnoreCase);
        }
        return fileName.Equals(pattern, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<List<LineMatch>> ExtractLineMatches(SearchHit hit, SearchAndReplaceParams parameters)
    {
        var matches = new List<LineMatch>();

        try
        {
            var content = await File.ReadAllTextAsync(hit.FilePath);
            var lines = content.Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (ContainsPattern(line, parameters))
                {
                    var contextBefore = GetContextLines(lines, i - parameters.ContextLines, i - 1);
                    var contextAfter = GetContextLines(lines, i + 1, i + parameters.ContextLines);

                    matches.Add(new LineMatch
                    {
                        FilePath = hit.FilePath,
                        LineNumber = i + 1, // 1-based
                        LineContent = line,
                        ContextLines = contextBefore.Concat(contextAfter).ToArray(),
                        StartLine = Math.Max(1, i + 1 - parameters.ContextLines),
                        EndLine = Math.Min(lines.Length, i + 1 + parameters.ContextLines)
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error extracting line matches from {FilePath}", hit.FilePath);
        }

        return matches;
    }

    private bool ContainsPattern(string line, SearchAndReplaceParams parameters)
    {
        var comparison = parameters.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        
        return parameters.SearchType.ToLowerInvariant() switch
        {
            "regex" => Regex.IsMatch(line, parameters.SearchPattern, 
                parameters.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase),
            "literal" => line.Contains(parameters.SearchPattern, comparison),
            "code" => line.Contains(parameters.SearchPattern, comparison), // TODO: Enhance with word boundaries
            _ => line.Contains(parameters.SearchPattern, comparison)
        };
    }

    private string[] GetContextLines(string[] allLines, int startIndex, int endIndex)
    {
        if (startIndex < 0 || endIndex >= allLines.Length || startIndex > endIndex)
            return Array.Empty<string>();

        var contextLines = new string[endIndex - startIndex + 1];
        Array.Copy(allLines, startIndex, contextLines, 0, contextLines.Length);
        return contextLines;
    }

    private async Task<List<ReplacementChange>> BuildReplacementChanges(
        List<LineMatch> matches,
        SearchAndReplaceParams parameters,
        CancellationToken cancellationToken)
    {
        var changes = new List<ReplacementChange>();
        
        // Group matches by file
        var matchesByFile = matches.GroupBy(m => m.FilePath);

        foreach (var fileGroup in matchesByFile)
        {
            var filePath = fileGroup.Key;
            
            try
            {
                var content = await File.ReadAllTextAsync(filePath, cancellationToken);
                var lines = content.Split('\n');

                foreach (var match in fileGroup)
                {
                    var lineIndex = match.LineNumber - 1; // Convert to 0-based
                    if (lineIndex < 0 || lineIndex >= lines.Length)
                        continue;

                    var originalLine = lines[lineIndex];
                    var change = BuildReplacementForLine(originalLine, parameters, match, filePath);
                    if (change != null)
                    {
                        changes.Add(change);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error building replacements for file {FilePath}", filePath);
            }
        }

        return changes;
    }

    private ReplacementChange? BuildReplacementForLine(
        string line, 
        SearchAndReplaceParams parameters, 
        LineMatch match, 
        string filePath)
    {
        // Use AdvancedPatternMatcher for enhanced matching capabilities
        string newLine;
        int columnStart = 0;
        int originalLength = 0;
        string matchedText = parameters.SearchPattern;

        // Handle regex separately as it has different logic
        if (parameters.SearchType.ToLowerInvariant() == "regex")
        {
            var regex = new Regex(parameters.SearchPattern, 
                parameters.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
            var regexMatch = regex.Match(line);
            if (!regexMatch.Success) return null;
            
            newLine = regex.Replace(line, parameters.ReplacePattern);
            columnStart = regexMatch.Index;
            originalLength = regexMatch.Length;
            matchedText = regexMatch.Value;
        }
        else
        {
            // Use AdvancedPatternMatcher for all other modes
            var matchResult = _patternMatcher.FindMatch(line, parameters.SearchPattern, parameters);
            if (!matchResult.Found) return null;

            // Perform the replacement using the advanced matcher
            newLine = _patternMatcher.PerformReplacement(line, parameters.SearchPattern, parameters.ReplacePattern, parameters);
            columnStart = matchResult.StartIndex;
            originalLength = matchResult.Length;
            matchedText = matchResult.MatchedText;

            // Log the matching mode used for debugging
            _logger.LogDebug("Used {MatchMode} matching for pattern '{Pattern}' in {FilePath}:{LineNumber}",
                matchResult.Mode, parameters.SearchPattern, filePath, match.LineNumber);
        }

        return new ReplacementChange
        {
            FilePath = filePath,
            LineNumber = match.LineNumber,
            ColumnStart = columnStart,
            OriginalLength = originalLength,
            OriginalText = matchedText, // Use the actual matched text, not the pattern
            ReplacementText = parameters.ReplacePattern,
            ModifiedLine = newLine?.TrimEnd('\r', '\n'),
            ContextBefore = match.ContextLines?.Take(parameters.ContextLines).ToArray(),
            ContextAfter = match.ContextLines?.Skip(parameters.ContextLines).ToArray()
        };
    }

    private async Task ApplyChangesToFiles(List<ReplacementChange> changes, CancellationToken cancellationToken)
    {
        // Group by file to minimize I/O
        var changesByFile = changes.GroupBy(c => c.FilePath);

        foreach (var fileGroup in changesByFile)
        {
            var filePath = fileGroup.Key;
            
            try
            {
                var lines = await File.ReadAllLinesAsync(filePath, cancellationToken);
                
                // Apply changes in reverse order to preserve line numbers
                foreach (var change in fileGroup.OrderByDescending(c => c.LineNumber))
                {
                    var lineIndex = change.LineNumber - 1; // Convert to 0-based
                    if (lineIndex >= 0 && lineIndex < lines.Length)
                    {
                        // Clean up any trailing carriage returns from ModifiedLine before writing
                        var cleanedLine = change.ModifiedLine?.TrimEnd('\r', '\n') ?? lines[lineIndex];
                        lines[lineIndex] = cleanedLine;
                        change.Applied = true;
                    }
                    else
                    {
                        change.Applied = false;
                        change.Error = $"Line {change.LineNumber} is out of range";
                    }
                }

                // Write the modified file
                await File.WriteAllLinesAsync(filePath, lines, cancellationToken);

                _logger.LogInformation("Applied {ChangeCount} changes to {FilePath}", 
                    fileGroup.Count(), filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to apply changes to {FilePath}", filePath);
                
                // Mark all changes in this file as failed
                foreach (var change in fileGroup)
                {
                    change.Applied = false;
                    change.Error = ex.Message;
                }
            }
        }
    }

    private SearchAndReplaceResult BuildResult(
        List<ReplacementChange> changes, 
        SearchAndReplaceParams parameters, 
        TimeSpan searchTime,
        TimeSpan? applyTime)
    {
        var fileSummaries = changes
            .GroupBy(c => c.FilePath)
            .Select(g => new FileChangeSummary
            {
                FilePath = ShortenPath(g.Key),
                ChangeCount = g.Count(),
                AllApplied = !parameters.Preview ? g.All(c => c.Applied == true) : null,
                LastModified = File.Exists(g.Key) ? File.GetLastWriteTime(g.Key) : null,
                FileSize = File.Exists(g.Key) ? new System.IO.FileInfo(g.Key).Length : null
            })
            .ToList();

        var totalFiles = fileSummaries.Count;
        var totalReplacements = changes.Count;
        var appliedCount = changes.Count(c => c.Applied == true);

        var summary = parameters.Preview
            ? $"Would change {totalReplacements} occurrences in {totalFiles} files"
            : $"Changed {appliedCount} of {totalReplacements} occurrences in {totalFiles} files";

        return new SearchAndReplaceResult
        {
            Summary = summary,
            Preview = parameters.Preview,
            Changes = changes,
            FileSummaries = fileSummaries,
            TotalFiles = totalFiles,
            TotalReplacements = totalReplacements,
            SearchTime = searchTime,
            ApplyTime = applyTime,
            SearchPattern = parameters.SearchPattern,
            ReplacePattern = parameters.ReplacePattern,
            Truncated = changes.Count >= parameters.MaxMatches,
            Insights = GenerateInsights(changes, parameters)
        };
    }

    private async Task<AIOptimizedResponse<SearchAndReplaceResult>> BuildNoMatchesResponse(
        SearchAndReplaceParams parameters, 
        TimeSpan searchTime)
    {
        var result = new SearchAndReplaceResult
        {
            Summary = "No matches found",
            Preview = parameters.Preview,
            Changes = new List<ReplacementChange>(),
            FileSummaries = new List<FileChangeSummary>(),
            TotalFiles = 0,
            TotalReplacements = 0,
            SearchTime = searchTime,
            SearchPattern = parameters.SearchPattern,
            ReplacePattern = parameters.ReplacePattern,
            Truncated = false,
            Insights = new List<string> { "No matches found - try broader search terms or different file patterns" }
        };

        var responseContext = new ResponseContext
        {
            TokenLimit = parameters.MaxTokens ?? 8000,
            ResponseMode = parameters.ResponseMode ?? "default",
            StoreFullResults = false,
            ToolName = Name
        };

        var responseBuilder = new SearchAndReplaceResponseBuilder(null, _storageService);
        return await responseBuilder.BuildResponseAsync(result, responseContext);
    }

    private List<string> GenerateInsights(List<ReplacementChange> changes, SearchAndReplaceParams parameters)
    {
        var insights = new List<string>();

        if (!changes.Any())
            return insights;

        // File type distribution
        var fileExtensions = changes
            .Select(c => Path.GetExtension(c.FilePath))
            .Where(ext => !string.IsNullOrEmpty(ext))
            .GroupBy(ext => ext)
            .OrderByDescending(g => g.Count())
            .Take(3)
            .Select(g => $"{g.Key}:{g.Count()}")
            .ToList();

        if (fileExtensions.Any())
        {
            insights.Add($"File types: {string.Join(", ", fileExtensions)}");
        }

        // High-frequency files
        var topFiles = changes
            .GroupBy(c => c.FilePath)
            .Where(g => g.Count() > 2)
            .OrderByDescending(g => g.Count())
            .Take(3)
            .Select(g => $"{Path.GetFileName(g.Key)} ({g.Count()} changes)")
            .ToList();

        if (topFiles.Any())
        {
            insights.Add($"High frequency: {string.Join(", ", topFiles)}");
        }

        // Safety reminder for non-preview mode
        if (!parameters.Preview)
        {
            insights.Add("Changes applied - files have been modified");
        }
        else
        {
            insights.Add("Preview mode - no files modified. Set preview:false to apply changes");
        }

        return insights;
    }

    private string ShortenPath(string fullPath)
    {
        // Convert to relative path or use just filename + parent directory
        try
        {
            var fileName = Path.GetFileName(fullPath);
            var directory = Path.GetFileName(Path.GetDirectoryName(fullPath));
            return string.IsNullOrEmpty(directory) ? fileName : $"{directory}/{fileName}";
        }
        catch
        {
            return Path.GetFileName(fullPath) ?? fullPath;
        }
    }
}
