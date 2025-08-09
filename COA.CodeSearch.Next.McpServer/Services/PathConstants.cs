namespace COA.CodeSearch.Next.McpServer.Services;

/// <summary>
/// Centralized constants for all path-related operations
/// </summary>
public static class PathConstants
{
    // Root directory names
    public const string BaseDirectoryName = ".coa";
    public const string CodeSearchDirectoryName = "codesearch";
    public const string IndexDirectoryName = "indexes";
    public const string LogsDirectoryName = "logs";
    public const string BackupsDirectoryName = "backups";
    
    // File names
    public const string WorkspaceMetadataFileName = "workspace_metadata.json";
    public const string BackupPrefixFormat = "backup_{0}";
    
    // Configuration keys
    public const string IndexBasePathConfigKey = "Lucene:IndexBasePath";
    public const string BasePathConfigKey = "CodeSearch:BasePath";
    
    // Default paths
    public static readonly string DefaultBasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), BaseDirectoryName, CodeSearchDirectoryName);
    
    // Excluded directories (common across services)
    public static readonly string[] DefaultExcludedDirectories = 
    {
        "bin", "obj", "node_modules", ".git", ".vs", "packages", "TestResults", 
        BaseDirectoryName, CodeSearchDirectoryName, ".codenav"
    };
    
    // Hash settings
    public const int WorkspaceHashLength = 16;
    public const int MaxSafeWorkspaceName = 30;
}