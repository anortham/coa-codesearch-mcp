using COA.CodeSearch.McpServer.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Text.Json;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Service that manages the lifecycle of memories, automatically resolving or updating them
/// based on file changes and other events
/// </summary>
public class MemoryLifecycleService : BackgroundService, IFileChangeSubscriber
{
    private readonly ILogger<MemoryLifecycleService> _logger;
    private readonly IMemoryService _memoryService;
    private readonly IOptions<MemoryLifecycleOptions> _options;
    private readonly ConcurrentDictionary<string, MemoryConfidenceData> _confidenceCache;
    private readonly ConcurrentDictionary<string, DateTime> _recentPendingResolutions;
    
    public MemoryLifecycleService(
        ILogger<MemoryLifecycleService> logger,
        IMemoryService memoryService,
        IOptions<MemoryLifecycleOptions> options)
    {
        _logger = logger;
        _memoryService = memoryService;
        _options = options;
        _confidenceCache = new ConcurrentDictionary<string, MemoryConfidenceData>();
        _recentPendingResolutions = new ConcurrentDictionary<string, DateTime>();
    }
    
    /// <summary>
    /// Background task that periodically checks for stale memories
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Delay startup to avoid interfering with MCP server initialization
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            
            _logger.LogInformation("MemoryLifecycleService started - monitoring file changes for automatic memory resolution");
            
            // Only run if explicitly enabled
            var enabled = _options.Value.Enabled ?? true;
            if (!enabled)
            {
                _logger.LogInformation("MemoryLifecycleService is disabled in configuration - no automatic memory resolution will occur");
                return;
            }
            
            _logger.LogInformation("MemoryLifecycleService: Starting periodic stale memory checks every {Hours} hours", _options.Value.CheckIntervalHours);
            
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckStaleMemoriesAsync(stoppingToken);
                    await Task.Delay(TimeSpan.FromHours(_options.Value.CheckIntervalHours), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancellation is requested
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in memory lifecycle background task");
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); // Wait before retry
                }
            }
        }
        catch (Exception ex)
        {
            // Log but don't throw - we don't want to crash the MCP server
            _logger.LogError(ex, "Fatal error in Memory Lifecycle Service - service will be disabled");
        }
    }
    
    /// <summary>
    /// Handle file change events from FileWatcherService
    /// </summary>
    public async Task OnFileChangedAsync(MemoryLifecycleFileChangeEvent changeEvent)
    {
        // CRITICAL: Skip processing if the changed file is within .codesearch directory
        // This prevents infinite loops when storing memories triggers file changes
        if (IsPathInCodeSearchDirectory(changeEvent.FilePath))
        {
            _logger.LogDebug("Skipping file change event for .codesearch directory file: {FilePath}", 
                changeEvent.FilePath);
            return;
        }
        
        _logger.LogInformation("MemoryLifecycleService: Processing file change: {FilePath} ({ChangeType})", 
            changeEvent.FilePath, changeEvent.ChangeType);
        
        try
        {
            // Find memories related to this file
            // Use a simple search that won't go through query expansion
            var relatedMemories = await _memoryService.SearchMemoriesAsync(new FlexibleMemorySearchRequest
            {
                Query = "*", // Search all memories
                MaxResults = 1000, // Get more since we'll filter
                IncludeArchived = false
            });
            
            // Filter memories that reference this file
            var memoriesReferencingFile = relatedMemories.Memories
                .Where(m => m.FilesInvolved.Any(f => 
                    f.Equals(changeEvent.FilePath, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            
            if (memoriesReferencingFile.Count == 0)
            {
                return;
            }
            
            _logger.LogInformation("MemoryLifecycleService: Found {Count} memories related to changed file: {FilePath}", 
                memoriesReferencingFile.Count, changeEvent.FilePath);
            
            // Process each memory
            foreach (var memory in memoriesReferencingFile)
            {
                await ProcessMemoryForFileChangeAsync(memory, changeEvent);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing file change for memory lifecycle");
        }
    }
    
    /// <summary>
    /// Process a single memory based on a file change
    /// </summary>
    private async Task ProcessMemoryForFileChangeAsync(FlexibleMemoryEntry memory, MemoryLifecycleFileChangeEvent changeEvent)
    {
        // Calculate confidence for auto-resolution
        var confidence = CalculateResolutionConfidence(memory, changeEvent);
        
        _logger.LogInformation("MemoryLifecycleService: Memory {Id} ({Type}) confidence for auto-resolution: {Confidence:F2}", 
            memory.Id, memory.Type, confidence);
        
        // Store confidence data for learning
        _confidenceCache.TryAdd(memory.Id, new MemoryConfidenceData
        {
            MemoryId = memory.Id,
            Confidence = confidence,
            ChangeEvent = changeEvent,
            CalculatedAt = DateTime.UtcNow
        });
        
        if (confidence >= _options.Value.AutoResolveThreshold)
        {
            // High confidence - auto-resolve
            await AutoResolveMemoryAsync(memory, changeEvent, confidence);
        }
        else if (confidence >= _options.Value.PendingResolutionThreshold)
        {
            // Medium confidence - create pending resolution
            await CreatePendingResolutionAsync(memory, changeEvent, confidence);
        }
        else
        {
            // Low confidence - just update last accessed
            _logger.LogDebug("Memory {Id} confidence too low for action ({Confidence:F2})", 
                memory.Id, confidence);
        }
    }
    
    /// <summary>
    /// Calculate confidence score for auto-resolving a memory based on file changes
    /// </summary>
    private float CalculateResolutionConfidence(FlexibleMemoryEntry memory, MemoryLifecycleFileChangeEvent changeEvent)
    {
        var weights = _options.Value.ConfidenceWeights;
        float totalScore = 0;
        float totalWeight = 0;
        
        // 1. Memory Type Factor
        var typeScore = GetMemoryTypeScore(memory.Type);
        totalScore += typeScore * weights.MemoryTypeWeight;
        totalWeight += weights.MemoryTypeWeight;
        
        // 2. File Relevance Factor
        var fileRelevance = CalculateFileRelevance(memory, changeEvent.FilePath);
        totalScore += fileRelevance * weights.FileRelevanceWeight;
        totalWeight += weights.FileRelevanceWeight;
        
        // 3. Change Type Factor
        var changeTypeScore = GetChangeTypeScore(changeEvent.ChangeType);
        totalScore += changeTypeScore * weights.ChangeTypeWeight;
        totalWeight += weights.ChangeTypeWeight;
        
        // 4. Memory Age Factor
        var ageScore = CalculateAgeScore(memory.Created);
        totalScore += ageScore * weights.AgeWeight;
        totalWeight += weights.AgeWeight;
        
        // 5. Memory Status Factor
        var statusScore = GetStatusScore(memory);
        totalScore += statusScore * weights.StatusWeight;
        totalWeight += weights.StatusWeight;
        
        // 6. Content Analysis (keywords)
        var contentScore = AnalyzeMemoryContent(memory, changeEvent);
        totalScore += contentScore * weights.ContentAnalysisWeight;
        totalWeight += weights.ContentAnalysisWeight;
        
        return totalWeight > 0 ? totalScore / totalWeight : 0;
    }
    
    private float GetMemoryTypeScore(string type)
    {
        return type switch
        {
            "TechnicalDebt" => 0.9f,      // High confidence for technical debt
            "BugReport" => 0.85f,         // High confidence for bugs
            "Question" => 0.7f,           // Medium-high for questions
            "CodePattern" => 0.5f,        // Medium - patterns may still be valid
            "ArchitecturalDecision" => 0.3f, // Low - architectural decisions rarely auto-resolve
            "SecurityRule" => 0.2f,       // Very low - security rules need careful review
            _ => 0.5f                     // Default medium confidence
        };
    }
    
    private float CalculateFileRelevance(FlexibleMemoryEntry memory, string changedFile)
    {
        if (memory.FilesInvolved.Length == 0)
            return 0.1f; // Low relevance if no files specified
        
        // Direct match
        if (memory.FilesInvolved.Any(f => f.Equals(changedFile, StringComparison.OrdinalIgnoreCase)))
            return 1.0f;
        
        // Same directory
        var changedDir = Path.GetDirectoryName(changedFile);
        if (memory.FilesInvolved.Any(f => 
            Path.GetDirectoryName(f)?.Equals(changedDir, StringComparison.OrdinalIgnoreCase) == true))
            return 0.7f;
        
        // Same project/namespace (heuristic based on path similarity)
        var commonPathLength = memory.FilesInvolved
            .Select(f => GetCommonPathLength(f, changedFile))
            .Max();
        
        return Math.Min(commonPathLength / (float)changedFile.Length, 0.6f);
    }
    
    private int GetCommonPathLength(string path1, string path2)
    {
        var parts1 = path1.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parts2 = path2.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        
        int commonParts = 0;
        for (int i = 0; i < Math.Min(parts1.Length, parts2.Length); i++)
        {
            if (parts1[i].Equals(parts2[i], StringComparison.OrdinalIgnoreCase))
                commonParts++;
            else
                break;
        }
        
        return string.Join(Path.DirectorySeparatorChar, parts1.Take(commonParts)).Length;
    }
    
    private float GetChangeTypeScore(MemoryLifecycleFileChangeType changeType)
    {
        return changeType switch
        {
            MemoryLifecycleFileChangeType.Deleted => 0.9f,    // Deleted files strongly suggest resolution
            MemoryLifecycleFileChangeType.Modified => 0.7f,   // Modified files likely address issues
            MemoryLifecycleFileChangeType.Created => 0.5f,    // New files might be solutions
            MemoryLifecycleFileChangeType.Renamed => 0.4f,    // Renames are less conclusive
            _ => 0.3f
        };
    }
    
    private float CalculateAgeScore(DateTime created)
    {
        var age = DateTime.UtcNow - created;
        
        if (age.TotalDays < 7)
            return 0.3f;  // Recent memories less likely to auto-resolve
        else if (age.TotalDays < 30)
            return 0.5f;  // Medium age
        else if (age.TotalDays < 90)
            return 0.7f;  // Older memories more likely outdated
        else
            return 0.9f;  // Very old memories very likely outdated
    }
    
    private float GetStatusScore(FlexibleMemoryEntry memory)
    {
        var status = memory.GetField<string>("status")?.ToLowerInvariant();
        
        return status switch
        {
            "pending" => 0.8f,      // Pending items likely need resolution
            "in_progress" => 0.6f,  // In progress might be completed
            "blocked" => 0.4f,      // Blocked items need investigation
            "resolved" => 0.1f,     // Already resolved (shouldn't happen)
            _ => 0.5f               // Unknown status
        };
    }
    
    private float AnalyzeMemoryContent(FlexibleMemoryEntry memory, MemoryLifecycleFileChangeEvent changeEvent)
    {
        var content = memory.Content.ToLowerInvariant();
        var fileName = Path.GetFileNameWithoutExtension(changeEvent.FilePath).ToLowerInvariant();
        
        float score = 0;
        
        // Check for resolution keywords
        var resolutionKeywords = new[] { "todo", "fixme", "bug", "issue", "problem", "error", "broken" };
        var matchedKeywords = resolutionKeywords.Count(k => content.Contains(k));
        score += matchedKeywords * 0.15f;
        
        // Check if the file name is mentioned in the content
        if (content.Contains(fileName))
            score += 0.3f;
        
        // Check for specific code elements mentioned
        if (changeEvent.ChangeType == MemoryLifecycleFileChangeType.Modified && content.Contains("method") || content.Contains("class"))
            score += 0.2f;
        
        return Math.Min(score, 1.0f);
    }
    
    /// <summary>
    /// Automatically resolve a memory with high confidence
    /// </summary>
    private async Task AutoResolveMemoryAsync(FlexibleMemoryEntry memory, MemoryLifecycleFileChangeEvent changeEvent, float confidence)
    {
        _logger.LogInformation("Auto-resolving memory {Id} (type: {Type}) with confidence {Confidence:F2}", 
            memory.Id, memory.Type, confidence);
        
        var updateRequest = new MemoryUpdateRequest
        {
            Id = memory.Id,
            FieldUpdates = MemoryLifecycleExtensions.CreateFieldUpdates(new Dictionary<string, object?>
            {
                ["status"] = "resolved",
                ["resolvedAt"] = DateTime.UtcNow,
                ["resolvedBy"] = "MemoryLifecycleService",
                ["resolutionConfidence"] = confidence,
                ["resolutionReason"] = $"Auto-resolved due to changes in {Path.GetFileName(changeEvent.FilePath)}"
            })
        };
        
        await _memoryService.UpdateMemoryAsync(updateRequest);
        
        // Archive functionality would be called here if needed
        // Note: ArchiveMemoriesAsync works on type and age, not individual memories
    }
    
    /// <summary>
    /// Create a pending resolution memory for medium confidence cases
    /// </summary>
    private async Task CreatePendingResolutionAsync(FlexibleMemoryEntry memory, MemoryLifecycleFileChangeEvent changeEvent, float confidence)
    {
        // Prevent creating PendingResolution for another PendingResolution
        if (memory.Type == "PendingResolution")
        {
            _logger.LogDebug("Skipping PendingResolution creation for memory {Id} which is already a PendingResolution", 
                memory.Id);
            return;
        }
        
        // Additional safety: Don't create PendingResolution for ResolutionFeedback memories
        if (memory.Type == "ResolutionFeedback")
        {
            _logger.LogDebug("Skipping PendingResolution creation for ResolutionFeedback memory {Id}", 
                memory.Id);
            return;
        }
        
        // Circuit breaker: Check if we recently created a PendingResolution for this memory
        if (_recentPendingResolutions.TryGetValue(memory.Id, out var lastCreated))
        {
            var timeSinceLastCreation = DateTime.UtcNow - lastCreated;
            if (timeSinceLastCreation < TimeSpan.FromMinutes(1))
            {
                _logger.LogWarning("Circuit breaker activated: Skipping PendingResolution creation for memory {Id} - " +
                    "last created {Seconds} seconds ago", memory.Id, timeSinceLastCreation.TotalSeconds);
                return;
            }
        }
        
        _logger.LogInformation("Creating pending resolution for memory {Id} with confidence {Confidence:F2}", 
            memory.Id, confidence);
        
        var pendingResolution = new FlexibleMemoryEntry
        {
            Id = Guid.NewGuid().ToString(),
            Type = "PendingResolution",
            Content = $"File change detected that may resolve memory '{memory.Id}' ({memory.Type}). " +
                     $"File '{Path.GetFileName(changeEvent.FilePath)}' was {changeEvent.ChangeType}. " +
                     $"Confidence: {confidence:F2}. Original memory: {memory.Content}",
            FilesInvolved = new[] { changeEvent.FilePath },
            Created = DateTime.UtcNow,
            Modified = DateTime.UtcNow,
            IsShared = memory.IsShared,
            AccessCount = 0
        };
        
        // Set custom fields
        pendingResolution.SetField("originalMemoryId", memory.Id);
        pendingResolution.SetField("confidence", confidence);
        pendingResolution.SetField("changeType", changeEvent.ChangeType.ToString());
        pendingResolution.SetField("status", "pending_review");
        
        await _memoryService.StoreMemoryAsync(pendingResolution);
        
        // Track this creation in the circuit breaker
        _recentPendingResolutions[memory.Id] = DateTime.UtcNow;
        
        // Clean up old entries (older than 5 minutes) to prevent memory leak
        var cutoffTime = DateTime.UtcNow.AddMinutes(-5);
        var oldEntries = _recentPendingResolutions
            .Where(kvp => kvp.Value < cutoffTime)
            .Select(kvp => kvp.Key)
            .ToList();
        foreach (var key in oldEntries)
        {
            _recentPendingResolutions.TryRemove(key, out _);
        }
    }
    
    /// <summary>
    /// Periodically check for stale memories
    /// </summary>
    private async Task CheckStaleMemoriesAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Checking for stale memories");
        
        var staleThreshold = DateTime.UtcNow.AddDays(-_options.Value.StaleAfterDays);
        
        var searchRequest = new FlexibleMemorySearchRequest
        {
            Query = "*",
            Facets = new Dictionary<string, string>
            {
                ["status"] = "pending"
            },
            MaxResults = 100,
            IncludeArchived = false
        };
        
        var results = await _memoryService.SearchMemoriesAsync(searchRequest);
        
        var staleMemories = results.Memories
            .Where(m => m.Created < staleThreshold)
            .ToList();
        
        if (staleMemories.Count > 0)
        {
            _logger.LogInformation("Found {Count} stale memories", staleMemories.Count);
            
            foreach (var memory in staleMemories)
            {
                await MarkMemoryAsStaleAsync(memory);
            }
        }
    }
    
    private async Task MarkMemoryAsStaleAsync(FlexibleMemoryEntry memory)
    {
        var updateRequest = new MemoryUpdateRequest
        {
            Id = memory.Id,
            FieldUpdates = MemoryLifecycleExtensions.CreateFieldUpdates(new Dictionary<string, object?>
            {
                ["isStale"] = true,
                ["markedStaleAt"] = DateTime.UtcNow
            })
        };
        
        await _memoryService.UpdateMemoryAsync(updateRequest);
    }
    
    /// <summary>
    /// Learn from user feedback on resolution decisions
    /// </summary>
    public async Task RecordResolutionFeedbackAsync(string memoryId, bool wasCorrect, string? feedback = null)
    {
        if (_confidenceCache.TryGetValue(memoryId, out var confidenceData))
        {
            var feedbackEntry = new FlexibleMemoryEntry
            {
                Id = Guid.NewGuid().ToString(),
                Type = "ResolutionFeedback",
                Content = $"Feedback for memory {memoryId}: {(wasCorrect ? "Correct" : "Incorrect")} resolution. " +
                         $"Original confidence: {confidenceData.Confidence:F2}. {feedback ?? ""}",
                Created = DateTime.UtcNow,
                Modified = DateTime.UtcNow,
                IsShared = true,
                AccessCount = 0
            };
            
            feedbackEntry.SetField("memoryId", memoryId);
            feedbackEntry.SetField("wasCorrect", wasCorrect);
            feedbackEntry.SetField("originalConfidence", confidenceData.Confidence);
            feedbackEntry.SetField("feedback", feedback);
            
            await _memoryService.StoreMemoryAsync(feedbackEntry);
            
            // TODO: Use this feedback to adjust confidence weights
            _logger.LogInformation("Recorded resolution feedback for memory {MemoryId}: {Correct}", 
                memoryId, wasCorrect);
        }
    }
    
    /// <summary>
    /// Check if a path is within the .codesearch directory
    /// </summary>
    private bool IsPathInCodeSearchDirectory(string path)
    {
        try
        {
            var normalizedPath = Path.GetFullPath(path);
            return normalizedPath.Contains(PathConstants.BaseDirectoryName, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking if path is in .codesearch directory: {Path}", path);
            // If we can't determine, err on the side of caution and skip processing
            return true;
        }
    }
}

/// <summary>
/// Configuration for memory lifecycle management
/// </summary>
public class MemoryLifecycleOptions
{
    public bool? Enabled { get; set; } = true;
    public float AutoResolveThreshold { get; set; } = 0.8f;
    public float PendingResolutionThreshold { get; set; } = 0.5f;
    public int CheckIntervalHours { get; set; } = 24;
    public int StaleAfterDays { get; set; } = 30;
    public int ArchiveAfterDays { get; set; } = 90;
    
    public ConfidenceWeights ConfidenceWeights { get; set; } = new();
}

/// <summary>
/// Weights for confidence calculation factors
/// </summary>
public class ConfidenceWeights
{
    public float MemoryTypeWeight { get; set; } = 0.25f;
    public float FileRelevanceWeight { get; set; } = 0.20f;
    public float ChangeTypeWeight { get; set; } = 0.15f;
    public float AgeWeight { get; set; } = 0.15f;
    public float StatusWeight { get; set; } = 0.15f;
    public float ContentAnalysisWeight { get; set; } = 0.10f;
}

/// <summary>
/// Interface for services that want to subscribe to file changes
/// </summary>
public interface IFileChangeSubscriber
{
    Task OnFileChangedAsync(MemoryLifecycleFileChangeEvent changeEvent);
}

/// <summary>
/// Cached confidence calculation data
/// </summary>
internal class MemoryConfidenceData
{
    public string MemoryId { get; set; } = string.Empty;
    public float Confidence { get; set; }
    public MemoryLifecycleFileChangeEvent ChangeEvent { get; set; } = null!;
    public DateTime CalculatedAt { get; set; }
}

/// <summary>
/// Extension methods for MemoryLifecycleService
/// </summary>
internal static class MemoryLifecycleExtensions
{
    /// <summary>
    /// Helper method to convert object dictionary to JsonElement dictionary
    /// </summary>
    public static Dictionary<string, JsonElement?> CreateFieldUpdates(Dictionary<string, object?> fields)
    {
        var result = new Dictionary<string, JsonElement?>();
        
        foreach (var kvp in fields)
        {
            if (kvp.Value == null)
            {
                result[kvp.Key] = null;
            }
            else
            {
                var json = JsonSerializer.Serialize(kvp.Value);
                result[kvp.Key] = JsonSerializer.Deserialize<JsonElement>(json);
            }
        }
        
        return result;
    }
}