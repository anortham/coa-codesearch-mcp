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
using COA.CodeSearch.McpServer.Services.TypeExtraction;
using COA.CodeSearch.McpServer.Tools.Models;
using COA.CodeSearch.McpServer.ResponseBuilders;
using COA.Mcp.Framework.Interfaces;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Extracts comprehensive symbol overview from a single file using Tree-sitter
/// </summary>
public class GetSymbolsOverviewTool : CodeSearchToolBase<GetSymbolsOverviewParameters, AIOptimizedResponse<SymbolsOverviewResult>>, IPrioritizedTool
{
    private readonly ITypeExtractionService _typeExtractionService;
    private readonly IResponseCacheService _cacheService;
    private readonly IResourceStorageService _storageService;
    private readonly ICacheKeyGenerator _keyGenerator;
    // Skip custom response builder for now - use direct response
    private readonly ILogger<GetSymbolsOverviewTool> _logger;

    public GetSymbolsOverviewTool(
        IServiceProvider serviceProvider,
        ITypeExtractionService typeExtractionService,
        IResponseCacheService cacheService,
        IResourceStorageService storageService,
        ICacheKeyGenerator keyGenerator,
        ILogger<GetSymbolsOverviewTool> logger) : base(serviceProvider)
    {
        _typeExtractionService = typeExtractionService;
        _cacheService = cacheService;
        _storageService = storageService;
        _keyGenerator = keyGenerator;
        // Skip custom response builder for now
        _logger = logger;
    }

    /// <summary>
    /// This tool handles validation internally in ExecuteInternalAsync, so disable framework validation
    /// </summary>
    protected override bool ShouldValidateDataAnnotations => false;

    public override string Name => ToolNames.GetSymbolsOverview;
    public override string Description => "EXTRACT ALL SYMBOLS from any file - Get complete overview of classes, methods, interfaces without reading entire files. Tree-sitter powered for accurate type information with line numbers.";
    public override ToolCategory Category => ToolCategory.Query;
    
    // IPrioritizedTool implementation - VERY HIGH priority for file exploration
    public int Priority => 95;
    public string[] PreferredScenarios => new[] { "file_exploration", "code_understanding", "api_discovery", "type_analysis", "quick_overview" };

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
            
            _logger.LogInformation("Extracting symbols overview from {FilePath}", filePath);
            
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
            
            // Extract types using Tree-sitter
            var typeExtractionResult = _typeExtractionService.ExtractTypes(content, filePath);
            
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
}