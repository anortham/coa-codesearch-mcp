using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using COA.Mcp.Framework;
using COA.Mcp.Framework.Base;
using COA.Mcp.Framework.Models;
using COA.Mcp.Framework.TokenOptimization;
using COA.Mcp.Framework.TokenOptimization.ResponseBuilders;
using COA.Mcp.Framework.TokenOptimization.Models;
using COA.Mcp.Framework.TokenOptimization.Caching;
using COA.Mcp.Framework.TokenOptimization.Storage;
using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Services.Lucene;
using COA.CodeSearch.McpServer.Services.Analysis;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.ResponseBuilders;
using COA.CodeSearch.McpServer.Scoring;
using Microsoft.Extensions.Logging;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Search.Highlight;
using Lucene.Net.Index;
using Lucene.Net.Util;
using COA.Mcp.Framework.Interfaces;
using COA.VSCodeBridge;
using COA.VSCodeBridge.Models;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Tool for searching within a specific file's content using the Lucene index
/// </summary>
public class FileContentSearchTool : McpToolBase<FileContentSearchParameters, AIOptimizedResponse<COA.CodeSearch.McpServer.Services.Lucene.SearchResult>>
{
    private readonly ILuceneIndexService _luceneIndexService;
    private readonly IResponseCacheService _cacheService;
    private readonly IResourceStorageService _storageService;
    private readonly ICacheKeyGenerator _keyGenerator;
    private readonly SearchResponseBuilder _responseBuilder;
    private readonly QueryPreprocessor _queryPreprocessor;
    private readonly COA.VSCodeBridge.IVSCodeBridge _vscode;
    private readonly ILogger<FileContentSearchTool> _logger;

    public FileContentSearchTool(
        ILuceneIndexService luceneIndexService,
        IResponseCacheService cacheService,
        IResourceStorageService storageService,
        ICacheKeyGenerator keyGenerator,
        QueryPreprocessor queryPreprocessor,
        COA.VSCodeBridge.IVSCodeBridge vscode,
        ILogger<FileContentSearchTool> logger) : base(logger)
    {
        _luceneIndexService = luceneIndexService;
        _cacheService = cacheService;
        _storageService = storageService;
        _keyGenerator = keyGenerator;
        _queryPreprocessor = queryPreprocessor;
        _vscode = vscode;
        _logger = logger;
        
        // Create response builder with dependencies
        _responseBuilder = new SearchResponseBuilder(null, storageService);
    }

    public override string Name => ToolNames.FileContentSearch;
    public override string Description => "Search within a specific file. Lightning fast using pre-built index. Perfect for finding methods, variables, or patterns in a known file.";
    public override ToolCategory Category => ToolCategory.Query;

    protected override async Task<AIOptimizedResponse<COA.CodeSearch.McpServer.Services.Lucene.SearchResult>> ExecuteInternalAsync(
        FileContentSearchParameters parameters,
        CancellationToken cancellationToken)
    {
        // Validate required parameters
        var pattern = ValidateRequired(parameters.Pattern, nameof(parameters.Pattern));
        var filePath = ValidateRequired(parameters.FilePath, nameof(parameters.FilePath));
        var workspacePath = ValidateRequired(parameters.WorkspacePath, nameof(parameters.WorkspacePath));
        
        // Resolve paths to absolute paths
        workspacePath = Path.GetFullPath(workspacePath);
        filePath = Path.GetFullPath(filePath);
        
        // Validate that the file is within the workspace
        if (!filePath.StartsWith(workspacePath, StringComparison.OrdinalIgnoreCase))
        {
            return CreateValidationError($"File {filePath} is not within workspace {workspacePath}");
        }
        
        // Generate cache key
        var cacheKey = _keyGenerator.GenerateKey(Name, parameters);
        
        // Check cache first (unless explicitly disabled)
        if (!parameters.NoCache)
        {
            var cached = await _cacheService.GetAsync<AIOptimizedResponse<COA.CodeSearch.McpServer.Services.Lucene.SearchResult>>(cacheKey);
            if (cached != null)
            {
                _logger.LogDebug("Returning cached file content search results for file: {FilePath}, pattern: {Pattern}", filePath, pattern);
                cached.Meta ??= new AIResponseMeta();
                if (cached.Meta.ExtensionData == null)
                    cached.Meta.ExtensionData = new Dictionary<string, object>();
                cached.Meta.ExtensionData["cacheHit"] = true;
                return cached;
            }
        }

        try
        {
            // Check if index exists
            if (!await _luceneIndexService.IndexExistsAsync(workspacePath, cancellationToken))
            {
                return CreateNoIndexError(workspacePath);
            }

            // Check if the file exists
            if (!File.Exists(filePath))
            {
                return CreateFileNotFoundError(filePath);
            }

            // Validate and preprocess query
            var searchType = parameters.SearchType ?? "standard";
            if (!_queryPreprocessor.IsValidQuery(pattern, searchType, out var errorMessage))
            {
                return CreateQueryParseError(pattern, errorMessage);
            }

            // Build the file-specific search query
            var analyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
            var contentQuery = _queryPreprocessor.BuildQuery(pattern, searchType, parameters.CaseSensitive, analyzer);
            
            // Create a boolean query that combines file path filter with content search
            var booleanQuery = new BooleanQuery();
            
            // Filter to the specific file
            var pathQuery = new TermQuery(new Term("path", filePath));
            booleanQuery.Add(pathQuery, Occur.MUST);
            
            // Add the content search
            booleanQuery.Add(contentQuery, Occur.MUST);
            
            // Apply scoring factors for better relevance within the file
            var scoringContext = new ScoringContext
            {
                QueryText = pattern,
                SearchType = searchType,
                WorkspacePath = workspacePath
            };
            
            var multiFactorQuery = new MultiFactorScoreQuery(booleanQuery, scoringContext, _logger);
            
            // Add scoring factors (fewer than workspace search since we're in one file)
            multiFactorQuery.AddScoringFactor(new ExactMatchBoostFactor(parameters.CaseSensitive)); // Exact phrase matches
            
            // Calculate max results with token budget consideration
            var responseMode = parameters.ResponseMode?.ToLowerInvariant() ?? "adaptive";
            var tokenBudget = parameters.MaxTokens;
            var safetyBudget = (int)Math.Min(tokenBudget * 0.5, 3000); // Use 50% of budget for single file
            var tokensPerResult = 150; // Estimate for single file results
            var budgetBasedMax = Math.Max(1, safetyBudget / tokensPerResult);
            
            var maxResults = Math.Min(parameters.MaxResults, budgetBasedMax);
            
            _logger.LogDebug("File content search: file={FilePath}, pattern={Pattern}, maxResults={MaxResults}", 
                filePath, pattern, maxResults);

            // Perform search with scoring
            var searchResult = await _luceneIndexService.SearchAsync(
                workspacePath, 
                multiFactorQuery,
                maxResults,
                true, // Include snippets for file content search
                cancellationToken);
            
            // Add query to result for insights
            searchResult.Query = $"file:{Path.GetFileName(filePath)} {pattern}";

            // Build response context
            var context = new ResponseContext
            {
                ResponseMode = responseMode,
                TokenLimit = parameters.MaxTokens,
                StoreFullResults = true,
                ToolName = Name,
                CacheKey = cacheKey
            };

            // Use response builder to create optimized response
            var result = await _responseBuilder.BuildResponseAsync(searchResult, context);

            // Send visualization to VS Code if requested
            if ((parameters.ShowInVSCode ?? false) && _vscode.IsConnected && searchResult.TotalHits > 0)
            {
                try
                {
                    var visualizationData = new
                    {
                        query = pattern,
                        filePath = filePath,
                        fileName = Path.GetFileName(filePath),
                        totalHits = searchResult.TotalHits,
                        searchTime = (int)searchResult.SearchTime.TotalMilliseconds,
                        results = searchResult.Hits?.Select(hit => new
                        {
                            filePath = hit.FilePath,
                            line = hit.LineNumber ?? 1,
                            column = 1,
                            score = hit.Score,
                            snippet = hit.Snippet ?? string.Join("\n", hit.ContextLines ?? new List<string>()),
                            preview = hit.Snippet ?? string.Join("\n", hit.ContextLines ?? new List<string>()),
                            startLine = hit.StartLine ?? (hit.LineNumber ?? 1),
                            endLine = hit.EndLine,
                            contextLines = hit.ContextLines
                        }).ToList()
                    };

                    await _vscode.SendVisualizationAsync(
                        "file-content-search",
                        visualizationData,
                        new VisualizationHint
                        {
                            Interactive = true,
                            ConsolidateTabs = true
                        });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send file content search results to VS Code Bridge");
                }
            }

            // Cache the successful response
            if (!parameters.NoCache && result.Success)
            {
                await _cacheService.SetAsync(cacheKey, result, new CacheEntryOptions
                {
                    AbsoluteExpiration = TimeSpan.FromMinutes(10), // Shorter cache for file-specific searches
                    Priority = searchResult.TotalHits > 0 ? CachePriority.High : CachePriority.Normal
                });
                _logger.LogDebug("Cached file content search results for file: {FilePath}, pattern: {Pattern}", filePath, pattern);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing file content search for file: {FilePath}, pattern: {Pattern}", filePath, pattern);
            return new AIOptimizedResponse<COA.CodeSearch.McpServer.Services.Lucene.SearchResult>
            {
                Success = false,
                Error = new COA.Mcp.Framework.Models.ErrorInfo
                {
                    Code = "SEARCH_ERROR",
                    Message = $"Error performing file content search: {ex.Message}",
                    Recovery = new COA.Mcp.Framework.Models.RecoveryInfo
                    {
                        Steps = new[]
                        {
                            "Verify the file path is correct",
                            "Check if the file exists in the workspace",
                            "Verify the query syntax is valid",
                            "Check if the workspace is properly indexed"
                        }
                    }
                }
            };
        }
    }

    private AIOptimizedResponse<COA.CodeSearch.McpServer.Services.Lucene.SearchResult> CreateNoIndexError(string workspacePath)
    {
        return new AIOptimizedResponse<COA.CodeSearch.McpServer.Services.Lucene.SearchResult>
        {
            Success = false,
            Error = new COA.Mcp.Framework.Models.ErrorInfo
            {
                Code = "NO_INDEX",
                Message = $"No index found for workspace: {workspacePath}",
                Recovery = new COA.Mcp.Framework.Models.RecoveryInfo
                {
                    Steps = new[]
                    {
                        $"Run index_workspace tool to create the index",
                        "Verify the workspace path is correct",
                        "Check if you have read permissions for the workspace"
                    }
                }
            },
            Insights = new List<string>
            {
                "The workspace needs to be indexed before searching",
                "File content search requires the file to be in the Lucene index"
            },
            Actions = new List<AIAction>
            {
                new AIAction
                {
                    Action = ToolNames.IndexWorkspace,
                    Description = "Create search index for this workspace",
                    Priority = 100
                }
            }
        };
    }

    private AIOptimizedResponse<COA.CodeSearch.McpServer.Services.Lucene.SearchResult> CreateFileNotFoundError(string filePath)
    {
        return new AIOptimizedResponse<COA.CodeSearch.McpServer.Services.Lucene.SearchResult>
        {
            Success = false,
            Error = new COA.Mcp.Framework.Models.ErrorInfo
            {
                Code = "FILE_NOT_FOUND",
                Message = $"File not found: {filePath}",
                Recovery = new COA.Mcp.Framework.Models.RecoveryInfo
                {
                    Steps = new[]
                    {
                        "Verify the file path is correct",
                        "Check if the file exists in the filesystem",
                        "Use file_search tool to find the correct file path",
                        "Ensure the file is within the workspace"
                    }
                }
            },
            Insights = new List<string>
            {
                "The specified file does not exist on the filesystem",
                "File content search requires the file to exist and be readable"
            },
            Actions = new List<AIAction>
            {
                new AIAction
                {
                    Action = ToolNames.FileSearch,
                    Description = "Search for files by name pattern",
                    Priority = 90
                }
            }
        };
    }

    private AIOptimizedResponse<COA.CodeSearch.McpServer.Services.Lucene.SearchResult> CreateQueryParseError(string pattern, string? customMessage = null)
    {
        return new AIOptimizedResponse<COA.CodeSearch.McpServer.Services.Lucene.SearchResult>
        {
            Success = false,
            Error = new COA.Mcp.Framework.Models.ErrorInfo
            {
                Code = "INVALID_QUERY",
                Message = customMessage ?? $"Could not parse search pattern: {pattern}",
                Recovery = new COA.Mcp.Framework.Models.RecoveryInfo
                {
                    Steps = new[]
                    {
                        "Use a more specific search pattern (3+ characters)",
                        "For operators, use: =>, ??, ?., ::, ->, +=, -=, ==, !=, >=, <=, &&, ||, <<, >>",
                        "Try different search types: literal, code, wildcard, fuzzy, phrase, regex",
                        "Check for unmatched quotes or parentheses"
                    }
                }
            },
            Insights = new List<string>
            {
                "The search pattern contains invalid syntax",
                "File content search supports all the same search types as text search"
            }
        };
    }

    private AIOptimizedResponse<COA.CodeSearch.McpServer.Services.Lucene.SearchResult> CreateValidationError(string message)
    {
        return new AIOptimizedResponse<COA.CodeSearch.McpServer.Services.Lucene.SearchResult>
        {
            Success = false,
            Error = new COA.Mcp.Framework.Models.ErrorInfo
            {
                Code = "VALIDATION_ERROR",
                Message = message,
                Recovery = new COA.Mcp.Framework.Models.RecoveryInfo
                {
                    Steps = new[]
                    {
                        "Verify all required parameters are provided",
                        "Check that file paths are correct and absolute",
                        "Ensure the file is within the specified workspace"
                    }
                }
            }
        };
    }
}