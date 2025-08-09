using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using COA.Mcp.Framework;
using COA.Mcp.Framework.Base;
using COA.Mcp.Framework.Models;
using COA.CodeSearch.Next.McpServer.Services;
using COA.CodeSearch.Next.McpServer.Services.Lucene;
using COA.CodeSearch.Next.McpServer.Services.Analysis;
using Microsoft.Extensions.Logging;
using Lucene.Net.Search;
using Lucene.Net.Index;
using Lucene.Net.Util;

namespace COA.CodeSearch.Next.McpServer.Tools;

/// <summary>
/// Tool for searching files by name pattern in indexed workspaces
/// </summary>
public class FileSearchTool : McpToolBase<FileSearchParameters, FileSearchResult>
{
    private readonly ILuceneIndexService _luceneIndexService;
    private readonly IPathResolutionService _pathResolutionService;
    private readonly ILogger<FileSearchTool> _logger;

    public FileSearchTool(
        ILuceneIndexService luceneIndexService,
        IPathResolutionService pathResolutionService,
        ILogger<FileSearchTool> logger) : base(logger)
    {
        _luceneIndexService = luceneIndexService;
        _pathResolutionService = pathResolutionService;
        _logger = logger;
    }

    public override string Name => ToolNames.FileSearch;
    public override string Description => "Search for files by name pattern in indexed workspaces";
    public override ToolCategory Category => ToolCategory.Query;

    protected override async Task<FileSearchResult> ExecuteInternalAsync(
        FileSearchParameters parameters,
        CancellationToken cancellationToken)
    {
        // Validate required parameters
        var pattern = ValidateRequired(parameters.Pattern, nameof(parameters.Pattern));
        var workspacePath = ValidateRequired(parameters.WorkspacePath, nameof(parameters.WorkspacePath));
        
        // Resolve to absolute path
        workspacePath = Path.GetFullPath(workspacePath);
        
        // Validate max results
        var maxResults = parameters.MaxResults ?? 100;
        maxResults = ValidateRange(maxResults, 1, 500, nameof(parameters.MaxResults));
        
        try
        {
            // Check if index exists
            if (!await _luceneIndexService.IndexExistsAsync(workspacePath, cancellationToken))
            {
                return new FileSearchResult
                {
                    Success = false,
                    Error = CreateValidationErrorResult(
                        ToolNames.FileSearch,
                        nameof(parameters.WorkspacePath),
                        $"No index found for workspace: {workspacePath}. Run index_workspace first."
                    ),
                    Files = new List<FileSearchMatch>(),
                    TotalMatches = 0
                };
            }
            
            // Create a MatchAllDocsQuery to get all indexed files
            // We'll filter by pattern in memory since filename patterns are complex
            var query = new MatchAllDocsQuery();
            
            var searchResult = await _luceneIndexService.SearchAsync(
                workspacePath,
                query,
                maxResults * 2,  // Get extra to account for filtering
                cancellationToken
            );
            
            // Process results and filter by pattern
            var files = new List<FileSearchMatch>();
            var useRegex = parameters.UseRegex ?? false;
            var regex = useRegex ? new Regex(pattern, RegexOptions.IgnoreCase) : null;
            var globPattern = useRegex ? null : ConvertGlobToRegex(pattern);
            
            foreach (var hit in searchResult.Hits)
            {
                var filePath = hit.FilePath;
                if (!string.IsNullOrEmpty(filePath))
                {
                    var fileName = Path.GetFileName(filePath);
                    
                    // Check if filename matches pattern
                    bool matches = useRegex 
                        ? regex!.IsMatch(fileName)
                        : globPattern!.IsMatch(fileName);
                    
                    if (matches)
                    {
                        files.Add(new FileSearchMatch
                        {
                            FilePath = filePath,
                            FileName = fileName,
                            Directory = Path.GetDirectoryName(filePath) ?? "",
                            Extension = Path.GetExtension(filePath)
                        });
                    }
                }
            }
            
            // Apply extension filter if provided
            if (!string.IsNullOrEmpty(parameters.ExtensionFilter))
            {
                var extensions = parameters.ExtensionFilter
                    .Split(',')
                    .Select(e => e.Trim().StartsWith('.') ? e.Trim() : "." + e.Trim())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    
                files = files.Where(f => extensions.Contains(f.Extension)).ToList();
            }
            
            // Sort by file name
            files = files.OrderBy(f => f.FileName).ThenBy(f => f.Directory).ToList();
            
            // Include directories if requested
            List<string>? directories = null;
            if (parameters.IncludeDirectories ?? false)
            {
                directories = files
                    .Select(f => f.Directory)
                    .Distinct()
                    .OrderBy(d => d)
                    .ToList();
            }
            
            return new FileSearchResult
            {
                Success = true,
                Files = files.Take(maxResults).ToList(),
                Directories = directories,
                TotalMatches = files.Count
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching for files");
            return new FileSearchResult
            {
                Success = false,
                Error = new ErrorInfo
                {
                    Code = "EXECUTION_ERROR",
                    Message = $"Error searching for files: {ex.Message}"
                },
                Files = new List<FileSearchMatch>(),
                TotalMatches = 0
            };
        }
    }
    
    private Regex ConvertGlobToRegex(string pattern)
    {
        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        return new Regex(regexPattern, RegexOptions.IgnoreCase);
    }
}

/// <summary>
/// Parameters for the FileSearch tool
/// </summary>
public class FileSearchParameters
{
    /// <summary>
    /// Path to the workspace directory to search
    /// </summary>
    [Required]
    [Description("Path to the workspace directory to search")]
    public string WorkspacePath { get; set; } = string.Empty;

    /// <summary>
    /// The search pattern (glob or regex)
    /// </summary>
    [Required]
    [Description("The search pattern (glob or regex)")]
    public string Pattern { get; set; } = string.Empty;

    /// <summary>
    /// Maximum number of results to return (default: 100, max: 500)
    /// </summary>
    [Description("Maximum number of results to return (default: 100, max: 500)")]
    public int? MaxResults { get; set; }

    /// <summary>
    /// Use regular expression instead of glob pattern
    /// </summary>
    [Description("Use regular expression instead of glob pattern")]
    public bool? UseRegex { get; set; }

    /// <summary>
    /// Include list of matching directories
    /// </summary>
    [Description("Include list of matching directories")]
    public bool? IncludeDirectories { get; set; }

    /// <summary>
    /// Comma-separated list of file extensions to filter (e.g., ".cs,.js")
    /// </summary>
    [Description("Comma-separated list of file extensions to filter (e.g., '.cs,.js')")]
    public string? ExtensionFilter { get; set; }
}

/// <summary>
/// Result from the FileSearch tool
/// </summary>
public class FileSearchResult : ToolResultBase
{
    public override string Operation => ToolNames.FileSearch;
    
    /// <summary>
    /// List of matching files
    /// </summary>
    public List<FileSearchMatch> Files { get; set; } = new();
    
    /// <summary>
    /// List of unique directories containing matches (if requested)
    /// </summary>
    public List<string>? Directories { get; set; }
    
    /// <summary>
    /// Total number of matches found
    /// </summary>
    public int TotalMatches { get; set; }
}

/// <summary>
/// Information about a matching file
/// </summary>
public class FileSearchMatch
{
    /// <summary>
    /// Full path to the file
    /// </summary>
    public string FilePath { get; set; } = string.Empty;
    
    /// <summary>
    /// File name without path
    /// </summary>
    public string FileName { get; set; } = string.Empty;
    
    /// <summary>
    /// Directory containing the file
    /// </summary>
    public string Directory { get; set; } = string.Empty;
    
    /// <summary>
    /// File extension
    /// </summary>
    public string Extension { get; set; } = string.Empty;
}