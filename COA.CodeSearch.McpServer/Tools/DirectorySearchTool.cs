using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Text.RegularExpressions;
using COA.Mcp.Framework;
using COA.Mcp.Framework.Base;
using COA.Mcp.Framework.Models;
using COA.Mcp.Framework.TokenOptimization.Caching;
using COA.Mcp.Framework.TokenOptimization.Storage;
using COA.Mcp.Framework.TokenOptimization.Models;
using COA.Mcp.Framework.TokenOptimization;
using COA.Mcp.Framework.TokenOptimization.ResponseBuilders;
using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Services.Lucene;
using FrameworkErrorInfo = COA.Mcp.Framework.Models.ErrorInfo;
using FrameworkRecoveryInfo = COA.Mcp.Framework.Models.RecoveryInfo;
using COA.CodeSearch.McpServer.ResponseBuilders;
using COA.CodeSearch.McpServer.Tools.Parameters;
using COA.CodeSearch.McpServer.Tools.Results;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using COA.VSCodeBridge;
using COA.VSCodeBridge.Models;
using Lucene.Net.Search;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Analysis.Standard;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Tool for searching directories by name pattern using Lucene index
/// </summary>
public class DirectorySearchTool : McpToolBase<DirectorySearchParameters, AIOptimizedResponse<DirectorySearchResult>>
{
    private readonly IPathResolutionService _pathResolutionService;
    private readonly ILuceneIndexService _luceneService;
    private readonly IResponseCacheService _cacheService;
    private readonly IResourceStorageService _storageService;
    private readonly ICacheKeyGenerator _keyGenerator;
    private readonly DirectorySearchResponseBuilder _responseBuilder;
    private readonly COA.VSCodeBridge.IVSCodeBridge _vscode;
    private readonly ILogger<DirectorySearchTool> _logger;
    
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".svn", ".hg", ".vs", ".vscode", ".idea",
        "bin", "obj", "node_modules", "packages", "dist",
        "build", "out", "target", ".next", ".nuxt"
    };

    public DirectorySearchTool(
        IPathResolutionService pathResolutionService,
        ILuceneIndexService luceneService,
        IResponseCacheService cacheService,
        IResourceStorageService storageService,
        ICacheKeyGenerator keyGenerator,
        COA.VSCodeBridge.IVSCodeBridge vscode,
        ILogger<DirectorySearchTool> logger)
        : base(logger)
    {
        _pathResolutionService = pathResolutionService;
        _luceneService = luceneService;
        _cacheService = cacheService;
        _storageService = storageService;
        _keyGenerator = keyGenerator;
        _responseBuilder = new DirectorySearchResponseBuilder(null, storageService);
        _vscode = vscode;
        _logger = logger;
    }

    public override string Name => ToolNames.DirectorySearch;
    public override string Description => "Search for directories by name pattern with token-optimized responses";
    public override ToolCategory Category => ToolCategory.Query;

    protected override async Task<AIOptimizedResponse<DirectorySearchResult>> ExecuteInternalAsync(
        DirectorySearchParameters parameters,
        CancellationToken cancellationToken)
    {
        // Validate parameters
        var workspacePath = ValidateRequired(parameters.WorkspacePath, nameof(parameters.WorkspacePath));
        var pattern = ValidateRequired(parameters.Pattern, nameof(parameters.Pattern));
        
        // Check if index exists for workspace
        if (!await _luceneService.IndexExistsAsync(workspacePath, cancellationToken))
        {
            return CreateIndexNotFoundError(workspacePath);
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
            // Build Lucene query for directory search
            var includeHidden = parameters.IncludeHidden ?? false;
            var useRegex = parameters.UseRegex ?? false;
            
            // For directory search, we search all files and extract directory information
            // We'll use a MatchAllDocsQuery and filter directories in memory
            Query luceneQuery = new MatchAllDocsQuery();
            
            // Execute search against Lucene index
            var searchResult = await _luceneService.SearchAsync(
                workspacePath, 
                luceneQuery, 
                10000, // Get many results to extract all directories
                false, // No snippets needed for directory search
                cancellationToken);
            
            // Extract unique directories from search results
            var allDirectories = new Dictionary<string, DirectoryMatch>(StringComparer.OrdinalIgnoreCase);
            
            
            foreach (var hit in searchResult.Hits)
            {
                // Try to get directory info from indexed fields first (more reliable)
                var directory = hit.Fields?.GetValueOrDefault("directory", "");
                var relativeDirectory = hit.Fields?.GetValueOrDefault("relativeDirectory", "");
                var directoryName = hit.Fields?.GetValueOrDefault("directoryName", "");
                
                
                // If we don't have directory fields, extract from file path
                if (string.IsNullOrEmpty(directory))
                {
                    var filePath = hit.FilePath ?? 
                                  hit.Fields?.GetValueOrDefault("path", "") ?? 
                                  hit.Fields?.GetValueOrDefault("relativePath", "") ?? "";
                                  
                    if (!string.IsNullOrEmpty(filePath))
                    {
                        directory = Path.GetDirectoryName(filePath) ?? "";
                        directoryName = Path.GetFileName(directory) ?? "";
                        relativeDirectory = string.IsNullOrEmpty(workspacePath) ? 
                            directory : Path.GetRelativePath(workspacePath, directory);
                    }
                }
                
                if (string.IsNullOrEmpty(directory))
                    continue;
                
                // Use the relativeDirectory field if available, otherwise compute it
                var normalizedDir = "";
                if (!string.IsNullOrEmpty(relativeDirectory))
                {
                    // Use the relative directory field directly
                    normalizedDir = relativeDirectory.Replace('\\', '/');
                }
                else
                {
                    // Strip workspace prefix from directory if present
                    var normalizedWorkspace = workspacePath.Replace('\\', '/').TrimEnd('/');
                    normalizedDir = directory.Replace('\\', '/');
                    
                    // Remove workspace prefix to get relative path
                    if (normalizedDir.StartsWith(normalizedWorkspace + "/", StringComparison.OrdinalIgnoreCase))
                    {
                        normalizedDir = normalizedDir.Substring(normalizedWorkspace.Length + 1);
                    }
                    else if (normalizedDir.Equals(normalizedWorkspace, StringComparison.OrdinalIgnoreCase))
                    {
                        normalizedDir = "";
                    }
                }
                
                // Now process the relative directory path
                var segments = normalizedDir.Split('/', StringSplitOptions.RemoveEmptyEntries);
                
                
                // Build up each directory level
                var currentPath = "";
                for (int i = 0; i < segments.Length; i++)
                {
                    var segment = segments[i];
                    currentPath = currentPath == "" ? segment : currentPath + "/" + segment;
                    
                    // Check if this directory should be excluded
                    if (ExcludedDirectories.Contains(segment, StringComparer.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    
                    // Skip hidden directories if not included
                    if (!includeHidden && segment.StartsWith("."))
                    {
                        continue;
                    }
                    
                    // Add or update directory entry
                    if (!allDirectories.ContainsKey(currentPath))
                    {
                        var parentPath = i > 0 ? string.Join("/", segments.Take(i)) : "";
                        
                        allDirectories[currentPath] = new DirectoryMatch
                        {
                            Path = currentPath,
                            Name = segment,
                            ParentPath = parentPath,
                            RelativePath = currentPath,
                            Depth = i + 1,
                            IsHidden = segment.StartsWith("."),
                            FileCount = 1,
                            SubdirectoryCount = 0
                        };
                        
                    }
                    else
                    {
                        allDirectories[currentPath].FileCount++;
                    }
                }
            }
            
            
            // Now filter directories by pattern
            var directoryMap = new Dictionary<string, DirectoryMatch>(StringComparer.OrdinalIgnoreCase);
            
            foreach (var kvp in allDirectories)
            {
                var dir = kvp.Value;
                
                // Check if directory name matches pattern
                bool matches = false;
                if (useRegex)
                {
                    try
                    {
                        var regex = new Regex(pattern, RegexOptions.IgnoreCase);
                        matches = regex.IsMatch(dir.Name);
                    }
                    catch
                    {
                        // Invalid regex, treat as literal
                        matches = dir.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase);
                    }
                }
                else
                {
                    matches = MatchGlobPattern(dir.Name, pattern);
                }
                
                
                if (matches)
                {
                    directoryMap[kvp.Key] = dir;
                }
            }
            
            
            // Calculate subdirectory counts (from all directories, not just matching ones)
            foreach (var dir in directoryMap.Values)
            {
                var subdirCount = allDirectories.Values
                    .Count(d => !string.IsNullOrEmpty(d.ParentPath) && 
                               d.ParentPath.Equals(dir.Path, StringComparison.OrdinalIgnoreCase));
                dir.SubdirectoryCount = subdirCount;
            }
            
            
            // Sort and limit results
            var directories = directoryMap.Values
                .OrderBy(d => d.Depth)
                .ThenBy(d => d.Name)
                .Take(maxResults)
                .ToList();
            
            
            stopwatch.Stop();
            
            // Ensure we report at least 1ms for very fast operations
            var elapsedMs = stopwatch.ElapsedMilliseconds;
            if (elapsedMs == 0 && stopwatch.ElapsedTicks > 0)
                elapsedMs = 1;
            
            var result = new DirectorySearchResult
            {
                Success = true,
                Directories = directories,
                TotalMatches = directoryMap.Count,
                Pattern = pattern,
                WorkspacePath = workspacePath,
                IncludedSubdirectories = parameters.IncludeSubdirectories ?? true,
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
            
            // Visualization removed - not needed for directory search results
            
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
    
    private bool MatchGlobPattern(string name, string pattern)
    {
        // Simple glob pattern matching
        // Convert glob to regex pattern
        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        
        return Regex.IsMatch(name, regexPattern, RegexOptions.IgnoreCase);
    }
    
    private AIOptimizedResponse<DirectorySearchResult> CreateIndexNotFoundError(string path)
    {
        return new AIOptimizedResponse<DirectorySearchResult>
        {
            Success = false,
            Error = new FrameworkErrorInfo
            {
                Code = "INDEX_NOT_FOUND",
                Message = $"No search index exists for workspace: {path}. Please index the workspace first.",
                Recovery = new FrameworkRecoveryInfo
                {
                    Steps = new[]
                    {
                        "Run the index_workspace tool first",
                        "Verify the workspace path is correct",
                        "Check that indexing completed successfully"
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
                        "Check if the workspace is indexed",
                        "Verify the search pattern is valid",
                        "Check system resources and permissions"
                    }
                }
            }
        };
    }
}