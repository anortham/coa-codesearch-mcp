using System.ComponentModel;
using System.Text.Json;
using COA.Mcp.Framework;
using COA.Mcp.Framework.Base;
using COA.Mcp.Framework.Models;
using COA.Mcp.Framework.Interfaces;
using COA.Mcp.Framework.TokenOptimization;
using COA.Mcp.Framework.TokenOptimization.ResponseBuilders;
using COA.Mcp.Framework.TokenOptimization.Models;
using COA.Mcp.Framework.TokenOptimization.Caching;
using COA.Mcp.Framework.TokenOptimization.Storage;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Services.Lucene;
using COA.CodeSearch.McpServer.Services.TypeExtraction;
using COA.CodeSearch.McpServer.Services.Analysis;
using COA.CodeSearch.McpServer.Tools.Models;
using COA.CodeSearch.McpServer.ResponseBuilders;
using Microsoft.Extensions.Logging;
using Lucene.Net.Search;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Util;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Go to definition tool that jumps directly to where a symbol is defined
/// </summary>
public class GoToDefinitionTool : CodeSearchToolBase<GoToDefinitionParameters, AIOptimizedResponse<SymbolDefinition>>, ITypeAware, IPrioritizedTool
{
    private readonly ILuceneIndexService _luceneIndexService;
    private readonly IResponseCacheService _cacheService;
    private readonly IResourceStorageService _storageService;
    private readonly ICacheKeyGenerator _keyGenerator;
    private readonly SmartQueryPreprocessor _queryProcessor;
    private readonly GoToDefinitionResponseBuilder _responseBuilder;
    private readonly ILogger<GoToDefinitionTool> _logger;
    private const LuceneVersion LUCENE_VERSION = LuceneVersion.LUCENE_48;

    public GoToDefinitionTool(
        IServiceProvider serviceProvider,
        ILuceneIndexService luceneIndexService,
        IResponseCacheService cacheService,
        IResourceStorageService storageService,
        ICacheKeyGenerator keyGenerator,
        SmartQueryPreprocessor queryProcessor,
        ILogger<GoToDefinitionTool> logger) : base(serviceProvider)
    {
        _luceneIndexService = luceneIndexService;
        _cacheService = cacheService;
        _storageService = storageService;
        _keyGenerator = keyGenerator;
        _queryProcessor = queryProcessor;
        _responseBuilder = new GoToDefinitionResponseBuilder(logger as ILogger<GoToDefinitionResponseBuilder>, storageService);
        _logger = logger;
    }

    public override string Name => ToolNames.GoToDefinition;
    public override string Description => "VERIFY BEFORE CODING - Jump to exact symbol definitions in <100ms. USE BEFORE writing any code that references types. Tree-sitter powered for accurate type extraction.";
    public override ToolCategory Category => ToolCategory.Query;
    
    // IPrioritizedTool implementation - HIGHEST priority for type verification
    public int Priority => 100;
    public string[] PreferredScenarios => new[] { "type_verification", "code_exploration", "before_coding" };

    protected override async Task<AIOptimizedResponse<SymbolDefinition>> ExecuteInternalAsync(
        GoToDefinitionParameters parameters,
        CancellationToken cancellationToken)
    {
        // Validate required parameters
        var symbolName = ValidateRequired(parameters.Symbol, nameof(parameters.Symbol));
        var workspacePath = ValidateRequired(parameters.WorkspacePath, nameof(parameters.WorkspacePath));
        
        // Resolve to absolute path
        workspacePath = Path.GetFullPath(workspacePath);
        
        // Generate cache key
        var cacheKey = _keyGenerator.GenerateKey(Name, parameters);
        
        // Check cache first (unless explicitly disabled)
        if (!parameters.NoCache)
        {
            var cached = await _cacheService.GetAsync<AIOptimizedResponse<SymbolDefinition>>(cacheKey);
            if (cached != null)
            {
                _logger.LogDebug("Returning cached goto definition result for {Symbol}", symbolName);
                return cached;
            }
        }

        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            // Use SmartQueryPreprocessor for optimal field selection and query processing
            // Symbol definitions work best with SearchMode.Symbol targeting type_names field
            var queryResult = _queryProcessor.Process(symbolName, SearchMode.Symbol);
            
            _logger.LogInformation("GoToDefinition: {Symbol} -> Field: {Field}, Query: {Query}, Reason: {Reason}", 
                symbolName, queryResult.TargetField, queryResult.ProcessedQuery, queryResult.Reason);
            
            // Build query using the processed query and target field
            var analyzer = new CodeAnalyzer(LUCENE_VERSION, preserveCase: false, splitCamelCase: true);
            Query query;
            if (parameters.CaseSensitive)
            {
                // For case-sensitive, use the target field from SmartQueryPreprocessor
                var parser = new QueryParser(LUCENE_VERSION, queryResult.TargetField, analyzer);
                query = parser.Parse(queryResult.ProcessedQuery);
            }
            else
            {
                // Case-insensitive search using processed query and target field
                var parser = new QueryParser(LUCENE_VERSION, queryResult.TargetField, analyzer);
                query = parser.Parse(queryResult.ProcessedQuery);
            }
            
            // Log the final query for debugging
            _logger.LogInformation("Generated Lucene query: {Query}", query.ToString());
            
            // Search for the definition
            var searchResult = await _luceneIndexService.SearchAsync(
                workspacePath, 
                query, 
                10, // Get a few candidates
                true, // Include snippets
                cancellationToken);
            
            stopwatch.Stop();
            
            _logger.LogInformation("GoToDefinition search returned {Count} hits for symbol '{Symbol}'", 
                searchResult.Hits?.Count ?? 0, symbolName);
            
            SymbolDefinition? definition = null;
            
            // Find the exact definition from the search results
            if (searchResult.Hits != null && searchResult.Hits.Count > 0)
            {
                foreach (var hit in searchResult.Hits)
                {
                    var typeInfoJson = hit.Fields?.ContainsKey("type_info") == true ? hit.Fields["type_info"] : null;
                    if (string.IsNullOrEmpty(typeInfoJson))
                        continue;
                    
                    try
                    {
                        // Deserialize using shared options from TypeExtractionResult
                        var typeData = JsonSerializer.Deserialize<TypeExtractionResult>(
                            typeInfoJson, 
                            TypeExtractionResult.DeserializationOptions);
                        if (typeData == null)
                        {
                            _logger.LogWarning("Deserialized typeData was null for {FilePath}", hit.FilePath);
                            continue;
                        }
                        
                        _logger.LogDebug("Deserialized typeData: Types={TypeCount}, Methods={MethodCount}, Language={Language}", 
                            typeData.Types?.Count ?? 0, typeData.Methods?.Count ?? 0, typeData.Language);
                        
                        // Look for exact match in types
                        if (typeData.Types != null)
                        {
                            _logger.LogDebug("Searching {TypeCount} types for symbol '{Symbol}'", typeData.Types.Count, symbolName);
                            foreach (var type in typeData.Types)
                            {
                                _logger.LogDebug("Comparing type '{TypeName}' with '{Symbol}'", type.Name, symbolName);
                            }
                            
                            var matchingType = typeData.Types.FirstOrDefault(t => 
                                t.Name.Equals(symbolName, parameters.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase));
                            
                            if (matchingType != null)
                            {
                                definition = new SymbolDefinition
                                {
                                    Name = matchingType.Name,
                                    Kind = matchingType.Kind,
                                    Signature = matchingType.Signature,
                                    FilePath = hit.FilePath ?? "",
                                    Line = matchingType.Line,
                                    Column = matchingType.Column,
                                    Language = typeData.Language,
                                    Modifiers = matchingType.Modifiers,
                                    BaseType = matchingType.BaseType,
                                    Interfaces = matchingType.Interfaces,
                                    Score = hit.Score,
                                    Snippet = GetContextSnippet(hit, matchingType.Line, parameters.ContextLines)
                                };
                                break; // Found the definition
                            }
                        }
                        
                        // If not found in types, look in methods
                        if (definition == null && typeData.Methods != null)
                        {
                            var matchingMethod = typeData.Methods.FirstOrDefault(m => 
                                m.Name.Equals(symbolName, parameters.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase));
                            
                            if (matchingMethod != null)
                            {
                                definition = new SymbolDefinition
                                {
                                    Name = matchingMethod.Name,
                                    Kind = "method",
                                    Signature = matchingMethod.Signature,
                                    FilePath = hit.FilePath ?? "",
                                    Line = matchingMethod.Line,
                                    Column = matchingMethod.Column,
                                    Language = typeData.Language,
                                    Modifiers = matchingMethod.Modifiers,
                                    ContainingType = matchingMethod.ContainingType,
                                    ReturnType = matchingMethod.ReturnType,
                                    Parameters = matchingMethod.Parameters,
                                    Score = hit.Score,
                                    Snippet = GetContextSnippet(hit, matchingMethod.Line, parameters.ContextLines)
                                };
                                break; // Found the definition
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Failed to parse type_info for {FilePath}", hit.FilePath);
                    }
                }
            }
            
            // Build response context
            var context = new ResponseContext
            {
                ResponseMode = "adaptive",  // GoToDefinition doesn't have ResponseMode parameter
                TokenLimit = parameters.ContextLines * 50 + 500,  // Estimate based on context lines
                StoreFullResults = false,  // Single result, no need to store
                ToolName = Name,
                CacheKey = cacheKey,
                CustomMetadata = new Dictionary<string, object>
                {
                    ["symbolName"] = symbolName  // Pass the symbol name for the response builder
                }
            };
            
            // Use response builder to create optimized response
            var response = await _responseBuilder.BuildResponseAsync(definition, context);
            
            // Cache the response
            if (!parameters.NoCache && response.Success)
            {
                await _cacheService.SetAsync(cacheKey, response, new CacheEntryOptions
                {
                    AbsoluteExpiration = TimeSpan.FromMinutes(10)
                });
            }
            
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Goto definition failed for {Symbol} in {WorkspacePath}", symbolName, workspacePath);
            
            return new AIOptimizedResponse<SymbolDefinition>
            {
                Success = false,
                Error = new COA.Mcp.Framework.Models.ErrorInfo
                {
                    Code = "GOTO_DEFINITION_ERROR",
                    Message = $"Failed to find definition: {ex.Message}",
                    Recovery = new COA.Mcp.Framework.Models.RecoveryInfo
                    {
                        Steps = new[]
                        {
                            "Ensure the workspace is indexed",
                            "Check if the symbol name is correct",
                            "Try symbol_search for broader search"
                        }
                    }
                }
            };
        }
    }
    
    private string? GetContextSnippet(SearchHit hit, int targetLine, int contextLines)
    {
        // Try to get context from the hit's stored content
        if (hit.ContextLines != null && hit.StartLine.HasValue && hit.EndLine.HasValue)
        {
            // If we already have context that includes the target line, use it
            if (targetLine >= hit.StartLine.Value && targetLine <= hit.EndLine.Value)
            {
                return string.Join("\n", hit.ContextLines);
            }
        }
        
        // Otherwise, try to extract from the full content if available
        var content = hit.Fields?.ContainsKey("content") == true ? hit.Fields["content"] : null;
        if (!string.IsNullOrEmpty(content))
        {
            var lines = content.Split('\n');
            var startLine = Math.Max(0, targetLine - contextLines - 1); // -1 because line numbers are 1-based
            var endLine = Math.Min(lines.Length - 1, targetLine + contextLines - 1);
            
            var contextSnippet = new List<string>();
            for (int i = startLine; i <= endLine; i++)
            {
                contextSnippet.Add(lines[i]);
            }
            
            return string.Join("\n", contextSnippet);
        }
        
        return null;
    }
}