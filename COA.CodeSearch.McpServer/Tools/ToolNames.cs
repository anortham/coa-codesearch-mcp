namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Constants for tool names used across the CodeSearch MCP server
/// </summary>
public static class ToolNames
{
    // Core search operations
    public const string IndexWorkspace = "index_workspace";
    public const string TextSearch = "text_search";
    public const string FileSearch = "file_search";
    public const string BatchOperations = "batch_operations";
    
    // Advanced search operations
    public const string LineSearch = "line_search";
    public const string SearchAndReplace = "search_and_replace";
    
    // File and directory operations
    public const string DirectorySearch = "directory_search";
    public const string RecentFiles = "recent_files";
    public const string SimilarFiles = "similar_files";
    
    // Navigation tools (from CodeNav consolidation)
    public const string SymbolSearch = "symbol_search";
    public const string FindReferences = "find_references";
    public const string GoToDefinition = "goto_definition";
    
    // Editing tools
    public const string InsertAtLine = "insert_at_line";
    public const string ReplaceLines = "replace_lines";
    public const string DeleteLines = "delete_lines";
}