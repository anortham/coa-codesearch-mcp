using System.Security.Cryptography;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Generates time-based IDs similar to MongoDB ObjectIds for any entity type
/// Format: {timestamp}-{random}
/// Where timestamp is Unix milliseconds (sortable) and random ensures uniqueness
/// </summary>
public static class TimestampIdGenerator
{
    private static readonly object _lock = new();
    private static int _counter = RandomNumberGenerator.GetInt32(0, 0xFFFFFF);
    
    /// <summary>
    /// Generate a new timestamp-based ID that is time-sortable and unique
    /// </summary>
    public static string GenerateId()
    {
        lock (_lock)
        {
            // Get current Unix timestamp in milliseconds (more precision than seconds)
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            
            // Increment counter and wrap around at 24 bits (like MongoDB)
            _counter = (_counter + 1) & 0xFFFFFF;
            
            // Format: {timestamp}-{counter:X6}
            // The X6 formats the counter as 6-digit hex (24 bits)
            return $"{timestamp:D13}-{_counter:X6}";
        }
    }
    
    /// <summary>
    /// Generate a timestamp-based ID for a specific DateTime
    /// Useful for migrating existing records
    /// </summary>
    public static string GenerateIdForTimestamp(DateTime utcTime)
    {
        lock (_lock)
        {
            // Convert to Unix timestamp in milliseconds
            var timestamp = new DateTimeOffset(utcTime, TimeSpan.Zero).ToUnixTimeMilliseconds();
            
            // Increment counter for uniqueness even with same timestamp
            _counter = (_counter + 1) & 0xFFFFFF;
            
            return $"{timestamp:D13}-{_counter:X6}";
        }
    }
    
    /// <summary>
    /// Extract the creation time from a timestamp-based ID
    /// </summary>
    public static DateTime? ExtractTimestamp(string id)
    {
        if (string.IsNullOrEmpty(id))
            return null;
            
        // Handle both new format (timestamp-counter) and checkpoint format
        var parts = id.Split('-');
        if (parts.Length < 2)
            return null;
        
        // For checkpoint IDs, timestamp is in second position
        var timestampPart = id.StartsWith("CHECKPOINT-") && parts.Length >= 3 ? parts[1] : parts[0];
            
        if (long.TryParse(timestampPart, out var timestamp))
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(timestamp).UtcDateTime;
        }
        
        return null;
    }
    
    /// <summary>
    /// Check if an ID is a timestamp-based ID
    /// </summary>
    public static bool IsTimestampId(string id)
    {
        if (string.IsNullOrEmpty(id))
            return false;
            
        // Check for new format: 13digits-6hex
        var parts = id.Split('-');
        if (parts.Length == 2 && 
            parts[0].Length == 13 && 
            parts[1].Length == 6 &&
            long.TryParse(parts[0], out _))
        {
            return true;
        }
        
        // Also recognize checkpoint format
        return id.StartsWith("CHECKPOINT-") && parts.Length == 3;
    }
    
    /// <summary>
    /// Compare two IDs for sorting (newer first)
    /// Handles both timestamp IDs and GUIDs
    /// </summary>
    public static int CompareIds(string id1, string id2)
    {
        var time1 = ExtractTimestamp(id1);
        var time2 = ExtractTimestamp(id2);
        
        // Both are timestamp-based
        if (time1.HasValue && time2.HasValue)
        {
            // Reverse comparison for descending order (newest first)
            return time2.Value.CompareTo(time1.Value);
        }
        
        // One or both are not timestamp-based (likely GUIDs)
        // Timestamp IDs should come after GUIDs when sorted descending
        if (time1.HasValue && !time2.HasValue)
            return -1; // id1 (timestamp) comes first
        if (!time1.HasValue && time2.HasValue)
            return 1; // id2 (timestamp) comes first
            
        // Both are non-timestamp (GUIDs), use string comparison
        return string.Compare(id2, id1, StringComparison.Ordinal);
    }
}