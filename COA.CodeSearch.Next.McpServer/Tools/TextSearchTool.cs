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

    public override string Name => "text_search";
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
                        "text_search",
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
                        "text_search",
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
            
            // Convert search results to our format
            var matches = searchResult.Hits.Select(r => new TextSearchMatch
            {
                FilePath = r.FilePath,
                Score = r.Score,
                LineNumber = r.Fields.ContainsKey("lineNumber") && int.TryParse(r.Fields["lineNumber"], out var ln) ? ln : null,
                Content = r.Content ?? string.Empty,
                Highlights = r.HighlightedFragments ?? new List<string>()
            }).ToList();
            
            return new TextSearchResult
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing text search for query '{Query}' in workspace '{Workspace}'", 
                query, workspacePath);
            
            return new TextSearchResult
            {
                Success = false,
                Error = CreateErrorResult("text_search", ex.Message),
                Query = query,
                WorkspacePath = workspacePath,
                Matches = new List<TextSearchMatch>(),
                TotalMatches = 0
            };
        }
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
    public override string Operation => "text_search";
    
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