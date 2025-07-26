using Microsoft.AspNetCore.Razor.Language;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;
using System.Text;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Embedded Razor analysis using NuGet packages (without external LSP server)
/// Provides basic analysis capabilities for .razor files
/// </summary>
public class EmbeddedRazorAnalyzer
{
    private readonly ILogger<EmbeddedRazorAnalyzer> _logger;
    private readonly RazorProjectEngine? _projectEngine;

    public EmbeddedRazorAnalyzer(ILogger<EmbeddedRazorAnalyzer> logger)
    {
        _logger = logger;
        
        try
        {
            // Create a basic Razor project engine for analysis
            var razorConfiguration = RazorConfiguration.Default;
            var projectSystem = RazorProjectFileSystem.Create("/");
            _projectEngine = RazorProjectEngine.Create(razorConfiguration, projectSystem);
            _logger.LogInformation("Embedded Razor analyzer initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize embedded Razor analyzer");
        }
    }

    /// <summary>
    /// Analyzes a Razor file and extracts basic document symbols
    /// </summary>
    public async Task<JsonNode?> GetDocumentSymbolsAsync(string filePath)
    {
        try
        {
            if (_projectEngine == null)
            {
                return CreateError("Razor analyzer not initialized");
            }

            if (!File.Exists(filePath))
            {
                return CreateError($"File not found: {filePath}");
            }

            var content = await File.ReadAllTextAsync(filePath);
            
            // Create Razor project item
            var projectItem = new InMemoryRazorProjectItem(filePath, content);
            var codeDocument = _projectEngine.Process(projectItem);
            
            // Extract basic symbols
            var symbols = new List<object>();
            
            // Add component symbol
            symbols.Add(new
            {
                name = Path.GetFileNameWithoutExtension(filePath),
                kind = "Class",
                range = new
                {
                    start = new { line = 0, character = 0 },
                    end = new { line = content.Split('\n').Length - 1, character = 0 }
                },
                detail = "Blazor Component"
            });

            // Simple parsing for @code blocks and directives
            var lines = content.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                
                // Detect @page directive
                if (line.StartsWith("@page"))
                {
                    symbols.Add(new
                    {
                        name = "Page Directive",
                        kind = "Property", 
                        range = new
                        {
                            start = new { line = i, character = 0 },
                            end = new { line = i, character = line.Length }
                        },
                        detail = line
                    });
                }
                
                // Detect @code blocks
                if (line.StartsWith("@code"))
                {
                    symbols.Add(new
                    {
                        name = "Code Block",
                        kind = "Method",
                        range = new
                        {
                            start = new { line = i, character = 0 },
                            end = new { line = i + 5, character = 0 } // Approximate
                        },
                        detail = "C# Code Block"
                    });
                }
            }

            return JsonValue.Create(symbols);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing Razor file: {FilePath}", filePath);
            return CreateError($"Analysis failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Provides basic hover information for Razor files
    /// </summary>
    public async Task<JsonNode?> GetHoverInfoAsync(string filePath, int line, int column)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return CreateError($"File not found: {filePath}");
            }

            var content = await File.ReadAllTextAsync(filePath);
            var lines = content.Split('\n');
            
            if (line >= lines.Length)
            {
                return null;
            }

            var currentLine = lines[line];
            
            // Basic hover information
            var hoverInfo = new
            {
                contents = new
                {
                    kind = "markdown",
                    value = $"**Blazor Component**\n\nFile: `{Path.GetFileName(filePath)}`\n\nLine {line + 1}: `{currentLine.Trim()}`"
                },
                range = new
                {
                    start = new { line, character = 0 },
                    end = new { line, character = currentLine.Length }
                }
            };

            return JsonValue.Create(hoverInfo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting hover info for {FilePath} at {Line}:{Column}", filePath, line, column);
            return CreateError($"Hover analysis failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Basic diagnostics for Razor files
    /// </summary>
    public async Task<JsonNode?> GetDiagnosticsAsync(string filePath)
    {
        try
        {
            if (_projectEngine == null)
            {
                return JsonValue.Create(Array.Empty<object>());
            }

            if (!File.Exists(filePath))
            {
                return CreateError($"File not found: {filePath}");
            }

            var content = await File.ReadAllTextAsync(filePath);
            
            var projectItem = new InMemoryRazorProjectItem(filePath, content);
            var codeDocument = _projectEngine.Process(projectItem);
            
            var diagnostics = new List<object>();
            
            // Get Razor diagnostics
            foreach (var diagnostic in codeDocument.GetCSharpDocument().Diagnostics)
            {
                diagnostics.Add(new
                {
                    severity = GetSeverity(diagnostic.Severity),
                    message = diagnostic.GetMessage(),
                    range = new
                    {
                        start = new { line = diagnostic.Span.LineIndex, character = diagnostic.Span.CharacterIndex },
                        end = new { line = diagnostic.Span.LineIndex, character = diagnostic.Span.CharacterIndex + diagnostic.Span.Length }
                    },
                    source = "razor"
                });
            }

            return JsonValue.Create(diagnostics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting diagnostics for {FilePath}", filePath);
            return CreateError($"Diagnostics analysis failed: {ex.Message}");
        }
    }

    private string GetSeverity(RazorDiagnosticSeverity severity)
    {
        return severity switch
        {
            RazorDiagnosticSeverity.Error => "error",
            RazorDiagnosticSeverity.Warning => "warning",
            _ => "info"
        };
    }

    private JsonNode CreateError(string message)
    {
        return JsonValue.Create(new { error = message });
    }
}