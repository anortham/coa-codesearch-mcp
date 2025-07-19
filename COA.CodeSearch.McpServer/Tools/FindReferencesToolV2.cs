using COA.CodeSearch.McpServer.Configuration;
using COA.CodeSearch.McpServer.Infrastructure;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Claude-optimized version of FindReferencesTool with progressive disclosure
/// </summary>
public class FindReferencesToolV2 : ClaudeOptimizedToolBase
{
    private readonly CodeAnalysisService _workspaceService;

    public FindReferencesToolV2(
        ILogger<FindReferencesToolV2> logger,
        CodeAnalysisService workspaceService,
        IResponseSizeEstimator sizeEstimator,
        IResultTruncator truncator,
        IOptions<ResponseLimitOptions> options,
        IDetailRequestCache detailCache)
        : base(sizeEstimator, truncator, options, logger, detailCache)
    {
        _workspaceService = workspaceService;
    }

    public async Task<object> ExecuteAsync(
        string filePath,
        int line,
        int column,
        bool includeDeclaration = true,
        ResponseMode mode = ResponseMode.Full,
        DetailRequest? detailRequest = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Handle detail requests
            if (detailRequest != null && DetailCache != null)
            {
                return await HandleDetailRequestAsync(detailRequest, cancellationToken);
            }

            Logger.LogInformation("FindReferences request for {FilePath} at {Line}:{Column}", filePath, line, column);

            // Get the document
            var document = await _workspaceService.GetDocumentAsync(filePath, cancellationToken);
            if (document == null)
            {
                return CreateErrorResponse<object>($"Could not find document: {filePath}");
            }

            // Get the source text and find the position
            var sourceText = await document.GetTextAsync(cancellationToken);
            var position = sourceText.Lines.GetPosition(new Microsoft.CodeAnalysis.Text.LinePosition(line - 1, column - 1));

            // Get semantic model
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
            if (semanticModel == null)
            {
                return CreateErrorResponse<object>("Could not get semantic model for document");
            }

            // Find the symbol at the position
            var symbol = await SymbolFinder.FindSymbolAtPositionAsync(semanticModel, position, document.Project.Solution.Workspace, cancellationToken);
            if (symbol == null)
            {
                return CreateErrorResponse<object>("No symbol found at the specified position");
            }

            // Find all references
            var references = await SymbolFinder.FindReferencesAsync(symbol, document.Project.Solution, cancellationToken);

            // Process references into our structure
            var referenceData = await ProcessReferencesAsync(references, includeDeclaration, cancellationToken);

            // Create Claude-optimized response
            var response = await CreateClaudeResponseAsync(
                referenceData,
                mode,
                data => CreateSummaryData(data, symbol),
                cancellationToken);

            // Add symbol info to full response types
            if (response is ClaudeOptimizedResponse<object> claudeResponse && 
                claudeResponse.Data is FindReferencesData fullData)
            {
                fullData.Symbol = CreateSymbolInfo(symbol);
            }

            return response;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in FindReferencesV2");
            return CreateErrorResponse<object>(ex.Message);
        }
    }

    private async Task<FindReferencesData> ProcessReferencesAsync(
        IEnumerable<ReferencedSymbol> references,
        bool includeDeclaration,
        CancellationToken cancellationToken)
    {
        var allReferences = new List<ReferenceInfo>();

        foreach (var reference in references)
        {
            foreach (var location in reference.Locations)
            {
                // Check if this is a definition
                var isDefinition = reference.Definition.Locations.Any(l => 
                    l.IsInSource && 
                    l.SourceTree?.FilePath == location.Document.FilePath && 
                    l.SourceSpan == location.Location.SourceSpan);
                
                // Skip declarations if not requested
                if (!includeDeclaration && isDefinition)
                    continue;

                // Skip if not in source
                if (!location.Location.IsInSource)
                    continue;

                var refDoc = location.Document;
                var span = location.Location.SourceSpan;
                var text = await refDoc.GetTextAsync(cancellationToken);
                var lineSpan = text.Lines.GetLinePositionSpan(span);
                var lineText = text.Lines[lineSpan.Start.Line].ToString();

                allReferences.Add(new ReferenceInfo
                {
                    FilePath = refDoc.FilePath ?? "",
                    Line = lineSpan.Start.Line + 1,
                    Column = lineSpan.Start.Character + 1,
                    EndLine = lineSpan.End.Line + 1,
                    EndColumn = lineSpan.End.Character + 1,
                    PreviewText = lineText.Trim(),
                    IsDefinition = isDefinition,
                    IsImplicit = location.IsImplicit,
                    Kind = DetermineReferenceKind(location, lineText, isDefinition)
                });
            }
        }

        return new FindReferencesData
        {
            References = allReferences,
            GroupedByFile = allReferences
                .GroupBy(r => r.FilePath)
                .ToDictionary(
                    g => g.Key,
                    g => g.ToList()
                )
        };
    }

    private ClaudeSummaryData CreateSummaryData(FindReferencesData data, ISymbol symbol)
    {
        var filePaths = data.References.Select(r => r.FilePath).Distinct().ToList();
        
        // Generate insights
        var insights = new List<string>();
        
        // Analyze reference patterns
        var definitionCount = data.References.Count(r => r.IsDefinition);
        var usageCount = data.References.Count - definitionCount;
        
        if (usageCount == 0)
        {
            insights.Add("Symbol is defined but never used - consider removing");
        }
        else if (usageCount < 3)
        {
            insights.Add($"Symbol has only {usageCount} usage(s) - consider inlining");
        }
        else if (usageCount > 50)
        {
            insights.Add($"Symbol is heavily used ({usageCount} references) - changes have wide impact");
        }

        // Add file pattern insights
        insights.AddRange(SmartAnalysisHelpers.AnalyzeFilePatterns(filePaths));

        // Identify hotspots
        var hotspots = IdentifyHotspots(
            data.References,
            r => r.FilePath,
            g => g.Count(),
            maxHotspots: 5);

        // Categorize files
        var categories = CategorizeFiles(data.References, r => r.FilePath);

        // Analyze reference kinds
        var kindSummary = data.References
            .GroupBy(r => r.Kind)
            .ToDictionary(g => g.Key, g => g.Count());

        return new ClaudeSummaryData
        {
            Overview = new Overview
            {
                TotalItems = data.References.Count,
                AffectedFiles = filePaths.Count,
                EstimatedFullResponseTokens = SizeEstimator.EstimateTokens(data),
                KeyInsights = insights
            },
            ByCategory = categories,
            Hotspots = hotspots,
            Preview = new ChangePreview
            {
                TopChanges = SmartAnalysisHelpers.CreatePreviewItems(
                    data.References.Take(5),
                    r => r.FilePath,
                    r => r.Line,
                    r => $"{r.Kind}: {r.PreviewText}",
                    maxItems: 5
                ),
                FullContext = false,
                GetFullContextCommand = new { detailLevel = "preview", includeContext = true }
            }
        };
    }

    private string DetermineReferenceKind(ReferenceLocation location, string lineText, bool isDefinition = false)
    {
        if (isDefinition)
            return "Definition";
        
        if (location.IsImplicit)
            return "Implicit";
        
        // Analyze the line text to determine usage type
        var trimmed = lineText.Trim();
        
        if (trimmed.Contains(" = ") || trimmed.Contains("="))
            return "Assignment";
        
        if (trimmed.Contains("new ") || trimmed.Contains("new("))
            return "Instantiation";
        
        if (trimmed.Contains("(") && trimmed.Contains(")"))
            return "MethodCall";
        
        if (trimmed.Contains(": "))
            return "TypeReference";
        
        return "Usage";
    }

    private SymbolInfo CreateSymbolInfo(ISymbol symbol)
    {
        return new SymbolInfo
        {
            Name = symbol.Name,
            Kind = symbol.Kind.ToString(),
            ContainerName = symbol.ContainingType?.ToDisplayString() ?? symbol.ContainingNamespace?.ToDisplayString() ?? "",
            Accessibility = symbol.DeclaredAccessibility.ToString(),
            IsStatic = symbol.IsStatic,
            IsAbstract = symbol.IsAbstract,
            IsVirtual = symbol.IsVirtual,
            Documentation = symbol.GetDocumentationCommentXml()
        };
    }

    protected override int GetTotalResults<T>(T data)
    {
        if (data is FindReferencesData refData)
        {
            return refData.References.Count;
        }
        return 0;
    }

    protected override NextActions GenerateNextActions<T>(T data, ResponseMode currentMode, ResponseMetadata metadata)
    {
        var actions = base.GenerateNextActions(data, currentMode, metadata);
        
        if (currentMode == ResponseMode.Summary && data is FindReferencesData refData)
        {
            // Add specific actions for find references
            actions.Recommended.Clear(); // Clear base recommendations
            
            // Recommend reviewing hotspots first
            actions.Recommended.Add(new RecommendedAction
            {
                Action = "review_hotspots",
                Description = "Review files with the most references",
                Reason = "Focus on high-impact areas first",
                EstimatedTokens = 3000,
                Priority = "high",
                Command = new 
                { 
                    detailLevel = "hotspots",
                    detailRequestToken = metadata.DetailRequestToken,
                    maxFiles = 5
                }
            });

            // Add category-specific actions
            if (refData.GroupedByFile.Values.Any(refs => refs.Any(r => r.FilePath.Contains("test", StringComparison.OrdinalIgnoreCase))))
            {
                actions.Recommended.Add(new RecommendedAction
                {
                    Action = "review_test_impact",
                    Description = "Review test file references",
                    Reason = "Tests are affected and may need updates",
                    EstimatedTokens = 2000,
                    Priority = "medium",
                    Command = new
                    {
                        detailLevel = "category",
                        category = "tests",
                        detailRequestToken = metadata.DetailRequestToken
                    }
                });
            }
        }
        
        return actions;
    }

    protected override ResultContext AnalyzeResultContext<T>(T data)
    {
        var context = base.AnalyzeResultContext(data);
        
        if (data is FindReferencesData refData)
        {
            var filePaths = refData.References.Select(r => r.FilePath).Distinct().ToList();
            var (impact, riskFactors) = SmartAnalysisHelpers.AssessImpact(filePaths, refData.References.Count);
            
            context.Impact = impact;
            context.RiskFactors = riskFactors;
            
            // Add reference-specific suggestions
            context.Suggestions = SmartAnalysisHelpers.GenerateSuggestions(
                filePaths,
                CategorizeFiles(refData.References, r => r.FilePath),
                IdentifyHotspots(refData.References, r => r.FilePath, g => g.Count())
            );
            
            // Add insights about usage patterns
            var usageTypes = refData.References.GroupBy(r => r.Kind).ToDictionary(g => g.Key, g => g.Count());
            if (usageTypes.ContainsKey("Assignment") && usageTypes["Assignment"] > 5)
            {
                context.KeyInsights.Add($"Symbol is frequently reassigned ({usageTypes["Assignment"]} times) - review for mutability concerns");
            }
        }
        
        return context;
    }

    private async Task<object> HandleDetailRequestAsync(DetailRequest request, CancellationToken cancellationToken)
    {
        if (DetailCache == null || string.IsNullOrEmpty(request.DetailRequestToken))
        {
            return CreateErrorResponse<object>("Detail request token is required");
        }

        var cachedData = DetailCache.GetDetailData<FindReferencesData>(request.DetailRequestToken);
        if (cachedData == null)
        {
            return CreateErrorResponse<object>("Invalid or expired detail request token");
        }

        return request.DetailLevelId switch
        {
            "hotspots" => GetHotspotDetails(cachedData, request),
            "category" => GetCategoryDetails(cachedData, request),
            "files" => GetFileDetails(cachedData, request),
            "preview" => GetPreviewDetails(cachedData, request),
            _ => CreateErrorResponse<object>($"Unknown detail level: {request.DetailLevelId}")
        };
    }

    private object GetHotspotDetails(FindReferencesData data, DetailRequest request)
    {
        var maxFiles = 5;
        if (request.AdditionalInfo?.TryGetValue("maxFiles", out var maxFilesObj) == true)
        {
            maxFiles = Convert.ToInt32(maxFilesObj);
        }

        var hotspotFiles = data.GroupedByFile
            .OrderByDescending(kvp => kvp.Value.Count)
            .Take(maxFiles)
            .Select(kvp => new
            {
                filePath = kvp.Key,
                references = kvp.Value,
                summary = new
                {
                    total = kvp.Value.Count,
                    byKind = kvp.Value.GroupBy(r => r.Kind).ToDictionary(g => g.Key, g => g.Count())
                }
            })
            .ToList();

        return new
        {
            success = true,
            detailLevel = "hotspots",
            files = hotspotFiles,
            metadata = new ResponseMetadata
            {
                TotalResults = hotspotFiles.Sum(f => f.references.Count),
                ReturnedResults = hotspotFiles.Sum(f => f.references.Count),
                EstimatedTokens = SizeEstimator.EstimateTokens(hotspotFiles)
            }
        };
    }

    private object GetCategoryDetails(FindReferencesData data, DetailRequest request)
    {
        var category = request.AdditionalInfo?["category"]?.ToString() ?? "controllers";
        
        var categoryFiles = data.GroupedByFile
            .Where(kvp => IsFileInCategory(kvp.Key, category))
            .Select(kvp => new
            {
                filePath = kvp.Key,
                references = kvp.Value,
                referenceCount = kvp.Value.Count
            })
            .ToList();

        return new
        {
            success = true,
            detailLevel = "category",
            category = category,
            files = categoryFiles,
            summary = new
            {
                totalFiles = categoryFiles.Count,
                totalReferences = categoryFiles.Sum(f => f.referenceCount)
            },
            metadata = new ResponseMetadata
            {
                TotalResults = categoryFiles.Sum(f => f.references.Count),
                ReturnedResults = categoryFiles.Sum(f => f.references.Count),
                EstimatedTokens = SizeEstimator.EstimateTokens(categoryFiles)
            }
        };
    }

    private object GetFileDetails(FindReferencesData data, DetailRequest request)
    {
        var targetFiles = request.TargetItems ?? new List<string>();
        
        var fileDetails = data.GroupedByFile
            .Where(kvp => !targetFiles.Any() || targetFiles.Contains(kvp.Key))
            .Select(kvp => new
            {
                filePath = kvp.Key,
                referenceCount = kvp.Value.Count,
                firstReference = kvp.Value.FirstOrDefault()
            })
            .ToList();

        return new
        {
            success = true,
            detailLevel = "files",
            files = fileDetails,
            metadata = new ResponseMetadata
            {
                TotalResults = fileDetails.Count,
                ReturnedResults = fileDetails.Count,
                EstimatedTokens = SizeEstimator.EstimateTokens(fileDetails)
            }
        };
    }

    private object GetPreviewDetails(FindReferencesData data, DetailRequest request)
    {
        var includeContext = request.AdditionalInfo?["includeContext"]?.ToString() == "true";
        var targetFiles = request.TargetItems ?? new List<string>();
        
        var references = data.References
            .Where(r => !targetFiles.Any() || targetFiles.Contains(r.FilePath))
            .Take(request.MaxResults ?? 20)
            .Select(r => new
            {
                file = r.FilePath,
                line = r.Line,
                column = r.Column,
                kind = r.Kind,
                preview = r.PreviewText,
                context = includeContext ? GetExtendedContext(r) : null
            })
            .ToList();

        return new
        {
            success = true,
            detailLevel = "preview",
            includeContext = includeContext,
            references = references,
            metadata = new ResponseMetadata
            {
                TotalResults = data.References.Count,
                ReturnedResults = references.Count,
                EstimatedTokens = SizeEstimator.EstimateTokens(references)
            }
        };
    }

    private bool IsFileInCategory(string filePath, string category)
    {
        var lower = filePath.ToLowerInvariant();
        return category.ToLowerInvariant() switch
        {
            "controllers" => lower.Contains("controller"),
            "services" => lower.Contains("service"),
            "tests" => lower.Contains("test") || lower.Contains("spec"),
            "pages" => lower.EndsWith(".razor") || lower.Contains("/pages/"),
            "models" => lower.Contains("model") || lower.Contains("dto"),
            _ => false
        };
    }

    private string? GetExtendedContext(ReferenceInfo reference)
    {
        // In a real implementation, this would fetch surrounding lines
        // For now, return a descriptive context
        return $"{reference.Kind} at line {reference.Line}";
    }

    // Data structures
    private class FindReferencesData
    {
        public List<ReferenceInfo> References { get; set; } = new();
        public Dictionary<string, List<ReferenceInfo>> GroupedByFile { get; set; } = new();
        public SymbolInfo? Symbol { get; set; }
    }

    private class ReferenceInfo
    {
        public string FilePath { get; set; } = "";
        public int Line { get; set; }
        public int Column { get; set; }
        public int EndLine { get; set; }
        public int EndColumn { get; set; }
        public string PreviewText { get; set; } = "";
        public bool IsDefinition { get; set; }
        public bool IsImplicit { get; set; }
        public string Kind { get; set; } = "Usage";
    }

    private class SymbolInfo
    {
        public string Name { get; set; } = "";
        public string Kind { get; set; } = "";
        public string ContainerName { get; set; } = "";
        public string Accessibility { get; set; } = "";
        public bool IsStatic { get; set; }
        public bool IsAbstract { get; set; }
        public bool IsVirtual { get; set; }
        public string? Documentation { get; set; }
    }
}