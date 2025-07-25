namespace COA.CodeSearch.McpServer.Constants;

/// <summary>
/// Centralized constants for all MCP tool names to avoid magic strings
/// and enable safe refactoring through IDE rename operations
/// </summary>
public static class ToolNames
{
    // Core navigation tools
    public const string GoToDefinition = "go_to_definition";
    public const string FindReferences = "find_references";
    public const string SearchSymbols = "search_symbols";
    public const string GetImplementations = "get_implementations";
    
    // Code information tools
    public const string GetHoverInfo = "get_hover_info";
    public const string GetDocumentSymbols = "get_document_symbols";
    public const string GetDiagnostics = "get_diagnostics";
    
    // Advanced analysis tools
    public const string GetCallHierarchy = "get_call_hierarchy";
    public const string RenameSymbol = "rename_symbol";
    public const string BatchOperations = "batch_operations";
    public const string AdvancedSymbolSearch = "advanced_symbol_search";
    public const string DependencyAnalysis = "dependency_analysis";
    public const string ProjectStructureAnalysis = "project_structure_analysis";
    
    // Text search tools
    public const string TextSearch = "text_search";
    public const string FileSearch = "file_search";
    public const string RecentFiles = "recent_files";
    public const string FileSizeAnalysis = "file_size_analysis";
    public const string SimilarFiles = "similar_files";
    public const string DirectorySearch = "directory_search";
    public const string IndexWorkspace = "index_workspace";
    
    // Memory system tools
    public const string StoreMemory = "store_memory";
    public const string SearchMemories = "search_memories";
    public const string GetMemory = "get_memory";
    public const string UpdateMemory = "update_memory";
    public const string MemoryTimeline = "memory_timeline";
    public const string MemoryDashboard = "memory_dashboard";
    public const string BackupMemories = "backup_memories";
    public const string RestoreMemories = "restore_memories";
    public const string RecallContext = "recall_context";
    
    // Memory linking tools
    public const string LinkMemories = "link_memories";
    public const string UnlinkMemories = "unlink_memories";
    public const string GetRelatedMemories = "get_related_memories";
    public const string FindSimilarMemories = "find_similar_memories";
    
    // Memory templates and suggestions
    public const string ListMemoryTemplates = "list_memory_templates";
    public const string CreateMemoryFromTemplate = "create_memory_from_template";
    public const string GetMemorySuggestions = "get_memory_suggestions";
    public const string ArchiveMemories = "archive_memories";
    public const string SummarizeMemories = "summarize_memories";
    
    // Temporary memory tools
    public const string StoreTemporaryMemory = "store_temporary_memory";
    public const string StoreGitCommitMemory = "store_git_commit_memory";
    public const string GetMemoriesForFile = "get_memories_for_file";
    
    // Checklist tools
    public const string CreateChecklist = "create_checklist";
    public const string ListChecklists = "list_checklists";
    public const string ViewChecklist = "view_checklist";
    public const string AddChecklistItems = "add_checklist_items";
    public const string ToggleChecklistItem = "toggle_checklist_item";
    public const string UpdateChecklistItem = "update_checklist_item";
    
    // TypeScript tools
    public const string SearchTypeScript = "search_typescript";
    public const string TypeScriptGoToDefinition = "typescript_go_to_definition";
    public const string TypeScriptFindReferences = "typescript_find_references";
    public const string TypeScriptHoverInfo = "typescript_hover_info";
    public const string TypeScriptRenameSymbol = "typescript_rename_symbol";
    
    // System tools
    public const string LogDiagnostics = "log_diagnostics";
    public const string GetVersion = "get_version";
}