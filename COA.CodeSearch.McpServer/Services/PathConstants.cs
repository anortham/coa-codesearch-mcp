namespace COA.CodeSearch.McpServer.Services;

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
        "bin", "obj", "node_modules", ".git", ".vs", ".vscode", "packages", "TestResults",
        "target", "dist", "build",
        BaseDirectoryName, CodeSearchDirectoryName, ".codenav"
    };
    
    // Blacklisted extensions (common across services) - files to exclude from indexing
    public static readonly string[] DefaultBlacklistedExtensions = 
    {
        // Binary files
        ".dll", ".exe", ".pdb", ".so", ".dylib", ".lib", ".a", ".o", ".obj",
        // Media files  
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".ico", ".svg", ".webp",
        ".mp3", ".mp4", ".avi", ".mov", ".wmv", ".flv", ".webm", ".mkv",
        // Archives
        ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz",
        // Database files
        ".db", ".sqlite", ".mdf", ".ldf", ".bak",
        // Temporary files (including Claude Code temp files with numbered suffixes)
        ".tmp", ".temp", ".cache", ".swp", ".swo",
        // Logs (large files)
        ".log"
    };
    
    // Hash settings
    public const int WorkspaceHashLength = 8; // Reduced from 16 for readability
    public const int MaxSafeWorkspaceName = 30;
}