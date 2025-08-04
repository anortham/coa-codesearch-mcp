using COA.CodeSearch.McpServer.Services;
using Xunit;

namespace COA.CodeSearch.McpServer.Tests;

public class CheckpointIdGeneratorTests
{
    [Fact]
    public void GenerateId_ReturnsValidFormat()
    {
        // Act
        var id = CheckpointIdGenerator.GenerateId();
        
        // Assert
        Assert.NotNull(id);
        Assert.StartsWith("CHECKPOINT-", id);
        
        var parts = id.Split('-');
        Assert.Equal(3, parts.Length);
        Assert.Equal("CHECKPOINT", parts[0]);
        Assert.True(long.TryParse(parts[1], out var timestamp));
        Assert.True(timestamp > 0);
        Assert.Equal(6, parts[2].Length); // 6-digit hex
    }
    
    [Fact]
    public void GenerateId_GeneratesUniqueIds()
    {
        // Act
        var id1 = CheckpointIdGenerator.GenerateId();
        var id2 = CheckpointIdGenerator.GenerateId();
        
        // Assert
        Assert.NotEqual(id1, id2);
    }
    
    [Fact]
    public void ExtractTimestamp_ValidId_ReturnsTimestamp()
    {
        // Arrange
        var beforeGeneration = DateTime.UtcNow;
        var id = CheckpointIdGenerator.GenerateId();
        var afterGeneration = DateTime.UtcNow;
        
        // Act
        var extractedTime = CheckpointIdGenerator.ExtractTimestamp(id);
        
        // Assert
        Assert.NotNull(extractedTime);
        Assert.True(extractedTime >= beforeGeneration.AddSeconds(-1));
        Assert.True(extractedTime <= afterGeneration.AddSeconds(1));
    }
    
    [Fact]
    public void ExtractTimestamp_InvalidId_ReturnsNull()
    {
        // Act & Assert
        Assert.Null(CheckpointIdGenerator.ExtractTimestamp(null!));
        Assert.Null(CheckpointIdGenerator.ExtractTimestamp(""));
        Assert.Null(CheckpointIdGenerator.ExtractTimestamp("INVALID"));
        Assert.Null(CheckpointIdGenerator.ExtractTimestamp("CHECKPOINT-INVALID-123456"));
    }
    
    [Fact]
    public void CompareIds_SortsNewerFirst()
    {
        // Arrange
        var id1 = "CHECKPOINT-1754271000000-000001"; // Older
        var id2 = "CHECKPOINT-1754271060000-000002"; // Newer (60 seconds later)
        
        // Act
        var result = CheckpointIdGenerator.CompareIds(id1, id2);
        
        // Assert
        Assert.True(result > 0); // id2 should come before id1 (newer first)
    }
    
    [Fact]
    public void GenerateId_IsSortable()
    {
        // Arrange
        var ids = new List<string>();
        
        // Act - Generate IDs with small delays
        for (int i = 0; i < 5; i++)
        {
            ids.Add(CheckpointIdGenerator.GenerateId());
            Thread.Sleep(10); // Small delay to ensure different timestamps
        }
        
        // Sort by ID string (lexicographic)
        var sortedIds = ids.OrderByDescending(id => id).ToList();
        
        // Assert - Latest generated should be first
        Assert.Equal(ids.Last(), sortedIds.First());
    }
}