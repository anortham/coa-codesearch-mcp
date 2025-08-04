using System.Security.Cryptography;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Generates time-based checkpoint IDs similar to MongoDB ObjectIds
/// Format: CHECKPOINT-{timestamp}-{random}
/// Where timestamp is Unix milliseconds (sortable) and random ensures uniqueness
/// </summary>
public static class CheckpointIdGenerator
{
    private static readonly object _lock = new();
    private static int _counter = RandomNumberGenerator.GetInt32(0, 0xFFFFFF);
    
    /// <summary>
    /// Generate a new checkpoint ID that is time-sortable and unique
    /// </summary>
    public static string GenerateId()
    {
        lock (_lock)
        {
            // Get current Unix timestamp in milliseconds (more precision than seconds)
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            
            // Increment counter and wrap around at 24 bits (like MongoDB)
            _counter = (_counter + 1) & 0xFFFFFF;
            
            // Format: CHECKPOINT-{timestamp}-{counter:X6}
            // The X6 formats the counter as 6-digit hex (24 bits)
            return $"CHECKPOINT-{timestamp:D13}-{_counter:X6}";
        }
    }
    
    /// <summary>
    /// Extract the creation time from a checkpoint ID
    /// </summary>
    public static DateTime? ExtractTimestamp(string checkpointId)
    {
        if (string.IsNullOrEmpty(checkpointId) || !checkpointId.StartsWith("CHECKPOINT-"))
            return null;
            
        var parts = checkpointId.Split('-');
        if (parts.Length != 3)
            return null;
            
        if (long.TryParse(parts[1], out var timestamp))
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(timestamp).UtcDateTime;
        }
        
        return null;
    }
    
    /// <summary>
    /// Compare two checkpoint IDs for sorting (newer first)
    /// </summary>
    public static int CompareIds(string id1, string id2)
    {
        var time1 = ExtractTimestamp(id1);
        var time2 = ExtractTimestamp(id2);
        
        if (!time1.HasValue || !time2.HasValue)
            return string.Compare(id1, id2, StringComparison.Ordinal);
            
        // Reverse comparison for descending order (newest first)
        return time2.Value.CompareTo(time1.Value);
    }
}