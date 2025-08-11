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
using COA.CodeSearch.Next.McpServer.Services;
using COA.CodeSearch.Next.McpServer.Services.Lucene;
using COA.CodeSearch.Next.McpServer.Models;
using COA.CodeSearch.Next.McpServer.ResponseBuilders;
using FrameworkErrorInfo = COA.Mcp.Framework.Models.ErrorInfo;
using FrameworkRecoveryInfo = COA.Mcp.Framework.Models.RecoveryInfo;
using Microsoft.Extensions.Logging;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Queries.Mlt;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;

namespace COA.CodeSearch.Next.McpServer.Tools;

/// <summary>
/// Tool for finding files similar to a given file using Lucene's MoreLikeThis functionality
/// </summary>
public class SimilarFilesTool : McpToolBase<SimilarFilesParameters, AIOptimizedResponse<SimilarFilesResult>>
{
    private readonly ILuceneIndexService _luceneIndexService;
    private readonly IResponseCacheService _cacheService;
    private readonly IResourceStorageService _storageService;
    private readonly ICacheKeyGenerator _keyGenerator;
    private readonly IPathResolutionService _pathResolutionService;
    private readonly SimilarFilesResponseBuilder _responseBuilder;
    private readonly ILogger<SimilarFilesTool> _logger;
    private const LuceneVersion LUCENE_VERSION = LuceneVersion.LUCENE_48;

    public SimilarFilesTool(
        ILuceneIndexService luceneIndexService,
        IResponseCacheService cacheService,
        IResourceStorageService storageService,
        ICacheKeyGenerator keyGenerator,
        IPathResolutionService pathResolutionService,
        ILogger<SimilarFilesTool> logger) : base(logger)
    {
        _luceneIndexService = luceneIndexService;
        _cacheService = cacheService;
        _storageService = storageService;
        _keyGenerator = keyGenerator;
        _pathResolutionService = pathResolutionService;
        _logger = logger;
        
        // Create response builder with dependencies
        _responseBuilder = new SimilarFilesResponseBuilder(null, storageService);
    }

    public override string Name => ToolNames.SimilarFiles;
    public override string Description => "Find files similar in content to a specified file using MoreLikeThis analysis";
    public override ToolCategory Category => ToolCategory.Query;

    protected override async Task<AIOptimizedResponse<SimilarFilesResult>> ExecuteInternalAsync(
        SimilarFilesParameters parameters,
        CancellationToken cancellationToken)
    {
        // Validate required parameters
        var filePath = ValidateRequired(parameters.FilePath, nameof(parameters.FilePath));
        var workspacePath = ValidateRequired(parameters.WorkspacePath, nameof(parameters.WorkspacePath));
        var maxResults = ValidateRange(parameters.MaxResults ?? 10, 1, 100, nameof(parameters.MaxResults));
        
        // Resolve to absolute paths
        workspacePath = Path.GetFullPath(workspacePath);
        filePath = Path.GetFullPath(filePath);
        
        // Ensure the file is within the workspace
        if (!filePath.StartsWith(workspacePath, StringComparison.OrdinalIgnoreCase))
        {
            return CreateFileNotInWorkspaceError(filePath, workspacePath);
        }
        
        // Generate cache key
        var cacheKey = _keyGenerator.GenerateKey(Name, parameters);
        
        // Check cache first (unless explicitly disabled)
        if (!parameters.NoCache)
        {
            var cached = await _cacheService.GetAsync<AIOptimizedResponse<SimilarFilesResult>>(cacheKey);
            if (cached != null)
            {
                _logger.LogDebug("Returning cached similar files for: {FilePath}", filePath);
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

            // Get the relative path for the query
            var relativePath = Path.GetRelativePath(workspacePath, filePath);
            
            // Find similar files using MoreLikeThis
            var similarFiles = await FindSimilarFilesAsync(
                workspacePath, 
                relativePath, 
                maxResults,
                parameters.MinScore ?? 0.1f,
                cancellationToken);
            
            if (similarFiles == null || !similarFiles.Any())
            {
                return CreateNoSimilarFilesResult(filePath);
            }
            
            // Create result for response builder
            var result = new SimilarFilesSearchResult
            {
                Success = true,
                QueryFile = filePath,
                WorkspacePath = workspacePath,
                TotalMatches = similarFiles.Count,
                SimilarFiles = similarFiles,
                SearchTimeMs = 0 // Will be set by actual search time
            };
            
            // Build response context
            var context = new ResponseContext
            {
                ResponseMode = parameters.ResponseMode ?? "adaptive",
                TokenLimit = parameters.MaxTokens ?? 8000,
                StoreFullResults = similarFiles.Count > 20,
                ToolName = Name,
                CacheKey = cacheKey
            };
            
            // Use response builder to create optimized response
            var response = await _responseBuilder.BuildResponseAsync(result, context);
            
            // Cache the response
            if (!parameters.NoCache)
            {
                await _cacheService.SetAsync(cacheKey, response, new CacheEntryOptions
                {
                    AbsoluteExpiration = TimeSpan.FromMinutes(5),
                    Priority = CachePriority.Normal
                });
            }
            
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to find similar files for: {FilePath}", filePath);
            
            return new AIOptimizedResponse<SimilarFilesResult>
            {
                Success = false,
                Error = new FrameworkErrorInfo
                {
                    Code = "SIMILARITY_ERROR",
                    Message = $"Failed to find similar files: {ex.Message}",
                    Recovery = new FrameworkRecoveryInfo
                    {
                        Steps = new[]
                        {
                            "Ensure the file exists and is indexed",
                            "Check if the index is up to date",
                            "Try with a different file",
                            "Rebuild the index if necessary"
                        }
                    }
                }
            };
        }
    }
    
    private Task<List<SimilarFile>> FindSimilarFilesAsync(
        string workspacePath, 
        string relativePath,
        int maxResults,
        float minScore,
        CancellationToken cancellationToken)
    {
        var similarFiles = new List<SimilarFile>();
        
        // Get index path
        var indexPath = _pathResolutionService.GetIndexPath(workspacePath);
        
        // Open the index directly for MoreLikeThis query
        using (var directory = FSDirectory.Open(indexPath))
        {
            if (!DirectoryReader.IndexExists(directory))
            {
                return Task.FromResult(similarFiles);
            }
            
            using (var reader = DirectoryReader.Open(directory))
            {
                var searcher = new IndexSearcher(reader);
                
                // Find the document for the given file
                var pathQuery = new TermQuery(new Term("path", relativePath));
                var topDocs = searcher.Search(pathQuery, 1);
                
                if (topDocs.TotalHits == 0)
                {
                    _logger.LogWarning("File not found in index: {Path}", relativePath);
                    return Task.FromResult(similarFiles);
                }
                
                var docId = topDocs.ScoreDocs[0].Doc;
                
                // Configure MoreLikeThis
                var mlt = new MoreLikeThis(reader)
                {
                    Analyzer = new StandardAnalyzer(LUCENE_VERSION),
                    MinTermFreq = 1,
                    MinDocFreq = 1,
                    MaxQueryTerms = 25,
                    MinWordLen = 3,
                    MaxWordLen = 50,
                    StopWords = null // Use default stop words
                };
                
                // Set fields to analyze (Note: FieldNames is a property in Lucene.Net 4.8)
                mlt.FieldNames = new[] { "content", "fileName" };
                
                // Create query from the document
                var query = mlt.Like(docId);
                
                if (query != null)
                {
                    // Search for similar documents
                    var similarDocs = searcher.Search(query, maxResults + 1); // +1 to exclude self
                    
                    foreach (var scoreDoc in similarDocs.ScoreDocs)
                    {
                        // Skip the original document
                        if (scoreDoc.Doc == docId)
                            continue;
                        
                        // Skip if below minimum score
                        if (scoreDoc.Score < minScore)
                            continue;
                        
                        var doc = searcher.Doc(scoreDoc.Doc);
                        var similarPath = doc.Get("path");
                        
                        if (!string.IsNullOrEmpty(similarPath))
                        {
                            var fullPath = Path.Combine(workspacePath, similarPath);
                            var fileInfo = new System.IO.FileInfo(fullPath);
                            
                            similarFiles.Add(new SimilarFile
                            {
                                Path = fullPath,
                                RelativePath = similarPath,
                                FileName = Path.GetFileName(similarPath),
                                Extension = Path.GetExtension(similarPath),
                                Score = scoreDoc.Score,
                                SizeBytes = fileInfo.Exists ? fileInfo.Length : 0,
                                LastModified = fileInfo.Exists ? fileInfo.LastWriteTimeUtc : DateTime.MinValue,
                                MatchReason = GenerateMatchReason(scoreDoc.Score)
                            });
                        }
                    }
                }
            }
        }
        
        return Task.FromResult(similarFiles);
    }
    
    private string GenerateMatchReason(float score)
    {
        if (score > 0.8f)
            return "Very high similarity - likely same code patterns and structure";
        else if (score > 0.6f)
            return "High similarity - similar code patterns and vocabulary";
        else if (score > 0.4f)
            return "Moderate similarity - some shared patterns";
        else if (score > 0.2f)
            return "Low similarity - few common elements";
        else
            return "Minimal similarity - basic vocabulary overlap";
    }
    
    private AIOptimizedResponse<SimilarFilesResult> CreateNoIndexError(string workspacePath)
    {
        return new AIOptimizedResponse<SimilarFilesResult>
        {
            Success = false,
            Error = new FrameworkErrorInfo
            {
                Code = "NO_INDEX",
                Message = $"No index found for workspace: {workspacePath}",
                Recovery = new FrameworkRecoveryInfo
                {
                    Steps = new[]
                    {
                        "Run index_workspace tool first to create the index",
                        "Verify the workspace path is correct",
                        "Check if the index was deleted or corrupted"
                    }
                }
            },
            Actions = new List<AIAction>
            {
                new AIAction
                {
                    Action = "index_workspace",
                    Description = "Index the workspace before searching",
                    Priority = 100
                }
            }
        };
    }
    
    private AIOptimizedResponse<SimilarFilesResult> CreateFileNotInWorkspaceError(string filePath, string workspacePath)
    {
        return new AIOptimizedResponse<SimilarFilesResult>
        {
            Success = false,
            Error = new FrameworkErrorInfo
            {
                Code = "FILE_NOT_IN_WORKSPACE",
                Message = $"File '{filePath}' is not within workspace '{workspacePath}'",
                Recovery = new FrameworkRecoveryInfo
                {
                    Steps = new[]
                    {
                        "Ensure the file path is within the workspace",
                        "Use a relative path from the workspace root",
                        "Check if the file has been moved outside the workspace"
                    }
                }
            }
        };
    }
    
    private AIOptimizedResponse<SimilarFilesResult> CreateNoSimilarFilesResult(string filePath)
    {
        return new AIOptimizedResponse<SimilarFilesResult>
        {
            Success = true,
            Data = new AIResponseData<SimilarFilesResult>
            {
                Results = new SimilarFilesResult
                {
                    Success = true,
                    QueryFile = filePath,
                    Message = "No similar files found. The file may be unique or not indexed.",
                    TotalMatches = 0
                },
                Count = 0
            },
            Insights = new List<string>
            {
                "No similar files were found in the index",
                "This could mean the file is unique in its content",
                "Try adjusting the minimum score threshold for broader matches"
            }
        };
    }
}

/// <summary>
/// Parameters for the SimilarFiles tool
/// </summary>
public class SimilarFilesParameters
{
    /// <summary>
    /// Path to the file to find similar files for
    /// </summary>
    [Required]
    [Description("Path to the file to find similar files for")]
    public string FilePath { get; set; } = string.Empty;
    
    /// <summary>
    /// Path to the workspace directory
    /// </summary>
    [Required]
    [Description("Path to the workspace directory")]
    public string WorkspacePath { get; set; } = string.Empty;
    
    /// <summary>
    /// Maximum number of similar files to return
    /// </summary>
    [Description("Maximum number of similar files to return (default: 10, max: 100)")]
    [Range(1, 100)]
    public int? MaxResults { get; set; }
    
    /// <summary>
    /// Minimum similarity score (0.0 to 1.0)
    /// </summary>
    [Description("Minimum similarity score (0.0 to 1.0, default: 0.1)")]
    [Range(0.0, 1.0)]
    public float? MinScore { get; set; }
    
    /// <summary>
    /// Response mode: 'summary', 'full', or 'adaptive'
    /// </summary>
    [Description("Response mode: 'summary', 'full', or 'adaptive' (default: adaptive)")]
    public string? ResponseMode { get; set; }
    
    /// <summary>
    /// Maximum tokens for response
    /// </summary>
    [Description("Maximum tokens for response (default: 8000)")]
    [Range(100, 100000)]
    public int? MaxTokens { get; set; }
    
    /// <summary>
    /// Disable caching for this request
    /// </summary>
    [Description("Disable caching for this request")]
    public bool NoCache { get; set; }
}

/// <summary>
/// Result from the SimilarFiles tool
/// </summary>
public class SimilarFilesResult : ToolResultBase
{
    public override string Operation => ToolNames.SimilarFiles;
    
    /// <summary>
    /// The file that was queried
    /// </summary>
    public string QueryFile { get; set; } = string.Empty;
    
    /// <summary>
    /// Total number of similar files found
    /// </summary>
    public int TotalMatches { get; set; }
    
    /// <summary>
    /// List of similar files (may be truncated based on response mode)
    /// </summary>
    public List<SimilarFile> Files { get; set; } = new();
    
    /// <summary>
    /// Optional message with additional information
    /// </summary>
    public new string? Message { get; set; }
}

/// <summary>
/// Represents a file similar to the query file
/// </summary>
public class SimilarFile
{
    /// <summary>
    /// Full path to the similar file
    /// </summary>
    public string Path { get; set; } = string.Empty;
    
    /// <summary>
    /// Relative path from workspace root
    /// </summary>
    public string RelativePath { get; set; } = string.Empty;
    
    /// <summary>
    /// File name without path
    /// </summary>
    public string FileName { get; set; } = string.Empty;
    
    /// <summary>
    /// File extension
    /// </summary>
    public string Extension { get; set; } = string.Empty;
    
    /// <summary>
    /// Similarity score (0.0 to 1.0)
    /// </summary>
    public float Score { get; set; }
    
    /// <summary>
    /// File size in bytes
    /// </summary>
    public long SizeBytes { get; set; }
    
    /// <summary>
    /// Last modified date
    /// </summary>
    public DateTime LastModified { get; set; }
    
    /// <summary>
    /// Reason for the match
    /// </summary>
    public string MatchReason { get; set; } = string.Empty;
}

/// <summary>
/// Result structure for response builder
/// </summary>
public class SimilarFilesSearchResult
{
    public bool Success { get; set; }
    public string QueryFile { get; set; } = string.Empty;
    public string WorkspacePath { get; set; } = string.Empty;
    public int TotalMatches { get; set; }
    public List<SimilarFile> SimilarFiles { get; set; } = new();
    public long SearchTimeMs { get; set; }
}