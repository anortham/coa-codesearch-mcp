using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using COA.Mcp.Framework;
using COA.Mcp.Framework.Base;
using COA.Mcp.Framework.Models;
using COA.CodeSearch.Next.McpServer.Services;
using COA.CodeSearch.Next.McpServer.Services.Lucene;
using COA.CodeSearch.Next.McpServer.Services.Analysis;
using Microsoft.Extensions.Logging;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Util;

namespace COA.CodeSearch.Next.McpServer.Tools;

/// <summary>
/// Tool for searching text content across indexed files
/// </summary>
public class TextSearchTool : McpToolBase<TextSearchParameters, TextSearchResult>
{
    private readonly ILuceneIndexService _luceneIndexService;
    private readonly IPathResolutionService _pathResolutionService;
    private readonly ILogger<TextSearchTool> _logger;

    public TextSearchTool(
        ILuceneIndexService luceneIndexService,
        IPathResolutionService pathResolutionService,
        ILogger<TextSearchTool> logger) : base(logger)
    {
        _luceneIndexService = luceneIndexService;
        _pathResolutionService = pathResolutionService;
        _logger = logger;
    }

    public override string Name => ToolNames.TextSearch;
    public override string Description => "Search for text content across all indexed files in a workspace. Supports full-text search with relevance scoring.";
    public override ToolCategory Category => ToolCategory.Query;

    protected override async Task<TextSearchResult> ExecuteInternalAsync(
        TextSearchParameters parameters,
        CancellationToken cancellationToken)
    {
        // Validate required parameters
        var query = ValidateRequired(parameters.Query, nameof(parameters.Query));
        var workspacePath = ValidateRequired(parameters.WorkspacePath, nameof(parameters.WorkspacePath));
        
        // Resolve to absolute path
        workspacePath = Path.GetFullPath(workspacePath);
        
        // Validate max results
        var maxResults = parameters.MaxResults ?? 50;
        maxResults = ValidateRange(maxResults, 1, 500, nameof(parameters.MaxResults));
        
        try
        {
            // Check if index exists
            if (!await _luceneIndexService.IndexExistsAsync(workspacePath, cancellationToken))
            {
                return new TextSearchResult
                {
                    Success = false,
                    Error = CreateValidationErrorResult(
                        ToolNames.TextSearch,
                        nameof(parameters.WorkspacePath),
                        $"No index found for workspace: {workspacePath}. Run index_workspace first."
                    ),
                    Query = query,
                    WorkspacePath = workspacePath,
                    Matches = new List<TextSearchMatch>(),
                    TotalMatches = 0
                };
            }
            
            // Parse the query string into a Lucene Query
            var analyzer = new CodeAnalyzer(LuceneVersion.LUCENE_48);
            var queryParser = new QueryParser(LuceneVersion.LUCENE_48, "content", analyzer);
            Lucene.Net.Search.Query luceneQuery;
            
            try
            {
                luceneQuery = queryParser.Parse(query);
            }
            catch (ParseException ex)
            {
                return new TextSearchResult
                {
                    Success = false,
                    Error = CreateValidationErrorResult(
                        ToolNames.TextSearch,
                        nameof(parameters.Query),
                        $"Invalid query syntax: {ex.Message}"
                    ),
                    Query = query,
                    WorkspacePath = workspacePath,
                    Matches = new List<TextSearchMatch>(),
                    TotalMatches = 0
                };
            }
            
            // Perform search
            var searchResult = await _luceneIndexService.SearchAsync(
                workspacePath,
                luceneQuery,
                maxResults,
                cancellationToken);
            
            // Convert search results to our format with content truncation
            const int maxContentLength = 500; // Limit content to prevent token overflow
            var matches = searchResult.Hits.Select(r => new TextSearchMatch
            {
                FilePath = r.FilePath,
                Score = r.Score,
                LineNumber = r.Fields.ContainsKey("lineNumber") && int.TryParse(r.Fields["lineNumber"], out var ln) ? ln : null,
                Content = TruncateContent(r.Content ?? string.Empty, maxContentLength),
                Highlights = r.HighlightedFragments ?? new List<string>()
            }).ToList();
            
            var result = new TextSearchResult
            {
                Success = true,
                Query = query,
                WorkspacePath = workspacePath,
                WorkspaceHash = _pathResolutionService.ComputeWorkspaceHash(workspacePath),
                Matches = matches,
                TotalMatches = searchResult.TotalHits,
                SearchDuration = searchResult.SearchTime,
                IndexPath = _pathResolutionService.GetIndexPath(workspacePath)
            };
            
            // Add insights for AI optimization
            result.Insights = GenerateSearchInsights(matches, searchResult.TotalHits);
            
            // Add suggested actions
            result.Actions = GenerateSearchActions(query, matches, searchResult.TotalHits);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing text search for query '{Query}' in workspace '{Workspace}'", 
                query, workspacePath);
            
            return new TextSearchResult
            {
                Success = false,
                Error = CreateErrorResult(ToolNames.TextSearch, ex.Message),
                Query = query,
                WorkspacePath = workspacePath,
                Matches = new List<TextSearchMatch>(),
                TotalMatches = 0
            };
        }
    }
    
    private string TruncateContent(string content, int maxLength)
    {
        if (string.IsNullOrEmpty(content) || content.Length <= maxLength)
            return content;
            
        // Try to truncate at a word boundary
        var truncated = content.Substring(0, maxLength);
        var lastSpace = truncated.LastIndexOf(' ');
        
        if (lastSpace > maxLength * 0.8) // If we have a space in the last 20%
        {
            truncated = truncated.Substring(0, lastSpace);
        }
        
        return truncated + "...";
    }
    
    private List<string> GenerateSearchInsights(List<TextSearchMatch> matches, int totalHits)
    {
        var insights = new List<string>();
        
        if (totalHits == 0)
        {
            insights.Add("No matches found. Consider broadening your search query or checking if the workspace is indexed.");
        }
        else if (totalHits == 1)
        {
            insights.Add("Found exactly one match.");
        }
        else if (totalHits > matches.Count)
        {
            insights.Add($"Showing top {matches.Count} of {totalHits} total matches.");
            insights.Add("Use more specific search terms to narrow results.");
        }
        
        // Analyze file distribution
        var fileGroups = matches.GroupBy(m => Path.GetExtension(m.FilePath)).OrderByDescending(g => g.Count()).ToList();
        if (fileGroups.Count > 1)
        {
            var topExtension = fileGroups.First();
            insights.Add($"Most matches found in {topExtension.Key} files ({topExtension.Count()} matches).");
        }
        
        // Analyze score distribution
        if (matches.Count > 0)
        {
            var topScore = matches.First().Score;
            var avgScore = matches.Average(m => m.Score);
            if (topScore > avgScore * 2)
            {
                insights.Add($"Top result has significantly higher relevance (score: {topScore:F2}).");
            }
        }
        
        return insights;
    }
    
    private List<AIAction> GenerateSearchActions(string query, List<TextSearchMatch> matches, int totalHits)
    {
        var actions = new List<AIAction>();
        
        if (totalHits == 0)
        {
            actions.Add(new AIAction
            {
                Action = "mcp__codesearch-next__index_workspace",
                Description = "Index the workspace to enable searching",
                Rationale = "No search results were found, suggesting the workspace may not be indexed"
            });
        }
        else if (totalHits > matches.Count)
        {
            actions.Add(new AIAction
            {
                Action = "mcp__codesearch-next__text_search",
                Description = $"Refine search with more specific terms (e.g., add file type to '{query}')",
                Rationale = $"Found {totalHits} total matches but showing only {matches.Count}. More specific search terms could help narrow results.",
                Parameters = new Dictionary<string, object>
                {
                    ["query"] = $"{query} ext:cs"  // Example refinement
                }
            });
        }
        
        if (matches.Count > 0)
        {
            var topMatch = matches.First();
            actions.Add(new AIAction
            {
                Action = "read_file",
                Description = $"Read the most relevant file: {Path.GetFileName(topMatch.FilePath)}",
                Rationale = $"This file has the highest relevance score ({topMatch.Score:F2}) for your search",
                Parameters = new Dictionary<string, object>
                {
                    ["path"] = topMatch.FilePath,
                    ["line_offset"] = topMatch.LineNumber ?? 1
                }
            });
        }
        
        return actions;
    }
}

/// <summary>
/// Parameters for text search operation
/// </summary>
public class TextSearchParameters
{
    [Required]
    [Description("The search query string")]
    public string Query { get; set; } = string.Empty;
    
    [Required]
    [Description("Path to the workspace directory to search")]
    public string WorkspacePath { get; set; } = string.Empty;
    
    [Range(1, 500)]
    [Description("Maximum number of results to return (default: 50, max: 500)")]
    public int? MaxResults { get; set; }
}

/// <summary>
/// Result of text search operation
/// </summary>
public class TextSearchResult : ToolResultBase
{
    public override string Operation => ToolNames.TextSearch;
    
    public string Query { get; set; } = string.Empty;
    public string WorkspacePath { get; set; } = string.Empty;
    public string WorkspaceHash { get; set; } = string.Empty;
    public string IndexPath { get; set; } = string.Empty;
    public List<TextSearchMatch> Matches { get; set; } = new();
    public int TotalMatches { get; set; }
    public TimeSpan? SearchDuration { get; set; }
}

/// <summary>
/// Represents a single text search match
/// </summary>
public class TextSearchMatch
{
    public string FilePath { get; set; } = string.Empty;
    public float Score { get; set; }
    public int? LineNumber { get; set; }
    public string Content { get; set; } = string.Empty;
    public List<string> Highlights { get; set; } = new();
}