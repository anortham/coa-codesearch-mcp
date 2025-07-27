namespace COA.CodeSearch.McpServer.Constants;

/// <summary>
/// Centralized constants for all MCP tool names to avoid magic strings
/// and enable safe refactoring through IDE rename operations
/// </summary>
public static class ToolNames
{
    // Batch operations
    public const string BatchOperations = "batch_operations";
    
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
    
    // System tools
    public const string LogDiagnostics = "log_diagnostics";
    public const string GetVersion = "get_version";
    public const string IndexHealthCheck = "index_health_check";
    public const string SystemHealthCheck = "system_health_check";
    public const string ToolUsageAnalytics = "tool_usage_analytics";
    
    // AI-optimized tools (Phase 3)
    public const string SearchAssistant = "search_assistant";
    public const string PatternDetector = "pattern_detector";
    public const string MemoryGraphNavigator = "memory_graph_navigator";
}