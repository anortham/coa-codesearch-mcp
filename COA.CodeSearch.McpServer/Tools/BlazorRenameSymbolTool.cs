using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Rename symbols in Blazor (.razor) files using Razor Language Server
/// </summary>
public class BlazorRenameSymbolTool : ITool
{
    public string ToolName => "blazor_rename_symbol";
    public string Description => "Rename symbols across Blazor (.razor) files - supports C# symbols with full refactoring";
    public ToolCategory Category => ToolCategory.Refactoring;
    
    private readonly ILogger<BlazorRenameSymbolTool> _logger;
    private readonly IRazorAnalysisService _razorAnalysisService;

    public BlazorRenameSymbolTool(
        ILogger<BlazorRenameSymbolTool> logger,
        IRazorAnalysisService razorAnalysisService)
    {
        _logger = logger;
        _razorAnalysisService = razorAnalysisService;
    }

    /// <summary>
    /// Renames the symbol at the specified location in a Blazor file
    /// </summary>
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
            _logger.LogInformation("Blazor RenameSymbol request for {FilePath} at {Line}:{Column} -> {NewName}", 
                filePath, line, column, newName);

            // Validate file path
            if (string.IsNullOrEmpty(filePath))
            {
                return CreateErrorResponse("File path is required");
            }

            // Check if this is a Razor file
            if (!IsRazorFile(filePath))
            {
                return CreateErrorResponse($"File {filePath} is not a Blazor (.razor) file");
            }

            // Check if file exists
            if (!File.Exists(filePath))
            {
                return CreateErrorResponse($"File not found: {filePath}");
            }

            // Validate position
            if (line < 1 || column < 1)
            {
                return CreateErrorResponse("Line and column must be positive integers (1-based)");
            }

            // Validate new name
            if (string.IsNullOrWhiteSpace(newName))
            {
                return CreateErrorResponse("New name is required and cannot be empty");
            }

            // Basic validation for C# identifier
            if (!IsValidCSharpIdentifier(newName))
            {
                return CreateErrorResponse($"'{newName}' is not a valid C# identifier");
            }

            // Check if Razor analysis service is available
            if (!_razorAnalysisService.IsAvailable)
            {
                // Try to initialize the service
                var initialized = await _razorAnalysisService.InitializeAsync(cancellationToken);
                if (!initialized)
                {
                    return CreateErrorResponse("Razor Language Server is not available. Please install VS Code with the C# extension.");
                }
            }

            // Get rename result from Razor analysis service
            var renameResult = await _razorAnalysisService.RenameSymbolAsync(filePath, line, column, newName, preview, cancellationToken);
            
            if (renameResult == null)
            {
                return new
                {
                    success = false,
                    canRename = false,
                    message = "Symbol cannot be renamed at the specified position",
                    location = new
                    {
                        filePath,
                        line,
                        column
                    },
                    suggestions = new[]
                    {
                        "Ensure the cursor is positioned on a renameable C# symbol (variable, method, type, etc.)",
                        "HTML elements and Razor markup cannot be renamed through LSP",
                        "Only C# symbols within @code blocks or expressions support renaming"
                    },
                    metadata = new
                    {
                        tool = "blazor_rename_symbol",
                        languageServer = "rzls",
                        preview,
                        timestamp = DateTime.UtcNow
                    }
                };
            }

            // Parse the rename result (LSP WorkspaceEdit format)
            var parsedResult = ParseRenameResult(renameResult.ToString() ?? "");

            var result = new
            {
                success = true,
                canRename = true,
                changes = parsedResult.changes,
                changeCount = parsedResult.changeCount,
                filesAffected = parsedResult.filesAffected,
                sourceLocation = new
                {
                    filePath,
                    line,
                    column
                },
                rename = new
                {
                    newName,
                    preview
                },
                warnings = parsedResult.warnings,
                raw = renameResult, // Include raw LSP response for debugging
                metadata = new
                {
                    tool = "blazor_rename_symbol",
                    languageServer = "rzls",
                    preview,
                    timestamp = DateTime.UtcNow
                }
            };

            _logger.LogInformation("Successfully processed rename for {FilePath} at {Line}:{Column} -> {NewName} ({ChangeCount} changes in {FileCount} files)",
                filePath, line, column, newName, parsedResult.changeCount, parsedResult.filesAffected.Length);

            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Blazor RenameSymbol operation was cancelled");
            return CreateErrorResponse("Operation was cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Blazor RenameSymbol for {FilePath} at {Line}:{Column}", filePath, line, column);
            return CreateErrorResponse($"Error renaming symbol: {ex.Message}");
        }
    }

    private bool IsRazorFile(string filePath)
    {
        return Path.GetExtension(filePath).Equals(".razor", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsValidCSharpIdentifier(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        // Basic C# identifier validation
        // First character must be letter or underscore
        if (!char.IsLetter(name[0]) && name[0] != '_')
            return false;

        // Remaining characters must be letters, digits, or underscores
        for (int i = 1; i < name.Length; i++)
        {
            if (!char.IsLetterOrDigit(name[i]) && name[i] != '_')
                return false;
        }

        // Check against C# keywords (basic set)
        var keywords = new HashSet<string>
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
            "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
            "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
            "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
            "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
            "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed",
            "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this",
            "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort",
            "using", "virtual", "void", "volatile", "while"
        };

        return !keywords.Contains(name.ToLowerInvariant());
    }

    private (object[] changes, int changeCount, string[] filesAffected, string[] warnings) ParseRenameResult(string renameResultJson)
    {
        try
        {
            // This is a simplified parser for LSP WorkspaceEdit format
            // In a production system, you'd use proper JSON parsing
            
            var changes = new List<object>();
            var filesAffected = new HashSet<string>();
            var warnings = new List<string>();

            // For now, return a placeholder structure
            // The actual implementation would parse the LSP WorkspaceEdit JSON
            if (!string.IsNullOrEmpty(renameResultJson) && renameResultJson != "{}")
            {
                changes.Add(new
                {
                    description = "Rename operation completed",
                    details = "Changes available in LSP WorkspaceEdit format",
                    note = "Use the raw field for detailed change information"
                });

                warnings.Add("Detailed change parsing not yet implemented - refer to raw LSP response");
            }

            return (changes.ToArray(), changes.Count, filesAffected.ToArray(), warnings.ToArray());
        }
        catch (Exception ex)
        {
            return (
                Array.Empty<object>(), 
                0, 
                Array.Empty<string>(), 
                new[] { $"Error parsing rename result: {ex.Message}" }
            );
        }
    }

    private object CreateErrorResponse(string message)
    {
        return new
        {
            success = false,
            canRename = false,
            error = message,
            changes = Array.Empty<object>(),
            changeCount = 0,
            filesAffected = Array.Empty<string>(),
            metadata = new
            {
                tool = "blazor_rename_symbol",
                timestamp = DateTime.UtcNow
            }
        };
    }
}