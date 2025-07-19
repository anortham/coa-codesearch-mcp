using COA.CodeSearch.McpServer.Configuration;
using COA.CodeSearch.McpServer.Infrastructure;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace COA.CodeSearch.McpServer.Tools;

public class RenameSymbolTool : McpToolBase
{
    private readonly CodeAnalysisService _workspaceService;

    public RenameSymbolTool(
        ILogger<RenameSymbolTool> logger, 
        CodeAnalysisService workspaceService,
        IResponseSizeEstimator sizeEstimator,
        IResultTruncator truncator,
        IOptions<ResponseLimitOptions> options)
        : base(sizeEstimator, truncator, options, logger)
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
        PaginationParams? pagination = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            Logger.LogInformation("RenameSymbol request for {FilePath} at {Line}:{Column} to '{NewName}'", filePath, line, column, newName);

            // Validate the new name
            if (string.IsNullOrWhiteSpace(newName))
            {
                return new
                {
                    success = false,
                    error = "New name cannot be empty"
                };
            }

            if (!IsValidIdentifier(newName))
            {
                return new
                {
                    success = false,
                    error = $"'{newName}' is not a valid identifier"
                };
            }

            // Get the document
            var document = await _workspaceService.GetDocumentAsync(filePath, cancellationToken);
            if (document == null)
            {
                return new
                {
                    success = false,
                    error = $"Could not find document: {filePath}"
                };
            }

            // Get the source text and find the position
            var sourceText = await document.GetTextAsync(cancellationToken);
            var position = sourceText.Lines.GetPosition(new Microsoft.CodeAnalysis.Text.LinePosition(line - 1, column - 1));

            // Get semantic model
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
            if (semanticModel == null)
            {
                return new
                {
                    success = false,
                    error = "Could not get semantic model for document"
                };
            }

            // Find the symbol at the position
            var symbol = await SymbolFinder.FindSymbolAtPositionAsync(semanticModel, position, document.Project.Solution.Workspace, cancellationToken);
            if (symbol == null)
            {
                return new
                {
                    success = false,
                    error = "No symbol found at the specified position"
                };
            }

            // Check if symbol can be renamed
            if (!CanRenameSymbol(symbol))
            {
                return new
                {
                    success = false,
                    error = $"Symbol '{symbol.Name}' cannot be renamed. It may be from metadata or a special symbol."
                };
            }

            var solution = document.Project.Solution;
            var options = new SymbolRenameOptions(
                RenameOverloads: false,
                RenameInStrings: false,
                RenameInComments: false,
                RenameFile: false);

            // Perform the rename
            var renameResult = await Renamer.RenameSymbolAsync(
                solution,
                symbol,
                options,
                newName,
                cancellationToken);

            // Get all the changes
            var changes = new List<FileChange>();
            var originalSolution = solution;
            
            foreach (var project in renameResult.Projects)
            {
                var originalProject = originalSolution.GetProject(project.Id);
                if (originalProject == null)
                    continue;

                foreach (var documentId in project.DocumentIds)
                {
                    var newDoc = project.GetDocument(documentId);
                    var oldDoc = originalProject.GetDocument(documentId);
                    
                    if (newDoc == null || oldDoc == null)
                        continue;

                    var newText = await newDoc.GetTextAsync(cancellationToken);
                    var oldText = await oldDoc.GetTextAsync(cancellationToken);
                    
                    var textChanges = newText.GetTextChanges(oldText);
                    if (textChanges.Count > 0)
                    {
                        var fileChange = new FileChange
                        {
                            FilePath = newDoc.FilePath ?? "",
                            Changes = new List<TextChange>()
                        };

                        foreach (var change in textChanges)
                        {
                            var lineSpan = oldText.Lines.GetLinePositionSpan(change.Span);
                            var newLineSpan = newText.Lines.GetLinePositionSpan(new Microsoft.CodeAnalysis.Text.TextSpan(change.Span.Start, change.NewText?.Length ?? 0));
                            
                            fileChange.Changes.Add(new TextChange
                            {
                                OldText = oldText.GetSubText(change.Span).ToString(),
                                NewText = change.NewText ?? "",
                                StartLine = lineSpan.Start.Line + 1,
                                StartColumn = lineSpan.Start.Character + 1,
                                EndLine = lineSpan.End.Line + 1,
                                EndColumn = lineSpan.End.Character + 1
                            });
                        }

                        changes.Add(fileChange);
                    }
                }
            }

            // Count total changes
            var totalChanges = changes.Sum(f => f.Changes.Count);

            // Handle different response modes
            if (mode == ResponseMode.Summary)
            {
                return CreateSummaryResponse(changes, symbol, newName, preview);
            }

            // Check if we need to truncate
            var maxTokens = GetMaxTokens();
            var truncatedChanges = Truncator.TruncateResults(
                changes,
                maxTokens,
                change => SizeEstimator.EstimateTokens(new { file = change.FilePath, changes = change.Changes }));

            if (truncatedChanges.IsTruncated && !preview)
            {
                // For non-preview mode, warn but still apply all changes
                Logger.LogWarning(
                    "Rename operation affects {Total} files, but only {Returned} can be shown in response",
                    truncatedChanges.TotalCount,
                    truncatedChanges.ReturnedCount);
            }

            // If not preview mode, apply ALL changes (not just truncated ones)
            if (!preview)
            {
                await _workspaceService.UpdateSolutionAsync(renameResult, cancellationToken);
                Logger.LogInformation("Applied rename of '{OldName}' to '{NewName}' across {FileCount} files", symbol.Name, newName, changes.Count);
            }

            // Build response
            var response = new
            {
                success = true,
                symbol = new
                {
                    oldName = symbol.Name,
                    newName = newName,
                    kind = symbol.Kind.ToString(),
                    containerName = symbol.ContainingType?.ToDisplayString() ?? symbol.ContainingNamespace?.ToDisplayString() ?? ""
                },
                preview = preview,
                totalFiles = changes.Count,
                totalChanges = totalChanges,
                fileChanges = truncatedChanges.Results.ToArray(),
                metadata = new ResponseMetadata
                {
                    TotalResults = truncatedChanges.TotalCount,
                    ReturnedResults = truncatedChanges.ReturnedCount,
                    IsTruncated = truncatedChanges.IsTruncated,
                    TruncationReason = truncatedChanges.TruncationReason,
                    EstimatedTokens = truncatedChanges.EstimatedReturnedTokens
                }
            };

            return response;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in RenameSymbol");
            return new
            {
                success = false,
                error = ex.Message
            };
        }
    }

    private object CreateSummaryResponse(List<FileChange> changes, ISymbol symbol, string newName, bool preview)
    {
        var summary = changes
            .GroupBy(c => c.FilePath)
            .Select(g => new
            {
                filePath = g.Key,
                changeCount = g.Sum(fc => fc.Changes.Count)
            })
            .OrderByDescending(s => s.changeCount)
            .ToList();

        // Generate a detail request token that encodes the context
        var detailToken = GenerateDetailRequestToken(symbol, newName, changes);

        return new
        {
            success = true,
            symbol = new
            {
                oldName = symbol.Name,
                newName = newName,
                kind = symbol.Kind.ToString(),
                containerName = symbol.ContainingType?.ToDisplayString() ?? symbol.ContainingNamespace?.ToDisplayString() ?? ""
            },
            preview = preview,
            summary = new
            {
                totalFiles = summary.Count,
                totalChanges = summary.Sum(s => s.changeCount),
                topFiles = summary.Take(10).ToList(),
                message = summary.Count > 10 
                    ? $"Showing top 10 files out of {summary.Count} total files affected" 
                    : null
            },
            metadata = new ResponseMetadata
            {
                TotalResults = changes.Count,
                ReturnedResults = 0, // Summary mode doesn't return detailed results
                IsTruncated = false,
                DetailRequestToken = detailToken,
                AvailableDetailLevels = new List<DetailLevel>
                {
                    new DetailLevel
                    {
                        Id = "files",
                        Name = "File List",
                        Description = "List of all affected files with change counts",
                        EstimatedTokens = summary.Count * 50,
                        IsActive = false
                    },
                    new DetailLevel
                    {
                        Id = "changes",
                        Name = "Change Details",
                        Description = "Detailed changes for specific files",
                        EstimatedTokens = summary.Sum(s => s.changeCount) * 100,
                        IsActive = false
                    },
                    new DetailLevel
                    {
                        Id = "preview",
                        Name = "Change Preview",
                        Description = "Preview of changes with before/after context",
                        EstimatedTokens = summary.Sum(s => s.changeCount) * 200,
                        IsActive = false
                    }
                }
            }
        };
    }

    private static bool IsValidIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        // Check if it's a keyword
        if (Microsoft.CodeAnalysis.CSharp.SyntaxFacts.IsKeywordKind(Microsoft.CodeAnalysis.CSharp.SyntaxFacts.GetKeywordKind(name)))
            return false;

        // Check if it starts with a letter or underscore
        if (!char.IsLetter(name[0]) && name[0] != '_')
            return false;

        // Check if all characters are valid
        for (int i = 1; i < name.Length; i++)
        {
            if (!char.IsLetterOrDigit(name[i]) && name[i] != '_')
                return false;
        }

        return true;
    }

    private static bool CanRenameSymbol(ISymbol symbol)
    {
        // Cannot rename symbols from metadata
        if (symbol.Locations.All(loc => loc.IsInMetadata))
            return false;

        // Cannot rename certain special symbols
        if (symbol.IsImplicitlyDeclared)
            return false;

        // Cannot rename operators
        if (symbol is IMethodSymbol method && method.MethodKind == MethodKind.UserDefinedOperator)
            return false;

        // Cannot rename compiler-generated symbols
        if (symbol.Name.StartsWith("<") && symbol.Name.Contains(">"))
            return false;

        return true;
    }

    private class FileChange
    {
        public string FilePath { get; set; } = "";
        public List<TextChange> Changes { get; set; } = new();
    }

    private class TextChange
    {
        public string OldText { get; set; } = "";
        public string NewText { get; set; } = "";
        public int StartLine { get; set; }
        public int StartColumn { get; set; }
        public int EndLine { get; set; }
        public int EndColumn { get; set; }
    }
    
    private string GenerateDetailRequestToken(ISymbol symbol, string newName, List<FileChange> changes)
    {
        // In a real implementation, this would cache the changes and return a token
        // For now, we'll encode basic info that could be used to reconstruct the request
        var tokenData = new
        {
            symbolName = symbol.Name,
            newName = newName,
            fileCount = changes.Count,
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        
        var json = System.Text.Json.JsonSerializer.Serialize(tokenData);
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
    }
    
    /// <summary>
    /// Handles detail requests for progressive disclosure
    /// </summary>
    public async Task<object> GetDetailsAsync(
        DetailRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // In a real implementation, this would:
            // 1. Validate the detail request token
            // 2. Retrieve cached data or re-execute the query
            // 3. Return only the requested details
            
            if (string.IsNullOrEmpty(request.DetailRequestToken))
            {
                return new
                {
                    success = false,
                    error = "Detail request token is required"
                };
            }
            
            // Example response structure for different detail levels
            return request.DetailLevelId switch
            {
                "files" => new
                {
                    success = true,
                    detailLevel = "files",
                    message = "File list details would be returned here",
                    // Would include full file list with change counts
                },
                "changes" => new
                {
                    success = true,
                    detailLevel = "changes",
                    targetFiles = request.TargetItems,
                    message = "Detailed changes for requested files would be returned here",
                    // Would include specific changes for requested files
                },
                "preview" => new
                {
                    success = true,
                    detailLevel = "preview",
                    targetFiles = request.TargetItems,
                    message = "Change previews with context would be returned here",
                    // Would include before/after previews
                },
                _ => new
                {
                    success = false,
                    error = $"Unknown detail level: {request.DetailLevelId}"
                }
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error getting rename details");
            return new
            {
                success = false,
                error = ex.Message
            };
        }
    }
}