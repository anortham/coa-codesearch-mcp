using COA.CodeSearch.McpServer.Configuration;
using COA.CodeSearch.McpServer.Constants;
using COA.CodeSearch.McpServer.Infrastructure;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Claude-optimized version of RenameSymbolTool with progressive disclosure
/// </summary>
public class RenameSymbolToolV2 : ClaudeOptimizedToolBase
{
    public override string ToolName => "rename_symbol_v2";
    public override string Description => "AI-optimized symbol renaming with risk assessment";
    public override ToolCategory Category => ToolCategory.Refactoring;
    private readonly CodeAnalysisService _workspaceService;
    private static readonly Regex IdentifierRegex = new(@"^[a-zA-Z_][a-zA-Z0-9_]*$", RegexOptions.Compiled);

    public RenameSymbolToolV2(
        ILogger<RenameSymbolToolV2> logger,
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
        string newName,
        bool preview = true,
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

            Logger.LogInformation("RenameSymbol request for {FilePath} at {Line}:{Column} to '{NewName}'", filePath, line, column, newName);

            // Validate the new name
            if (string.IsNullOrWhiteSpace(newName))
            {
                return CreateErrorResponse<object>("New name cannot be empty");
            }

            if (!IsValidIdentifier(newName))
            {
                return CreateErrorResponse<object>($"'{newName}' is not a valid identifier");
            }

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

            // Check if symbol can be renamed
            if (!CanRenameSymbol(symbol))
            {
                return CreateErrorResponse<object>($"The symbol '{symbol.Name}' cannot be renamed (it may be a built-in type or external symbol)");
            }

            // Prepare rename options
            var renameOptions = new SymbolRenameOptions(
                RenameOverloads: true,
                RenameInStrings: false,
                RenameInComments: false,
                RenameFile: false);

            // Get rename solution
            var solution = document.Project.Solution;
            
            // Quick scope estimation to avoid token explosion
            var estimatedScope = await EstimateRenameScopeAsync(symbol, solution, cancellationToken);
            Logger.LogInformation("Estimated rename scope: {References} references across {Files} files", estimatedScope.EstimatedReferences, estimatedScope.EstimatedFiles);

            // Force summary mode for operations that might cause token overflow
            // Lower thresholds to prevent token issues - even small renames can generate massive detailed responses
            if (estimatedScope.EstimatedReferences > 5 || estimatedScope.EstimatedFiles > 3)
            {
                Logger.LogInformation("Forcing summary mode due to scope estimate: {References} refs across {Files} files", 
                    estimatedScope.EstimatedReferences, estimatedScope.EstimatedFiles);
                mode = ResponseMode.Summary;
            }

            var newSolution = await Renamer.RenameSymbolAsync(
                solution,
                symbol,
                renameOptions,
                newName,
                cancellationToken);

            // Calculate changes based on mode
            RenameData renameData;
            if (mode == ResponseMode.Summary)
            {
                renameData = await CalculateRenameSummaryAsync(solution, newSolution, symbol, newName, estimatedScope, cancellationToken);
            }
            else
            {
                renameData = await CalculateRenameChangesAsync(solution, newSolution, symbol, newName, cancellationToken);
            }

            // Apply changes if not in preview mode
            if (!preview)
            {
                var applyResult = await ApplyRenameChangesAsync(newSolution, cancellationToken);
                if (!applyResult.Success)
                {
                    return CreateErrorResponse<object>(applyResult.Error ?? "Failed to apply rename changes");
                }
                renameData.Applied = true;
                renameData.ApplyMessage = applyResult.Message;
            }

            // Create AI-optimized response
            var response = CreateAiOptimizedResponse(renameData, symbol, newName, mode, cancellationToken);
            return response;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in RenameSymbolV2");
            return CreateErrorResponse<object>(ex.Message);
        }
    }

    private async Task<RenameScope> EstimateRenameScopeAsync(
        ISymbol symbol,
        Solution solution,
        CancellationToken cancellationToken)
    {
        try
        {
            // Quick reference search to estimate scope without calculating all changes
            var references = await SymbolFinder.FindReferencesAsync(symbol, solution, cancellationToken);
            
            var estimatedReferences = 0;
            var estimatedFiles = new HashSet<string>();
            
            foreach (var reference in references)
            {
                foreach (var location in reference.Locations)
                {
                    if (!location.Location.IsInSource) continue;
                    
                    estimatedReferences++;
                    if (location.Document?.FilePath != null)
                    {
                        estimatedFiles.Add(location.Document.FilePath);
                    }
                }
            }
            
            return new RenameScope
            {
                EstimatedReferences = estimatedReferences,
                EstimatedFiles = estimatedFiles.Count,
                ReferencedFiles = estimatedFiles.ToList()
            };
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to estimate rename scope, assuming small scope");
            return new RenameScope { EstimatedReferences = 1, EstimatedFiles = 1, ReferencedFiles = new List<string>() };
        }
    }

    private Task<RenameData> CalculateRenameSummaryAsync(
        Solution originalSolution,
        Solution newSolution,
        ISymbol symbol,
        string newName,
        RenameScope scope,
        CancellationToken cancellationToken)
    {
        // Create summary data without calculating all detailed changes
        var fileChanges = new List<FileChange>();
        
        // Group files by estimated changes for summary
        foreach (var filePath in scope.ReferencedFiles.Take(20)) // Limit for summary
        {
            var estimatedChanges = Math.Max(1, scope.EstimatedReferences / Math.Max(1, scope.EstimatedFiles));
            fileChanges.Add(new FileChange
            {
                FilePath = filePath,
                ChangeCount = estimatedChanges,
                Changes = new List<TextChange>() // Empty for summary mode
            });
        }

        return Task.FromResult(new RenameData
        {
            Symbol = CreateSymbolInfo(symbol),
            OldName = symbol.Name,
            NewName = newName,
            FileChanges = fileChanges,
            TotalChanges = scope.EstimatedReferences,
            AffectedFiles = scope.EstimatedFiles,
            Preview = true,
            Applied = false,
            IsSummary = true
        });
    }

    private async Task<RenameData> CalculateRenameChangesAsync(
        Solution originalSolution,
        Solution newSolution,
        ISymbol symbol,
        string newName,
        CancellationToken cancellationToken)
    {
        var changes = new List<FileChange>();
        var changedDocuments = new Dictionary<string, List<TextChange>>();

        // Get only the documents that actually changed
        var solutionChanges = newSolution.GetChanges(originalSolution);
        
        foreach (var projectChanges in solutionChanges.GetProjectChanges())
        {
            foreach (var documentId in projectChanges.GetChangedDocuments())
            {
                var originalDoc = originalSolution.GetDocument(documentId);
                var newDoc = newSolution.GetDocument(documentId);
            
            if (originalDoc == null || newDoc == null) continue;

            var originalText = await originalDoc.GetTextAsync(cancellationToken);
            var newText = await newDoc.GetTextAsync(cancellationToken);
            var textChanges = newText.GetTextChanges(originalText);

            if (textChanges.Any())
            {
                var filePath = originalDoc.FilePath ?? "";
                changedDocuments[filePath] = new List<TextChange>();

                foreach (var change in textChanges)
                {
                    var lineSpan = originalText.Lines.GetLinePositionSpan(change.Span);
                    var line = originalText.Lines[lineSpan.Start.Line];
                    var oldLineText = line.ToString();
                    
                    // Apply the change to get new line text
                    var startInLine = Math.Max(0, change.Span.Start - line.Start);
                    var endInLine = Math.Min(oldLineText.Length, change.Span.End - line.Start);
                    
                    var newLineText = oldLineText;
                    if (startInLine < oldLineText.Length)
                    {
                        newLineText = oldLineText.Substring(0, startInLine) +
                                     (change.NewText ?? "") +
                                     (endInLine < oldLineText.Length ? oldLineText.Substring(endInLine) : "");
                    }

                    changedDocuments[filePath].Add(new TextChange
                    {
                        FilePath = filePath,
                        Line = lineSpan.Start.Line + 1,
                        Column = lineSpan.Start.Character + 1,
                        OldText = originalText.GetSubText(change.Span).ToString(),
                        NewText = change.NewText ?? "",
                        PreviewLine = newLineText.Trim()
                    });
                }

                changes.Add(new FileChange
                {
                    FilePath = filePath,
                    ChangeCount = textChanges.Count(),
                    Changes = changedDocuments[filePath]
                });
            }
            }
        }

        return new RenameData
        {
            Symbol = CreateSymbolInfo(symbol),
            OldName = symbol.Name,
            NewName = newName,
            FileChanges = changes,
            TotalChanges = changes.Sum(fc => fc.ChangeCount),
            AffectedFiles = changes.Count,
            Preview = true,
            Applied = false
        };
    }

    private object CreateAiOptimizedResponse(RenameData data, ISymbol symbol, string newName, ResponseMode mode, CancellationToken cancellationToken)
    {
        // Calculate risk assessment
        var risk = AssessRenameRisk(data, symbol);
        
        // Get top hotspots
        var hotspots = data.FileChanges
            .OrderByDescending(fc => fc.ChangeCount)
            .Take(5)
            .Select(fc => new { file = fc.FilePath, refs = fc.ChangeCount })
            .ToList();

        // Generate actions with priority ordering
        var actions = new List<object>();
        
        if (data.TotalChanges > 10)
        {
            actions.Add(new 
            { 
                id = "review_hotspots", 
                cmd = new { detailLevel = "hotspots" }, 
                tokens = Math.Min(3000, hotspots.Count * 600),
                priority = "recommended"
            });
        }

        if (!data.Applied && data.Preview)
        {
            actions.Add(new 
            { 
                id = "apply", 
                cmd = new { preview = false }, 
                tokens = 200,
                priority = risk == "high" ? "caution" : "normal"
            });
        }

        if (mode == ResponseMode.Summary && data.TotalChanges < 100)
        {
            actions.Add(new 
            { 
                id = "full_details", 
                cmd = new { responseMode = "full" }, 
                tokens = EstimateFullResponseTokens(data),
                priority = "available"
            });
        }

        return new
        {
            success = true,
            operation = ToolNames.RenameSymbol,
            symbol = new
            {
                old = symbol.Name,
                @new = newName,
                type = symbol.Kind.ToString().ToLowerInvariant(),
                scope = symbol.DeclaredAccessibility.ToString().ToLowerInvariant()
            },
            impact = new
            {
                refs = data.TotalChanges,
                files = data.AffectedFiles,
                risk = risk
            },
            preview = data.Preview,
            applied = data.Applied,
            hotspots = hotspots,
            actions = actions,
            meta = new
            {
                mode = mode.ToString().ToLowerInvariant(),
                tokens = SizeEstimator.EstimateTokens(new { symbol = data.Symbol, changes = data.FileChanges.Take(5) }),
                cached = $"rename_{Guid.NewGuid().ToString("N")[..8]}"
            }
        };
    }

    private string AssessRenameRisk(RenameData data, ISymbol symbol)
    {
        if (data.TotalChanges > 50 || data.AffectedFiles > 15)
            return "high";
        
        if (symbol.DeclaredAccessibility == Accessibility.Public && data.AffectedFiles > 5)
            return "high";
            
        if (data.TotalChanges > 20 || data.AffectedFiles > 8)
            return "medium";
            
        return "low";
    }

    private int EstimateFullResponseTokens(RenameData data)
    {
        // Conservative estimate based on typical change details
        return Math.Min(25000, data.TotalChanges * 150 + data.AffectedFiles * 100);
    }

    private ClaudeSummaryData CreateSummaryData(RenameData data, ISymbol symbol, string newName)
    {
        var insights = new List<string>();
        
        // Analyze impact
        if (data.TotalChanges > 100)
        {
            insights.Add($"High-impact rename: {data.TotalChanges} occurrences across {data.AffectedFiles} files");
        }
        else if (data.TotalChanges > 50)
        {
            insights.Add($"Moderate-impact rename: {data.TotalChanges} occurrences in {data.AffectedFiles} files");
        }
        
        // Symbol type insights
        if (symbol.Kind == SymbolKind.NamedType)
        {
            insights.Add($"Renaming {symbol.DeclaredAccessibility.ToString().ToLower()} class/interface will affect all usages and derived types");
        }
        else if (symbol.Kind == SymbolKind.Method)
        {
            insights.Add("Method rename - check for interface implementations and overrides");
        }
        else if (symbol.Kind == SymbolKind.Property)
        {
            insights.Add("Property rename - may affect data binding and serialization");
        }

        // File pattern insights
        var filePaths = data.FileChanges.Select(fc => fc.FilePath).ToList();
        insights.AddRange(SmartAnalysisHelpers.AnalyzeFilePatterns(filePaths));

        // Hotspots
        var hotspots = IdentifyHotspots(
            data.FileChanges,
            fc => fc.FilePath,
            fcs => fcs.Sum(fc => fc.ChangeCount),
            maxHotspots: 5);

        // Categories
        var categories = CategorizeFiles(data.FileChanges, fc => fc.FilePath);
        
        // Add category-specific insights
        if (categories.ContainsKey("tests") && categories["tests"].Files > 0)
        {
            insights.Add($"Test files affected ({categories["tests"].Files} files) - ensure test names remain meaningful");
        }

        return new ClaudeSummaryData
        {
            Overview = new Overview
            {
                TotalItems = data.TotalChanges,
                AffectedFiles = data.AffectedFiles,
                EstimatedFullResponseTokens = SizeEstimator.EstimateTokens(data),
                KeyInsights = insights
            },
            ByCategory = categories,
            Hotspots = hotspots,
            Preview = new ChangePreview
            {
                TopChanges = data.FileChanges
                    .SelectMany(fc => fc.Changes.Select(c => new { File = fc, Change = c }))
                    .Take(5)
                    .Select(x => new PreviewItem
                    {
                        File = x.File.FilePath,
                        Line = x.Change.Line,
                        Preview = x.Change.PreviewLine,
                        Context = $"{x.Change.OldText} → {x.Change.NewText}"
                    })
                    .ToList(),
                FullContext = false,
                GetFullContextCommand = new { detailLevel = "changes", maxChanges = 20 }
            }
        };
    }

    private async Task<ApplyResult> ApplyRenameChangesAsync(Solution newSolution, CancellationToken cancellationToken)
    {
        try
        {
            var workspace = newSolution.Workspace;
            var applied = workspace.TryApplyChanges(newSolution);
            
            if (!applied)
            {
                return new ApplyResult 
                { 
                    Success = false, 
                    Error = "Failed to apply changes to workspace" 
                };
            }

            // Save all changed documents
            // Get only the documents that actually changed
            var changedDocs = new List<DocumentId>();
            var solutionChanges = newSolution.GetChanges(workspace.CurrentSolution);
            
            foreach (var projectChanges in solutionChanges.GetProjectChanges())
            {
                changedDocs.AddRange(projectChanges.GetChangedDocuments());
            }
            foreach (var docId in changedDocs)
            {
                var doc = newSolution.GetDocument(docId);
                if (doc?.FilePath != null)
                {
                    var text = await doc.GetTextAsync(cancellationToken);
                    await File.WriteAllTextAsync(doc.FilePath, text.ToString(), cancellationToken);
                }
            }

            return new ApplyResult 
            { 
                Success = true, 
                Message = $"Successfully renamed symbol across {changedDocs.Count()} files" 
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error applying rename changes");
            return new ApplyResult 
            { 
                Success = false, 
                Error = $"Error applying changes: {ex.Message}" 
            };
        }
    }

    private bool IsValidIdentifier(string name)
    {
        return IdentifierRegex.IsMatch(name);
    }

    private bool CanRenameSymbol(ISymbol symbol)
    {
        // Cannot rename symbols from metadata
        if (symbol.Locations.All(loc => loc.IsInMetadata))
            return false;

        // Cannot rename certain special symbols
        if (symbol.IsImplicitlyDeclared || symbol.IsExtern)
            return false;

        // Cannot rename tuple elements
        if (symbol is IFieldSymbol field && field.CorrespondingTupleField != null)
            return false;

        return true;
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
        if (data is RenameData renameData)
        {
            return renameData.TotalChanges;
        }
        return 0;
    }

    protected override NextActions GenerateNextActions<T>(T data, ResponseMode currentMode, ResponseMetadata metadata)
    {
        var actions = base.GenerateNextActions(data, currentMode, metadata);
        
        if (currentMode == ResponseMode.Summary && data is RenameData renameData)
        {
            actions.Recommended.Clear();
            
            // Recommend reviewing hotspots
            actions.Recommended.Add(new RecommendedAction
            {
                Action = "review_hotspots",
                Description = "Review files with the most changes",
                Reason = "Focus on files with highest concentration of renames",
                EstimatedTokens = 3000,
                Priority = "high",
                Command = new 
                { 
                    detailLevel = "hotspots",
                    detailRequestToken = metadata.DetailRequestToken,
                    maxFiles = 5
                }
            });

            // If in preview mode, recommend applying
            if (renameData.Preview && !renameData.Applied)
            {
                actions.Recommended.Add(new RecommendedAction
                {
                    Action = "apply_rename",
                    Description = "Apply the rename operation",
                    Reason = "Preview looks good, ready to apply changes",
                    EstimatedTokens = 500,
                    Priority = "medium",
                    Command = new
                    {
                        preview = false,
                        // Include original parameters for re-execution
                        warningMessage = "This will modify files on disk"
                    }
                });
            }

            // Recommend reviewing test changes
            if (renameData.FileChanges.Any(fc => fc.FilePath.Contains("test", StringComparison.OrdinalIgnoreCase)))
            {
                actions.Recommended.Add(new RecommendedAction
                {
                    Action = "review_test_changes",
                    Description = "Review changes in test files",
                    Reason = "Ensure test names remain descriptive",
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
        
        if (data is RenameData renameData)
        {
            var filePaths = renameData.FileChanges.Select(fc => fc.FilePath).ToList();
            var (impact, riskFactors) = SmartAnalysisHelpers.AssessImpact(filePaths, renameData.TotalChanges);
            
            context.Impact = impact;
            context.RiskFactors = riskFactors;
            
            // Add rename-specific risk factors
            if (renameData.Symbol?.Kind == "Property")
            {
                context.RiskFactors.Add("Property rename may affect serialization/data binding");
            }
            
            if (renameData.Symbol?.Accessibility == "Public")
            {
                context.RiskFactors.Add("Public API change - may affect external consumers");
            }
            
            // Suggestions
            context.Suggestions = new List<string>();
            
            if (renameData.TotalChanges > 100)
            {
                context.Suggestions.Add("Consider running tests after applying this large rename");
            }
            
            if (renameData.Preview && !renameData.Applied)
            {
                context.Suggestions.Add("Review the preview carefully before applying");
            }
        }
        
        return context;
    }

    private Task<object> HandleDetailRequestAsync(DetailRequest request, CancellationToken cancellationToken)
    {
        if (DetailCache == null || string.IsNullOrEmpty(request.DetailRequestToken))
        {
            return Task.FromResult<object>(CreateErrorResponse<object>("Detail request token is required"));
        }

        var cachedData = DetailCache.GetDetailData<RenameData>(request.DetailRequestToken);
        if (cachedData == null)
        {
            return Task.FromResult<object>(CreateErrorResponse<object>("Invalid or expired detail request token"));
        }

        var result = request.DetailLevelId switch
        {
            "hotspots" => GetHotspotDetails(cachedData, request),
            "category" => GetCategoryDetails(cachedData, request),
            "changes" => GetChangeDetails(cachedData, request),
            "files" => GetFileDetails(cachedData, request),
            _ => CreateErrorResponse<object>($"Unknown detail level: {request.DetailLevelId}")
        };
        
        return Task.FromResult(result);
    }

    private object GetHotspotDetails(RenameData data, DetailRequest request)
    {
        var maxFiles = 5;
        if (request.AdditionalInfo?.TryGetValue("maxFiles", out var maxFilesObj) == true)
        {
            maxFiles = Convert.ToInt32(maxFilesObj);
        }

        var hotspotFiles = data.FileChanges
            .OrderByDescending(fc => fc.ChangeCount)
            .Take(maxFiles)
            .ToList();

        return new
        {
            success = true,
            detailLevel = "hotspots",
            files = hotspotFiles,
            summary = new
            {
                totalFiles = hotspotFiles.Count,
                totalChanges = hotspotFiles.Sum(fc => fc.ChangeCount),
                averageChangesPerFile = hotspotFiles.Any() ? hotspotFiles.Average(fc => fc.ChangeCount) : 0
            },
            metadata = new ResponseMetadata
            {
                TotalResults = hotspotFiles.Sum(fc => fc.ChangeCount),
                ReturnedResults = hotspotFiles.Sum(fc => fc.ChangeCount),
                EstimatedTokens = SizeEstimator.EstimateTokens(hotspotFiles)
            }
        };
    }

    private object GetCategoryDetails(RenameData data, DetailRequest request)
    {
        var category = request.AdditionalInfo?["category"]?.ToString() ?? "controllers";
        
        var categoryFiles = data.FileChanges
            .Where(fc => IsFileInCategory(fc.FilePath, category))
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
                totalChanges = categoryFiles.Sum(fc => fc.ChangeCount)
            },
            metadata = new ResponseMetadata
            {
                TotalResults = categoryFiles.Sum(fc => fc.ChangeCount),
                ReturnedResults = categoryFiles.Sum(fc => fc.ChangeCount),
                EstimatedTokens = SizeEstimator.EstimateTokens(categoryFiles)
            }
        };
    }

    private object GetChangeDetails(RenameData data, DetailRequest request)
    {
        var maxChanges = Convert.ToInt32(request.MaxResults ?? 20);
        var targetFiles = request.TargetItems ?? new List<string>();
        
        var changes = data.FileChanges
            .Where(fc => !targetFiles.Any() || targetFiles.Contains(fc.FilePath))
            .SelectMany(fc => fc.Changes.Select(c => new
            {
                file = fc.FilePath,
                line = c.Line,
                column = c.Column,
                oldText = c.OldText,
                newText = c.NewText,
                preview = c.PreviewLine
            }))
            .Take(maxChanges)
            .ToList();

        return new
        {
            success = true,
            detailLevel = "changes",
            changes = changes,
            metadata = new ResponseMetadata
            {
                TotalResults = data.TotalChanges,
                ReturnedResults = changes.Count,
                EstimatedTokens = SizeEstimator.EstimateTokens(changes)
            }
        };
    }

    private object GetFileDetails(RenameData data, DetailRequest request)
    {
        var targetFiles = request.TargetItems ?? new List<string>();
        
        var files = data.FileChanges
            .Where(fc => !targetFiles.Any() || targetFiles.Contains(fc.FilePath))
            .Select(fc => new
            {
                filePath = fc.FilePath,
                changeCount = fc.ChangeCount,
                firstChange = fc.Changes.FirstOrDefault()
            })
            .ToList();

        return new
        {
            success = true,
            detailLevel = "files",
            files = files,
            metadata = new ResponseMetadata
            {
                TotalResults = data.FileChanges.Count,
                ReturnedResults = files.Count,
                EstimatedTokens = SizeEstimator.EstimateTokens(files)
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

    // Data structures
    private class RenameData
    {
        public SymbolInfo? Symbol { get; set; }
        public string OldName { get; set; } = "";
        public string NewName { get; set; } = "";
        public List<FileChange> FileChanges { get; set; } = new();
        public int TotalChanges { get; set; }
        public int AffectedFiles { get; set; }
        public bool Preview { get; set; }
        public bool Applied { get; set; }
        public string? ApplyMessage { get; set; }
        public bool IsSummary { get; set; }
    }

    private class RenameScope
    {
        public int EstimatedReferences { get; set; }
        public int EstimatedFiles { get; set; }
        public List<string> ReferencedFiles { get; set; } = new();
    }

    private class FileChange
    {
        public string FilePath { get; set; } = "";
        public int ChangeCount { get; set; }
        public List<TextChange> Changes { get; set; } = new();
    }

    private class TextChange
    {
        public string FilePath { get; set; } = "";
        public int Line { get; set; }
        public int Column { get; set; }
        public string OldText { get; set; } = "";
        public string NewText { get; set; } = "";
        public string PreviewLine { get; set; } = "";
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

    private class ApplyResult
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? Error { get; set; }
    }
}