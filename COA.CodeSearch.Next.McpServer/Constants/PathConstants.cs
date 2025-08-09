namespace COA.CodeSearch.Next.McpServer.Constants;

/// <summary>
/// Centralized constants for all path-related operations
/// </summary>
public static class PathConstants
{
    // Root directory configuration
    public const string DefaultBasePath = "~/.coa/codesearch";
    
    // Directory names
    public const string IndexDirectoryName = "indexes";
    public const string LogsDirectoryName = "logs";
    public const string BackupsDirectoryName = "backups";
    
    // File names
    public const string WorkspaceMetadataFileName = "workspace.metadata.json";
    public const string BackupPrefixFormat = "backup_{0}";
    
    // Configuration keys
    public const string BasePathConfigKey = "CodeSearch:BasePath";
    public const string IndexRootPathConfigKey = "CodeSearch:Lucene:IndexRootPath";
    
    // Excluded directories (common across services)
    public static readonly string[] DefaultExcludedDirectories = 
    {
        "bin", "obj", "node_modules", ".git", ".vs", ".vscode", ".idea",
        "packages", ".nuget", "TestResults", "dist", "build", "target",
        ".codesearch", ".coa", ".codenav"
    };
    
    // Hash settings
    public const int WorkspaceHashLength = 8;  // Shorter hash for readability
    public const int MaxSafeWorkspaceName = 30;
}