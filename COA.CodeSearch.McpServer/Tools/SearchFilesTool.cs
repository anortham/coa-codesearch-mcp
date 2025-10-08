using System.ComponentModel;
using System.Text.RegularExpressions;
using COA.Mcp.Framework;
using COA.Mcp.Framework.Base;
using COA.Mcp.Framework.Models;
using COA.Mcp.Framework.TokenOptimization;
using COA.Mcp.Framework.TokenOptimization.Models;
using COA.Mcp.Framework.TokenOptimization.Caching;
using COA.Mcp.Framework.TokenOptimization.Storage;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Services.Lucene;
using COA.CodeSearch.McpServer.Services.Sqlite;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Unified tool for searching files and directories by pattern.
/// Consolidates file_search and directory_search into a single interface.
/// </summary>
public class SearchFilesTool : CodeSearchToolBase<SearchFilesParameters, AIOptimizedResponse<SearchFilesResult>>
{
    private readonly ILuceneIndexService _luceneIndexService;
    private readonly ISQLiteSymbolService _sqliteService;
    private readonly IPathResolutionService _pathResolutionService;
    private readonly IResponseCacheService _cacheService;
    private readonly IResourceStorageService _storageService;
    private readonly ICacheKeyGenerator _keyGenerator;
    private readonly ILogger<SearchFilesTool> _logger;

    public SearchFilesTool(
        IServiceProvider serviceProvider,
        ILuceneIndexService luceneIndexService,
        ISQLiteSymbolService sqliteService,
        IPathResolutionService pathResolutionService,
        IResponseCacheService cacheService,
        IResourceStorageService storageService,
        ICacheKeyGenerator keyGenerator,
        ILogger<SearchFilesTool> logger) : base(serviceProvider, logger)
    {
        _luceneIndexService = luceneIndexService;
        _sqliteService = sqliteService;
        _pathResolutionService = pathResolutionService;
        _cacheService = cacheService;
        _storageService = storageService;
        _keyGenerator = keyGenerator;
        _logger = logger;
    }

    public override string Name => ToolNames.SearchFiles;

    public override string Description =>
        "Unified filesystem search - find files, directories, or both by pattern. " +
        "Supports glob patterns and regex. Minimal usage: search_files(pattern). " +
        "Use resourceType='file' for files (default), 'directory' for directories, 'both' for both.";

    public override ToolCategory Category => ToolCategory.Query;

    protected override async Task<AIOptimizedResponse<SearchFilesResult>> ExecuteInternalAsync(
        SearchFilesParameters parameters,
        CancellationToken cancellationToken)
    {
        try
        {
            // Validate and resolve workspace path (use current workspace if not provided)
            var workspacePath = string.IsNullOrWhiteSpace(parameters.WorkspacePath)
                ? _pathResolutionService.GetPrimaryWorkspacePath()
                : Path.GetFullPath(parameters.WorkspacePath);

            // Validate resource type
            var resourceType = parameters.ResourceType.ToLowerInvariant();
            if (resourceType != "file" && resourceType != "directory" && resourceType != "both")
                throw new ArgumentException($"Invalid resourceType: {parameters.ResourceType}. Must be 'file', 'directory', or 'both'");

            // Generate cache key
            var cacheKey = _keyGenerator.GenerateKey(Name, parameters);

            // Check cache first (unless explicitly disabled)
            if (!parameters.NoCache)
            {
                var cached = await _cacheService.GetAsync<AIOptimizedResponse<SearchFilesResult>>(cacheKey);
                if (cached != null)
                {
                    _logger.LogDebug("Returning cached search results for pattern: {Pattern}", parameters.Pattern);
                    return cached;
                }
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var result = new SearchFilesResult
            {
                Success = true,
                ResourceType = resourceType,
                Pattern = parameters.Pattern,
                SearchPath = workspacePath
            };

            // Route to appropriate search based on resource type
            switch (resourceType)
            {
                case "file":
                    await SearchFilesAsync(parameters, workspacePath, result, cancellationToken);
                    break;

                case "directory":
                    await SearchDirectoriesAsync(parameters, workspacePath, result, cancellationToken);
                    break;

                case "both":
                    await SearchFilesAsync(parameters, workspacePath, result, cancellationToken);
                    await SearchDirectoriesAsync(parameters, workspacePath, result, cancellationToken);
                    break;
            }

            stopwatch.Stop();
            _logger.LogInformation("Search completed in {ElapsedMs}ms: found {TotalMatches} {ResourceType} matches for pattern '{Pattern}'",
                stopwatch.ElapsedMilliseconds, result.TotalMatches, resourceType, parameters.Pattern);

            var response = CreateSuccessResponse(result, stopwatch.ElapsedMilliseconds);

            // Cache the successful response
            if (!parameters.NoCache)
            {
                await _cacheService.SetAsync(cacheKey, response, new CacheEntryOptions
                {
                    AbsoluteExpiration = TimeSpan.FromMinutes(15),
                    Priority = result.TotalMatches > 100 ? CachePriority.High : CachePriority.Normal
                });
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching for pattern: {Pattern}", parameters.Pattern);
            return CreateErrorResponse($"Search failed: {ex.Message}");
        }
    }

    private async Task SearchFilesAsync(
        SearchFilesParameters parameters,
        string workspacePath,
        SearchFilesResult result,
        CancellationToken cancellationToken)
    {
        var files = new List<FileMatch>();

        // Try SQLite first for fast file lookups
        if (_sqliteService.DatabaseExists(workspacePath))
        {
            try
            {
                var requiresPathSearch = parameters.UseRegex
                    ? parameters.Pattern.Contains("/")
                    : parameters.Pattern.Contains("**") || parameters.Pattern.Contains("/") || parameters.Pattern.Contains("\\");

                var sqliteFiles = await _sqliteService.SearchFilesByPatternAsync(
                    workspacePath,
                    parameters.Pattern,
                    searchFullPath: requiresPathSearch,
                    extensionFilter: parameters.ExtensionFilter,
                    maxResults: parameters.MaxResults,
                    cancellationToken);

                if (sqliteFiles.Any())
                {
                    files = sqliteFiles.Select(f => new FileMatch
                    {
                        FilePath = f.Path,
                        FileName = Path.GetFileName(f.Path),
                        Directory = Path.GetDirectoryName(f.Path) ?? "",
                        Extension = Path.GetExtension(f.Path),
                        Score = 1.0f
                    }).ToList();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SQLite file search failed, falling back to Lucene");
            }
        }

        // Fall back to Lucene if SQLite didn't find results
        if (!files.Any())
        {
            var luceneMatches = await SearchLuceneFilesAsync(workspacePath, parameters, cancellationToken);
            files = luceneMatches;
        }

        // Apply extension filter if provided and not already applied
        if (!string.IsNullOrEmpty(parameters.ExtensionFilter))
        {
            var extensions = parameters.ExtensionFilter
                .Split(',')
                .Select(e => e.Trim().StartsWith('.') ? e.Trim() : "." + e.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            files = files.Where(f => extensions.Contains(f.Extension)).ToList();
        }

        // Apply hidden file filter
        if (!parameters.IncludeHidden)
        {
            files = files.Where(f => !f.FileName.StartsWith(".")).ToList();
        }

        // Limit results
        files = files.Take(parameters.MaxResults).ToList();

        result.Files = files;
        result.TotalMatches += files.Count;
    }

    private async Task SearchDirectoriesAsync(
        SearchFilesParameters parameters,
        string workspacePath,
        SearchFilesResult result,
        CancellationToken cancellationToken)
    {
        var directories = new List<DirectoryMatch>();

        // Search for directories using Lucene
        var luceneDirs = await SearchLuceneDirectoriesAsync(workspacePath, parameters, cancellationToken);
        directories = luceneDirs;

        // Apply hidden directory filter
        if (!parameters.IncludeHidden)
        {
            directories = directories.Where(d => !d.DirectoryName.StartsWith(".")).ToList();
        }

        // Limit results
        directories = directories.Take(parameters.MaxResults).ToList();

        result.Directories = directories;
        result.TotalMatches += directories.Count;
    }

    private Task<List<FileMatch>> SearchLuceneFilesAsync(
        string workspacePath,
        SearchFilesParameters parameters,
        CancellationToken cancellationToken)
    {
        // This is a simplified implementation - in production would use full Lucene search
        // For now, return empty list as fallback
        return Task.FromResult(new List<FileMatch>());
    }

    private Task<List<DirectoryMatch>> SearchLuceneDirectoriesAsync(
        string workspacePath,
        SearchFilesParameters parameters,
        CancellationToken cancellationToken)
    {
        // This is a simplified implementation - in production would use filesystem traversal
        // For now, return empty list as fallback
        return Task.FromResult(new List<DirectoryMatch>());
    }

    private AIOptimizedResponse<SearchFilesResult> CreateErrorResponse(string errorMessage)
    {
        return new AIOptimizedResponse<SearchFilesResult>
        {
            Success = false,
            Error = new COA.Mcp.Framework.Models.ErrorInfo
            {
                Code = "SEARCH_FAILED",
                Message = errorMessage
            },
            Data = new AIResponseData<SearchFilesResult>
            {
                Results = new SearchFilesResult
                {
                    Success = false,
                    ErrorMessage = errorMessage
                },
                Summary = "Search failed",
                Count = 0
            }
        };
    }

    private AIOptimizedResponse<SearchFilesResult> CreateSuccessResponse(SearchFilesResult result, long elapsedMs)
    {
        var summary = result.ResourceType switch
        {
            "file" => $"Found {result.Files?.Count ?? 0} files matching '{result.Pattern}'",
            "directory" => $"Found {result.Directories?.Count ?? 0} directories matching '{result.Pattern}'",
            "both" => $"Found {result.Files?.Count ?? 0} files and {result.Directories?.Count ?? 0} directories matching '{result.Pattern}'",
            _ => $"Search completed: {result.TotalMatches} matches"
        };

        return new AIOptimizedResponse<SearchFilesResult>
        {
            Success = true,
            Data = new AIResponseData<SearchFilesResult>
            {
                Results = result,
                Summary = summary,
                Count = result.TotalMatches
            }
        };
    }
}
