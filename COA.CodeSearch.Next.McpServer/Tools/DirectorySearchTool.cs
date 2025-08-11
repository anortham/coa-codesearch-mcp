using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using COA.Mcp.Framework;
using COA.Mcp.Framework.Base;
using COA.Mcp.Framework.Models;
using COA.Mcp.Framework.TokenOptimization;
using COA.Mcp.Framework.TokenOptimization.Models;
using COA.Mcp.Framework.TokenOptimization.Caching;
using COA.Mcp.Framework.TokenOptimization.Storage;
using COA.Mcp.Framework.TokenOptimization.ResponseBuilders;
using COA.CodeSearch.Next.McpServer.Services;
using COA.CodeSearch.Next.McpServer.Services.Lucene;
using COA.CodeSearch.Next.McpServer.Models;
using FrameworkErrorInfo = COA.Mcp.Framework.Models.ErrorInfo;
using FrameworkRecoveryInfo = COA.Mcp.Framework.Models.RecoveryInfo;
using COA.CodeSearch.Next.McpServer.ResponseBuilders;
using COA.CodeSearch.Next.McpServer.Tools.Parameters;
using COA.CodeSearch.Next.McpServer.Tools.Results;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace COA.CodeSearch.Next.McpServer.Tools;

/// <summary>
/// Tool for searching directories by name pattern in indexed workspaces
/// </summary>
public class DirectorySearchTool : McpToolBase<DirectorySearchParameters, AIOptimizedResponse<DirectorySearchResult>>
{
    private readonly IPathResolutionService _pathResolutionService;
    private readonly IResponseCacheService _cacheService;
    private readonly IResourceStorageService _storageService;
    private readonly ICacheKeyGenerator _keyGenerator;
    private readonly DirectorySearchResponseBuilder _responseBuilder;
    private readonly ILogger<DirectorySearchTool> _logger;
    
    // Directories to exclude from search
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".svn", ".hg", ".vs", ".vscode", ".idea",
        "bin", "obj", "node_modules", "packages", "dist",
        "build", "out", "target", ".next", ".nuxt"
    };

    public DirectorySearchTool(
        IPathResolutionService pathResolutionService,
        IResponseCacheService cacheService,
        IResourceStorageService storageService,
        ICacheKeyGenerator keyGenerator,
        ILogger<DirectorySearchTool> logger) : base(logger)
    {
        _pathResolutionService = pathResolutionService;
        _cacheService = cacheService;
        _storageService = storageService;
        _keyGenerator = keyGenerator;
        _responseBuilder = new DirectorySearchResponseBuilder(null, storageService);
        _logger = logger;
    }

    public override string Name => ToolNames.DirectorySearch;
    public override string Description => "Search for directories by name pattern with token-optimized responses";
    public override ToolCategory Category => ToolCategory.Query;

    protected override async Task<AIOptimizedResponse<DirectorySearchResult>> ExecuteInternalAsync(
        DirectorySearchParameters parameters,
        CancellationToken cancellationToken)
    {
        // Validate required parameters
        var pattern = ValidateRequired(parameters.Pattern, nameof(parameters.Pattern));
        var workspacePath = ValidateRequired(parameters.WorkspacePath, nameof(parameters.WorkspacePath));
        
        // Resolve to absolute path
        workspacePath = Path.GetFullPath(workspacePath);
        
        if (!Directory.Exists(workspacePath))
        {
            return CreateDirectoryNotFoundError(workspacePath);
        }
        
        // Validate max results
        var maxResults = parameters.MaxResults ?? 100;
        maxResults = ValidateRange(maxResults, 1, 500, nameof(parameters.MaxResults));
        
        // Generate cache key
        var cacheKey = _keyGenerator.GenerateKey(Name, parameters);
        
        // Check cache first (unless explicitly disabled)
        if (!parameters.NoCache)
        {
            var cached = await _cacheService.GetAsync<AIOptimizedResponse<DirectorySearchResult>>(cacheKey);
            if (cached != null)
            {
                _logger.LogDebug("Returning cached directory search results for pattern: {Pattern}", pattern);
                cached.Meta ??= new AIResponseMeta();
                if (cached.Meta.ExtensionData == null)
                    cached.Meta.ExtensionData = new Dictionary<string, object>();
                cached.Meta.ExtensionData["cacheHit"] = true;
                return cached;
            }
        }
        
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            // Perform directory search
            var includeSubdirs = parameters.IncludeSubdirectories ?? true;
            var includeHidden = parameters.IncludeHidden ?? false;
            var useRegex = parameters.UseRegex ?? false;
            
            var searchOption = includeSubdirs ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var directories = new List<DirectoryMatch>();
            
            // Create pattern matcher
            Regex? regex = null;
            if (useRegex)
            {
                try
                {
                    regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
                }
                catch (ArgumentException ex)
                {
                    return CreateInvalidPatternError(pattern, ex.Message);
                }
            }
            
            // Search directories using simple recursive approach
            await Task.Run(() =>
            {
                SearchDirectoriesRecursive(workspacePath, workspacePath, pattern, regex, useRegex,
                    includeHidden, includeSubdirs, directories, maxResults, 0, cancellationToken);
            }, cancellationToken);
            
            // Sort by depth then by name
            directories = directories
                .OrderBy(d => d.Depth)
                .ThenBy(d => d.Name)
                .ToList();
            
            stopwatch.Stop();
            
            // Ensure we report at least 1ms for very fast operations
            var elapsedMs = stopwatch.ElapsedMilliseconds;
            if (elapsedMs == 0 && stopwatch.ElapsedTicks > 0)
                elapsedMs = 1;
            
            var result = new DirectorySearchResult
            {
                Success = true,
                Directories = directories.Take(maxResults).ToList(),
                TotalMatches = directories.Count,
                Pattern = pattern,
                WorkspacePath = workspacePath,
                IncludedSubdirectories = includeSubdirs,
                SearchTimeMs = elapsedMs
            };
            
            _logger.LogInformation("Directory search completed: {Count} matches for pattern '{Pattern}' in {Time}ms",
                result.TotalMatches, pattern, result.SearchTimeMs);
            
            // Build response using the async method
            var responseMode = parameters.ResponseMode ?? "adaptive";
            var maxTokens = parameters.MaxTokens ?? 8000;
            
            var context = new ResponseContext
            {
                ResponseMode = responseMode,
                TokenLimit = maxTokens,
                StoreFullResults = true,
                ToolName = Name
            };
            
            var response = await _responseBuilder.BuildResponseAsync(result, context);
            
            // Add metadata
            response.Meta ??= new AIResponseMeta();
            response.Meta.ExecutionTime = $"{stopwatch.ElapsedMilliseconds}ms";
            
            // Cache the response
            if (!parameters.NoCache)
            {
                var cacheOptions = new CacheEntryOptions
                {
                    AbsoluteExpiration = TimeSpan.FromMinutes(5)
                };
                await _cacheService.SetAsync(cacheKey, response, cacheOptions);
            }
            
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Directory search failed for pattern: {Pattern}", pattern);
            return CreateErrorResponse(ex);
        }
    }
    
    private void SearchDirectoriesRecursive(
        string rootPath,
        string currentPath,
        string pattern,
        Regex? regex,
        bool useRegex,
        bool includeHidden,
        bool includeSubdirs,
        List<DirectoryMatch> results,
        int maxResults,
        int depth,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested || results.Count >= maxResults)
            return;
        
        try
        {
            var dirInfo = new DirectoryInfo(currentPath);
            
            // Get all subdirectories first
            var subDirs = dirInfo.GetDirectories().OrderBy(d => d.Name).ToList();
            
            // Check each subdirectory
            foreach (var subDir in subDirs)
            {
                if (cancellationToken.IsCancellationRequested || results.Count >= maxResults)
                    break;
                
                var dirName = subDir.Name;
                
                // Skip excluded directories
                if (ExcludedDirectories.Contains(dirName))
                    continue;
                
                // Skip hidden directories if not included
                if (!includeHidden && dirName.StartsWith("."))
                    continue;
                
                // Check if this directory matches the pattern
                bool matches = false;
                if (useRegex && regex != null)
                {
                    matches = regex.IsMatch(dirName);
                }
                else
                {
                    matches = MatchGlobPattern(dirName, pattern);
                }
                
                if (matches)
                {
                    var relativePath = Path.GetRelativePath(rootPath, subDir.FullName);
                    
                    var match = new DirectoryMatch
                    {
                        Path = subDir.FullName,
                        Name = dirName,
                        ParentPath = currentPath,
                        RelativePath = relativePath,
                        Depth = depth + 1,
                        IsHidden = dirName.StartsWith(".")
                    };
                    
                    // Count direct children
                    try
                    {
                        match.FileCount = subDir.GetFiles().Length;
                        match.SubdirectoryCount = subDir.GetDirectories().Length;
                    }
                    catch
                    {
                        // Ignore access errors
                    }
                    
                    results.Add(match);
                }
                
                // Recurse into subdirectory if enabled
                if (includeSubdirs)
                {
                    SearchDirectoriesRecursive(rootPath, subDir.FullName, pattern, regex, useRegex,
                        includeHidden, includeSubdirs, results, maxResults, depth + 1, cancellationToken);
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Skip directories we can't access
            _logger.LogDebug("Skipping inaccessible directory: {Path}", currentPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error searching directory: {Path}", currentPath);
        }
    }
    
    private bool MatchGlobPattern(string name, string pattern)
    {
        // Simple glob pattern matching
        // Convert glob to regex pattern
        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        
        return Regex.IsMatch(name, regexPattern, RegexOptions.IgnoreCase);
    }
    
    private AIOptimizedResponse<DirectorySearchResult> CreateDirectoryNotFoundError(string path)
    {
        return new AIOptimizedResponse<DirectorySearchResult>
        {
            Success = false,
            Error = new FrameworkErrorInfo
            {
                Code = "DIRECTORY_NOT_FOUND",
                Message = $"Directory not found: {path}",
                Recovery = new FrameworkRecoveryInfo
                {
                    Steps = new[]
                    {
                        "Verify the directory path exists",
                        "Check for typos in the path",
                        "Ensure you have read permissions"
                    }
                }
            }
        };
    }
    
    private AIOptimizedResponse<DirectorySearchResult> CreateInvalidPatternError(string pattern, string details)
    {
        return new AIOptimizedResponse<DirectorySearchResult>
        {
            Success = false,
            Error = new FrameworkErrorInfo
            {
                Code = "INVALID_PATTERN",
                Message = $"Invalid search pattern: {pattern} - {details}",
                Recovery = new FrameworkRecoveryInfo
                {
                    Steps = new[]
                    {
                        "Check regex syntax if using regex mode",
                        "Escape special characters properly",
                        "Use simpler glob patterns like '*test*'"
                    }
                }
            }
        };
    }
    
    private AIOptimizedResponse<DirectorySearchResult> CreateErrorResponse(Exception ex)
    {
        return new AIOptimizedResponse<DirectorySearchResult>
        {
            Success = false,
            Error = new FrameworkErrorInfo
            {
                Code = "SEARCH_ERROR",
                Message = $"Directory search failed: {ex.Message}",
                Recovery = new FrameworkRecoveryInfo
                {
                    Steps = new[]
                    {
                        "Check the workspace path is valid",
                        "Verify you have read permissions",
                        "Try a simpler search pattern"
                    }
                }
            }
        };
    }
}