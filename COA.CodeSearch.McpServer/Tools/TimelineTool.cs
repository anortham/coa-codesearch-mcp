using System.Text;
using System.Text.Json;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// User-facing tool for viewing memories in a chronological timeline format
/// </summary>
public class TimelineTool : ITool
{
    private readonly ILogger<TimelineTool> _logger;
    private readonly FlexibleMemoryService _memoryService;
    
    public string ToolName => "memory_timeline";
    public string Description => "View memories in a chronological timeline format";
    public ToolCategory Category => ToolCategory.Memory;
    
    public TimelineTool(
        ILogger<TimelineTool> logger, 
        FlexibleMemoryService memoryService)
    {
        _logger = logger;
        _memoryService = memoryService;
    }
    
    /// <summary>
    /// Get a chronological timeline view of memories
    /// </summary>
    public async Task<TimelineResult> GetTimelineAsync(
        int days = 7,
        string[]? types = null,
        bool includeArchived = false,
        bool includeExpired = false,
        int maxPerGroup = 10)
    {
        try
        {
            // Calculate date range
            var endDate = DateTime.UtcNow;
            var startDate = endDate.AddDays(-days);
            
            // Search for memories in date range
            var request = new FlexibleMemorySearchRequest
            {
                Query = "*",
                Types = types,
                OrderBy = "created",
                OrderDescending = true,
                MaxResults = 500, // Get more than we'll display for better grouping
                IncludeArchived = includeArchived,
                DateRange = new DateRangeFilter
                {
                    From = startDate,
                    To = endDate
                }
            };
            
            var searchResult = await _memoryService.SearchMemoriesAsync(request);
            
            // Filter out expired working memories unless requested
            var memories = searchResult.Memories;
            if (!includeExpired)
            {
                memories = memories.Where(m => !IsExpiredWorkingMemory(m)).ToList();
            }
            
            // Group memories by time period
            var groups = GroupMemoriesByTimePeriod(memories, maxPerGroup);
            
            // Build timeline
            var timeline = BuildTimeline(groups, searchResult.TotalFound, days);
            
            return new TimelineResult
            {
                Success = true,
                Timeline = timeline,
                TotalMemories = memories.Count,
                DateRange = new { From = startDate, To = endDate },
                Groups = groups.Select(g => new TimelineGroup
                {
                    Period = g.Key,
                    Count = g.Value.Count,
                    Types = g.Value.GroupBy(m => m.Type).ToDictionary(t => t.Key, t => t.Count())
                }).ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating timeline");
            return new TimelineResult
            {
                Success = false,
                Timeline = $"Error generating timeline: {ex.Message}"
            };
        }
    }
    
    private bool IsExpiredWorkingMemory(FlexibleMemoryEntry memory)
    {
        if (memory.Type != "WorkingMemory") return false;
        
        var expiresAt = memory.GetField<DateTime?>("expiresAt");
        if (expiresAt.HasValue && expiresAt.Value < DateTime.UtcNow)
        {
            return true;
        }
        
        // Check session expiry
        var sessionExpiry = memory.GetField<string>("sessionExpiry");
        if (sessionExpiry == "end-of-session")
        {
            // Assume sessions older than 24 hours are expired
            return memory.Created < DateTime.UtcNow.AddDays(-1);
        }
        
        return false;
    }
    
    private Dictionary<string, List<FlexibleMemoryEntry>> GroupMemoriesByTimePeriod(
        List<FlexibleMemoryEntry> memories, int maxPerGroup)
    {
        var now = DateTime.UtcNow;
        var groups = new Dictionary<string, List<FlexibleMemoryEntry>>();
        
        foreach (var memory in memories)
        {
            var period = GetTimePeriod(memory.Created, now);
            if (!groups.ContainsKey(period))
            {
                groups[period] = new List<FlexibleMemoryEntry>();
            }
            
            if (groups[period].Count < maxPerGroup)
            {
                groups[period].Add(memory);
            }
        }
        
        return groups;
    }
    
    private string GetTimePeriod(DateTime date, DateTime now)
    {
        var diff = now - date;
        
        if (diff.TotalHours < 1) return "Last Hour";
        if (diff.TotalHours < 24) return "Today";
        if (diff.TotalHours < 48) return "Yesterday";
        if (diff.TotalDays < 7) return "This Week";
        if (diff.TotalDays < 14) return "Last Week";
        if (diff.TotalDays < 30) return "This Month";
        if (diff.TotalDays < 60) return "Last Month";
        return "Older";
    }
    
    private string BuildTimeline(
        Dictionary<string, List<FlexibleMemoryEntry>> groups,
        int totalFound,
        int days)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Memory Timeline - Last {days} Days");
        sb.AppendLine($"*{totalFound} total memories found*");
        sb.AppendLine();
        
        // Define period order
        var periodOrder = new[] 
        { 
            "Last Hour", "Today", "Yesterday", "This Week", 
            "Last Week", "This Month", "Last Month", "Older" 
        };
        
        foreach (var period in periodOrder)
        {
            if (!groups.ContainsKey(period) || groups[period].Count == 0) continue;
            
            var memories = groups[period];
            sb.AppendLine($"## {period} ({memories.Count} memories)");
            sb.AppendLine();
            
            foreach (var memory in memories)
            {
                sb.AppendLine(FormatMemoryEntry(memory));
            }
            
            sb.AppendLine();
        }
        
        return sb.ToString();
    }
    
    private string FormatMemoryEntry(FlexibleMemoryEntry memory)
    {
        var sb = new StringBuilder();
        
        // Header with type and time
        var timeAgo = GetFriendlyTimeAgo(memory.Created);
        sb.AppendLine($"### [{memory.Type}] {timeAgo}");
        
        // Content preview (first 150 chars)
        var contentPreview = memory.Content.Length > 150 
            ? memory.Content.Substring(0, 147) + "..." 
            : memory.Content;
        sb.AppendLine(contentPreview);
        
        // Key fields
        var status = memory.GetField<string>("status");
        var priority = memory.GetField<string>("priority");
        var category = memory.GetField<string>("category");
        
        var fields = new List<string>();
        if (!string.IsNullOrEmpty(status)) fields.Add($"Status: {status}");
        if (!string.IsNullOrEmpty(priority)) fields.Add($"Priority: {priority}");
        if (!string.IsNullOrEmpty(category)) fields.Add($"Category: {category}");
        
        if (fields.Any())
        {
            sb.AppendLine($"*{string.Join(" | ", fields)}*");
        }
        
        // Files involved
        if (memory.FilesInvolved.Any())
        {
            var fileList = string.Join(", ", memory.FilesInvolved.Take(3).Select(Path.GetFileName));
            if (memory.FilesInvolved.Length > 3)
            {
                fileList += $" +{memory.FilesInvolved.Length - 3} more";
            }
            sb.AppendLine($"Files: {fileList}");
        }
        
        // Memory ID for reference
        sb.AppendLine($"`{memory.Id}`");
        sb.AppendLine();
        
        return sb.ToString();
    }
    
    private string GetFriendlyTimeAgo(DateTime date)
    {
        var diff = DateTime.UtcNow - date;
        
        if (diff.TotalMinutes < 1) return "just now";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes} minutes ago";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours} hours ago";
        if (diff.TotalDays < 7) return $"{(int)diff.TotalDays} days ago";
        if (diff.TotalDays < 30) return $"{(int)(diff.TotalDays / 7)} weeks ago";
        return date.ToString("MMM dd, yyyy");
    }
}

public class TimelineResult
{
    public bool Success { get; set; }
    public string Timeline { get; set; } = "";
    public int TotalMemories { get; set; }
    public object? DateRange { get; set; }
    public List<TimelineGroup> Groups { get; set; } = new();
}

public class TimelineGroup
{
    public string Period { get; set; } = "";
    public int Count { get; set; }
    public Dictionary<string, int> Types { get; set; } = new();
}