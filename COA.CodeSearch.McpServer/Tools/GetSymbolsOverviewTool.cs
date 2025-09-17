using System.ComponentModel;
using System.Text.Json;
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
using COA.CodeSearch.McpServer.Services.TypeExtraction;
using COA.CodeSearch.McpServer.Services.Analysis;
using COA.CodeSearch.McpServer.Tools.Models;
using COA.CodeSearch.McpServer.ResponseBuilders;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Util;
using COA.Mcp.Framework.Interfaces;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Extracts comprehensive symbol overview from a single file using Tree-sitter
/// </summary>
public class GetSymbolsOverviewTool : CodeSearchToolBase<GetSymbolsOverviewParameters, AIOptimizedResponse<SymbolsOverviewResult>>, IPrioritizedTool
{
    private readonly ITypeExtractionService _typeExtractionService;
    private readonly ILuceneIndexService _luceneIndexService;
    private readonly CodeAnalyzer _codeAnalyzer;
    private readonly IResponseCacheService _cacheService;
    private readonly IResourceStorageService _storageService;
    private readonly ICacheKeyGenerator _keyGenerator;
    private readonly ILogger<GetSymbolsOverviewTool> _logger;
    private const LuceneVersion LUCENE_VERSION = LuceneVersion.LUCENE_48;

    /// <summary>
    /// Initializes a new instance of the GetSymbolsOverviewTool with required dependencies.
    /// </summary>
    /// <param name="serviceProvider">Service provider for dependency resolution</param>
    /// <param name="typeExtractionService">Type extraction service for symbol analysis</param>
    /// <param name="luceneIndexService">Lucene index service for search operations</param>
    /// <param name="codeAnalyzer">Code analysis service</param>
    /// <param name="cacheService">Response caching service</param>
    /// <param name="storageService">Resource storage service</param>
    /// <param name="keyGenerator">Cache key generator</param>
    /// <param name="logger">Logger instance</param>
    public GetSymbolsOverviewTool(
        IServiceProvider serviceProvider,
        ITypeExtractionService typeExtractionService,
        ILuceneIndexService luceneIndexService,
        CodeAnalyzer codeAnalyzer,
        IResponseCacheService cacheService,
        IResourceStorageService storageService,
        ICacheKeyGenerator keyGenerator,
        ILogger<GetSymbolsOverviewTool> logger) : base(serviceProvider, logger)
    {
        _typeExtractionService = typeExtractionService;
        _luceneIndexService = luceneIndexService;
        _codeAnalyzer = codeAnalyzer;
        _cacheService = cacheService;
        _storageService = storageService;
        _keyGenerator = keyGenerator;
        _logger = logger;
    }

    /// <summary>
    /// This tool handles validation internally in ExecuteInternalAsync, so disable framework validation
    /// </summary>
    protected override bool ShouldValidateDataAnnotations => false;

    /// <summary>
    /// Gets the tool name identifier.
    /// </summary>
    public override string Name => ToolNames.GetSymbolsOverview;

    /// <summary>
    /// Gets the tool description explaining its purpose and usage scenarios.
    /// </summary>
    public override string Description => "EXTRACT ALL SYMBOLS from any file - Get complete overview of classes, methods, interfaces without reading entire files. Tree-sitter powered for accurate type information with line numbers.";

    /// <summary>
    /// Gets the tool category for classification purposes.
    /// </summary>
    public override ToolCategory Category => ToolCategory.Query;

    /// <summary>
    /// Gets the priority level for this tool. Higher values indicate higher priority.
    /// </summary>
    public int Priority => 95;

    /// <summary>
    /// Gets the preferred usage scenarios for this tool.
    /// </summary>
    public string[] PreferredScenarios => new[] { "file_exploration", "code_understanding", "api_discovery", "type_analysis", "quick_overview" };

    /// <summary>
    /// Executes the symbols overview operation to extract all symbols from a file.
    /// </summary>
    /// <param name="parameters">Symbols overview parameters including file path and extraction options</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>Symbols overview results with all extracted symbols and their details</returns>
    protected override async Task<AIOptimizedResponse<SymbolsOverviewResult>> ExecuteInternalAsync(
        GetSymbolsOverviewParameters parameters,
        CancellationToken cancellationToken)
    {
        // Validate required parameters
        if (string.IsNullOrWhiteSpace(parameters.FilePath))
        {
            return new AIOptimizedResponse<SymbolsOverviewResult>
            {
                Success = false,
                Error = new ErrorInfo
                {
                    Code = "SYMBOLS_OVERVIEW_ERROR",
                    Message = "File path is required"
                }
            };
        }
        var filePath = parameters.FilePath;
        
        // Convert to absolute path
        if (!Path.IsPathRooted(filePath))
        {
            filePath = Path.GetFullPath(filePath);
        }
        
        // Validate file exists
        if (!File.Exists(filePath))
        {
            return new AIOptimizedResponse<SymbolsOverviewResult>
            {
                Success = false,
                Error = new ErrorInfo
                {
                    Code = "FILE_NOT_FOUND",
                    Message = $"File not found: {filePath}",
                    Recovery = new RecoveryInfo
                    {
                        Steps = new[]
                        {
                            "Check if the file path is correct",
                            "Ensure the file exists and is accessible",
                            "Use file_search tool to find the correct file"
                        }
                    }
                }
            };
        }
        
        // Generate cache key
        var cacheKey = _keyGenerator.GenerateKey(Name, parameters);
        
        // Check cache first (unless explicitly disabled)
        if (!parameters.NoCache)
        {
            var cached = await _cacheService.GetAsync<AIOptimizedResponse<SymbolsOverviewResult>>(cacheKey);
            if (cached != null)
            {
                _logger.LogDebug("Returning cached symbols overview for {FilePath}", filePath);
                return cached;
            }
        }

        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            _logger.LogInformation("Getting symbols overview from {FilePath}", filePath);
            
            // First try to get type information from the index (optimization)
            TypeExtractionResult? typeExtractionResult = await TryGetTypeInfoFromIndexAsync(filePath, parameters.WorkspacePath, cancellationToken);
            
            // If not found in index or index lookup failed, fall back to direct extraction
            if (typeExtractionResult == null || !typeExtractionResult.Success)
            {
                _logger.LogDebug("Type info not found in index for {FilePath}, falling back to direct extraction", filePath);
                
                // Read file content
                string content;
                try
                {
                    content = await File.ReadAllTextAsync(filePath, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to read file {FilePath}", filePath);
                    return new AIOptimizedResponse<SymbolsOverviewResult>
                    {
                        Success = false,
                        Error = new ErrorInfo
                        {
                            Code = "FILE_READ_ERROR",
                            Message = $"Failed to read file: {ex.Message}"
                        }
                    };
                }
                
                // Extract types using Tree-sitter as fallback
                typeExtractionResult = await _typeExtractionService.ExtractTypes(content, filePath);
            }
            else
            {
                _logger.LogDebug("Successfully retrieved type info from index for {FilePath}", filePath);
            }
            
            stopwatch.Stop();
            
            if (!typeExtractionResult.Success)
            {
                _logger.LogWarning("Type extraction failed for {FilePath}", filePath);
                return new AIOptimizedResponse<SymbolsOverviewResult>
                {
                    Success = false,
                    Error = new ErrorInfo
                    {
                        Code = "TYPE_EXTRACTION_FAILED",
                        Message = "Failed to extract type information from file",
                        Recovery = new RecoveryInfo
                        {
                            Steps = new[]
                            {
                                "Check if the file contains valid code",
                                "Ensure the file extension is supported",
                                "Try with a different file"
                            }
                        }
                    }
                };
            }
            
            _logger.LogInformation("Successfully extracted {TypeCount} types and {MethodCount} methods from {FilePath}", 
                typeExtractionResult.Types.Count, typeExtractionResult.Methods.Count, filePath);
            
            // Build comprehensive overview
            var result = BuildSymbolsOverview(typeExtractionResult, filePath, stopwatch.Elapsed, parameters);
            
            // Create simple response for now (skip optimization)
            var response = new AIOptimizedResponse<SymbolsOverviewResult>
            {
                Success = true,
                Data = new AIResponseData<SymbolsOverviewResult> { Results = result },
                Message = $"Extracted {result.TotalSymbols} symbols from {Path.GetFileName(filePath)}"
            };
            
            // Cache the response
            if (!parameters.NoCache)
            {
                await _cacheService.SetAsync(cacheKey, response, new CacheEntryOptions
                {
                    AbsoluteExpiration = TimeSpan.FromMinutes(10) // Cache for 10 minutes since files don't change often
                });
            }
            
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Symbols overview extraction failed for {FilePath}", filePath);
            
            return new AIOptimizedResponse<SymbolsOverviewResult>
            {
                Success = false,
                Error = new ErrorInfo
                {
                    Code = "SYMBOLS_OVERVIEW_ERROR",
                    Message = $"Failed to extract symbols overview: {ex.Message}",
                    Recovery = new RecoveryInfo
                    {
                        Steps = new[]
                        {
                            "Check if the file path is valid",
                            "Ensure the file contains parseable code",
                            "Try with a simpler file first"
                        }
                    }
                }
            };
        }
    }
    
    private SymbolsOverviewResult BuildSymbolsOverview(
        TypeExtractionResult typeData, 
        string filePath, 
        TimeSpan extractionTime,
        GetSymbolsOverviewParameters parameters)
    {
        var result = new SymbolsOverviewResult
        {
            FilePath = filePath,
            Language = typeData.Language,
            ExtractionTime = extractionTime,
            Success = true
        };
        
        // Group types by kind
        foreach (var type in typeData.Types)
        {
            var typeOverview = new TypeOverview
            {
                Name = type.Name,
                Kind = type.Kind,
                Signature = type.Signature,
                Line = parameters.IncludeLineNumbers ? type.Line : 0,
                Column = parameters.IncludeLineNumbers ? type.Column : 0,
                Modifiers = type.Modifiers ?? new List<string>(),
                BaseType = parameters.IncludeInheritance ? type.BaseType : null,
                Interfaces = parameters.IncludeInheritance ? type.Interfaces : null
            };
            
            // Add methods that belong to this type
            if (parameters.IncludeMethods)
            {
                var typeMethods = typeData.Methods
                    .Where(m => m.ContainingType == type.Name)
                    .Select(m => new MethodOverview
                    {
                        Name = m.Name,
                        Signature = m.Signature,
                        ReturnType = m.ReturnType,
                        Line = parameters.IncludeLineNumbers ? m.Line : 0,
                        Column = parameters.IncludeLineNumbers ? m.Column : 0,
                        Modifiers = m.Modifiers ?? new List<string>(),
                        Parameters = m.Parameters ?? new List<string>(),
                        ContainingType = m.ContainingType
                    })
                    .OrderBy(m => m.Line)
                    .ToList();
                
                typeOverview.Methods = typeMethods;
                typeOverview.MethodCount = typeMethods.Count;
            }
            
            // Categorize by type kind
            switch (type.Kind.ToLowerInvariant())
            {
                case "class":
                    result.Classes.Add(typeOverview);
                    break;
                case "interface":
                    result.Interfaces.Add(typeOverview);
                    break;
                case "struct":
                case "record":
                    result.Structs.Add(typeOverview);
                    break;
                case "enum":
                    result.Enums.Add(typeOverview);
                    break;
                default:
                    // Add to classes as fallback
                    result.Classes.Add(typeOverview);
                    break;
            }
        }
        
        // Add standalone methods (not part of any type)
        if (parameters.IncludeMethods)
        {
            var standaloneMethods = typeData.Methods
                .Where(m => string.IsNullOrEmpty(m.ContainingType))
                .Select(m => new MethodOverview
                {
                    Name = m.Name,
                    Signature = m.Signature,
                    ReturnType = m.ReturnType,
                    Line = parameters.IncludeLineNumbers ? m.Line : 0,
                    Column = parameters.IncludeLineNumbers ? m.Column : 0,
                    Modifiers = m.Modifiers ?? new List<string>(),
                    Parameters = m.Parameters ?? new List<string>(),
                    ContainingType = m.ContainingType
                })
                .OrderBy(m => m.Line)
                .ToList();
            
            result.Methods = standaloneMethods;
        }
        
        // Calculate total symbols
        result.TotalSymbols = result.Classes.Count + result.Interfaces.Count + 
                             result.Structs.Count + result.Enums.Count + result.Methods.Count;
        
        _logger.LogDebug("Built overview: {Classes} classes, {Interfaces} interfaces, {Structs} structs, {Enums} enums, {Methods} standalone methods",
            result.Classes.Count, result.Interfaces.Count, result.Structs.Count, result.Enums.Count, result.Methods.Count);
        
        return result;
    }

    /// <summary>
    /// Attempts to retrieve type information from the Lucene index instead of re-extracting from file content.
    /// This provides significant performance improvements for indexed files.
    /// </summary>
    private async Task<TypeExtractionResult?> TryGetTypeInfoFromIndexAsync(string filePath, string? workspacePath, CancellationToken cancellationToken)
    {
        try
        {
            // If no workspace path provided, we can't search the index
            if (string.IsNullOrEmpty(workspacePath))
            {
                _logger.LogDebug("No workspace path provided, skipping index lookup for {FilePath}", filePath);
                return null;
            }

            // Convert to absolute path for consistent searching
            var absoluteFilePath = Path.GetFullPath(filePath);
            
            // Search for this specific file in the index
            var parser = new QueryParser(LUCENE_VERSION, "path", _codeAnalyzer);
            var query = parser.Parse($"\"{absoluteFilePath}\"");
            
            var searchResult = await _luceneIndexService.SearchAsync(
                workspacePath, 
                query, 
                1, // We only need one result - the exact file
                false, // No snippets needed
                cancellationToken);
            
            if (searchResult.Hits == null || searchResult.Hits.Count == 0)
            {
                _logger.LogDebug("File {FilePath} not found in index", filePath);
                return null;
            }
            
            var hit = searchResult.Hits.First();
            
            // Get the stored type_info JSON from the index
            var typeInfoJson = hit.Fields?.ContainsKey("type_info") == true ? hit.Fields["type_info"] : null;
            
            if (string.IsNullOrEmpty(typeInfoJson))
            {
                _logger.LogDebug("No type_info found in index for {FilePath}", filePath);
                return null;
            }
            
            // Deserialize the stored type information using shared options
            var typeData = JsonSerializer.Deserialize<TypeExtractionResult>(
                typeInfoJson, 
                TypeExtractionResult.DeserializationOptions);
                
            if (typeData == null)
            {
                _logger.LogWarning("Failed to deserialize type_info for {FilePath}", filePath);
                return null;
            }
            
            _logger.LogInformation("Successfully retrieved {TypeCount} types and {MethodCount} methods from index for {FilePath}", 
                typeData.Types?.Count ?? 0, typeData.Methods?.Count ?? 0, filePath);
            
            return typeData;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to retrieve type info from index for {FilePath}, will fall back to extraction", filePath);
            return null;
        }
    }
}