using Microsoft.AspNetCore.Razor.Language;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Manages virtual document projection between .razor files and generated C# code
/// Handles position mapping for LSP operations
/// </summary>
public class RazorVirtualDocumentManager
{
    private readonly ILogger<RazorVirtualDocumentManager> _logger;
    private readonly ConcurrentDictionary<string, RazorVirtualDocument> _virtualDocuments = new();
    private readonly RazorProjectEngine _projectEngine;
    
    public RazorVirtualDocumentManager(ILogger<RazorVirtualDocumentManager> logger)
    {
        _logger = logger;
        _projectEngine = CreateRazorProjectEngine();
    }

    /// <summary>
    /// Gets or creates a virtual document for the specified .razor file
    /// </summary>
    /// <param name="razorFilePath">Path to the .razor file</param>
    /// <returns>Virtual document containing generated C# code and mappings</returns>
    public async Task<RazorVirtualDocument?> GetVirtualDocumentAsync(string razorFilePath)
    {
        if (string.IsNullOrEmpty(razorFilePath) || !File.Exists(razorFilePath))
        {
            _logger.LogWarning("Razor file not found: {Path}", razorFilePath);
            return null;
        }

        // Check cache first
        var normalizedPath = Path.GetFullPath(razorFilePath);
        if (_virtualDocuments.TryGetValue(normalizedPath, out var cached))
        {
            // Check if file has been modified since cache
            var lastWrite = File.GetLastWriteTimeUtc(razorFilePath);
            if (cached.LastModified >= lastWrite)
            {
                return cached;
            }
        }

        try
        {
            // Read and process the .razor file
            var razorContent = await File.ReadAllTextAsync(razorFilePath);
            var virtualDoc = await CreateVirtualDocumentAsync(razorFilePath, razorContent);
            
            if (virtualDoc != null)
            {
                _virtualDocuments.AddOrUpdate(normalizedPath, virtualDoc, (_, _) => virtualDoc);
            }
            
            return virtualDoc;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create virtual document for {Path}", razorFilePath);
            return null;
        }
    }

    /// <summary>
    /// Maps a position in a .razor file to the corresponding position in generated C# code
    /// </summary>
    /// <param name="razorFilePath">Path to the .razor file</param>
    /// <param name="razorLine">Line number in .razor file (1-based)</param>
    /// <param name="razorColumn">Column number in .razor file (1-based)</param>
    /// <returns>Mapped position in generated C# or null if not mappable</returns>
    public async Task<(int CSharpLine, int CSharpColumn)?> MapRazorToCSharpAsync(
        string razorFilePath, int razorLine, int razorColumn)
    {
        var virtualDoc = await GetVirtualDocumentAsync(razorFilePath);
        if (virtualDoc?.SourceMappings == null)
        {
            return null;
        }

        try
        {
            // Convert to 0-based indexing for internal processing
            var razorPosition = GetPositionFromLineColumn(virtualDoc.RazorSourceText, razorLine - 1, razorColumn - 1);
            
            // Find the mapping that contains this position
            foreach (var mapping in virtualDoc.SourceMappings)
            {
                if (razorPosition >= mapping.OriginalSpan.Start && 
                    razorPosition < mapping.OriginalSpan.End)
                {
                    // Calculate offset within the span
                    var offsetInSpan = razorPosition - mapping.OriginalSpan.Start;
                    var csharpPosition = mapping.GeneratedSpan.Start + offsetInSpan;
                    
                    // Convert back to line/column (1-based)
                    var csharpLinePosition = virtualDoc.CSharpSourceText.Lines.GetLinePosition(csharpPosition);
                    return (csharpLinePosition.Line + 1, csharpLinePosition.Character + 1);
                }
            }
            
            _logger.LogDebug("No mapping found for Razor position {Line}:{Column} in {Path}", 
                razorLine, razorColumn, razorFilePath);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error mapping Razor to C# position in {Path}", razorFilePath);
            return null;
        }
    }

    /// <summary>
    /// Maps a position in generated C# code back to the corresponding position in .razor file
    /// </summary>
    /// <param name="razorFilePath">Path to the .razor file</param>
    /// <param name="csharpLine">Line number in generated C# (1-based)</param>
    /// <param name="csharpColumn">Column number in generated C# (1-based)</param>
    /// <returns>Mapped position in .razor file or null if not mappable</returns>
    public async Task<(int RazorLine, int RazorColumn)?> MapCSharpToRazorAsync(
        string razorFilePath, int csharpLine, int csharpColumn)
    {
        var virtualDoc = await GetVirtualDocumentAsync(razorFilePath);
        if (virtualDoc?.SourceMappings == null)
        {
            return null;
        }

        try
        {
            // Convert to 0-based indexing for internal processing
            var csharpPosition = GetPositionFromLineColumn(virtualDoc.CSharpSourceText, csharpLine - 1, csharpColumn - 1);
            
            // Find the mapping that contains this position
            foreach (var mapping in virtualDoc.SourceMappings)
            {
                if (csharpPosition >= mapping.GeneratedSpan.Start && 
                    csharpPosition < mapping.GeneratedSpan.End)
                {
                    // Calculate offset within the span
                    var offsetInSpan = csharpPosition - mapping.GeneratedSpan.Start;
                    var razorPosition = mapping.OriginalSpan.Start + offsetInSpan;
                    
                    // Convert back to line/column (1-based)
                    var razorLinePosition = virtualDoc.RazorSourceText.Lines.GetLinePosition(razorPosition);
                    return (razorLinePosition.Line + 1, razorLinePosition.Character + 1);
                }
            }
            
            _logger.LogDebug("No mapping found for C# position {Line}:{Column} in {Path}", 
                csharpLine, csharpColumn, razorFilePath);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error mapping C# to Razor position in {Path}", razorFilePath);
            return null;
        }
    }

    /// <summary>
    /// Invalidates the cached virtual document for a file
    /// </summary>
    /// <param name="razorFilePath">Path to the .razor file</param>
    public void InvalidateDocument(string razorFilePath)
    {
        if (!string.IsNullOrEmpty(razorFilePath))
        {
            var normalizedPath = Path.GetFullPath(razorFilePath);
            _virtualDocuments.TryRemove(normalizedPath, out _);
            _logger.LogDebug("Invalidated virtual document for {Path}", razorFilePath);
        }
    }

    /// <summary>
    /// Clears all cached virtual documents
    /// </summary>
    public void ClearCache()
    {
        _virtualDocuments.Clear();
        _logger.LogDebug("Cleared all virtual document cache");
    }

    private async Task<RazorVirtualDocument?> CreateVirtualDocumentAsync(string filePath, string content)
    {
        try
        {
            // Create Razor source document
            var razorSourceText = SourceText.From(content, Encoding.UTF8);
            var razorDocument = RazorSourceDocument.Create(content, filePath);
            
            // Generate C# code using Razor engine
            // Create a project item for the document  
            var projectItem = new InMemoryRazorProjectItem(filePath, content);
            var codeDocument = _projectEngine.Process(projectItem);
            var csharpDocument = RazorCodeDocumentExtensions.GetCSharpDocument(codeDocument);
            
            if (csharpDocument.Diagnostics.Any(d => d.Severity == RazorDiagnosticSeverity.Error))
            {
                _logger.LogWarning("Razor compilation errors in {Path}: {Errors}", 
                    filePath, string.Join(", ", csharpDocument.Diagnostics.Select(d => d.GetMessage())));
            }
            
            var csharpSourceText = SourceText.From(csharpDocument.GeneratedCode ?? "", Encoding.UTF8);
            
            // Extract source mappings for position translation
            var sourceMappings = ExtractSourceMappings(codeDocument);
            
            return new RazorVirtualDocument
            {
                FilePath = filePath,
                RazorSourceText = razorSourceText,
                CSharpSourceText = csharpSourceText,
                GeneratedCSharpCode = csharpDocument.GeneratedCode ?? "",
                SourceMappings = sourceMappings,
                LastModified = File.GetLastWriteTimeUtc(filePath),
                CodeDocument = codeDocument
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process Razor file {Path}", filePath);
            return null;
        }
    }

    private List<SourceMapping> ExtractSourceMappings(RazorCodeDocument codeDocument)
    {
        var mappings = new List<SourceMapping>();
        
        try
        {
            var syntaxTree = codeDocument.GetSyntaxTree();
            var csharpDocument = codeDocument.GetCSharpDocument();
            
            foreach (var mapping in csharpDocument.SourceMappings)
            {
                mappings.Add(new SourceMapping
                {
                    OriginalSpan = new TextSpan(mapping.OriginalSpan.AbsoluteIndex, mapping.OriginalSpan.Length),
                    GeneratedSpan = new TextSpan(mapping.GeneratedSpan.AbsoluteIndex, mapping.GeneratedSpan.Length)
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract source mappings");
        }
        
        return mappings;
    }

    private RazorProjectEngine CreateRazorProjectEngine()
    {
        var fileSystem = RazorProjectFileSystem.Create(".");
        var projectEngine = RazorProjectEngine.Create(RazorConfiguration.Default, fileSystem, builder =>
        {
            // Configure for Blazor components
            builder.SetNamespace("Components");
            builder.AddDefaultImports("@using Microsoft.AspNetCore.Components");
            builder.AddDefaultImports("@using Microsoft.AspNetCore.Components.Web");
        });
        
        return projectEngine;
    }

    private int GetPositionFromLineColumn(SourceText sourceText, int line, int column)
    {
        if (line < 0 || line >= sourceText.Lines.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(line));
        }
        
        var textLine = sourceText.Lines[line];
        if (column < 0 || column > textLine.Span.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(column));
        }
        
        return textLine.Start + column;
    }
}

/// <summary>
/// Represents a virtual document containing both Razor source and generated C# code
/// </summary>
public class RazorVirtualDocument
{
    public required string FilePath { get; init; }
    public required SourceText RazorSourceText { get; init; }
    public required SourceText CSharpSourceText { get; init; }
    public required string GeneratedCSharpCode { get; init; }
    public required List<SourceMapping> SourceMappings { get; init; }
    public required DateTime LastModified { get; init; }
    public required RazorCodeDocument CodeDocument { get; init; }
}

/// <summary>
/// Represents a mapping between original Razor source and generated C# code
/// </summary>
public class SourceMapping
{
    public required TextSpan OriginalSpan { get; init; }
    public required TextSpan GeneratedSpan { get; init; }
}

/// <summary>
/// In-memory implementation of RazorProjectItem for virtual documents
/// </summary>
internal class InMemoryRazorProjectItem : RazorProjectItem
{
    private readonly string _content;

    public InMemoryRazorProjectItem(string filePath, string content)
    {
        FilePath = filePath;
        PhysicalPath = filePath;
        RelativePhysicalPath = Path.GetFileName(filePath);
        _content = content;
    }

    public override string BasePath => "";
    public override string FilePath { get; }
    public override string PhysicalPath { get; }
    public override string RelativePhysicalPath { get; }
    public override bool Exists => true;

    public override Stream Read() => new MemoryStream(Encoding.UTF8.GetBytes(_content));
}