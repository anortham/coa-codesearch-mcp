using System.Text.Json;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using COA.Mcp.Protocol;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// MCP tools for the flexible memory system
/// </summary>
public class FlexibleMemoryTools : ITool
{
    public string ToolName => "flexible_memory";
    public string Description => "Flexible memory storage and retrieval";
    public ToolCategory Category => ToolCategory.Memory;
    private readonly ILogger<FlexibleMemoryTools> _logger;
    private readonly FlexibleMemoryService _memoryService;
    private readonly IPathResolutionService _pathResolution;
    
    public FlexibleMemoryTools(
        ILogger<FlexibleMemoryTools> logger, 
        FlexibleMemoryService memoryService,
        IPathResolutionService pathResolution)
    {
        _logger = logger;
        _memoryService = memoryService;
        _pathResolution = pathResolution;
    }
    
    /// <summary>
    /// Store a new memory with flexible schema
    /// </summary>
    public async Task<StoreMemoryResult> StoreMemoryAsync(
        string memoryType,
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
                Type = memoryType,
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
                    $"Successfully stored {memoryType} memory" : 
                    "Failed to store memory"
            };
        }
        catch (InvalidParametersException ipe)
        {
            // Extract validation error details for the user
            return new StoreMemoryResult
            {
                Success = false,
                Message = ipe.Message
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
        catch (InvalidParametersException ipe)
        {
            // Extract validation error details for the user
            return new StoreMemoryResult
            {
                Success = false,
                Message = ipe.Message
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
        bool boostFrequent = false,
        bool? enableQueryExpansion = null)
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
                BoostFrequent = boostFrequent,
                EnableQueryExpansion = enableQueryExpansion
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
    public async Task<ArchiveMemoriesResult> ArchiveMemoriesAsync(string memoryType, int daysOld)
    {
        try
        {
            var archived = await _memoryService.ArchiveMemoriesAsync(memoryType, TimeSpan.FromDays(daysOld));
            
            return new ArchiveMemoriesResult
            {
                Success = true,
                ArchivedCount = archived,
                Message = $"Archived {archived} {memoryType} memories older than {daysOld} days"
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
        string memoryType,
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
                Types = new[] { memoryType },
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
                    Message = $"No {memoryType} memories older than {daysOld} days found"
                };
            }
            
            var summaries = new List<FlexibleMemoryEntry>();
            var processedCount = 0;
            
            // Process memories in batches
            for (int i = 0; i < oldMemories.Count; i += batchSize)
            {
                var batch = oldMemories.Skip(i).Take(batchSize).ToList();
                var summary = CreateBatchSummary(batch, memoryType);
                
                // Store the summary
                var summaryMemory = new FlexibleMemoryEntry
                {
                    Type = $"{memoryType}Summary",
                    Content = summary.Content,
                    IsShared = batch.Any(m => m.IsShared),
                    Fields = new Dictionary<string, JsonElement>
                    {
                        ["originalType"] = JsonSerializer.SerializeToElement(memoryType),
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
    
    private BatchSummary CreateBatchSummary(List<FlexibleMemoryEntry> memories, string memoryType)
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
            $"Summary of {memories.Count} {memoryType} memories from {memories.Min(m => m.Created):yyyy-MM-dd} to {memories.Max(m => m.Created):yyyy-MM-dd}",
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
        var typeSpecificInsights = GetTypeSpecificInsights(memories, memoryType);
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
    
    private List<string> GetTypeSpecificInsights(List<FlexibleMemoryEntry> memories, string memoryType)
    {
        var insights = new List<string>();
        
        switch (memoryType)
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
    
    /// <summary>
    /// Create a memory from a template with placeholders
    /// </summary>
    public async Task<StoreMemoryResult> CreateFromTemplateAsync(
        string templateId,
        Dictionary<string, string> placeholders,
        string[]? files = null,
        Dictionary<string, JsonElement>? additionalFields = null)
    {
        try
        {
            var templates = MemoryTemplates.GetAll();
            if (!templates.TryGetValue(templateId, out var template))
            {
                return new StoreMemoryResult
                {
                    Success = false,
                    Message = $"Template '{templateId}' not found. Available templates: {string.Join(", ", templates.Keys)}"
                };
            }
            
            // Validate required placeholders
            var missingPlaceholders = template.RequiredPlaceholders
                .Where(p => !placeholders.ContainsKey(p) || string.IsNullOrWhiteSpace(placeholders[p]))
                .ToList();
                
            if (missingPlaceholders.Any())
            {
                return new StoreMemoryResult
                {
                    Success = false,
                    Message = $"Missing required placeholders: {string.Join(", ", missingPlaceholders)}"
                };
            }
            
            // Replace placeholders in content template
            var content = template.ContentTemplate;
            foreach (var placeholder in placeholders)
            {
                content = content.Replace($"{{{placeholder.Key}}}", placeholder.Value);
            }
            
            // Merge fields
            var fields = new Dictionary<string, JsonElement>(template.DefaultFields);
            if (additionalFields != null)
            {
                foreach (var field in additionalFields)
                {
                    fields[field.Key] = field.Value;
                }
            }
            
            // Add template info to fields
            fields["fromTemplate"] = JsonSerializer.SerializeToElement(templateId);
            fields["createdFromTemplate"] = JsonSerializer.SerializeToElement(DateTime.UtcNow);
            
            // Add default tags
            if (template.DefaultTags.Any())
            {
                var existingTags = fields.ContainsKey("tags") 
                    ? JsonSerializer.Deserialize<List<string>>(fields["tags"]) ?? new List<string>()
                    : new List<string>();
                    
                existingTags.AddRange(template.DefaultTags);
                fields["tags"] = JsonSerializer.SerializeToElement(existingTags.Distinct().ToList());
            }
            
            // Create the memory
            var memory = new FlexibleMemoryEntry
            {
                Type = template.MemoryType,
                Content = content,
                IsShared = true,
                FilesInvolved = files ?? Array.Empty<string>(),
                Fields = fields
            };
            
            var success = await _memoryService.StoreMemoryAsync(memory);
            
            return new StoreMemoryResult
            {
                Success = success,
                MemoryId = success ? memory.Id : null,
                Message = success 
                    ? $"Created {template.Name} memory from template"
                    : "Failed to store memory"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating memory from template");
            return new StoreMemoryResult
            {
                Success = false,
                Message = $"Error: {ex.Message}"
            };
        }
    }
    
    /// <summary>
    /// List available memory templates
    /// </summary>
    public Task<ListTemplatesResult> ListTemplatesAsync()
    {
        try
        {
            var templates = MemoryTemplates.GetAll();
            var templateList = templates.Values.Select(t => new TemplateInfo
            {
                Id = t.Id,
                Name = t.Name,
                Description = t.Description,
                MemoryType = t.MemoryType,
                RequiredPlaceholders = t.RequiredPlaceholders,
                PlaceholderDescriptions = t.PlaceholderDescriptions,
                ExampleUsage = GenerateExampleUsage(t)
            }).ToList();
            
            return Task.FromResult(new ListTemplatesResult
            {
                Success = true,
                Templates = templateList,
                Message = $"Found {templateList.Count} templates"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing templates");
            return Task.FromResult(new ListTemplatesResult
            {
                Success = false,
                Message = $"Error: {ex.Message}"
            });
        }
    }
    
    private string GenerateExampleUsage(MemoryTemplate template)
    {
        var placeholders = string.Join(" ", template.RequiredPlaceholders.Select(p => $"--{p} \"...\""));
        return $"create_memory_from_template --templateId \"{template.Id}\" {placeholders}";
    }
    
    /// <summary>
    /// Get context-aware memory suggestions based on current work context
    /// </summary>
    public async Task<MemorySuggestionsResult> GetMemorySuggestionsAsync(
        string currentContext,
        string? currentFile = null,
        string[]? recentFiles = null,
        int maxSuggestions = 5)
    {
        try
        {
            var suggestions = new List<MemorySuggestion>();
            
            // 1. Find related memories based on context
            var searchRequest = new FlexibleMemorySearchRequest
            {
                Query = currentContext,
                MaxResults = 10,
                BoostRecent = true
            };
            
            var searchResult = await _memoryService.SearchMemoriesAsync(searchRequest);
            if (searchResult.Memories.Any())
            {
                // Suggest following up on pending items
                var pendingMemories = searchResult.Memories
                    .Where(m => m.GetField<string>("status") == "pending")
                    .Take(2);
                    
                foreach (var memory in pendingMemories)
                {
                    suggestions.Add(new MemorySuggestion
                    {
                        Type = "follow-up",
                        Title = $"Follow up on: {memory.Type}",
                        Description = memory.Content.Length > 100 
                            ? memory.Content.Substring(0, 100) + "..." 
                            : memory.Content,
                        Action = $"update_memory --id \"{memory.Id}\" --status \"in-progress\"",
                        Relevance = 0.9
                    });
                }
                
                // Suggest related decisions or patterns
                var relatedKnowledge = searchResult.Memories
                    .Where(m => m.Type == "ArchitecturalDecision" || m.Type == "CodePattern")
                    .Take(2);
                    
                foreach (var memory in relatedKnowledge)
                {
                    suggestions.Add(new MemorySuggestion
                    {
                        Type = "reference",
                        Title = $"Consider: {memory.Type}",
                        Description = memory.Content.Length > 100 
                            ? memory.Content.Substring(0, 100) + "..." 
                            : memory.Content,
                        Action = $"get_memory --id \"{memory.Id}\"",
                        Relevance = 0.7
                    });
                }
            }
            
            // 2. Suggest templates based on context keywords
            var templates = MemoryTemplates.GetAll();
            var contextLower = currentContext.ToLowerInvariant();
            
            if (contextLower.Contains("review") || contextLower.Contains("code quality"))
            {
                suggestions.Add(new MemorySuggestion
                {
                    Type = "template",
                    Title = "Create Code Review Finding",
                    Description = "Document issues found during code review",
                    Action = $"create_memory_from_template --templateId \"code-review\" --file \"{currentFile ?? "file.cs"}\"",
                    Relevance = 0.8
                });
            }
            
            if (contextLower.Contains("performance") || contextLower.Contains("slow") || contextLower.Contains("optimize"))
            {
                suggestions.Add(new MemorySuggestion
                {
                    Type = "template",
                    Title = "Track Performance Issue",
                    Description = "Document performance problems that need optimization",
                    Action = "create_memory_from_template --templateId \"performance-issue\"",
                    Relevance = 0.8
                });
            }
            
            if (contextLower.Contains("security") || contextLower.Contains("vulnerability"))
            {
                suggestions.Add(new MemorySuggestion
                {
                    Type = "template",
                    Title = "Document Security Finding",
                    Description = "Record security vulnerabilities or concerns",
                    Action = "create_memory_from_template --templateId \"security-audit\"",
                    Relevance = 0.9
                });
            }
            
            // 3. Suggest creating working memory for current task
            if (!string.IsNullOrWhiteSpace(currentContext))
            {
                suggestions.Add(new MemorySuggestion
                {
                    Type = "working-memory",
                    Title = "Save Current Context",
                    Description = "Store this context as working memory for later reference",
                    Action = $"store_temporary_memory --content \"{currentContext}\" --expiresIn \"4h\"",
                    Relevance = 0.6
                });
            }
            
            // 4. If working with specific files, suggest file-specific memories
            if (currentFile != null)
            {
                var fileMemoriesRequest = new FlexibleMemorySearchRequest
                {
                    Query = currentFile,
                    MaxResults = 5
                };
                
                var fileMemories = await _memoryService.SearchMemoriesAsync(fileMemoriesRequest);
                if (fileMemories.Memories.Any())
                {
                    suggestions.Add(new MemorySuggestion
                    {
                        Type = "file-context",
                        Title = $"View memories for {Path.GetFileName(currentFile)}",
                        Description = $"Found {fileMemories.TotalFound} memories related to this file",
                        Action = $"search_memories --query \"{currentFile}\"",
                        Relevance = 0.7
                    });
                }
            }
            
            // Sort by relevance and take top suggestions
            var topSuggestions = suggestions
                .OrderByDescending(s => s.Relevance)
                .Take(maxSuggestions)
                .ToList();
            
            return new MemorySuggestionsResult
            {
                Success = true,
                Suggestions = topSuggestions,
                Message = $"Generated {topSuggestions.Count} context-aware suggestions"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating memory suggestions");
            return new MemorySuggestionsResult
            {
                Success = false,
                Message = $"Error: {ex.Message}"
            };
        }
    }
    
    /// <summary>
    /// Get memory system dashboard with statistics and health information
    /// </summary>
    public async Task<MemoryDashboardResult> GetMemoryDashboardAsync()
    {
        try
        {
            var dashboard = new MemoryDashboardResult { Success = true };
            
            // Get all memories for analysis
            var allMemoriesRequest = new FlexibleMemorySearchRequest
            {
                Query = "*",
                MaxResults = 10000,
                IncludeArchived = true
            };
            var allMemories = await _memoryService.SearchMemoriesAsync(allMemoriesRequest);
            
            // Calculate statistics
            var stats = new MemoryStatistics();
            var now = DateTime.UtcNow;
            
            foreach (var memory in allMemories.Memories)
            {
                stats.TotalMemories++;
                
                // Check status
                var status = memory.GetField<string>(MemoryFields.Status);
                if (status == MemoryStatus.Resolved)
                    stats.ResolvedMemories++;
                    
                // Check if archived
                if (memory.GetField<bool>("archived"))
                    stats.ArchivedMemories++;
                else
                    stats.ActiveMemories++;
                    
                // Check if expired (working memory)
                var expiresAt = memory.GetField<DateTime?>(MemoryFields.ExpiresAt);
                if (expiresAt.HasValue && expiresAt.Value < now)
                    stats.ExpiredMemories++;
                    
                // Check if working memory
                if (memory.Type == MemoryTypes.WorkingMemory || memory.GetField<bool>("isWorkingMemory"))
                    stats.WorkingMemories++;
                    
                // Check if has links
                if (memory.Fields.Any(f => f.Key.StartsWith("link_") || f.Key.StartsWith("linkedTo")))
                    stats.LinkedMemories++;
            }
            
            if (allMemories.Memories.Any())
            {
                stats.OldestMemory = allMemories.Memories.Min(m => m.Created);
                stats.NewestMemory = allMemories.Memories.Max(m => m.Created);
                stats.AverageMemoryAgeInDays = allMemories.Memories
                    .Average(m => (now - m.Created).TotalDays);
            }
            
            dashboard.Statistics = stats;
            
            // Type distribution
            dashboard.TypeDistribution = allMemories.Memories
                .GroupBy(m => m.Type)
                .ToDictionary(g => g.Key, g => g.Count())
                .OrderByDescending(kv => kv.Value)
                .ToDictionary(kv => kv.Key, kv => kv.Value);
                
            // Status distribution
            dashboard.StatusDistribution = allMemories.Memories
                .Select(m => m.GetField<string>(MemoryFields.Status) ?? "none")
                .GroupBy(s => s)
                .ToDictionary(g => g.Key, g => g.Count())
                .OrderByDescending(kv => kv.Value)
                .ToDictionary(kv => kv.Key, kv => kv.Value);
                
            // Health checks
            dashboard.HealthIssues = AnalyzeMemoryHealth(allMemories.Memories, stats);
            
            // Recent activity (last 24 hours)
            var recentCutoff = now.AddDays(-1);
            var recentMemories = allMemories.Memories
                .Where(m => m.Created > recentCutoff || m.Modified > recentCutoff)
                .ToList();
                
            dashboard.RecentActivities = new List<RecentActivity>
            {
                new RecentActivity
                {
                    Type = "Created",
                    Action = "New memories",
                    Timestamp = recentMemories.Any() ? recentMemories.Max(m => m.Created) : DateTime.MinValue,
                    Count = recentMemories.Count(m => m.Created > recentCutoff)
                },
                new RecentActivity
                {
                    Type = "Modified",
                    Action = "Updated memories",
                    Timestamp = recentMemories.Any() ? recentMemories.Max(m => m.Modified) : DateTime.MinValue,
                    Count = recentMemories.Count(m => m.Modified > recentCutoff && m.Modified != m.Created)
                }
            };
            
            // Storage info
            dashboard.Storage = await GetStorageInfoAsync();
            
            // Build summary message
            var messageParts = new List<string>
            {
                $"Memory System Dashboard - {stats.TotalMemories} total memories",
                $"Active: {stats.ActiveMemories}, Archived: {stats.ArchivedMemories}, Resolved: {stats.ResolvedMemories}"
            };
            
            if (dashboard.HealthIssues.Any(h => h.Severity == "error"))
            {
                messageParts.Add($"⚠️ {dashboard.HealthIssues.Count(h => h.Severity == "error")} critical issues detected");
            }
            
            dashboard.Message = string.Join("\n", messageParts);
            
            return dashboard;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating memory dashboard");
            return new MemoryDashboardResult
            {
                Success = false,
                Message = $"Error: {ex.Message}"
            };
        }
    }
    
    private List<MemoryHealthIssue> AnalyzeMemoryHealth(List<FlexibleMemoryEntry> memories, MemoryStatistics stats)
    {
        var issues = new List<MemoryHealthIssue>();
        var now = DateTime.UtcNow;
        
        // Check for too many unresolved items
        var unresolvedTechnicalDebt = memories
            .Where(m => m.Type == MemoryTypes.TechnicalDebt && 
                       m.GetField<string>(MemoryFields.Status) != MemoryStatus.Resolved)
            .Count();
            
        if (unresolvedTechnicalDebt > 50)
        {
            issues.Add(new MemoryHealthIssue
            {
                Severity = "warning",
                Issue = "High unresolved technical debt",
                Recommendation = "Consider reviewing and prioritizing technical debt items",
                AffectedCount = unresolvedTechnicalDebt
            });
        }
        
        // Check for expired working memories
        if (stats.ExpiredMemories > 0)
        {
            issues.Add(new MemoryHealthIssue
            {
                Severity = "info",
                Issue = "Expired working memories present",
                Recommendation = "These will be filtered out automatically",
                AffectedCount = stats.ExpiredMemories
            });
        }
        
        // Check for old unarchived memories
        var oldActiveMemories = memories
            .Where(m => !m.GetField<bool>("archived") && 
                       (now - m.Created).TotalDays > 180)
            .Count();
            
        if (oldActiveMemories > 100)
        {
            issues.Add(new MemoryHealthIssue
            {
                Severity = "warning",
                Issue = "Many old active memories",
                Recommendation = "Consider archiving or summarizing memories older than 6 months",
                AffectedCount = oldActiveMemories
            });
        }
        
        // Check for memories without files
        var memoriesWithoutFiles = memories
            .Where(m => !m.FilesInvolved.Any() && 
                       m.Type != MemoryTypes.WorkingMemory)
            .Count();
            
        if (memoriesWithoutFiles > stats.TotalMemories * 0.3)
        {
            issues.Add(new MemoryHealthIssue
            {
                Severity = "info",
                Issue = "Many memories lack file associations",
                Recommendation = "Consider adding file references for better context",
                AffectedCount = memoriesWithoutFiles
            });
        }
        
        // Check for duplicate content
        var duplicateGroups = memories
            .GroupBy(m => m.Content.Trim().ToLowerInvariant())
            .Where(g => g.Count() > 1)
            .ToList();
            
        if (duplicateGroups.Any())
        {
            issues.Add(new MemoryHealthIssue
            {
                Severity = "info",
                Issue = "Potential duplicate memories detected",
                Recommendation = "Review and consolidate duplicate entries",
                AffectedCount = duplicateGroups.Sum(g => g.Count())
            });
        }
        
        return issues.OrderBy(i => i.Severity == "error" ? 0 : i.Severity == "warning" ? 1 : 2).ToList();
    }
    
    private Task<StorageInfo> GetStorageInfoAsync()
    {
        var storage = new StorageInfo();
        
        try
        {
            // Get base directory from PathResolutionService
            var baseDir = _pathResolution.GetBasePath();
            
            if (Directory.Exists(baseDir))
            {
                // Calculate total size
                var dirInfo = new DirectoryInfo(baseDir);
                storage.TotalSizeBytes = GetDirectorySize(dirInfo);
                storage.FormattedSize = FormatBytes(storage.TotalSizeBytes);
                
                // Check for memory directories
                var projectMemoryDir = _pathResolution.GetProjectMemoryPath();
                var localMemoryDir = _pathResolution.GetLocalMemoryPath();
                
                if (Directory.Exists(projectMemoryDir))
                {
                    storage.IndexSizeBytes += GetDirectorySize(new DirectoryInfo(projectMemoryDir));
                }
                
                if (Directory.Exists(localMemoryDir))
                {
                    storage.IndexSizeBytes += GetDirectorySize(new DirectoryInfo(localMemoryDir));
                }
                
                // Check for backup file
                var backupRoot = Path.Combine(_pathResolution.GetBasePath(), PathConstants.BackupsDirectoryName);
                var backupFile = Path.Combine(backupRoot, "memories.db");
                if (File.Exists(backupFile))
                {
                    var fileInfo = new FileInfo(backupFile);
                    storage.BackupSizeBytes = fileInfo.Length;
                    storage.LastBackup = fileInfo.LastWriteTimeUtc;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error calculating storage info");
        }
        
        return Task.FromResult(storage);
    }
    
    private long GetDirectorySize(DirectoryInfo dir)
    {
        try
        {
            return dir.GetFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
        }
        catch
        {
            return 0;
        }
    }
    
    private string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
    
    /// <summary>
    /// Store a git commit memory
    /// </summary>
    public async Task<StoreMemoryResult> StoreGitCommitMemoryAsync(
        string sha,
        string message,
        string description,
        string? author = null,
        DateTime? commitDate = null,
        string[]? filesChanged = null,
        string? branch = null,
        Dictionary<string, JsonElement>? additionalFields = null)
    {
        try
        {
            var fields = additionalFields ?? new Dictionary<string, JsonElement>();
            
            // Add git-specific fields
            fields["sha"] = JsonDocument.Parse($"\"{sha}\"").RootElement;
            fields["commitMessage"] = JsonDocument.Parse($"\"{message}\"").RootElement;
            
            if (!string.IsNullOrEmpty(author))
                fields["author"] = JsonDocument.Parse($"\"{author}\"").RootElement;
            
            if (commitDate.HasValue)
                fields["commitDate"] = JsonDocument.Parse($"\"{commitDate.Value:O}\"").RootElement;
            
            if (!string.IsNullOrEmpty(branch))
                fields["branch"] = JsonDocument.Parse($"\"{branch}\"").RootElement;
            
            if (filesChanged != null && filesChanged.Length > 0)
                fields["filesChanged"] = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(filesChanged));
            
            return await StoreMemoryAsync(
                MemoryTypes.GitCommit,
                description,
                isShared: true,
                files: filesChanged,
                fields: fields
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error storing git commit memory");
            return new StoreMemoryResult
            {
                Success = false,
                Message = $"Error: {ex.Message}"
            };
        }
    }
    
    /// <summary>
    /// Find all memories related to a specific file
    /// </summary>
    public async Task<FileMemoriesResult> GetMemoriesForFileAsync(string filePath, bool includeArchived = false)
    {
        try
        {
            // Normalize the file path
            var normalizedPath = filePath.Replace('\\', '/');
            
            // Search for memories that mention this file
            var searchParams = new FlexibleMemorySearchRequest
            {
                Query = $"\"{normalizedPath}\"",
                MaxResults = 100,
                IncludeArchived = includeArchived
            };
            
            var searchResult = await _memoryService.SearchMemoriesAsync(searchParams);
            
            // Filter to memories that actually involve this file
            var fileMemories = searchResult.Memories
                .Where(m => m.FilesInvolved.Any(f => f.Replace('\\', '/').EndsWith(normalizedPath, StringComparison.OrdinalIgnoreCase)) ||
                           m.Content.Contains(normalizedPath, StringComparison.OrdinalIgnoreCase))
                .ToList();
            
            // Group by memory type
            var groupedByType = fileMemories
                .GroupBy(m => m.Type)
                .ToDictionary(g => g.Key, g => g.ToList());
            
            return new FileMemoriesResult
            {
                Success = true,
                FilePath = filePath,
                TotalMemories = fileMemories.Count,
                MemoriesByType = groupedByType,
                Memories = fileMemories,
                Message = $"Found {fileMemories.Count} memories related to {Path.GetFileName(filePath)}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding memories for file");
            return new FileMemoriesResult
            {
                Success = false,
                FilePath = filePath,
                Message = $"Error: {ex.Message}"
            };
        }
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

public class MemoryDashboardResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public MemoryStatistics Statistics { get; set; } = new();
    public List<MemoryHealthIssue> HealthIssues { get; set; } = new();
    public Dictionary<string, int> TypeDistribution { get; set; } = new();
    public Dictionary<string, int> StatusDistribution { get; set; } = new();
    public List<RecentActivity> RecentActivities { get; set; } = new();
    public StorageInfo Storage { get; set; } = new();
}

public class MemoryStatistics
{
    public int TotalMemories { get; set; }
    public int ActiveMemories { get; set; }
    public int ArchivedMemories { get; set; }
    public int ExpiredMemories { get; set; }
    public int ResolvedMemories { get; set; }
    public int LinkedMemories { get; set; }
    public int WorkingMemories { get; set; }
    public DateTime OldestMemory { get; set; }
    public DateTime NewestMemory { get; set; }
    public double AverageMemoryAgeInDays { get; set; }
}

public class MemoryHealthIssue
{
    public string Severity { get; set; } = ""; // "warning", "error", "info"
    public string Issue { get; set; } = "";
    public string Recommendation { get; set; } = "";
    public int AffectedCount { get; set; }
}

public class RecentActivity
{
    public string Type { get; set; } = "";
    public string Action { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public int Count { get; set; }
}

public class StorageInfo
{
    public long TotalSizeBytes { get; set; }
    public string FormattedSize { get; set; } = "";
    public long IndexSizeBytes { get; set; }
    public long BackupSizeBytes { get; set; }
    public DateTime? LastBackup { get; set; }
    public DateTime? LastIndexOptimization { get; set; }
}

public class FileMemoriesResult
{
    public bool Success { get; set; }
    public string FilePath { get; set; } = "";
    public int TotalMemories { get; set; }
    public Dictionary<string, List<FlexibleMemoryEntry>> MemoriesByType { get; set; } = new();
    public List<FlexibleMemoryEntry> Memories { get; set; } = new();
    public string Message { get; set; } = "";
}