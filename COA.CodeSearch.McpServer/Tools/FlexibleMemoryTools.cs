using System.Text.Json;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// MCP tools for the flexible memory system
/// </summary>
public class FlexibleMemoryTools
{
    private readonly ILogger<FlexibleMemoryTools> _logger;
    private readonly FlexibleMemoryService _memoryService;
    
    public FlexibleMemoryTools(ILogger<FlexibleMemoryTools> logger, FlexibleMemoryService memoryService)
    {
        _logger = logger;
        _memoryService = memoryService;
    }
    
    /// <summary>
    /// Store a new memory with flexible schema
    /// </summary>
    public async Task<StoreMemoryResult> StoreMemoryAsync(
        string type,
        string content,
        bool isShared = true,
        string? sessionId = null,
        string[]? files = null,
        Dictionary<string, JsonElement>? fields = null)
    {
        try
        {
            var memory = new FlexibleMemoryEntry
            {
                Type = type,
                Content = content,
                IsShared = isShared,
                SessionId = sessionId ?? "",
                FilesInvolved = files ?? Array.Empty<string>(),
                Fields = fields ?? new Dictionary<string, JsonElement>()
            };
            
            var success = await _memoryService.StoreMemoryAsync(memory);
            
            return new StoreMemoryResult
            {
                Success = success,
                MemoryId = success ? memory.Id : null,
                Message = success ? 
                    $"Successfully stored {type} memory" : 
                    "Failed to store memory"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error storing memory");
            return new StoreMemoryResult
            {
                Success = false,
                Message = $"Error: {ex.Message}"
            };
        }
    }
    
    /// <summary>
    /// Store a working memory (temporary, session-scoped)
    /// </summary>
    public async Task<StoreMemoryResult> StoreWorkingMemoryAsync(
        string content,
        string? expiresIn = "end-of-session",
        string? sessionId = null,
        string[]? files = null,
        Dictionary<string, JsonElement>? fields = null)
    {
        try
        {
            // Calculate expiration time
            DateTime? expiresAt = null;
            if (!string.IsNullOrEmpty(expiresIn) && expiresIn != "end-of-session")
            {
                if (expiresIn.EndsWith("h"))
                {
                    var hours = int.Parse(expiresIn.TrimEnd('h'));
                    expiresAt = DateTime.UtcNow.AddHours(hours);
                }
                else if (expiresIn.EndsWith("m"))
                {
                    var minutes = int.Parse(expiresIn.TrimEnd('m'));
                    expiresAt = DateTime.UtcNow.AddMinutes(minutes);
                }
                else if (expiresIn.EndsWith("d"))
                {
                    var days = int.Parse(expiresIn.TrimEnd('d'));
                    expiresAt = DateTime.UtcNow.AddDays(days);
                }
            }
            
            var workingFields = fields ?? new Dictionary<string, JsonElement>();
            
            // Add expiration field if specified
            if (expiresAt.HasValue)
            {
                workingFields[MemoryFields.ExpiresAt] = JsonSerializer.SerializeToElement(expiresAt.Value);
            }
            
            // Add working memory marker
            workingFields["isWorkingMemory"] = JsonSerializer.SerializeToElement(true);
            workingFields["sessionExpiry"] = JsonSerializer.SerializeToElement(expiresIn ?? "end-of-session");
            
            var memory = new FlexibleMemoryEntry
            {
                Type = MemoryTypes.WorkingMemory,
                Content = content,
                IsShared = false, // Working memories are always local
                SessionId = sessionId ?? Guid.NewGuid().ToString(),
                FilesInvolved = files ?? Array.Empty<string>(),
                Fields = workingFields
            };
            
            var success = await _memoryService.StoreMemoryAsync(memory);
            
            return new StoreMemoryResult
            {
                Success = success,
                MemoryId = success ? memory.Id : null,
                Message = success ? 
                    $"Working memory stored (expires: {expiresIn})" : 
                    "Failed to store working memory"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error storing working memory");
            return new StoreMemoryResult
            {
                Success = false,
                Message = $"Error: {ex.Message}"
            };
        }
    }
    
    /// <summary>
    /// Search memories with advanced filtering
    /// </summary>
    public async Task<FlexibleMemorySearchResult> SearchMemoriesAsync(
        string? query = null,
        string[]? types = null,
        string? dateRange = null,
        Dictionary<string, string>? facets = null,
        string? orderBy = null,
        bool orderDescending = true,
        int maxResults = 50,
        bool includeArchived = false,
        bool boostRecent = false,
        bool boostFrequent = false)
    {
        try
        {
            var request = new FlexibleMemorySearchRequest
            {
                Query = query ?? "*",
                Types = types,
                Facets = facets,
                OrderBy = orderBy,
                OrderDescending = orderDescending,
                MaxResults = maxResults,
                IncludeArchived = includeArchived,
                BoostRecent = boostRecent,
                BoostFrequent = boostFrequent
            };
            
            if (!string.IsNullOrEmpty(dateRange))
            {
                request.DateRange = new DateRangeFilter { RelativeTime = dateRange };
            }
            
            return await _memoryService.SearchMemoriesAsync(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching memories");
            return new FlexibleMemorySearchResult
            {
                Memories = new List<FlexibleMemoryEntry>(),
                TotalFound = 0,
                FacetCounts = new Dictionary<string, Dictionary<string, int>>()
            };
        }
    }
    
    /// <summary>
    /// Update an existing memory
    /// </summary>
    public async Task<UpdateMemoryResult> UpdateMemoryAsync(
        string id,
        string? content = null,
        Dictionary<string, JsonElement?>? fieldUpdates = null,
        string[]? addFiles = null,
        string[]? removeFiles = null)
    {
        try
        {
            var request = new MemoryUpdateRequest
            {
                Id = id,
                Content = content,
                FieldUpdates = fieldUpdates ?? new Dictionary<string, JsonElement?>(),
                AddFiles = addFiles,
                RemoveFiles = removeFiles
            };
            
            var success = await _memoryService.UpdateMemoryAsync(request);
            
            return new UpdateMemoryResult
            {
                Success = success,
                Message = success ? 
                    "Memory updated successfully" : 
                    "Memory not found or update failed"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating memory");
            return new UpdateMemoryResult
            {
                Success = false,
                Message = $"Error: {ex.Message}"
            };
        }
    }
    
    /// <summary>
    /// Store a technical debt memory
    /// </summary>
    public async Task<StoreMemoryResult> StoreTechnicalDebtAsync(
        string description,
        string? status = null,
        string? priority = null,
        string? category = null,
        int? estimatedHours = null,
        string[]? files = null,
        string[]? tags = null)
    {
        var fields = new Dictionary<string, JsonElement>();
        
        if (!string.IsNullOrEmpty(status))
            fields[MemoryFields.Status] = JsonDocument.Parse($"\"{status}\"").RootElement;
        
        if (!string.IsNullOrEmpty(priority))
            fields[MemoryFields.Priority] = JsonDocument.Parse($"\"{priority}\"").RootElement;
        
        if (!string.IsNullOrEmpty(category))
            fields[MemoryFields.Category] = JsonDocument.Parse($"\"{category}\"").RootElement;
        
        if (estimatedHours.HasValue)
            fields["estimatedHours"] = JsonDocument.Parse(estimatedHours.Value.ToString()).RootElement;
        
        if (tags != null && tags.Length > 0)
            fields[MemoryFields.Tags] = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(tags));
        
        return await StoreMemoryAsync(
            MemoryTypes.TechnicalDebt,
            description,
            isShared: true,
            files: files,
            fields: fields
        );
    }
    
    /// <summary>
    /// Store a question memory
    /// </summary>
    public async Task<StoreMemoryResult> StoreQuestionAsync(
        string question,
        string? context = null,
        string? status = null,
        string[]? files = null,
        string[]? tags = null)
    {
        var fields = new Dictionary<string, JsonElement>();
        
        if (!string.IsNullOrEmpty(status))
            fields[MemoryFields.Status] = JsonDocument.Parse($"\"{status}\"").RootElement;
        
        if (!string.IsNullOrEmpty(context))
            fields["context"] = JsonDocument.Parse($"\"{context}\"").RootElement;
        
        if (tags != null && tags.Length > 0)
            fields[MemoryFields.Tags] = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(tags));
        
        return await StoreMemoryAsync(
            MemoryTypes.Question,
            question,
            isShared: true,
            files: files,
            fields: fields
        );
    }
    
    /// <summary>
    /// Store a deferred task memory
    /// </summary>
    public async Task<StoreMemoryResult> StoreDeferredTaskAsync(
        string task,
        string reason,
        DateTime? deferredUntil = null,
        string? priority = null,
        string[]? files = null,
        string[]? blockedBy = null)
    {
        var fields = new Dictionary<string, JsonElement>
        {
            ["reason"] = JsonDocument.Parse($"\"{reason}\"").RootElement,
            [MemoryFields.Status] = JsonDocument.Parse($"\"{MemoryStatus.Deferred}\"").RootElement
        };
        
        if (deferredUntil.HasValue)
            fields["deferredUntil"] = JsonDocument.Parse($"\"{deferredUntil.Value:O}\"").RootElement;
        
        if (!string.IsNullOrEmpty(priority))
            fields[MemoryFields.Priority] = JsonDocument.Parse($"\"{priority}\"").RootElement;
        
        if (blockedBy != null && blockedBy.Length > 0)
            fields["blockedBy"] = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(blockedBy));
        
        return await StoreMemoryAsync(
            MemoryTypes.DeferredTask,
            task,
            isShared: true,
            files: files,
            fields: fields
        );
    }
    
    /// <summary>
    /// Find memories similar to a given memory
    /// </summary>
    public async Task<SimilarMemoriesResult> FindSimilarMemoriesAsync(string memoryId, int maxResults = 10)
    {
        try
        {
            var similar = await _memoryService.FindSimilarMemoriesAsync(memoryId, maxResults);
            
            return new SimilarMemoriesResult
            {
                Success = true,
                SimilarMemories = similar,
                Message = $"Found {similar.Count} similar memories"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding similar memories");
            return new SimilarMemoriesResult
            {
                Success = false,
                SimilarMemories = new List<FlexibleMemoryEntry>(),
                Message = $"Error: {ex.Message}"
            };
        }
    }
    
    /// <summary>
    /// Mark a memory as resolved/completed
    /// </summary>
    public async Task<UpdateMemoryResult> MarkMemoryResolvedAsync(
        string id,
        string? resolutionNote = null)
    {
        var fieldUpdates = new Dictionary<string, JsonElement?>
        {
            [MemoryFields.Status] = JsonDocument.Parse($"\"{MemoryStatus.Resolved}\"").RootElement,
            ["resolvedAt"] = JsonDocument.Parse($"\"{DateTime.UtcNow:O}\"").RootElement
        };
        
        if (!string.IsNullOrEmpty(resolutionNote))
        {
            fieldUpdates["resolutionNote"] = JsonDocument.Parse($"\"{resolutionNote}\"").RootElement;
        }
        
        return await UpdateMemoryAsync(id, fieldUpdates: fieldUpdates);
    }
    
    /// <summary>
    /// Archive old memories of a specific type
    /// </summary>
    public async Task<ArchiveMemoriesResult> ArchiveMemoriesAsync(string type, int daysOld)
    {
        try
        {
            var archived = await _memoryService.ArchiveMemoriesAsync(type, TimeSpan.FromDays(daysOld));
            
            return new ArchiveMemoriesResult
            {
                Success = true,
                ArchivedCount = archived,
                Message = $"Archived {archived} {type} memories older than {daysOld} days"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error archiving memories");
            return new ArchiveMemoriesResult
            {
                Success = false,
                ArchivedCount = 0,
                Message = $"Error: {ex.Message}"
            };
        }
    }
    
    /// <summary>
    /// Get memory by ID
    /// </summary>
    public async Task<GetMemoryResult> GetMemoryByIdAsync(string id)
    {
        try
        {
            var memory = await _memoryService.GetMemoryByIdAsync(id);
            
            if (memory != null)
            {
                return new GetMemoryResult
                {
                    Success = true,
                    Memory = memory,
                    Message = "Memory found"
                };
            }
            else
            {
                return new GetMemoryResult
                {
                    Success = false,
                    Message = "Memory not found"
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting memory by ID");
            return new GetMemoryResult
            {
                Success = false,
                Message = $"Error: {ex.Message}"
            };
        }
    }
    
    /// <summary>
    /// Summarize old memories to save space and improve relevance
    /// </summary>
    public async Task<SummarizeMemoriesResult> SummarizeMemoriesAsync(
        string type,
        int daysOld,
        int batchSize = 10,
        bool preserveOriginals = false)
    {
        try
        {
            // Search for old memories of the specified type
            var searchRequest = new FlexibleMemorySearchRequest
            {
                Query = "*",
                Types = new[] { type },
                MaxResults = 1000,
                OrderBy = "created",
                OrderDescending = false
            };
            
            var searchResult = await _memoryService.SearchMemoriesAsync(searchRequest);
            var cutoffDate = DateTime.UtcNow.AddDays(-daysOld);
            var oldMemories = searchResult.Memories
                .Where(m => m.Created < cutoffDate)
                .ToList();
            
            if (oldMemories.Count == 0)
            {
                return new SummarizeMemoriesResult
                {
                    Success = true,
                    ProcessedCount = 0,
                    SummaryCount = 0,
                    Message = $"No {type} memories older than {daysOld} days found"
                };
            }
            
            var summaries = new List<FlexibleMemoryEntry>();
            var processedCount = 0;
            
            // Process memories in batches
            for (int i = 0; i < oldMemories.Count; i += batchSize)
            {
                var batch = oldMemories.Skip(i).Take(batchSize).ToList();
                var summary = CreateBatchSummary(batch, type);
                
                // Store the summary
                var summaryMemory = new FlexibleMemoryEntry
                {
                    Type = $"{type}Summary",
                    Content = summary.Content,
                    IsShared = batch.Any(m => m.IsShared),
                    Fields = new Dictionary<string, JsonElement>
                    {
                        ["originalType"] = JsonSerializer.SerializeToElement(type),
                        ["dateRange"] = JsonSerializer.SerializeToElement(new
                        {
                            from = batch.Min(m => m.Created),
                            to = batch.Max(m => m.Created)
                        }),
                        ["originalCount"] = JsonSerializer.SerializeToElement(batch.Count),
                        ["summarizedOn"] = JsonSerializer.SerializeToElement(DateTime.UtcNow)
                    }
                };
                
                // Add combined files from all memories
                var allFiles = batch.SelectMany(m => m.FilesInvolved).Distinct().ToArray();
                summaryMemory.FilesInvolved = allFiles;
                
                if (await _memoryService.StoreMemoryAsync(summaryMemory))
                {
                    summaries.Add(summaryMemory);
                    
                    // Archive or delete originals
                    if (!preserveOriginals)
                    {
                        foreach (var memory in batch)
                        {
                            memory.SetField("summarizedIn", summaryMemory.Id);
                            memory.SetField("archived", true);
                            memory.SetField("archivedDate", DateTime.UtcNow);
                            await _memoryService.StoreMemoryAsync(memory);
                        }
                    }
                }
                
                processedCount += batch.Count;
            }
            
            return new SummarizeMemoriesResult
            {
                Success = true,
                ProcessedCount = processedCount,
                SummaryCount = summaries.Count,
                Summaries = summaries,
                Message = $"Summarized {processedCount} memories into {summaries.Count} summaries"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error summarizing memories");
            return new SummarizeMemoriesResult
            {
                Success = false,
                Message = $"Error: {ex.Message}"
            };
        }
    }
    
    private BatchSummary CreateBatchSummary(List<FlexibleMemoryEntry> memories, string type)
    {
        var summary = new BatchSummary();
        
        // Group memories by common themes/patterns
        var keyThemes = ExtractKeyThemes(memories);
        var commonFiles = memories
            .SelectMany(m => m.FilesInvolved)
            .GroupBy(f => f)
            .Where(g => g.Count() > 1)
            .Select(g => new { File = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(5)
            .ToList();
        
        // Build summary content
        var contentParts = new List<string>
        {
            $"Summary of {memories.Count} {type} memories from {memories.Min(m => m.Created):yyyy-MM-dd} to {memories.Max(m => m.Created):yyyy-MM-dd}",
            "",
            "Key Themes:"
        };
        
        foreach (var theme in keyThemes.Take(5))
        {
            contentParts.Add($"- {theme.Theme} ({theme.Count} occurrences)");
        }
        
        if (commonFiles.Any())
        {
            contentParts.Add("");
            contentParts.Add("Most referenced files:");
            foreach (var file in commonFiles)
            {
                contentParts.Add($"- {Path.GetFileName(file.File)} ({file.Count} references)");
            }
        }
        
        // Add specific insights based on memory type
        var typeSpecificInsights = GetTypeSpecificInsights(memories, type);
        if (typeSpecificInsights.Any())
        {
            contentParts.Add("");
            contentParts.Add("Insights:");
            contentParts.AddRange(typeSpecificInsights);
        }
        
        summary.Content = string.Join("\n", contentParts);
        return summary;
    }
    
    private List<ThemeCount> ExtractKeyThemes(List<FlexibleMemoryEntry> memories)
    {
        var wordFrequency = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "and", "or", "but", "in", "on", "at", "to", "for",
            "of", "with", "by", "from", "as", "is", "was", "are", "were", "been"
        };
        
        foreach (var memory in memories)
        {
            var words = memory.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 3 && !stopWords.Contains(w))
                .Select(w => w.Trim('.', ',', '!', '?', ';', ':'));
            
            foreach (var word in words)
            {
                wordFrequency[word] = wordFrequency.GetValueOrDefault(word) + 1;
            }
        }
        
        return wordFrequency
            .Where(kv => kv.Value > 1)
            .OrderByDescending(kv => kv.Value)
            .Select(kv => new ThemeCount { Theme = kv.Key, Count = kv.Value })
            .ToList();
    }
    
    private List<string> GetTypeSpecificInsights(List<FlexibleMemoryEntry> memories, string type)
    {
        var insights = new List<string>();
        
        switch (type)
        {
            case "WorkSession":
                var totalSessions = memories.Count;
                var avgSessionsPerWeek = totalSessions / Math.Max(1, (memories.Max(m => m.Created) - memories.Min(m => m.Created)).TotalDays / 7);
                insights.Add($"Average {avgSessionsPerWeek:F1} sessions per week");
                break;
                
            case "TechnicalDebt":
                var resolvedCount = memories.Count(m => m.GetField<string>("status") == "resolved");
                var pendingCount = memories.Count(m => m.GetField<string>("status") == "pending");
                insights.Add($"{resolvedCount} resolved, {pendingCount} still pending");
                if (pendingCount > 0)
                {
                    insights.Add($"Resolution rate: {(resolvedCount * 100.0 / memories.Count):F1}%");
                }
                break;
                
            case "ArchitecturalDecision":
                var categories = memories
                    .Select(m => m.GetField<string>("category"))
                    .Where(c => !string.IsNullOrEmpty(c))
                    .GroupBy(c => c)
                    .OrderByDescending(g => g.Count())
                    .Take(3);
                foreach (var cat in categories)
                {
                    insights.Add($"{cat.Key}: {cat.Count()} decisions");
                }
                break;
        }
        
        return insights;
    }
    
    private class BatchSummary
    {
        public string Content { get; set; } = "";
    }
    
    private class ThemeCount
    {
        public string Theme { get; set; } = "";
        public int Count { get; set; }
    }
}

// Result classes

public class StoreMemoryResult
{
    public bool Success { get; set; }
    public string? MemoryId { get; set; }
    public string Message { get; set; } = "";
}

public class UpdateMemoryResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
}

public class SimilarMemoriesResult
{
    public bool Success { get; set; }
    public List<FlexibleMemoryEntry> SimilarMemories { get; set; } = new();
    public string Message { get; set; } = "";
}

public class ArchiveMemoriesResult
{
    public bool Success { get; set; }
    public int ArchivedCount { get; set; }
    public string Message { get; set; } = "";
}

public class GetMemoryResult
{
    public bool Success { get; set; }
    public FlexibleMemoryEntry? Memory { get; set; }
    public string Message { get; set; } = "";
}

public class SummarizeMemoriesResult
{
    public bool Success { get; set; }
    public int ProcessedCount { get; set; }
    public int SummaryCount { get; set; }
    public List<FlexibleMemoryEntry> Summaries { get; set; } = new();
    public string Message { get; set; } = "";
}