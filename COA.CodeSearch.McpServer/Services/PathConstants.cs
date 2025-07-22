namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Centralized constants for all path-related operations
/// </summary>
public static class PathConstants
{
    // Root directory names
    public const string BaseDirectoryName = ".codesearch";
    public const string IndexDirectoryName = "index";
    public const string LogsDirectoryName = "logs";
    public const string BackupsDirectoryName = "backups";
    
    // Memory directory names
    public const string ProjectMemoryDirectoryName = "project-memory";
    public const string LocalMemoryDirectoryName = "local-memory";
    
    // File names
    public const string WorkspaceMetadataFileName = "workspace_metadata.json";
    public const string BackupPrefixFormat = "backup_{0}";
    
    // Configuration keys
    public const string IndexBasePathConfigKey = "Lucene:IndexBasePath";
    
    // Excluded directories (common across services)
    public static readonly string[] DefaultExcludedDirectories = 
    {
        "bin", "obj", "node_modules", ".git", ".vs", "packages", "TestResults", BaseDirectoryName
    };
    
    // Hash settings
    public const int WorkspaceHashLength = 16;
    public const int MaxSafeWorkspaceName = 30;
    
    // TypeScript installer paths
    public const string TypeScriptInstallerDirectory = "COA.CodeSearch.McpServer";
    public const string TypeScriptSubDirectory = "typescript";
}