using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Services.Analysis;
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
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Util;
using DiffMatchPatch;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Enhanced search and replace tool using DiffMatchPatch and workspace permissions.
/// Fixes multi-line matching issues and provides reliable concurrency protection.
/// </summary>
public class SearchAndReplaceTool : CodeSearchToolBase<SearchAndReplaceParams, AIOptimizedResponse<SearchAndReplaceResult>>
{
    private readonly ILuceneIndexService _indexService;
    private readonly SmartQueryPreprocessor _queryProcessor;
    private readonly IResourceStorageService _storageService;
    private readonly CodeAnalyzer _codeAnalyzer;
    private readonly UnifiedFileEditService _editService;
    private readonly IWorkspacePermissionService _permissionService;
    private readonly ILogger<SearchAndReplaceTool> _logger;

    /// <summary>
    /// Initializes a new instance of the SearchAndReplaceTool with required dependencies.
    /// </summary>
    /// <param name="serviceProvider">Service provider for dependency resolution</param>
    /// <param name="indexService">Lucene index service for search operations</param>
    /// <param name="queryProcessor">Smart query preprocessing service</param>
    /// <param name="storageService">Resource storage service</param>
    /// <param name="codeAnalyzer">Code analysis service</param>
    /// <param name="editService">Unified file edit service</param>
    /// <param name="permissionService">Workspace permission service</param>
    /// <param name="logger">Logger instance</param>
    public SearchAndReplaceTool(
        IServiceProvider serviceProvider,
        ILuceneIndexService indexService,
        SmartQueryPreprocessor queryProcessor,
        IResourceStorageService storageService,
        CodeAnalyzer codeAnalyzer,
        UnifiedFileEditService editService,
        IWorkspacePermissionService permissionService,
        ILogger<SearchAndReplaceTool> logger) : base(serviceProvider, logger)
    {
        _indexService = indexService;
        _queryProcessor = queryProcessor;
        _storageService = storageService;
        _codeAnalyzer = codeAnalyzer;
        _editService = editService;
        _permissionService = permissionService;
        _logger = logger;
    }

    /// <summary>
    /// Gets the tool name identifier.
    /// </summary>
    public override string Name => ToolNames.SearchAndReplace;

    /// <summary>
    /// Gets the tool description explaining its purpose and usage scenarios.
    /// </summary>
    public override string Description =>
        "BULK updates across files - Replace patterns everywhere at once. SAFER than manual edits - preview mode by default. " +
        "Perfect for: renaming, refactoring, fixing patterns. Consolidates search→read→edit workflow. " +
        "Enhanced with multi-line support, workspace safety, and DiffMatchPatch reliability.";

    /// <summary>
    /// Gets the tool category for classification purposes.
    /// </summary>
    public override ToolCategory Category => ToolCategory.Query;

    /// <summary>
    /// Executes the search and replace operation across multiple files.
    /// </summary>
    /// <param name="parameters">Search and replace parameters including patterns and options</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>Search and replace results with affected files and change summaries</returns>
    protected override async Task<AIOptimizedResponse<SearchAndReplaceResult>> ExecuteInternalAsync(
        SearchAndReplaceParams parameters,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var searchStartTime = DateTime.UtcNow;

        try
        {
            // Validate parameters
            if (string.IsNullOrWhiteSpace(parameters.SearchPattern))
                return CreateErrorResponse("Search pattern cannot be empty");

            // Use workspace path directly
            var workspacePath = parameters.WorkspacePath ?? Directory.GetCurrentDirectory();
            
            // Check if index exists
            if (!await _indexService.IndexExistsAsync(workspacePath, cancellationToken))
            {
                return CreateErrorResponse($"No index found for workspace: {workspacePath}. Please run index_workspace first.");
            }

            // Check workspace permissions for editing operations
            if (!parameters.Preview)
            {
                var permissionRequest = new EditPermissionRequest();
                var permissionResult = await _permissionService.IsEditAllowedAsync(permissionRequest, cancellationToken);
                if (!permissionResult.Allowed)
                {
                    return CreateErrorResponse($"Edit not allowed: {permissionResult.Reason}");
                }
            }

            // Extract simple search term for Lucene file discovery
            var luceneSearchTerm = ExtractLuceneSearchTerm(parameters.SearchPattern, parameters.SearchType);
            
            // Build Lucene query for finding candidate files
            var queryBuilder = new QueryParser(LuceneVersion.LUCENE_48, "content", new StandardAnalyzer(LuceneVersion.LUCENE_48));
            queryBuilder.AllowLeadingWildcard = true;
            
            Lucene.Net.Search.Query query;
            try
            {
            query = queryBuilder.Parse(luceneSearchTerm);
            }
            catch (Exception ex)
            {
            // Fallback to wildcard search if Lucene parsing fails
            _logger.LogWarning("Failed to parse Lucene query '{LuceneSearchTerm}': {Error}. Using fallback search.", luceneSearchTerm, ex.Message);
            query = queryBuilder.Parse("*"); // Search all files as fallback
            }

            // Search for matching files
            var searchResults = await _indexService.SearchAsync(workspacePath, query, parameters.MaxMatches, cancellationToken);
            var searchTime = DateTime.UtcNow - searchStartTime;
            
            if (searchResults.TotalHits == 0)
            {
                return CreateSuccessResponse(new SearchAndReplaceResult
                {
                    Summary = "No matches found for the search pattern",
                    Preview = parameters.Preview,
                    Changes = new List<ReplacementChange>(),
                    FileSummaries = new List<FileChangeSummary>(),
                    TotalFiles = 0,
                    TotalReplacements = 0,
                    SearchTime = searchTime,
                    SearchPattern = parameters.SearchPattern,
                    ReplacePattern = parameters.ReplacePattern ?? string.Empty,
                    Truncated = false
                });
            }

            // Process each file that had matches
            var changes = new List<ReplacementChange>();
            var fileSummaries = new Dictionary<string, FileChangeSummary>();
            int totalReplacements = 0;
            var applyStartTime = DateTime.UtcNow;

            foreach (var hit in searchResults.Hits.Take(parameters.MaxMatches))
            {
                var filePath = hit.FilePath;
                
                try
                {
                    // Setup edit options
                    var editOptions = new EditOptions
                    {
                        PreviewMode = parameters.Preview,
                        MatchMode = parameters.MatchMode ?? "literal",
                        CaseSensitive = parameters.CaseSensitive,
                        ContextLines = parameters.ContextLines,
                        FuzzyThreshold = parameters.FuzzyThreshold,
                        FuzzyDistance = parameters.FuzzyDistance
                    };

                    // Apply search and replace using UnifiedFileEditService
                    var editResult = await _editService.ApplySearchReplaceAsync(
                        filePath,
                        parameters.SearchPattern,
                        parameters.ReplacePattern ?? string.Empty,
                        editOptions,
                        cancellationToken);

                    if (editResult.Success && editResult.ChangesMade && editResult.Diffs != null)
                    {
                        // Convert DiffMatchPatch diffs to our model format
                        var fileChanges = ConvertDiffsToReplacementChanges(editResult.Diffs, filePath, parameters.ContextLines);
                        changes.AddRange(fileChanges);
                        totalReplacements += fileChanges.Count;

                        // Create file summary
                        var fileSummary = new FileChangeSummary
                        {
                            FilePath = filePath,
                            ChangeCount = fileChanges.Count,
                            LastModified = File.GetLastWriteTimeUtc(filePath),
                            FileSize = new System.IO.FileInfo(filePath).Length,
                            AllApplied = !parameters.Preview
                        };
                        fileSummaries[filePath] = fileSummary;

                        _logger.LogDebug("Applied {Count} replacements in {File}", 
                            fileChanges.Count, filePath);
                    }
                    else if (editResult.Success && !editResult.ChangesMade)
                    {
                        // File was processed but no changes were needed
                        _logger.LogDebug("No changes needed in {File}", filePath);
                    }
                    else if (!editResult.Success)
                    {
                        // Handle failed operations
                        var errorChange = new ReplacementChange
                        {
                            FilePath = filePath,
                            LineNumber = 1,
                            ColumnStart = 0,
                            OriginalLength = 0,
                            OriginalText = "",
                            ReplacementText = "",
                            Applied = false,
                            Error = editResult.ErrorMessage
                        };
                        changes.Add(errorChange);
                        
                        _logger.LogWarning("Failed to process {File}: {Error}", filePath, editResult.ErrorMessage);
                    }
                }
                catch (Exception ex)
                {
                    var errorChange = new ReplacementChange
                    {
                        FilePath = filePath,
                        LineNumber = 1,
                        ColumnStart = 0,
                        OriginalLength = 0,
                        OriginalText = "",
                        ReplacementText = "",
                        Applied = false,
                        Error = ex.Message
                    };
                    changes.Add(errorChange);
                    
                    _logger.LogError(ex, "Exception processing {File}", filePath);
                }
            }

            var applyTime = parameters.Preview ? null : (TimeSpan?)(DateTime.UtcNow - applyStartTime);
            stopwatch.Stop();

            var result = new SearchAndReplaceResult
            {
                Summary = GenerateResultSummary(fileSummaries.Values.ToList(), totalReplacements, parameters.Preview),
                Preview = parameters.Preview,
                Changes = changes,
                FileSummaries = fileSummaries.Values.ToList(),
                TotalFiles = fileSummaries.Count,
                TotalReplacements = totalReplacements,
                SearchTime = searchTime,
                ApplyTime = applyTime,
                SearchPattern = parameters.SearchPattern,
                ReplacePattern = parameters.ReplacePattern ?? string.Empty,
                Truncated = searchResults.TotalHits > parameters.MaxMatches
            };

            var successCount = fileSummaries.Count;
            _logger.LogInformation("Search and replace completed: {Success}/{Total} files processed, {Replacements} total replacements in {Time}ms",
                successCount, fileSummaries.Count, totalReplacements, stopwatch.ElapsedMilliseconds);

            return CreateSuccessResponse(result);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Search and replace failed after {Time}ms", stopwatch.ElapsedMilliseconds);
            return CreateErrorResponse($"Search and replace operation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Converts DiffMatchPatch diffs to ReplacementChange objects
    /// </summary>
    private List<ReplacementChange> ConvertDiffsToReplacementChanges(List<Diff> diffs, string filePath, int contextLines)
    {
        var changes = new List<ReplacementChange>();
        var lineNumber = 1;
        var columnPosition = 0;

        for (int i = 0; i < diffs.Count; i++)
        {
            var diff = diffs[i];
            
            if (diff.operation == Operation.DELETE && i + 1 < diffs.Count && diffs[i + 1].operation == Operation.INSERT)
            {
                // This is a replacement operation
                var deleteText = diff.text ?? "";
                var insertText = diffs[i + 1].text ?? "";
                
                var change = new ReplacementChange
                {
                    FilePath = filePath,
                    LineNumber = lineNumber,
                    ColumnStart = columnPosition,
                    OriginalLength = deleteText.Length,
                    OriginalText = deleteText,
                    ReplacementText = insertText,
                    Applied = true // Will be set correctly based on preview mode
                };
                
                changes.Add(change);
                i++; // Skip the next INSERT diff since we've processed it
                
                // Update position after replacement
                var newlineCount = insertText.Count(c => c == '\n');
                if (newlineCount > 0)
                {
                    lineNumber += newlineCount;
                    columnPosition = insertText.Length - insertText.LastIndexOf('\n') - 1;
                }
                else
                {
                    columnPosition += insertText.Length;
                }
            }
            else if (diff.operation == Operation.EQUAL)
            {
                // Update position for unchanged text
                var text = diff.text ?? "";
                var newlineCount = text.Count(c => c == '\n');
                if (newlineCount > 0)
                {
                    lineNumber += newlineCount;
                    columnPosition = text.Length - text.LastIndexOf('\n') - 1;
                }
                else
                {
                    columnPosition += text.Length;
                }
            }
        }

        return changes;
    }

    /// <summary>
    /// Generates a human-readable summary of the operation results
    /// </summary>
    private string GenerateResultSummary(List<FileChangeSummary> fileSummaries, int totalReplacements, bool preview)
    {
        var mode = preview ? "Found" : "Applied";
        var fileCount = fileSummaries.Count;
        var summary = $"{mode} {totalReplacements} replacement{(totalReplacements == 1 ? "" : "s")} across {fileCount} file{(fileCount == 1 ? "" : "s")}";
        
        var errorCount = fileSummaries.Count(f => f.AllApplied == false);
        if (errorCount > 0)
            summary += $" ({errorCount} file{(errorCount == 1 ? "" : "s")} had errors)";
            
        return summary;
    }

    private AIOptimizedResponse<SearchAndReplaceResult> CreateErrorResponse(string errorMessage)
    {
        return new AIOptimizedResponse<SearchAndReplaceResult>
        {
            Success = false,
            Error = new COA.Mcp.Framework.Models.ErrorInfo
            {
                Code = "SEARCH_REPLACE_FAILED",
                Message = errorMessage
            },
            Data = new AIResponseData<SearchAndReplaceResult>
            {
                Results = new SearchAndReplaceResult
                {
                    Summary = "Search and replace failed",
                    Preview = true,
                    Changes = new List<ReplacementChange>(),
                    FileSummaries = new List<FileChangeSummary>(),
                    TotalFiles = 0,
                    TotalReplacements = 0,
                    SearchTime = TimeSpan.Zero,
                    SearchPattern = "",
                    ReplacePattern = "",
                    Truncated = false
                },
                Summary = "Search and replace operation failed",
                Count = 0
            }
        };
    }

    private AIOptimizedResponse<SearchAndReplaceResult> CreateSuccessResponse(SearchAndReplaceResult result)
    {
        return new AIOptimizedResponse<SearchAndReplaceResult>
        {
            Success = true,
            Data = new AIResponseData<SearchAndReplaceResult>
            {
                Results = result,
                Summary = result.Summary,
                Count = result.TotalReplacements
            }
        };
    }
        /// <summary>
        /// Extracts a simple Lucene-compatible search term from complex search patterns.
        /// This is used to find candidate files that might contain matches, not for the actual replacement.
        /// </summary>
        private string ExtractLuceneSearchTerm(string searchPattern, string? searchType)
        {
            if (string.IsNullOrWhiteSpace(searchPattern))
                return "*";

            // For literal searches, use a simplified version of the pattern
            if (searchType == "literal")
            {
                // Extract the most significant words for Lucene search
                var words = searchPattern
                    .Split(new[] { ' ', '\t', '\n', '\r', '(', ')', '{', '}', '[', ']', ';', ',', '.', '!', '?' }, 
                           StringSplitOptions.RemoveEmptyEntries)
                    .Where(w => w.Length >= 3) // Only meaningful words
                    .Take(3) // Limit to first 3 words to avoid complex queries
                    .ToList();

                if (words.Count > 0)
                {
                    return string.Join(" AND ", words.Select(w => $"\"{w}\""));
                }
            }

            // For regex searches, extract literal parts
            if (searchType == "regex")
            {
                var literalParts = ExtractLiteralPartsFromRegex(searchPattern);
                if (literalParts.Any())
                {
                    return string.Join(" AND ", literalParts.Take(3).Select(p => $"\"{p}\""));
                }
            }

            // Fallback: try to extract any alphanumeric sequences
            var matches = Regex.Matches(searchPattern, @"\b[a-zA-Z_][a-zA-Z0-9_]{2,}\b")
                              .Cast<Match>()
                              .Select(m => m.Value)
                              .Where(v => !IsCommonWord(v))
                              .Distinct()
                              .Take(3)
                              .ToList();

            if (matches.Any())
            {
                return string.Join(" AND ", matches.Select(m => $"\"{m}\""));
            }

            // Ultimate fallback: search all files
            return "*";
        }

        /// <summary>
        /// Extracts literal parts from regex patterns for Lucene search
        /// </summary>
        private List<string> ExtractLiteralPartsFromRegex(string regexPattern)
        {
            var literalParts = new List<string>();
        
            try
            {
                // Remove common regex metacharacters and extract literal sequences
                var cleaned = regexPattern
                    .Replace(@"\s*", " ")
                    .Replace(@"\s+", " ")
                    .Replace(@"\.", ".")
                    .Replace(@"\(", "(")
                    .Replace(@"\)", ")")
                    .Replace(@"\\", @"\");

                // Extract meaningful literal sequences (3+ characters)
                var matches = Regex.Matches(cleaned, @"[a-zA-Z_][a-zA-Z0-9_]{2,}")
                                  .Cast<Match>()
                                  .Select(m => m.Value)
                                  .Where(v => !IsCommonWord(v))
                                  .Distinct()
                                  .ToList();

                literalParts.AddRange(matches);
            }
            catch
            {
                // If regex parsing fails, fall back to simple word extraction
            }

            return literalParts;
        }

        /// <summary>
        /// Checks if a word is too common to be useful for search
        /// </summary>
        private bool IsCommonWord(string word)
        {
            var commonWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "the", "and", "for", "are", "but", "not", "you", "all", "can", "had", "her", "was", "one", "our", "out", "day", "get", "use", "man", "new", "now", "old", "see", "way", "who", "boy", "did", "its", "let", "put", "say", "she", "too", "use"
            };
        
            return commonWords.Contains(word);
        }
}