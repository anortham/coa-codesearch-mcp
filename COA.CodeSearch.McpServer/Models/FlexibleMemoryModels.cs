using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace COA.CodeSearch.McpServer.Models;

/// <summary>
/// Flexible memory entry that supports dynamic fields and rich metadata
/// </summary>
public class FlexibleMemoryEntry
{
    /// <summary>
    /// Unique identifier for the memory entry
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    /// <summary>
    /// Memory type - can be anything, not limited to predefined enums
    /// Examples: "ArchitecturalDecision", "TechnicalDebt", "Question", "Idea", etc.
    /// </summary>
    public string Type { get; set; } = string.Empty;
    
    /// <summary>
    /// The main content of the memory
    /// </summary>
    public string Content { get; set; } = string.Empty;
    
    /// <summary>
    /// When this memory was created
    /// </summary>
    public DateTime Created { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// When this memory was last modified
    /// </summary>
    public DateTime Modified { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Timestamp ticks for SQLite compatibility
    /// </summary>
    public long TimestampTicks => Created.Ticks;
    
    /// <summary>
    /// Extended fields for flexible data storage
    /// Examples: Status, Priority, DueDate, RelatedTo, Tags, Context, AssignedTo, etc.
    /// </summary>
    public Dictionary<string, JsonElement> Fields { get; set; } = new();
    
    /// <summary>
    /// Files that are related to this memory
    /// </summary>
    public string[] FilesInvolved { get; set; } = Array.Empty<string>();
    
    /// <summary>
    /// Session ID when this memory was created
    /// </summary>
    public string SessionId { get; set; } = string.Empty;
    
    /// <summary>
    /// Whether this memory should be shared (version controlled) or kept local
    /// </summary>
    public bool IsShared { get; set; } = true;
    
    /// <summary>
    /// Access count for boosting frequently accessed memories
    /// </summary>
    public int AccessCount { get; set; } = 0;
    
    /// <summary>
    /// Last time this memory was accessed (for relevance boosting)
    /// </summary>
    public DateTime? LastAccessed { get; set; }
    
    // Helper methods for common field operations
    
    /// <summary>
    /// Get a field value as a specific type
    /// </summary>
    public T? GetField<T>(string fieldName)
    {
        if (Fields.TryGetValue(fieldName, out var element))
        {
            try
            {
                return JsonSerializer.Deserialize<T>(element.GetRawText());
            }
            catch
            {
                return default;
            }
        }
        return default;
    }
    
    /// <summary>
    /// Set a field value
    /// </summary>
    public void SetField<T>(string fieldName, T value)
    {
        var json = JsonSerializer.Serialize(value);
        Fields[fieldName] = JsonDocument.Parse(json).RootElement;
    }
    
    /// <summary>
    /// Get status field if it exists
    /// </summary>
    public string? Status => GetField<string>("status");
    
    /// <summary>
    /// Get priority field if it exists
    /// </summary>
    public string? Priority => GetField<string>("priority");
    
    /// <summary>
    /// Get tags field if it exists
    /// </summary>
    public string[]? Tags => GetField<string[]>("tags");
    
    /// <summary>
    /// Get related memory IDs if they exist
    /// </summary>
    public string[]? RelatedTo => GetField<string[]>("relatedTo");
}

/// <summary>
/// Predefined memory types for common use cases (not exhaustive)
/// </summary>
public static class MemoryTypes
{
    // Existing types (for backwards compatibility)
    public const string ArchitecturalDecision = "ArchitecturalDecision";
    public const string CodePattern = "CodePattern";
    public const string SecurityRule = "SecurityRule";
    public const string ProjectInsight = "ProjectInsight";
    public const string WorkSession = "WorkSession";
    public const string ConversationSummary = "ConversationSummary";
    public const string PersonalContext = "PersonalContext";
    public const string TemporaryNote = "TemporaryNote";
    
    // New flexible types
    public const string TechnicalDebt = "TechnicalDebt";
    public const string DeferredTask = "DeferredTask";
    public const string Question = "Question";
    public const string Assumption = "Assumption";
    public const string Experiment = "Experiment";
    public const string Learning = "Learning";
    public const string Blocker = "Blocker";
    public const string Idea = "Idea";
    public const string CodeReview = "CodeReview";
    public const string BugReport = "BugReport";
    public const string PerformanceIssue = "PerformanceIssue";
    public const string Refactoring = "Refactoring";
    public const string Documentation = "Documentation";
    public const string Dependency = "Dependency";
    public const string Configuration = "Configuration";
}

/// <summary>
/// Common field names for extended fields
/// </summary>
public static class MemoryFields
{
    public const string Status = "status";
    public const string Priority = "priority";
    public const string DueDate = "dueDate";
    public const string Tags = "tags";
    public const string RelatedTo = "relatedTo";
    public const string BlockedBy = "blockedBy";
    public const string Context = "context";
    public const string AssignedTo = "assignedTo";
    public const string Complexity = "complexity";
    public const string Impact = "impact";
    public const string Risk = "risk";
    public const string Component = "component";
    public const string Version = "version";
    public const string Reasoning = "reasoning";
    public const string Category = "category";
    public const string Confidence = "confidence";
    public const string ExpiresAt = "expiresAt";
}

/// <summary>
/// Common status values
/// </summary>
public static class MemoryStatus
{
    public const string Pending = "pending";
    public const string InProgress = "in-progress";
    public const string Done = "done";
    public const string Blocked = "blocked";
    public const string Deferred = "deferred";
    public const string Cancelled = "cancelled";
    public const string UnderReview = "under-review";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
}

/// <summary>
/// Common priority values
/// </summary>
public static class MemoryPriority
{
    public const string Critical = "critical";
    public const string High = "high";
    public const string Medium = "medium";
    public const string Low = "low";
    public const string Nice = "nice-to-have";
}

/// <summary>
/// Enhanced search request with faceting and advanced filters
/// </summary>
public class FlexibleMemorySearchRequest
{
    /// <summary>
    /// Main search query (can be natural language)
    /// </summary>
    public string Query { get; set; } = string.Empty;
    
    /// <summary>
    /// Filter by memory types
    /// </summary>
    public string[]? Types { get; set; }
    
    /// <summary>
    /// Faceted search filters (field:value pairs)
    /// Example: { "status": "pending", "priority": "high" }
    /// </summary>
    public Dictionary<string, string>? Facets { get; set; }
    
    /// <summary>
    /// Date range filter
    /// </summary>
    public DateRangeFilter? DateRange { get; set; }
    
    /// <summary>
    /// Find memories related to these IDs
    /// </summary>
    public string[]? RelatedToIds { get; set; }
    
    /// <summary>
    /// Maximum depth for relationship traversal
    /// </summary>
    public int RelationshipDepth { get; set; } = 1;
    
    /// <summary>
    /// Order by field
    /// </summary>
    public string? OrderBy { get; set; }
    
    /// <summary>
    /// Order descending
    /// </summary>
    public bool OrderDescending { get; set; } = true;
    
    /// <summary>
    /// Maximum results to return
    /// </summary>
    public int MaxResults { get; set; } = 50;
    
    /// <summary>
    /// Include archived memories
    /// </summary>
    public bool IncludeArchived { get; set; } = false;
    
    /// <summary>
    /// Boost recent memories in scoring
    /// </summary>
    public bool BoostRecent { get; set; } = true;
    
    /// <summary>
    /// Boost frequently accessed memories
    /// </summary>
    public bool BoostFrequent { get; set; } = true;
}

/// <summary>
/// Date range filter for temporal queries
/// </summary>
public class DateRangeFilter
{
    /// <summary>
    /// Start date (inclusive)
    /// </summary>
    public DateTime? From { get; set; }
    
    /// <summary>
    /// End date (inclusive)
    /// </summary>
    public DateTime? To { get; set; }
    
    /// <summary>
    /// Relative time expression (e.g., "last-7-days", "last-week")
    /// </summary>
    public string? RelativeTime { get; set; }
    
    /// <summary>
    /// Parse relative time expressions
    /// </summary>
    public void ParseRelativeTime()
    {
        if (string.IsNullOrEmpty(RelativeTime)) return;
        
        var now = DateTime.UtcNow;
        switch (RelativeTime.ToLower())
        {
            case "today":
                From = now.Date;
                To = now;
                break;
            case "yesterday":
                From = now.Date.AddDays(-1);
                To = now.Date.AddTicks(-1);
                break;
            case "last-week":
            case "last-7-days":
                From = now.AddDays(-7);
                To = now;
                break;
            case "last-month":
            case "last-30-days":
                From = now.AddDays(-30);
                To = now;
                break;
            case "last-hour":
                From = now.AddHours(-1);
                To = now;
                break;
            default:
                // Try to parse patterns like "last-N-days", "last-N-hours"
                var parts = RelativeTime.Split('-');
                if (parts.Length == 3 && parts[0] == "last" && int.TryParse(parts[1], out var n))
                {
                    switch (parts[2])
                    {
                        case "days":
                            From = now.AddDays(-n);
                            To = now;
                            break;
                        case "hours":
                            From = now.AddHours(-n);
                            To = now;
                            break;
                        case "weeks":
                            From = now.AddDays(-n * 7);
                            To = now;
                            break;
                    }
                }
                break;
        }
    }
}

/// <summary>
/// Enhanced search result with facets and insights
/// </summary>
public class FlexibleMemorySearchResult
{
    /// <summary>
    /// The memory entries that matched the search
    /// </summary>
    public List<FlexibleMemoryEntry> Memories { get; set; } = new();
    
    /// <summary>
    /// Total number of memories found (before limiting)
    /// </summary>
    public int TotalFound { get; set; }
    
    /// <summary>
    /// Facet counts for filtering
    /// Example: { "type": { "TechnicalDebt": 15, "Question": 8 }, "status": { "pending": 20, "done": 3 } }
    /// </summary>
    public Dictionary<string, Dictionary<string, int>> FacetCounts { get; set; } = new();
    
    /// <summary>
    /// Terms that were highlighted in the results
    /// </summary>
    public string[] HighlightedTerms { get; set; } = Array.Empty<string>();
    
    /// <summary>
    /// Suggested related searches
    /// </summary>
    public string[] SuggestedQueries { get; set; } = Array.Empty<string>();
    
    /// <summary>
    /// AI-generated insights about the results
    /// </summary>
    public MemorySearchInsights? Insights { get; set; }
}

/// <summary>
/// AI-generated insights about search results
/// </summary>
public class MemorySearchInsights
{
    /// <summary>
    /// Summary of what was found
    /// </summary>
    public string Summary { get; set; } = string.Empty;
    
    /// <summary>
    /// Key patterns identified
    /// </summary>
    public string[] Patterns { get; set; } = Array.Empty<string>();
    
    /// <summary>
    /// Recommended actions based on the results
    /// </summary>
    public string[] RecommendedActions { get; set; } = Array.Empty<string>();
    
    /// <summary>
    /// Related memory clusters
    /// </summary>
    public MemoryCluster[] Clusters { get; set; } = Array.Empty<MemoryCluster>();
}

/// <summary>
/// A cluster of related memories
/// </summary>
public class MemoryCluster
{
    /// <summary>
    /// Cluster name or theme
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Memory IDs in this cluster
    /// </summary>
    public string[] MemoryIds { get; set; } = Array.Empty<string>();
    
    /// <summary>
    /// Common characteristics
    /// </summary>
    public string[] CommonTraits { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Request to update a memory
/// </summary>
public class MemoryUpdateRequest
{
    /// <summary>
    /// Memory ID to update
    /// </summary>
    public string Id { get; set; } = string.Empty;
    
    /// <summary>
    /// Fields to update (null values mean remove the field)
    /// </summary>
    public Dictionary<string, JsonElement?> FieldUpdates { get; set; } = new();
    
    /// <summary>
    /// Optional new content
    /// </summary>
    public string? Content { get; set; }
    
    /// <summary>
    /// Add these files to FilesInvolved
    /// </summary>
    public string[]? AddFiles { get; set; }
    
    /// <summary>
    /// Remove these files from FilesInvolved
    /// </summary>
    public string[]? RemoveFiles { get; set; }
}

/// <summary>
/// Working memory for current session
/// </summary>
public class WorkingMemory : FlexibleMemoryEntry
{
    /// <summary>
    /// When this working memory expires
    /// </summary>
    public DateTime ExpiresAt { get; set; }
    
    /// <summary>
    /// Create a working memory that expires at end of session
    /// </summary>
    public static WorkingMemory CreateSessionScoped(string content)
    {
        var memory = new WorkingMemory
        {
            Type = "WorkingMemory",
            Content = content,
            IsShared = false,
            ExpiresAt = DateTime.UtcNow.AddHours(24) // Default 24 hour expiry
        };
        memory.SetField(MemoryFields.ExpiresAt, memory.ExpiresAt);
        return memory;
    }
}