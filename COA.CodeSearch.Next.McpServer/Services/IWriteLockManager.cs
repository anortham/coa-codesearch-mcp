namespace COA.CodeSearch.Next.McpServer.Services;

/// <summary>
/// Interface for managing Lucene index write.lock files
/// </summary>
public interface IWriteLockManager
{
    /// <summary>
    /// Perform smart startup cleanup with tiered approach
    /// </summary>
    Task<WriteLockCleanupResult> SmartStartupCleanupAsync();
    
    /// <summary>
    /// Force remove a stuck lock file (use with caution)
    /// </summary>
    Task<bool> ForceRemoveLockAsync(string indexPath);
}