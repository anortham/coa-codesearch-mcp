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
using COA.CodeSearch.McpServer.Services.TypeExtraction;
using Microsoft.Extensions.Logging;
using Lucene.Net.Analysis.Standard;
using System.Text;
using System.Text.Json;
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
/// Text search tool using the BaseResponseBuilder pattern for consistent response building
/// </summary>
public class TextSearchTool : McpToolBase<TextSearchParameters, AIOptimizedResponse<COA.CodeSearch.McpServer.Services.Lucene.SearchResult>>
{
    private readonly ILuceneIndexService _luceneIndexService;
    private readonly IResponseCacheService _cacheService;
    private readonly IResourceStorageService _storageService;
    private readonly ICacheKeyGenerator _keyGenerator;
    private readonly SearchResponseBuilder _responseBuilder;
    private readonly QueryPreprocessor _queryPreprocessor;
    private readonly IQueryTypeDetector? _queryTypeDetector;
    private readonly IProjectKnowledgeService _projectKnowledgeService;
    private readonly SmartDocumentationService _smartDocumentationService;
    private readonly COA.VSCodeBridge.IVSCodeBridge _vscode;
    private readonly ILogger<TextSearchTool> _logger;

    public TextSearchTool(
        IServiceProvider serviceProvider,
        ILuceneIndexService luceneIndexService,
        IResponseCacheService cacheService,
        IResourceStorageService storageService,
        ICacheKeyGenerator keyGenerator,
        QueryPreprocessor queryPreprocessor,
        IQueryTypeDetector? queryTypeDetector,
        IProjectKnowledgeService projectKnowledgeService,
        SmartDocumentationService smartDocumentationService,
        COA.VSCodeBridge.IVSCodeBridge vscode,
        ILogger<TextSearchTool> logger) : base(serviceProvider)
    {
        _luceneIndexService = luceneIndexService;
        _cacheService = cacheService;
        _storageService = storageService;
        _keyGenerator = keyGenerator;
        _queryPreprocessor = queryPreprocessor;
        _queryTypeDetector = queryTypeDetector;
        _projectKnowledgeService = projectKnowledgeService;
        _smartDocumentationService = smartDocumentationService;
        _vscode = vscode;
        _logger = logger;
        
        // Create response builder with dependencies
        _responseBuilder = new SearchResponseBuilder(logger as ILogger<SearchResponseBuilder>, storageService);
    }

    public override string Name => ToolNames.TextSearch;
    public override string Description => "SEARCH BEFORE CODING - Find existing implementations to avoid duplicates. PROACTIVELY use before writing ANY new feature. Discovers: function definitions, error patterns, similar code.";
    public override ToolCategory Category => ToolCategory.Query;


    protected override async Task<AIOptimizedResponse<COA.CodeSearch.McpServer.Services.Lucene.SearchResult>> ExecuteInternalAsync(
        TextSearchParameters parameters,
        CancellationToken cancellationToken)
    {
        // Validate required parameters
        var query = ValidateRequired(parameters.Query, nameof(parameters.Query));
        var workspacePath = ValidateRequired(parameters.WorkspacePath, nameof(parameters.WorkspacePath));
        
        // Resolve to absolute path
        workspacePath = Path.GetFullPath(workspacePath);
        
        // Generate cache key
        var cacheKey = _keyGenerator.GenerateKey(Name, parameters);
        
        // Check cache first (unless explicitly disabled)
        if (!parameters.NoCache)
        {
            var cached = await _cacheService.GetAsync<AIOptimizedResponse<COA.CodeSearch.McpServer.Services.Lucene.SearchResult>>(cacheKey);
            if (cached != null)
            {
                _logger.LogDebug("Returning cached search results for query: {Query}", query);
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

            // Validate and preprocess query
            var searchType = parameters.SearchType ?? "standard";
            if (!_queryPreprocessor.IsValidQuery(query, searchType, out var errorMessage))
            {
                return CreateQueryParseError(query, errorMessage);
            }

            // Build query with proper preprocessing
            var analyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
            var luceneQuery = _queryPreprocessor.BuildQuery(query, searchType, parameters.CaseSensitive, analyzer);
            
            // Apply scoring factors for better relevance
            var scoringContext = new ScoringContext
            {
                QueryText = query,
                SearchType = searchType,
                WorkspacePath = workspacePath
            };
            
            var multiFactorQuery = new MultiFactorScoreQuery(luceneQuery, scoringContext, _logger);
            
            // Add scoring factors - these dramatically improve search relevance
            multiFactorQuery.AddScoringFactor(new PathRelevanceFactor(_logger)); // Deboosting test files
            multiFactorQuery.AddScoringFactor(new FilenameRelevanceFactor());    // Boosting filename matches
            multiFactorQuery.AddScoringFactor(new FileTypeRelevanceFactor());    // Prioritize code files
            multiFactorQuery.AddScoringFactor(new RecencyBoostFactor());         // Boost recently modified
            multiFactorQuery.AddScoringFactor(new ExactMatchBoostFactor(parameters.CaseSensitive)); // Exact phrase matches
            multiFactorQuery.AddScoringFactor(new InterfaceImplementationFactor(_logger)); // Reduce mock/test noise for interface searches
            
            // Add type definition boost if this looks like a type query
            if (_queryTypeDetector?.IsLikelyTypeQuery(query) ?? false)
            {
                multiFactorQuery.AddScoringFactor(new TypeDefinitionBoostFactor());
            }

            // Implement aggressive token-aware limiting like the old system
            // The old system targeted ~1500 tokens with ~5 results for maximum relevance
            var responseMode = parameters.ResponseMode?.ToLowerInvariant() ?? "adaptive";
            
            // Estimate tokens per result (old system used ~300 tokens per result as baseline)
            var hasContext = false; // We don't use context in this tool
            var tokensPerResult = hasContext ? 200 : 100; 
            
            // Calculate token budget (be conservative like old system)
            var tokenBudget = parameters.MaxTokens;
            var safetyBudget = (int)Math.Min(tokenBudget * 0.4, 2000); // Use only 40% of budget, max 2000 tokens for results
            
            // Calculate max results based on token budget
            var budgetBasedMax = Math.Max(1, safetyBudget / tokensPerResult);
            
            // Apply mode-specific limits (but respect budget limits)
            var maxResults = responseMode switch
            {
                "full" => Math.Min(budgetBasedMax, 10),     // Full mode: reduced from 15
                "summary" => Math.Min(budgetBasedMax, 2),   // Summary: ultra-lean - just 2 results
                _ => Math.Min(budgetBasedMax, 3)            // Default: lean - just top 3 results
            };
            
            _logger.LogDebug("Token-aware search limits: budget={Budget}, tokensPerResult={TokensPerResult}, maxResults={MaxResults}, mode={Mode}, Query={Query}", 
                safetyBudget, tokensPerResult, maxResults, responseMode, query);

            // Perform search with scoring
            // Always include snippets for better context in results
            var includeSnippets = true;  // Always generate snippets for rich results
            var searchResult = await _luceneIndexService.SearchAsync(
                workspacePath, 
                multiFactorQuery,  // Use the multi-factor query instead of plain query
                maxResults,
                includeSnippets,
                cancellationToken);
            
            // Add query to result for insights
            searchResult.Query = query;
            
            // Enhance results with type information if relevant
            if (_queryTypeDetector?.IsLikelyTypeQuery(query) ?? false)
            {
                await EnhanceWithTypeInformation(searchResult, workspacePath, cancellationToken);
            }

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
                    // Log what we have in searchResult before sending to VS Code
                    if (searchResult.Hits?.Any() == true)
                    {
                        var firstHit = searchResult.Hits.First();
                        _logger.LogDebug("First hit before VS Code visualization: Snippet={HasSnippet}, ContextLines={LineCount}, StartLine={StartLine}", 
                            !string.IsNullOrEmpty(firstHit.Snippet), 
                            firstHit.ContextLines?.Count ?? 0, 
                            firstHit.StartLine);
                    }
                    
                    // Create enhanced visualization data with richer context for VS Code (separate from AI response)
                    // Use the original search hits before token reduction for richer VS Code display
                    var visualizationData = new
                    {
                        query = query,
                        totalHits = searchResult.TotalHits,
                        searchTime = (int)searchResult.SearchTime.TotalMilliseconds,
                        results = searchResult.Hits?.Select(hit => new
                        {
                            filePath = hit.FilePath,
                            line = hit.LineNumber ?? 1,  // The actual match line (for navigation)
                            column = 1,
                            score = hit.Score,
                            snippet = hit.Snippet ?? string.Join("\n", hit.ContextLines ?? new List<string>()),
                            preview = hit.Snippet ?? string.Join("\n", hit.ContextLines ?? new List<string>()),
                            startLine = hit.StartLine ?? (hit.LineNumber ?? 1),  // First line of context display
                            endLine = hit.EndLine,
                            contextLines = hit.ContextLines
                        }).ToList()
                    };

                    // Send visualization using the generic protocol
                    await _vscode.SendVisualizationAsync(
                        "code-search",
                        visualizationData,
                        new VisualizationHint
                        {
                            Interactive = true,
                            ConsolidateTabs = true
                        });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send search results to VS Code Bridge");
                    // Don't fail the operation if visualization fails
                }
            }

            // Auto-documentation: store findings in ProjectKnowledge if enabled
            if (parameters.DocumentFindings && result.Success && searchResult.TotalHits > 0)
            {
                await DocumentSearchFindingsAsync(parameters, searchResult, query);
            }

            // Cache the successful response
            if (!parameters.NoCache && result.Success)
            {
                await _cacheService.SetAsync(cacheKey, result, new CacheEntryOptions
                {
                    AbsoluteExpiration = TimeSpan.FromMinutes(15),
                    Priority = searchResult.TotalHits > 100 ? CachePriority.High : CachePriority.Normal
                });
                _logger.LogDebug("Cached search results for query: {Query}", query);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing text search for query: {Query}", query);
            return new AIOptimizedResponse<COA.CodeSearch.McpServer.Services.Lucene.SearchResult>
            {
                Success = false,
                Error = new COA.Mcp.Framework.Models.ErrorInfo
                {
                    Code = "SEARCH_ERROR",
                    Message = $"Error performing search: {ex.Message}",
                    Recovery = new COA.Mcp.Framework.Models.RecoveryInfo
                    {
                        Steps = new[]
                        {
                            "Verify the query syntax is valid",
                            "Check if the workspace is properly indexed",
                            "Try a simpler query",
                            "Check logs for detailed error information"
                        }
                    }
                }
            };
        }
    }

    private AIOptimizedResponse<COA.CodeSearch.McpServer.Services.Lucene.SearchResult> CreateNoIndexError(string workspacePath)
    {
        var result = new AIOptimizedResponse<COA.CodeSearch.McpServer.Services.Lucene.SearchResult>
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
                "Indexing creates a searchable database of file contents"
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
        return result;
    }

    private AIOptimizedResponse<COA.CodeSearch.McpServer.Services.Lucene.SearchResult> CreateQueryParseError(string query, string? customMessage = null)
    {
        var result = new AIOptimizedResponse<COA.CodeSearch.McpServer.Services.Lucene.SearchResult>
        {
            Success = false,
            Error = new COA.Mcp.Framework.Models.ErrorInfo
            {
                Code = "INVALID_QUERY",
                Message = customMessage ?? $"Could not parse search query: {query}",
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
                "The query contains invalid syntax",
                "Common issues: unmatched quotes, invalid operators, special characters"
            },
            Actions = new List<AIAction>
            {
                new AIAction
                {
                    Action = "simplify_query",
                    Description = "Try a simpler query without special operators",
                    Priority = 90
                },
                new AIAction
                {
                    Action = "quote_phrase",
                    Description = "Put phrases in quotes for exact matching",
                    Priority = 80
                }
            }
        };
        return result;
    }

    /// <summary>
    /// Enhance search results with type information from stored metadata
    /// </summary>
    private Task EnhanceWithTypeInformation(COA.CodeSearch.McpServer.Services.Lucene.SearchResult searchResult, string workspacePath, CancellationToken cancellationToken)
    {
        if (searchResult.Hits == null || !searchResult.Hits.Any())
            return Task.CompletedTask;
            
        foreach (var hit in searchResult.Hits)
        {
            try
            {
                var typeInfoJson = hit.Fields.GetValueOrDefault("type_info");
                if (string.IsNullOrEmpty(typeInfoJson))
                    continue;
                    
                // Deserialize using shared options from StoredTypeInfo
                var typeData = JsonSerializer.Deserialize<StoredTypeInfo>(
                    typeInfoJson, 
                    StoredTypeInfo.DeserializationOptions);
                if (typeData == null)
                    continue;
                    
                var relevantTypes = FindRelevantTypes(typeData, hit.LineNumber ?? 0);
                
                hit.TypeContext = new TypeContext
                {
                    NearbyTypes = relevantTypes.Types,
                    NearbyMethods = relevantTypes.Methods,
                    ContainingType = DetermineContainingType(typeData, hit.LineNumber ?? 0),
                    Language = typeData.Language
                };
                
                // Make type information UNMISSABLE for Claude
                hit.EnhancedSnippet = BuildProminentTypeSnippet(hit.TypeContext, hit.Snippet);
                
                _logger.LogDebug("Enhanced hit {FilePath} with type context: {TypeCount} types, {MethodCount} methods", 
                    hit.FilePath, relevantTypes.Types.Count, relevantTypes.Methods.Count);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to enhance hit with type information for {FilePath}", hit.FilePath);
            }
        }
        
        return Task.CompletedTask;
    }
    
    private (List<Services.TypeExtraction.TypeInfo> Types, List<Services.TypeExtraction.MethodInfo> Methods) FindRelevantTypes(StoredTypeInfo typeData, int lineNumber)
    {
        var contextRadius = 50;
        var types = typeData.Types?.Where(t => Math.Abs(t.Line - lineNumber) <= contextRadius)
                                   .OrderBy(t => Math.Abs(t.Line - lineNumber))
                                   .Take(3)
                                   .ToList() ?? new List<Services.TypeExtraction.TypeInfo>();
                                   
        var methods = typeData.Methods?.Where(m => Math.Abs(m.Line - lineNumber) <= contextRadius)
                                       .OrderBy(m => Math.Abs(m.Line - lineNumber))
                                       .Take(5)
                                       .ToList() ?? new List<Services.TypeExtraction.MethodInfo>();
                                       
        return (types, methods);
    }
    
    private string? DetermineContainingType(StoredTypeInfo typeData, int lineNumber)
    {
        var containingType = typeData.Types?
            .Where(t => t.Line <= lineNumber)
            .OrderByDescending(t => t.Line)
            .FirstOrDefault();
            
        if (containingType != null)
        {
            return $"{containingType.Kind} {containingType.Name}";
        }
        
        return null;
    }
    
    /// <summary>
    /// Build a prominent type snippet that Claude cannot miss
    /// </summary>
    private string BuildProminentTypeSnippet(TypeContext typeContext, string? originalSnippet)
    {
        var sb = new StringBuilder();
        
        if (typeContext?.ContainingType != null)
        {
            // Add prominent type banner that's impossible to miss
            sb.AppendLine($"╔══ 📦 {typeContext.ContainingType} ══╗");
            
            // Add method signatures if available (most valuable for Claude)
            if (typeContext.NearbyMethods?.Any() == true)
            {
                sb.AppendLine("🔧 Available Methods:");
                foreach (var method in typeContext.NearbyMethods.Take(5))
                {
                    var signature = method.Signature?.Replace("public ", "").Replace("private ", "").Replace("protected ", "").Trim();
                    if (!string.IsNullOrEmpty(signature))
                    {
                        sb.AppendLine($"  • {signature}");
                    }
                }
            }
            
            // Add type definitions if available
            if (typeContext.NearbyTypes?.Any() == true)
            {
                var relevantTypes = typeContext.NearbyTypes.Take(3);
                if (relevantTypes.Count() > 1) // Only show if there are additional types
                {
                    sb.AppendLine("📋 Related Types:");
                    foreach (var type in relevantTypes.Skip(1)) // Skip the containing type
                    {
                        sb.AppendLine($"  • {type.Kind} {type.Name}");
                    }
                }
            }
            
            sb.AppendLine("╚══════════════════════════╝");
            sb.AppendLine();
        }
        
        // Append original snippet
        if (!string.IsNullOrEmpty(originalSnippet))
        {
            sb.Append(originalSnippet);
        }
        
        return sb.ToString();
    }
    
    /// <summary>
    /// Document search findings in ProjectKnowledge based on intelligent pattern detection
    /// </summary>
    private async Task DocumentSearchFindingsAsync(TextSearchParameters parameters, COA.CodeSearch.McpServer.Services.Lucene.SearchResult searchResult, string query)
    {
        try
        {
            // Extract file paths from search results
            var filePaths = searchResult.Hits?.Select(h => h.FilePath).ToArray();
            
            // Get documentation recommendation from smart service
            var recommendation = _smartDocumentationService.AnalyzeSearchResults(
                query, 
                searchResult.TotalHits, 
                filePaths
            );

            if (!recommendation.ShouldDocument)
            {
                _logger.LogDebug("No documentation recommended for query: {Query}", query);
                return;
            }

            // Use explicit FindingType if provided, otherwise use recommendation
            var knowledgeType = parameters.FindingType ?? recommendation.KnowledgeType ?? "TechnicalDebt";
            var content = recommendation.Content ?? $"Found {searchResult.TotalHits} instances of '{query}'";
            var tags = recommendation.Tags ?? new[] { "codesearch-auto", "investigation" };
            var priority = recommendation.Priority ?? "medium";

            // Add search context to metadata - all values must be strings for ProjectKnowledge
            var metadata = recommendation.Metadata ?? new Dictionary<string, object>();
            metadata["workspace"] = Path.GetFileName(parameters.WorkspacePath);  // Project name, not full path
            metadata["searchQuery"] = query;
            metadata["resultCount"] = searchResult.TotalHits.ToString();  // Convert to string
            metadata["workspacePath"] = parameters.WorkspacePath;
            metadata["searchType"] = parameters.SearchType;
            metadata["caseSensitive"] = parameters.CaseSensitive.ToString();  // Convert to string

            // Store in ProjectKnowledge
            var knowledgeId = await _projectKnowledgeService.StoreKnowledgeAsync(
                content,
                knowledgeType,
                metadata,
                tags,
                priority
            );

            if (knowledgeId != null)
            {
                _logger.LogInformation("Auto-documented search findings: Query='{Query}', Type={Type}, KnowledgeId={Id}", 
                    query, knowledgeType, knowledgeId);
            }
            else
            {
                _logger.LogWarning("Failed to auto-document search findings for query: {Query}", query);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error auto-documenting search findings for query: {Query}", query);
            // Don't throw - documentation failure shouldn't break search
        }
    }


}