using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Tools;

public class RenameSymbolTool
{
    private readonly ILogger<RenameSymbolTool> _logger;
    private readonly CodeAnalysisService _workspaceService;

    public RenameSymbolTool(ILogger<RenameSymbolTool> logger, CodeAnalysisService workspaceService)
    {
        _logger = logger;
        _workspaceService = workspaceService;
    }

    public async Task<object> ExecuteAsync(
        string filePath,
        int line,
        int column,
        string newName,
        bool preview = true,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("RenameSymbol request for {FilePath} at {Line}:{Column} to '{NewName}'", filePath, line, column, newName);

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

            var result = new
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
                fileChanges = changes.ToArray()
            };

            // If not preview mode, apply the changes
            if (!preview)
            {
                await _workspaceService.UpdateSolutionAsync(renameResult, cancellationToken);
                _logger.LogInformation("Applied rename of '{OldName}' to '{NewName}' across {FileCount} files", symbol.Name, newName, changes.Count);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in RenameSymbol");
            return new
            {
                success = false,
                error = ex.Message
            };
        }
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
}