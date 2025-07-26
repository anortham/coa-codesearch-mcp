using COA.CodeSearch.McpServer.Constants;
using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Tools;
using COA.Mcp.Protocol;
using Microsoft.Extensions.DependencyInjection;
using static COA.CodeSearch.McpServer.Tools.Registration.ToolRegistrationHelper;

namespace COA.CodeSearch.McpServer.Tools.Registration;

/// <summary>
/// Registers Blazor-related tools with the MCP server
/// </summary>
public static class BlazorToolRegistrations
{
    public static void RegisterBlazorTools(ToolRegistry registry, IServiceProvider serviceProvider)
    {
        // Blazor navigation tools
        var goToDefTool = serviceProvider.GetService<BlazorGoToDefinitionTool>();
        if (goToDefTool != null)
        {
            RegisterBlazorGoToDefinition(registry, goToDefTool);
        }
        
        var findRefsTool = serviceProvider.GetService<BlazorFindReferencesTool>();
        if (findRefsTool != null)
        {
            RegisterBlazorFindReferences(registry, findRefsTool);
        }
        
        var hoverTool = serviceProvider.GetService<BlazorHoverInfoTool>();
        if (hoverTool != null)
        {
            RegisterBlazorHoverInfo(registry, hoverTool);
        }
        
        var renameTool = serviceProvider.GetService<BlazorRenameSymbolTool>();
        if (renameTool != null)
        {
            RegisterBlazorRenameSymbol(registry, renameTool);
        }
        
        var documentSymbolsTool = serviceProvider.GetService<BlazorGetDocumentSymbolsTool>();
        if (documentSymbolsTool != null)
        {
            RegisterBlazorGetDocumentSymbols(registry, documentSymbolsTool);
        }
        
        var diagnosticsTool = serviceProvider.GetService<BlazorGetDiagnosticsTool>();
        if (diagnosticsTool != null)
        {
            RegisterBlazorGetDiagnostics(registry, diagnosticsTool);
        }
    }
    
    private static void RegisterBlazorGoToDefinition(ToolRegistry registry, BlazorGoToDefinitionTool tool)
    {
        registry.RegisterTool<BlazorNavigationParams>(
            name: "blazor_go_to_definition",
            description: "Navigate to symbol definitions in Blazor (.razor) files - supports C# code within components",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    filePath = new { type = "string", description = "Path to the .razor file" },
                    line = new { type = "integer", description = "Line number (1-based)" },
                    column = new { type = "integer", description = "Column number (1-based)" }
                },
                required = new[] { "filePath", "line", "column" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var result = await tool.ExecuteAsync(
                    ValidateRequired(parameters.FilePath, "filePath"),
                    ValidatePositive(parameters.Line, "line"),
                    ValidatePositive(parameters.Column, "column"),
                    ct);
                    
                return CreateSuccessResult(result);
            }
        );
    }
    
    private static void RegisterBlazorFindReferences(ToolRegistry registry, BlazorFindReferencesTool tool)
    {
        registry.RegisterTool<BlazorFindReferencesParams>(
            name: "blazor_find_references",
            description: "Find all references to symbols in Blazor (.razor) files - supports C# symbols within components",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    filePath = new { type = "string", description = "Path to the .razor file" },
                    line = new { type = "integer", description = "Line number (1-based)" },
                    column = new { type = "integer", description = "Column number (1-based)" },
                    includeDeclaration = new { type = "boolean", description = "Include the declaration", @default = true }
                },
                required = new[] { "filePath", "line", "column" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var result = await tool.ExecuteAsync(
                    ValidateRequired(parameters.FilePath, "filePath"),
                    ValidatePositive(parameters.Line, "line"),
                    ValidatePositive(parameters.Column, "column"),
                    parameters.IncludeDeclaration ?? true,
                    ct);
                    
                return CreateSuccessResult(result);
            }
        );
    }
    
    private static void RegisterBlazorHoverInfo(ToolRegistry registry, BlazorHoverInfoTool tool)
    {
        registry.RegisterTool<BlazorNavigationParams>(
            name: "blazor_hover_info",
            description: "Get hover information (types, documentation, signatures) for symbols in Blazor (.razor) files",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    filePath = new { type = "string", description = "Path to the .razor file" },
                    line = new { type = "integer", description = "Line number (1-based)" },
                    column = new { type = "integer", description = "Column number (1-based)" }
                },
                required = new[] { "filePath", "line", "column" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var result = await tool.ExecuteAsync(
                    ValidateRequired(parameters.FilePath, "filePath"),
                    ValidatePositive(parameters.Line, "line"),
                    ValidatePositive(parameters.Column, "column"),
                    ct);
                    
                return CreateSuccessResult(result);
            }
        );
    }
    
    private static void RegisterBlazorRenameSymbol(ToolRegistry registry, BlazorRenameSymbolTool tool)
    {
        registry.RegisterTool<BlazorRenameParams>(
            name: "blazor_rename_symbol",
            description: "Rename symbols across Blazor (.razor) files - supports C# symbols with full refactoring",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    filePath = new { type = "string", description = "Path to the .razor file" },
                    line = new { type = "integer", description = "Line number (1-based)" },
                    column = new { type = "integer", description = "Column number (1-based)" },
                    newName = new { type = "string", description = "New name for the symbol" },
                    preview = new { type = "boolean", description = "Preview changes without applying them", @default = true }
                },
                required = new[] { "filePath", "line", "column", "newName" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var result = await tool.ExecuteAsync(
                    ValidateRequired(parameters.FilePath, "filePath"),
                    ValidatePositive(parameters.Line, "line"),
                    ValidatePositive(parameters.Column, "column"),
                    ValidateRequired(parameters.NewName, "newName"),
                    parameters.Preview ?? true,
                    ct);
                    
                return CreateSuccessResult(result);
            }
        );
    }
    
    private static void RegisterBlazorGetDocumentSymbols(ToolRegistry registry, BlazorGetDocumentSymbolsTool tool)
    {
        registry.RegisterTool<BlazorDocumentSymbolsParams>(
            name: "blazor_get_document_symbols",
            description: "Get document outline/symbols for Blazor (.razor) files - shows component structure, methods, properties",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    filePath = new { type = "string", description = "Path to the .razor file" },
                    includeMembers = new { type = "boolean", description = "Include class members", @default = true }
                },
                required = new[] { "filePath" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var result = await tool.ExecuteAsync(
                    ValidateRequired(parameters.FilePath, "filePath"),
                    parameters.IncludeMembers ?? true,
                    ct);
                    
                return CreateSuccessResult(result);
            }
        );
    }
    
    private static void RegisterBlazorGetDiagnostics(ToolRegistry registry, BlazorGetDiagnosticsTool tool)
    {
        registry.RegisterTool<BlazorDiagnosticsParams>(
            name: "blazor_get_diagnostics",
            description: "Get compilation diagnostics (errors, warnings, hints) for Blazor (.razor) files",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    filePath = new { type = "string", description = "Path to the .razor file" },
                    severities = new { 
                        type = "array", 
                        items = new { type = "string" },
                        description = "Filter by severity levels (error, warning, hint)", 
                        @default = new string[0] 
                    },
                    includeHints = new { type = "boolean", description = "Include hint/info diagnostics", @default = false },
                    refreshDiagnostics = new { type = "boolean", description = "Force refresh diagnostics before retrieval", @default = false }
                },
                required = new[] { "filePath" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var result = await tool.ExecuteAsync(
                    ValidateRequired(parameters.FilePath, "filePath"),
                    parameters.Severities ?? Array.Empty<string>(),
                    parameters.IncludeHints ?? false,
                    parameters.RefreshDiagnostics ?? false,
                    ct);
                    
                return CreateSuccessResult(result);
            }
        );
    }
}

// Parameter classes for Blazor tools
public class BlazorNavigationParams
{
    public string FilePath { get; set; } = "";
    public int Line { get; set; }
    public int Column { get; set; }
}

public class BlazorFindReferencesParams
{
    public string FilePath { get; set; } = "";
    public int Line { get; set; }
    public int Column { get; set; }
    public bool? IncludeDeclaration { get; set; }
}

public class BlazorRenameParams
{
    public string FilePath { get; set; } = "";
    public int Line { get; set; }
    public int Column { get; set; }
    public string NewName { get; set; } = "";
    public bool? Preview { get; set; }
}

public class BlazorDocumentSymbolsParams
{
    public string FilePath { get; set; } = "";
    public bool? IncludeMembers { get; set; }
}

public class BlazorDiagnosticsParams
{
    public string FilePath { get; set; } = "";
    public string[] Severities { get; set; } = Array.Empty<string>();
    public bool? IncludeHints { get; set; }
    public bool? RefreshDiagnostics { get; set; }
}