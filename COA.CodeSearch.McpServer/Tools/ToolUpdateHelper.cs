// This is a temporary helper file for updating tools to implement ITool
// DELETE THIS FILE after the update is complete

namespace COA.CodeSearch.McpServer.Tools;

public static class ToolUpdateHelper
{
    // Tool information for batch updates
    public static readonly Dictionary<string, (string description, ToolCategory category)> ToolInfo = new()
    {
        // Navigation tools
        ["GoToDefinitionTool"] = ("Navigate to symbol definitions instantly - works across entire solutions for C# and TypeScript", ToolCategory.Navigation),
        ["FindReferencesTool"] = ("Find all references to a symbol across the codebase", ToolCategory.Navigation),
        ["GetCallHierarchyTool"] = ("Trace method call chains to understand execution flow", ToolCategory.Navigation),
        ["GetImplementationsTool"] = ("Discover all concrete implementations of interfaces or abstract classes", ToolCategory.Navigation),
        
        // Search tools
        ["SearchSymbolsTool"] = ("Search for C# symbols by name with wildcards and fuzzy matching", ToolCategory.Search),
        ["FastTextSearchTool"] = ("Blazing fast text search across millions of lines using Lucene", ToolCategory.Search),
        ["FastFileSearchTool"] = ("Find files by name with fuzzy matching and typo correction", ToolCategory.Search),
        ["FastDirectorySearchTool"] = ("Search for directories with fuzzy matching", ToolCategory.Search),
        ["FastRecentFilesTool"] = ("Find recently modified files using indexed timestamps", ToolCategory.Search),
        ["FastSimilarFilesTool"] = ("Find files with similar content using 'More Like This'", ToolCategory.Search),
        ["AdvancedSymbolSearchTool"] = ("Advanced symbol search with semantic filters", ToolCategory.Search),
        
        // Analysis tools
        ["GetDocumentSymbolsTool"] = ("Get outline of all symbols in a file", ToolCategory.Analysis),
        ["FastFileSizeAnalysisTool"] = ("Analyze files by size and distribution", ToolCategory.Analysis),
        ["GetHoverInfoTool"] = ("Get detailed type information and documentation", ToolCategory.Analysis),
        
        // Refactoring tools
        
        // TypeScript tools
        ["TypeScriptGoToDefinitionTool"] = ("Navigate to TypeScript definitions using tsserver", ToolCategory.TypeScript),
        ["TypeScriptFindReferencesTool"] = ("Find all references to TypeScript symbols", ToolCategory.TypeScript),
        ["TypeScriptSearchTool"] = ("Search for TypeScript symbols across codebase", ToolCategory.TypeScript),
        ["TypeScriptHoverInfoTool"] = ("Get TypeScript type information and docs", ToolCategory.TypeScript),
        ["TypeScriptRenameTool"] = ("Rename TypeScript symbols using Language Service", ToolCategory.TypeScript),
        
        // Infrastructure tools
        ["IndexWorkspaceTool"] = ("Build search index for blazing fast searches", ToolCategory.Infrastructure),
        ["SetLoggingTool"] = ("Control file-based logging dynamically", ToolCategory.Infrastructure),
        ["GetVersionTool"] = ("Get version and build information", ToolCategory.Infrastructure),
        
        // Batch tools
        ["BatchOperationsTool"] = ("Execute multiple operations in parallel", ToolCategory.Batch),
        
        // Memory tools (collections)
        ["ClaudeMemoryTools"] = ("Essential memory system operations", ToolCategory.Memory),
        ["FlexibleMemoryTools"] = ("Flexible memory storage and retrieval", ToolCategory.Memory),
        ["ChecklistTools"] = ("Persistent checklist management", ToolCategory.Memory),
        ["MemoryLinkingTools"] = ("Memory relationship management", ToolCategory.Memory),
        ["TimelineTool"] = ("View memories in chronological timeline", ToolCategory.Memory),
        
        // V2 Tools (all inherit from ClaudeOptimizedToolBase)
        ["FindReferencesToolV2"] = ("AI-optimized reference finding with insights", ToolCategory.Navigation),
        ["SearchSymbolsToolV2"] = ("AI-optimized symbol search with structured response", ToolCategory.Search),
        ["FastTextSearchToolV2"] = ("AI-optimized text search with insights", ToolCategory.Search),
        ["FastFileSearchToolV2"] = ("AI-optimized file search with hotspots", ToolCategory.Search),
        ["GetDiagnosticsToolV2"] = ("AI-optimized diagnostics with categorization", ToolCategory.Analysis),
        ["DependencyAnalysisToolV2"] = ("AI-optimized dependency analysis", ToolCategory.Analysis),
        ["ProjectStructureAnalysisToolV2"] = ("AI-optimized project analysis", ToolCategory.Analysis),
        ["GetImplementationsToolV2"] = ("AI-optimized implementation discovery", ToolCategory.Analysis),
        ["GetCallHierarchyToolV2"] = ("AI-optimized call hierarchy analysis", ToolCategory.Analysis),
        ["RenameSymbolToolV2"] = ("AI-optimized symbol renaming with risk assessment", ToolCategory.Refactoring),
        ["BatchOperationsToolV2"] = ("AI-optimized batch operations", ToolCategory.Batch),
        ["FlexibleMemorySearchToolV2"] = ("AI-optimized memory search", ToolCategory.Memory),
    };
    
    public static string GetToolName(string className)
    {
        // Convert class name to tool name
        // GoToDefinitionTool -> go_to_definition
        // FastTextSearchToolV2 -> fast_text_search_v2
        
        var name = className
            .Replace("Tool", "")
            .Replace("Tools", "");
            
        // Handle V2 suffix specially
        if (name.EndsWith("V2"))
        {
            name = name.Substring(0, name.Length - 2) + "_v2";
        }
        
        // Convert to snake_case
        var result = "";
        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
            {
                result += "_";
            }
            result += char.ToLower(name[i]);
        }
        
        return result;
    }
}