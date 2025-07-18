using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Text;

namespace COA.Roslyn.McpServer.Tests;

public static class MockHelper
{
    public static async Task<(Workspace Workspace, Document Document, int Position)> CreateMockWorkspaceAsync(string code, int line, int column)
    {
        var projectId = ProjectId.CreateNewId();
        var documentId = DocumentId.CreateNewId(projectId);
        
        var solution = new AdhocWorkspace()
            .CurrentSolution
            .AddProject(projectId, "TestProject", "TestAssembly", LanguageNames.CSharp)
            .AddMetadataReference(projectId, MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
            .AddDocument(documentId, "TestCode.cs", SourceText.From(code, Encoding.UTF8));
        
        var document = solution.GetDocument(documentId)!;
        var text = await document.GetTextAsync();
        var linePosition = new LinePosition(line - 1, column - 1); // Convert to 0-based
        var position = text.Lines.GetPosition(linePosition);
        
        return (solution.Workspace, document, position);
    }
    
    public static async Task<(SemanticModel Model, SyntaxToken Token, int Position)> GetSemanticInfoAsync(Document document, int position)
    {
        var root = await document.GetSyntaxRootAsync() ?? throw new InvalidOperationException("No syntax root");
        var semanticModel = await document.GetSemanticModelAsync() ?? throw new InvalidOperationException("No semantic model");
        var token = root.FindToken(position);
        
        return (semanticModel, token, position);
    }
}