using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.Next.McpServer.Services;

/// <summary>
/// Manages write.lock files for Lucene indexes to prevent corruption
/// Provides tiered cleanup strategies based on risk level
/// </summary>
public class WriteLockManager : IWriteLockManager
{
    private const string WRITE_LOCK_FILENAME = "write.lock";
    private const string SEGMENTS_FILENAME = "segments.gen";
    private readonly IPathResolutionService _pathResolution;
    private readonly ILogger<WriteLockManager> _logger;

    public WriteLockManager(
        IPathResolutionService pathResolution,
        ILogger<WriteLockManager> logger)
    {
        _pathResolution = pathResolution;
        _logger = logger;
    }

    /// <summary>
    /// Smart startup cleanup with tiered approach based on risk
    /// TIER 1: Auto-clean test artifacts (very low risk)
    /// TIER 2: Auto-clean workspace locks with safety checks (low risk)
    /// TIER 3: Diagnose-only for production indexes (report but don't clean)
    /// </summary>
    public async Task<WriteLockCleanupResult> SmartStartupCleanupAsync()
    {
        var testArtifactMinAge = TimeSpan.FromMinutes(1);
        var workspaceMinAge = TimeSpan.FromMinutes(5);
        
        _logger.LogInformation("STARTUP: Smart cleanup - Tiered approach based on risk level");
        
        var result = new WriteLockCleanupResult();
        
        // TIER 1: SAFE AUTO-CLEANUP - Test artifacts
        try
        {
            var testCleanupCount = await CleanupTestArtifactsAsync(testArtifactMinAge);
            result.TestArtifactsRemoved = testCleanupCount;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TIER 1 CLEANUP: Failed to clean test artifacts");
            result.Errors.Add($"Test artifacts: {ex.Message}");
        }
        
        // TIER 2: CONSERVATIVE AUTO-CLEANUP - Workspace indexes with safety checks
        try
        {
            var workspaceCleanupCount = await CleanupWorkspaceIndexesAsync(workspaceMinAge);
            result.WorkspaceLocksRemoved = workspaceCleanupCount;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TIER 2 CLEANUP: Failed to clean workspace locks");
            result.Errors.Add($"Workspace locks: {ex.Message}");
        }
        
        // TIER 3: DIAGNOSE-ONLY - Report stuck locks but don't remove
        try
        {
            var stuckLocks = await DiagnoseStuckIndexesAsync();
            result.StuckLocksFound = stuckLocks.Count;
            result.StuckLocks = stuckLocks;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TIER 3 DIAGNOSE: Failed to diagnose stuck locks");
            result.Errors.Add($"Diagnosis: {ex.Message}");
        }
        
        // Log summary
        _logger.LogInformation(
            "STARTUP CLEANUP COMPLETE - Test artifacts removed: {TestCount}, " +
            "Workspace locks removed: {WorkspaceCount}, Stuck locks found: {StuckCount}, " +
            "Errors: {ErrorCount}",
            result.TestArtifactsRemoved, result.WorkspaceLocksRemoved, 
            result.StuckLocksFound, result.Errors.Count);
        
        return result;
    }

    /// <summary>
    /// TIER 1: Clean up test artifacts (very safe)
    /// </summary>
    private Task<int> CleanupTestArtifactsAsync(TimeSpan minAge)
    {
        var cleanupCount = 0;
        _logger.LogDebug("TIER 1: Cleaning test artifacts older than {MinAge}", minAge);
        
        var indexRoot = _pathResolution.GetIndexRootPath();
        if (!Directory.Exists(indexRoot))
        {
            return Task.FromResult(0);
        }
        
        // Find test artifact locks
        var testPatterns = new[] { "/bin/debug/", "/bin/release/", "/testprojects/", "/test" };
        
        foreach (var indexDir in Directory.GetDirectories(indexRoot))
        {
            var lockPath = Path.Combine(indexDir, WRITE_LOCK_FILENAME);
            
            if (!File.Exists(lockPath))
                continue;
                
            // Check if it's a test artifact
            var normalizedPath = lockPath.Replace('\\', '/').ToLowerInvariant();
            var isTestArtifact = testPatterns.Any(pattern => normalizedPath.Contains(pattern));
            
            if (!isTestArtifact)
                continue;
                
            try
            {
                var lockAge = DateTime.UtcNow - File.GetLastWriteTimeUtc(lockPath);
                
                if (lockAge > minAge)
                {
                    File.Delete(lockPath);
                    cleanupCount++;
                    _logger.LogInformation("TIER 1: Removed test artifact lock: {Path} (age: {Age})", 
                        lockPath, lockAge);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("TIER 1: Could not process test lock {Path}: {Error}", 
                    lockPath, ex.Message);
            }
        }
        
        if (cleanupCount > 0)
        {
            _logger.LogInformation("TIER 1: Cleaned {Count} test artifact locks", cleanupCount);
        }
        
        return Task.FromResult(cleanupCount);
    }

    /// <summary>
    /// TIER 2: Clean up workspace locks with safety checks
    /// </summary>
    private async Task<int> CleanupWorkspaceIndexesAsync(TimeSpan minAge)
    {
        var cleanupCount = 0;
        _logger.LogDebug("TIER 2: Cleaning workspace locks with safety validation");
        
        var indexRoot = _pathResolution.GetIndexRootPath();
        if (!Directory.Exists(indexRoot))
        {
            return 0;
        }
        
        foreach (var indexDir in Directory.GetDirectories(indexRoot))
        {
            var lockPath = Path.Combine(indexDir, WRITE_LOCK_FILENAME);
            
            if (!File.Exists(lockPath))
                continue;
                
            try
            {
                var lockAge = DateTime.UtcNow - File.GetLastWriteTimeUtc(lockPath);
                
                if (lockAge > minAge)
                {
                    // Safety checks before deletion
                    if (await IsSafeToRemoveLockAsync(lockPath))
                    {
                        File.Delete(lockPath);
                        cleanupCount++;
                        _logger.LogInformation("TIER 2: Removed stuck workspace lock: {Path} (age: {Age})", 
                            lockPath, lockAge);
                    }
                    else
                    {
                        _logger.LogWarning("TIER 2: Skipped unsafe lock removal: {Path} (age: {Age})", 
                            lockPath, lockAge);
                    }
                }
                else
                {
                    _logger.LogDebug("TIER 2: Found recent workspace lock: {Path} (age: {Age}) - keeping", 
                        lockPath, lockAge);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("TIER 2: Could not process workspace lock {Path}: {Error}", 
                    lockPath, ex.Message);
            }
        }
        
        if (cleanupCount > 0)
        {
            _logger.LogInformation("TIER 2: Cleaned {Count} workspace locks", cleanupCount);
        }
        
        return cleanupCount;
    }

    /// <summary>
    /// TIER 3: Diagnose stuck locks (no automatic removal)
    /// </summary>
    private async Task<List<StuckLockInfo>> DiagnoseStuckIndexesAsync()
    {
        var stuckLocks = new List<StuckLockInfo>();
        var lockTimeout = TimeSpan.FromMinutes(15);
        
        _logger.LogDebug("TIER 3: Diagnosing stuck locks (no auto-removal)");
        
        var indexRoot = _pathResolution.GetIndexRootPath();
        if (!Directory.Exists(indexRoot))
        {
            return stuckLocks;
        }
        
        foreach (var indexDir in Directory.GetDirectories(indexRoot))
        {
            var lockPath = Path.Combine(indexDir, WRITE_LOCK_FILENAME);
            
            if (!File.Exists(lockPath))
                continue;
                
            try
            {
                var lockAge = DateTime.UtcNow - File.GetLastWriteTimeUtc(lockPath);
                
                if (lockAge > lockTimeout)
                {
                    var diagnostics = await GetLockDiagnosticsAsync(lockPath);
                    
                    stuckLocks.Add(new StuckLockInfo
                    {
                        Path = lockPath,
                        IndexPath = indexDir,
                        Age = lockAge,
                        FileSize = diagnostics.FileSizeBytes,
                        IsAccessible = diagnostics.IsAccessible
                    });
                    
                    // Extract workspace name from descriptive directory name
                    var dirName = Path.GetFileName(indexDir);
                    var workspaceName = dirName.Contains('_') 
                        ? dirName.Substring(0, dirName.LastIndexOf('_'))
                        : "unknown";
                    
                    _logger.LogError(
                        "CRITICAL: Found stuck lock for workspace '{Workspace}' at {Path}. " +
                        "Lock Age: {LockAge}, File Size: {FileSize} bytes, Accessible: {IsAccessible}. " +
                        "This indicates improper disposal of index writers! " +
                        "Manual intervention may be required: delete the write.lock file after ensuring no processes are using the index.",
                        workspaceName, lockPath, lockAge, diagnostics.FileSizeBytes, diagnostics.IsAccessible);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("TIER 3: Could not diagnose lock {Path}: {Error}", 
                    lockPath, ex.Message);
            }
        }
        
        if (stuckLocks.Count > 0)
        {
            _logger.LogWarning("TIER 3: Found {Count} stuck locks requiring attention", stuckLocks.Count);
        }
        
        return stuckLocks;
    }

    /// <summary>
    /// Safety checks before removing a lock file
    /// </summary>
    private async Task<bool> IsSafeToRemoveLockAsync(string lockPath)
    {
        try
        {
            // Check 1: File is not currently being written to
            var initialSize = new FileInfo(lockPath).Length;
            await Task.Delay(100); // Brief pause
            var currentSize = new FileInfo(lockPath).Length;
            
            if (initialSize != currentSize)
            {
                _logger.LogDebug("SAFETY CHECK: Lock file {Path} is growing - likely in use", lockPath);
                return false;
            }
            
            // Check 2: Try to get exclusive access briefly
            try
            {
                using var fs = File.Open(lockPath, FileMode.Open, FileAccess.Read, FileShare.None);
                // If we can get exclusive access, it's probably safe
                return true;
            }
            catch (IOException)
            {
                _logger.LogDebug("SAFETY CHECK: Lock file {Path} is in use by another process", lockPath);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("SAFETY CHECK: Could not verify safety for {Path}: {Error}", 
                lockPath, ex.Message);
            return false; // Err on the side of caution
        }
    }

    /// <summary>
    /// Get diagnostic information about a lock file
    /// </summary>
    private Task<LockDiagnostics> GetLockDiagnosticsAsync(string lockPath)
    {
        var diagnostics = new LockDiagnostics();
        
        try
        {
            var fileInfo = new FileInfo(lockPath);
            diagnostics.FileSizeBytes = fileInfo.Length;
            diagnostics.LastWriteTime = fileInfo.LastWriteTimeUtc;
            diagnostics.Age = DateTime.UtcNow - diagnostics.LastWriteTime;
            
            // Check if we can access the file
            try
            {
                using var fs = File.Open(lockPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                diagnostics.IsAccessible = true;
            }
            catch
            {
                diagnostics.IsAccessible = false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Could not get diagnostics for {Path}: {Error}", lockPath, ex.Message);
        }
        
        return Task.FromResult(diagnostics);
    }

    /// <summary>
    /// Force remove a stuck lock (use with extreme caution)
    /// </summary>
    public async Task<bool> ForceRemoveLockAsync(string indexPath)
    {
        var lockPath = Path.Combine(indexPath, WRITE_LOCK_FILENAME);
        
        if (!File.Exists(lockPath))
        {
            _logger.LogWarning("No lock file found at {Path}", lockPath);
            return false;
        }
        
        try
        {
            // First try safe removal
            if (await IsSafeToRemoveLockAsync(lockPath))
            {
                File.Delete(lockPath);
                _logger.LogInformation("Successfully removed lock file at {Path} (safe removal)", lockPath);
                return true;
            }
            
            // If not safe, log warning but still try
            _logger.LogWarning("FORCE REMOVAL: Lock at {Path} appears to be in use, attempting force removal", lockPath);
            
            // Try multiple times with delays
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    File.Delete(lockPath);
                    _logger.LogInformation("FORCE REMOVAL: Successfully removed lock on attempt {Attempt}", attempt);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("FORCE REMOVAL: Attempt {Attempt} failed: {Error}", attempt, ex.Message);
                    if (attempt < 3)
                    {
                        await Task.Delay(500 * attempt); // Increasing delay
                    }
                }
            }
            
            _logger.LogError("FORCE REMOVAL: Failed to remove lock after 3 attempts");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FORCE REMOVAL: Unexpected error removing lock at {Path}", lockPath);
            return false;
        }
    }

    private class LockDiagnostics
    {
        public long FileSizeBytes { get; set; }
        public DateTime LastWriteTime { get; set; }
        public TimeSpan Age { get; set; }
        public bool IsAccessible { get; set; }
    }
}

public class WriteLockCleanupResult
{
    public int TestArtifactsRemoved { get; set; }
    public int WorkspaceLocksRemoved { get; set; }
    public int StuckLocksFound { get; set; }
    public List<StuckLockInfo> StuckLocks { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}

public class StuckLockInfo
{
    public string Path { get; set; } = string.Empty;
    public string IndexPath { get; set; } = string.Empty;
    public TimeSpan Age { get; set; }
    public long FileSize { get; set; }
    public bool IsAccessible { get; set; }
}