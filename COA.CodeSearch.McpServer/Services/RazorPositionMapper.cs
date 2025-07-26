using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Advanced position mapping utilities for Razor LSP operations
/// Handles complex position transformations and mapping scenarios
/// </summary>
public class RazorPositionMapper
{
    private readonly ILogger<RazorPositionMapper> _logger;
    private readonly RazorVirtualDocumentManager _virtualDocumentManager;

    public RazorPositionMapper(ILogger<RazorPositionMapper> logger, RazorVirtualDocumentManager virtualDocumentManager)
    {
        _logger = logger;
        _virtualDocumentManager = virtualDocumentManager;
    }

    /// <summary>
    /// Maps a Razor file position to LSP coordinates for communication with rzls.exe
    /// </summary>
    /// <param name="razorFilePath">Path to the .razor file</param>
    /// <param name="line">Line number (1-based)</param>
    /// <param name="column">Column number (1-based)</param>
    /// <returns>LSP position (0-based) or null if invalid</returns>
    public async Task<LspPosition?> MapToLspPositionAsync(string razorFilePath, int line, int column)
    {
        try
        {
            if (line < 1 || column < 1)
            {
                _logger.LogWarning("Invalid position: {Line}:{Column} (must be 1-based)", line, column);
                return null;
            }

            var virtualDoc = await _virtualDocumentManager.GetVirtualDocumentAsync(razorFilePath);
            if (virtualDoc == null)
            {
                _logger.LogWarning("No virtual document for {Path}", razorFilePath);
                return null;
            }

            // Validate position is within document bounds
            if (!IsValidPosition(virtualDoc.RazorSourceText, line - 1, column - 1))
            {
                _logger.LogWarning("Position {Line}:{Column} is out of bounds for {Path}", line, column, razorFilePath);
                return null;
            }

            // Convert to 0-based for LSP protocol
            return new LspPosition
            {
                Line = line - 1,
                Character = column - 1
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error mapping to LSP position for {Path} at {Line}:{Column}", razorFilePath, line, column);
            return null;
        }
    }

    /// <summary>
    /// Maps an LSP position back to Razor file coordinates
    /// </summary>
    /// <param name="lspPosition">LSP position (0-based)</param>
    /// <returns>Razor position (1-based)</returns>
    public (int Line, int Column) MapFromLspPosition(LspPosition lspPosition)
    {
        return (lspPosition.Line + 1, lspPosition.Character + 1);
    }

    /// <summary>
    /// Converts LSP Location objects to Roslyn Location objects
    /// </summary>
    /// <param name="lspLocation">LSP location from rzls.exe response</param>
    /// <param name="razorFilePath">Original .razor file path</param>
    /// <returns>Roslyn Location or null if conversion fails</returns>
    public async Task<Location?> ConvertLspLocationToRoslynAsync(dynamic lspLocation, string razorFilePath)
    {
        try
        {
            if (lspLocation?.uri == null || lspLocation?.range == null)
            {
                return null;
            }

            var uri = lspLocation.uri.ToString();
            var filePath = ConvertUriToFilePath(uri);
            
            // Handle file URIs that might point to generated C# or original .razor
            if (!string.Equals(filePath, razorFilePath, StringComparison.OrdinalIgnoreCase))
            {
                // This might be a generated C# file reference, try to map back to Razor
                filePath = await TryMapGeneratedFileToRazorAsync(filePath, razorFilePath);
            }

            var virtualDoc = await _virtualDocumentManager.GetVirtualDocumentAsync(filePath);
            if (virtualDoc == null)
            {
                _logger.LogWarning("No virtual document for location mapping: {Path}", (string)filePath);
                return null;
            }

            // Extract LSP range
            var startLine = (int)lspLocation.range.start.line;
            var startChar = (int)lspLocation.range.start.character;
            var endLine = (int)lspLocation.range.end.line;
            var endChar = (int)lspLocation.range.end.character;

            // Convert LSP positions to text spans
            var startPosition = GetPositionFromLineColumn(virtualDoc.RazorSourceText, startLine, startChar);
            var endPosition = GetPositionFromLineColumn(virtualDoc.RazorSourceText, endLine, endChar);
            
            if (startPosition < 0 || endPosition < 0 || endPosition < startPosition)
            {
                _logger.LogWarning("Invalid LSP range: ({StartLine},{StartChar}) to ({EndLine},{EndChar})", 
                    startLine, startChar, endLine, endChar);
                return null;
            }

            var textSpan = new TextSpan(startPosition, endPosition - startPosition);
            return Location.Create(filePath, textSpan, virtualDoc.RazorSourceText.Lines.GetLinePositionSpan(textSpan));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error converting LSP location to Roslyn location");
            return null;
        }
    }

    /// <summary>
    /// Converts multiple LSP locations to Roslyn locations
    /// </summary>
    /// <param name="lspLocations">Array of LSP locations</param>
    /// <param name="razorFilePath">Original .razor file path</param>
    /// <returns>Array of valid Roslyn locations</returns>
    public async Task<Location[]> ConvertLspLocationsToRoslynAsync(IEnumerable<dynamic> lspLocations, string razorFilePath)
    {
        var locations = new List<Location>();
        
        foreach (var lspLocation in lspLocations)
        {
            var location = await ConvertLspLocationToRoslynAsync(lspLocation, razorFilePath);
            if (location != null)
            {
                locations.Add(location);
            }
        }
        
        return locations.ToArray();
    }

    /// <summary>
    /// Validates that a position is within the bounds of a source text
    /// </summary>
    /// <param name="sourceText">Source text to validate against</param>
    /// <param name="line">Line number (0-based)</param>
    /// <param name="column">Column number (0-based)</param>
    /// <returns>True if position is valid</returns>
    public bool IsValidPosition(Microsoft.CodeAnalysis.Text.SourceText sourceText, int line, int column)
    {
        if (line < 0 || line >= sourceText.Lines.Count)
        {
            return false;
        }

        var textLine = sourceText.Lines[line];
        return column >= 0 && column <= textLine.Span.Length;
    }

    /// <summary>
    /// Gets the absolute position from line and column coordinates
    /// </summary>
    /// <param name="sourceText">Source text</param>
    /// <param name="line">Line number (0-based)</param>
    /// <param name="column">Column number (0-based)</param>
    /// <returns>Absolute position or -1 if invalid</returns>
    public int GetPositionFromLineColumn(Microsoft.CodeAnalysis.Text.SourceText sourceText, int line, int column)
    {
        try
        {
            if (!IsValidPosition(sourceText, line, column))
            {
                return -1;
            }

            var textLine = sourceText.Lines[line];
            return textLine.Start + column;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting position from line {Line}, column {Column}", line, column);
            return -1;
        }
    }

    /// <summary>
    /// Determines if a position is within C# code blocks in a Razor file
    /// </summary>
    /// <param name="razorFilePath">Path to the .razor file</param>
    /// <param name="line">Line number (1-based)</param>
    /// <param name="column">Column number (1-based)</param>
    /// <returns>True if position is in C# code, false if in HTML/Razor markup</returns>
    public async Task<bool> IsPositionInCSharpCodeAsync(string razorFilePath, int line, int column)
    {
        try
        {
            var virtualDoc = await _virtualDocumentManager.GetVirtualDocumentAsync(razorFilePath);
            if (virtualDoc == null)
            {
                return false;
            }

            // Convert to 0-based indexing
            var position = GetPositionFromLineColumn(virtualDoc.RazorSourceText, line - 1, column - 1);
            if (position < 0)
            {
                return false;
            }

            // Check if this position maps to generated C# code
            foreach (var mapping in virtualDoc.SourceMappings)
            {
                if (position >= mapping.OriginalSpan.Start && position < mapping.OriginalSpan.End)
                {
                    return true; // Position has a mapping to C# code
                }
            }

            return false; // Position is in HTML/Razor markup
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if position is in C# code for {Path} at {Line}:{Column}", 
                razorFilePath, line, column);
            return false;
        }
    }

    /// <summary>
    /// Gets the appropriate language context for a position (C# or Razor/HTML)
    /// </summary>
    /// <param name="razorFilePath">Path to the .razor file</param>
    /// <param name="line">Line number (1-based)</param>
    /// <param name="column">Column number (1-based)</param>
    /// <returns>Language context information</returns>
    public async Task<LanguageContext> GetLanguageContextAsync(string razorFilePath, int line, int column)
    {
        var isInCSharp = await IsPositionInCSharpCodeAsync(razorFilePath, line, column);
        
        return new LanguageContext
        {
            Language = isInCSharp ? "csharp" : "razor",
            IsInCSharpCode = isInCSharp,
            SupportsGoToDefinition = isInCSharp,
            SupportsFindReferences = isInCSharp,
            SupportsRename = isInCSharp,
            SupportsHover = true // Both C# and Razor support hover
        };
    }

    private string ConvertUriToFilePath(string uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out var parsedUri))
        {
            return parsedUri.LocalPath;
        }
        
        // Handle file:// URIs manually if needed
        if (uri.StartsWith("file:///"))
        {
            return uri[8..].Replace('/', Path.DirectorySeparatorChar);
        }
        
        return uri;
    }

    private async Task<string> TryMapGeneratedFileToRazorAsync(string generatedFilePath, string originalRazorPath)
    {
        // If the generated file path looks like it's related to the Razor file, return the original
        // This is a simplified approach - in practice, we might need more sophisticated mapping
        
        var razorFileName = Path.GetFileNameWithoutExtension(originalRazorPath);
        var generatedFileName = Path.GetFileName(generatedFilePath);
        
        if (generatedFileName.Contains(razorFileName) || 
            generatedFileName.Contains("Generated") ||
            generatedFileName.Contains(".razor.g."))
        {
            return originalRazorPath;
        }
        
        return generatedFilePath;
    }
}

/// <summary>
/// Represents an LSP position (0-based indexing)
/// </summary>
public class LspPosition
{
    public required int Line { get; init; }
    public required int Character { get; init; }
}

/// <summary>
/// Represents the language context for a position in a Razor file
/// </summary>
public class LanguageContext
{
    public required string Language { get; init; }
    public required bool IsInCSharpCode { get; init; }
    public required bool SupportsGoToDefinition { get; init; }
    public required bool SupportsFindReferences { get; init; }
    public required bool SupportsRename { get; init; }
    public required bool SupportsHover { get; init; }
}