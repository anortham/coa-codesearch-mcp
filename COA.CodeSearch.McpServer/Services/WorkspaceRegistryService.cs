using System.Text.Json;
using COA.CodeSearch.McpServer.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// High-performance workspace registry service with async I/O and memory caching
/// Provides centralized workspace metadata management for the CodeSearch system
/// </summary>
public class WorkspaceRegistryService : IWorkspaceRegistryService
{
    private const string CACHE_KEY = "workspace_registry";
    private static readonly TimeSpan CACHE_DURATION = TimeSpan.FromSeconds(5);
    
    private readonly IPathResolutionService _pathResolution;
    private readonly IMemoryCache _cache;
    private readonly ILogger<WorkspaceRegistryService> _logger;
    private readonly SemaphoreSlim _registryLock = new(1, 1);
    
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    
    /// <summary>
    /// Path to the global registry file
    /// </summary>
    private string RegistryPath => Path.Combine(_pathResolution.GetIndexRootPath(), "workspace_registry.json");
    
    /// <summary>
    /// Path to the backup registry file
    /// </summary>
    private string BackupRegistryPath => $"{RegistryPath}.backup";
    
    public WorkspaceRegistryService(
        IPathResolutionService pathResolution,
        IMemoryCache cache,
        ILogger<WorkspaceRegistryService> logger)
    {
        _pathResolution = pathResolution;
        _cache = cache;
        _logger = logger;
    }
    
    #region Registry Operations
    
    public async Task<WorkspaceRegistry> LoadRegistryAsync()
    {
        // Check memory cache first (microseconds)
        if (_cache.TryGetValue(CACHE_KEY, out WorkspaceRegistry? cached) && cached != null)
        {
            _logger.LogDebug("Registry loaded from cache");
            return cached;
        }
        
        await _registryLock.WaitAsync();
        try
        {
            // Double-check after acquiring lock
            if (_cache.TryGetValue(CACHE_KEY, out cached) && cached != null)
            {
                return cached;
            }
            
            var registry = await LoadRegistryFromDiskAsync();
            
            // Update statistics
            UpdateRegistryStatistics(registry);
            
            // Cache with sliding expiration and size (required when SizeLimit is set)
            var cacheOptions = new MemoryCacheEntryOptions()
                .SetSlidingExpiration(CACHE_DURATION)
                .SetSize(1); // Registry is a single cached item
            _cache.Set(CACHE_KEY, registry, cacheOptions);
            
            _logger.LogDebug("Registry loaded from disk and cached. Workspaces: {Count}, Orphans: {OrphanCount}", 
                registry.Workspaces.Count, registry.OrphanedIndexes.Count);
                
            return registry;
        }
        finally
        {
            _registryLock.Release();
        }
    }
    
    public async Task SaveRegistryAsync(WorkspaceRegistry registry)
    {
        await _registryLock.WaitAsync();
        try
        {
            // Update metadata
            registry.LastUpdated = DateTime.UtcNow;
            UpdateRegistryStatistics(registry);
            
            // Atomic write with temp file
            var tempPath = $"{RegistryPath}.tmp";
            var json = JsonSerializer.Serialize(registry, _jsonOptions);
            
            await File.WriteAllTextAsync(tempPath, json);
            File.Move(tempPath, RegistryPath, true);
            
            // Create backup
            if (File.Exists(RegistryPath))
            {
                File.Copy(RegistryPath, BackupRegistryPath, true);
            }
            
            // Update cache with size (required when SizeLimit is set)
            var cacheOptions = new MemoryCacheEntryOptions()
                .SetSlidingExpiration(CACHE_DURATION)
                .SetSize(1); // Registry is a single cached item
            _cache.Set(CACHE_KEY, registry, cacheOptions);
            
            _logger.LogDebug("Registry saved successfully. Workspaces: {Count}, Orphans: {OrphanCount}", 
                registry.Workspaces.Count, registry.OrphanedIndexes.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save registry to {Path}", RegistryPath);
            throw;
        }
        finally
        {
            _registryLock.Release();
        }
    }
    
    public async Task<WorkspaceRegistry> GetOrCreateRegistryAsync()
    {
        if (File.Exists(RegistryPath))
        {
            return await LoadRegistryAsync();
        }
        
        _logger.LogInformation("Creating new workspace registry");
        var newRegistry = new WorkspaceRegistry();
        await SaveRegistryAsync(newRegistry);
        return newRegistry;
    }
    
    public async Task RefreshRegistryAsync()
    {
        _cache.Remove(CACHE_KEY);
        await LoadRegistryAsync();
        _logger.LogDebug("Registry cache invalidated and refreshed");
    }
    
    #endregion
    
    #region Workspace Management
    
    public async Task<WorkspaceEntry?> RegisterWorkspaceAsync(string workspacePath)
    {
        try
        {
            var normalizedPath = _pathResolution.GetFullPath(workspacePath);
            var hash = _pathResolution.ComputeWorkspaceHash(normalizedPath);
            var directoryName = Path.GetFileName(_pathResolution.GetIndexPath(normalizedPath));
            
            var registry = await LoadRegistryAsync();
            
            // Check if already registered
            if (registry.Workspaces.TryGetValue(hash, out var existing))
            {
                // Update last accessed time
                existing.LastAccessed = DateTime.UtcNow;
                await SaveRegistryAsync(registry);
                _logger.LogDebug("Workspace already registered, updated last accessed: {Path}", normalizedPath);
                return existing;
            }
            
            // Create new workspace entry
            var workspace = new WorkspaceEntry
            {
                Hash = hash,
                OriginalPath = normalizedPath,
                DirectoryName = directoryName,
                DisplayName = Path.GetFileName(normalizedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                Status = WorkspaceStatus.Active,
                CreatedAt = DateTime.UtcNow,
                LastAccessed = DateTime.UtcNow
            };
            
            registry.Workspaces[hash] = workspace;
            
            // Remove from orphans if it was there
            var orphanKey = registry.OrphanedIndexes.Keys.FirstOrDefault(k => 
                k.Contains(hash, StringComparison.OrdinalIgnoreCase) || 
                registry.OrphanedIndexes[k].DirectoryName == directoryName);
                
            if (orphanKey != null)
            {
                registry.OrphanedIndexes.Remove(orphanKey);
                _logger.LogInformation("Workspace {Path} was orphaned, now registered properly", normalizedPath);
            }
            
            await SaveRegistryAsync(registry);
            
            _logger.LogInformation("Registered new workspace: {Path} (hash: {Hash})", normalizedPath, hash);
            return workspace;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register workspace: {Path}", workspacePath);
            return null;
        }
    }
    
    public async Task<bool> UnregisterWorkspaceAsync(string workspaceHash)
    {
        var registry = await LoadRegistryAsync();
        
        if (registry.Workspaces.Remove(workspaceHash))
        {
            await SaveRegistryAsync(registry);
            _logger.LogInformation("Unregistered workspace with hash: {Hash}", workspaceHash);
            return true;
        }
        
        return false;
    }
    
    public async Task<WorkspaceEntry?> GetWorkspaceByHashAsync(string hash)
    {
        var registry = await LoadRegistryAsync();
        return registry.Workspaces.GetValueOrDefault(hash);
    }
    
    public async Task<WorkspaceEntry?> GetWorkspaceByPathAsync(string path)
    {
        var normalizedPath = _pathResolution.GetFullPath(path);
        var hash = _pathResolution.ComputeWorkspaceHash(normalizedPath);
        return await GetWorkspaceByHashAsync(hash);
    }
    
    public async Task<WorkspaceEntry?> GetWorkspaceByDirectoryNameAsync(string directoryName)
    {
        var registry = await LoadRegistryAsync();
        return registry.Workspaces.Values.FirstOrDefault(w => 
            w.DirectoryName.Equals(directoryName, StringComparison.OrdinalIgnoreCase));
    }
    
    public async Task<IReadOnlyList<WorkspaceEntry>> GetAllWorkspacesAsync()
    {
        var registry = await LoadRegistryAsync();
        return registry.Workspaces.Values.ToList().AsReadOnly();
    }
    
    public async Task<bool> IsWorkspaceRegisteredAsync(string workspacePath)
    {
        var workspace = await GetWorkspaceByPathAsync(workspacePath);
        return workspace != null;
    }
    
    public async Task<bool> IsDirectoryRegisteredAsync(string directoryName)
    {
        var workspace = await GetWorkspaceByDirectoryNameAsync(directoryName);
        return workspace != null;
    }
    
    #endregion
    
    #region Workspace Status Updates
    
    public async Task UpdateWorkspaceStatusAsync(string hash, WorkspaceStatus status)
    {
        var registry = await LoadRegistryAsync();
        if (registry.Workspaces.TryGetValue(hash, out var workspace))
        {
            workspace.Status = status;
            await SaveRegistryAsync(registry);
        }
    }
    
    public async Task UpdateLastAccessedAsync(string hash)
    {
        var registry = await LoadRegistryAsync();
        if (registry.Workspaces.TryGetValue(hash, out var workspace))
        {
            workspace.LastAccessed = DateTime.UtcNow;
            await SaveRegistryAsync(registry);
        }
    }
    
    public async Task UpdateWorkspaceStatisticsAsync(string hash, int documentCount, long indexSizeBytes)
    {
        var registry = await LoadRegistryAsync();
        if (registry.Workspaces.TryGetValue(hash, out var workspace))
        {
            workspace.DocumentCount = documentCount;
            workspace.IndexSizeBytes = indexSizeBytes;
            await SaveRegistryAsync(registry);
        }
    }
    
    #endregion
    
    #region Orphan Management
    
    public async Task<OrphanedIndex> MarkAsOrphanedAsync(string directoryName, OrphanReason reason, string? attemptedPath = null)
    {
        var registry = await LoadRegistryAsync();
        
        var indexPath = Path.Combine(_pathResolution.GetIndexRootPath(), directoryName);
        var lastModified = Directory.Exists(indexPath) ? Directory.GetLastWriteTimeUtc(indexPath) : DateTime.UtcNow;
        var sizeBytes = 0L;
        
        if (Directory.Exists(indexPath))
        {
            try
            {
                sizeBytes = Directory.GetFiles(indexPath, "*", SearchOption.AllDirectories)
                    .Sum(file => new FileInfo(file).Length);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not calculate size of orphaned index: {Path}", indexPath);
            }
        }
        
        var orphan = new OrphanedIndex
        {
            DirectoryName = directoryName,
            DiscoveredAt = DateTime.UtcNow,
            LastModified = lastModified,
            Reason = reason,
            ScheduledForDeletion = DateTime.UtcNow.AddDays(7), // 7-day grace period
            SizeBytes = sizeBytes,
            AttemptedPath = attemptedPath
        };
        
        registry.OrphanedIndexes[directoryName] = orphan;
        await SaveRegistryAsync(registry);
        
        _logger.LogWarning("Marked index as orphaned: {Directory} (reason: {Reason}, size: {Size} bytes)", 
            directoryName, reason, sizeBytes);
            
        return orphan;
    }
    
    public async Task<IReadOnlyList<OrphanedIndex>> GetOrphanedIndexesAsync()
    {
        var registry = await LoadRegistryAsync();
        return registry.OrphanedIndexes.Values.ToList().AsReadOnly();
    }
    
    public async Task<bool> RemoveOrphanedIndexAsync(string directoryName)
    {
        var registry = await LoadRegistryAsync();
        if (registry.OrphanedIndexes.Remove(directoryName))
        {
            await SaveRegistryAsync(registry);
            _logger.LogInformation("Removed orphaned index from registry: {Directory}", directoryName);
            return true;
        }
        return false;
    }
    
    public async Task<IReadOnlyList<OrphanedIndex>> GetOrphansReadyForCleanupAsync()
    {
        var registry = await LoadRegistryAsync();
        var now = DateTime.UtcNow;
        return registry.OrphanedIndexes.Values
            .Where(o => o.ScheduledForDeletion <= now)
            .ToList().AsReadOnly();
    }
    
    #endregion
    
    #region Migration
    
    public async Task<MigrationResult> MigrateFromIndividualMetadataAsync()
    {
        var result = new MigrationResult { Success = true };
        
        try
        {
            var indexRoot = _pathResolution.GetIndexRootPath();
            if (!Directory.Exists(indexRoot))
            {
                result.Log.Add("Index root directory does not exist - no migration needed");
                return result;
            }
            
            var registry = new WorkspaceRegistry();
            var directories = Directory.GetDirectories(indexRoot);
            
            result.Log.Add($"Found {directories.Length} index directories to examine");
            
            foreach (var indexDir in directories)
            {
                var directoryName = Path.GetFileName(indexDir);
                var metadataFile = Path.Combine(indexDir, "workspace_metadata.json");
                
                try
                {
                    if (File.Exists(metadataFile))
                    {
                        // Try to read existing metadata
                        var json = await File.ReadAllTextAsync(metadataFile);
                        var metadata = JsonSerializer.Deserialize<WorkspaceIndexInfo>(json, _jsonOptions);
                        
                        if (metadata?.OriginalPath != null)
                        {
                            var workspace = new WorkspaceEntry
                            {
                                Hash = _pathResolution.ComputeWorkspaceHash(metadata.OriginalPath),
                                OriginalPath = metadata.OriginalPath,
                                DirectoryName = directoryName,
                                DisplayName = Path.GetFileName(metadata.OriginalPath.TrimEnd('\\', '/')),
                                Status = Directory.Exists(metadata.OriginalPath) ? WorkspaceStatus.Active : WorkspaceStatus.Missing,
                                CreatedAt = metadata.CreatedAt,
                                LastAccessed = metadata.LastAccessed,
                                DocumentCount = metadata.DocumentCount > int.MaxValue 
                                    ? int.MaxValue 
                                    : (int)metadata.DocumentCount,
                                IndexSizeBytes = metadata.IndexSizeBytes
                            };
                            
                            registry.Workspaces[workspace.Hash] = workspace;
                            result.WorkspacesMigrated++;
                            result.Log.Add($"Migrated workspace: {metadata.OriginalPath}");
                            
                            if (!Directory.Exists(metadata.OriginalPath))
                            {
                                result.Log.Add($"  WARNING: Original path no longer exists");
                            }
                        }
                        else
                        {
                            await MarkAsOrphanedInRegistry(registry, directoryName, OrphanReason.CorruptedMetadata);
                            result.OrphansDiscovered++;
                            result.Log.Add($"Marked as orphaned (corrupted metadata): {directoryName}");
                        }
                    }
                    else
                    {
                        // No metadata file - try to resolve from directory name
                        var resolvedPath = await TryResolveFromDirectoryName(directoryName);
                        if (resolvedPath != null && Directory.Exists(resolvedPath))
                        {
                            var workspace = new WorkspaceEntry
                            {
                                Hash = _pathResolution.ComputeWorkspaceHash(resolvedPath),
                                OriginalPath = resolvedPath,
                                DirectoryName = directoryName,
                                DisplayName = Path.GetFileName(resolvedPath.TrimEnd('\\', '/')),
                                Status = WorkspaceStatus.Active,
                                CreatedAt = Directory.GetCreationTimeUtc(indexDir),
                                LastAccessed = DateTime.UtcNow
                            };
                            
                            registry.Workspaces[workspace.Hash] = workspace;
                            result.WorkspacesMigrated++;
                            result.Log.Add($"Resolved and migrated: {resolvedPath}");
                        }
                        else
                        {
                            await MarkAsOrphanedInRegistry(registry, directoryName, OrphanReason.NoMetadataFile, resolvedPath);
                            result.OrphansDiscovered++;
                            result.Log.Add($"Marked as orphaned (no metadata): {directoryName}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Error processing {directoryName}: {ex.Message}");
                    await MarkAsOrphanedInRegistry(registry, directoryName, OrphanReason.CorruptedMetadata);
                    result.OrphansDiscovered++;
                }
            }
            
            await SaveRegistryAsync(registry);
            result.Log.Add($"Migration completed. Workspaces: {result.WorkspacesMigrated}, Orphans: {result.OrphansDiscovered}");
            
            _logger.LogInformation("Workspace registry migration completed. Migrated: {Migrated}, Orphans: {Orphans}", 
                result.WorkspacesMigrated, result.OrphansDiscovered);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add($"Migration failed: {ex.Message}");
            _logger.LogError(ex, "Workspace registry migration failed");
        }
        
        return result;
    }
    
    public Task<bool> IsMigrationNeededAsync()
    {
        return Task.FromResult(!File.Exists(RegistryPath) && Directory.Exists(_pathResolution.GetIndexRootPath()));
    }
    
    #endregion
    
    #region Private Helper Methods
    
    private async Task<WorkspaceRegistry> LoadRegistryFromDiskAsync()
    {
        if (!File.Exists(RegistryPath))
        {
            _logger.LogDebug("Registry file does not exist, creating new registry");
            return new WorkspaceRegistry();
        }
        
        try
        {
            var json = await File.ReadAllTextAsync(RegistryPath);
            var registry = JsonSerializer.Deserialize<WorkspaceRegistry>(json, _jsonOptions);
            return registry ?? new WorkspaceRegistry();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load registry from {Path}, trying backup", RegistryPath);
            
            // Try backup
            if (File.Exists(BackupRegistryPath))
            {
                try
                {
                    var backupJson = await File.ReadAllTextAsync(BackupRegistryPath);
                    var backupRegistry = JsonSerializer.Deserialize<WorkspaceRegistry>(backupJson, _jsonOptions);
                    _logger.LogWarning("Loaded registry from backup file");
                    return backupRegistry ?? new WorkspaceRegistry();
                }
                catch (Exception backupEx)
                {
                    _logger.LogError(backupEx, "Backup registry also corrupted");
                }
            }
            
            _logger.LogWarning("Creating new registry due to corruption");
            return new WorkspaceRegistry();
        }
    }
    
    private void UpdateRegistryStatistics(WorkspaceRegistry registry)
    {
        registry.Statistics = new RegistryStatistics
        {
            TotalWorkspaces = registry.Workspaces.Count,
            TotalOrphans = registry.OrphanedIndexes.Count,
            TotalIndexSizeBytes = registry.Workspaces.Values.Sum(w => w.IndexSizeBytes),
            TotalDocuments = registry.Workspaces.Values.Sum(w => w.DocumentCount)
        };
    }
    
    private Task<string?> TryResolveFromDirectoryName(string directoryName)
    {
        // This is a simplified version of the logic in PathResolutionService.TryResolveWorkspacePath
        var parts = directoryName.Split('_');
        if (parts.Length >= 2)
        {
            var workspaceName = string.Join("_", parts.Take(parts.Length - 1));
            
            var possibleLocations = new[]
            {
                Path.Combine("C:\\source", workspaceName.Replace('_', ' ')),
                Path.Combine("C:\\source", workspaceName),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "source", workspaceName),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), workspaceName),
                Path.Combine(Directory.GetCurrentDirectory(), workspaceName)
            };
            
            foreach (var location in possibleLocations)
            {
                if (Directory.Exists(location))
                {
                    var expectedHash = _pathResolution.ComputeWorkspaceHash(location);
                    if (directoryName.EndsWith(expectedHash, StringComparison.OrdinalIgnoreCase))
                    {
                        return Task.FromResult<string?>(location);
                    }
                }
            }
        }
        
        return Task.FromResult<string?>(null);
    }
    
    private Task MarkAsOrphanedInRegistry(WorkspaceRegistry registry, string directoryName, OrphanReason reason, string? attemptedPath = null)
    {
        var indexPath = Path.Combine(_pathResolution.GetIndexRootPath(), directoryName);
        var lastModified = Directory.Exists(indexPath) ? Directory.GetLastWriteTimeUtc(indexPath) : DateTime.UtcNow;
        var sizeBytes = 0L;
        
        if (Directory.Exists(indexPath))
        {
            try
            {
                sizeBytes = Directory.GetFiles(indexPath, "*", SearchOption.AllDirectories)
                    .Sum(file => new FileInfo(file).Length);
            }
            catch
            {
                // Ignore errors calculating size
            }
        }
        
        registry.OrphanedIndexes[directoryName] = new OrphanedIndex
        {
            DirectoryName = directoryName,
            DiscoveredAt = DateTime.UtcNow,
            LastModified = lastModified,
            Reason = reason,
            ScheduledForDeletion = DateTime.UtcNow.AddDays(7),
            SizeBytes = sizeBytes,
            AttemptedPath = attemptedPath
        };
        
        return Task.CompletedTask;
    }
    
    #endregion
}