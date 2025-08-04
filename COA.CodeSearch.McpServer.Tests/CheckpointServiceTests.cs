using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace COA.CodeSearch.McpServer.Tests;

public class CheckpointServiceTests
{
    private readonly Mock<ILogger<CheckpointService>> _loggerMock;
    private readonly Mock<IMemoryService> _memoryServiceMock;
    private readonly CheckpointService _checkpointService;

    public CheckpointServiceTests()
    {
        _loggerMock = new Mock<ILogger<CheckpointService>>();
        _memoryServiceMock = new Mock<IMemoryService>();
        
        _checkpointService = new CheckpointService(
            _loggerMock.Object,
            _memoryServiceMock.Object);
    }

    [Fact]
    public async Task StoreCheckpointAsync_Success_ReturnsTimeBasedId()
    {
        // Arrange
        _memoryServiceMock.Setup(x => x.StoreMemoryAsync(It.IsAny<FlexibleMemoryEntry>()))
            .ReturnsAsync(true);

        // Act
        var result = await _checkpointService.StoreCheckpointAsync("Test checkpoint content", "session123");

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.CheckpointId);
        Assert.StartsWith("CHECKPOINT-", result.CheckpointId);
        
        // Verify the ID format (CHECKPOINT-{timestamp}-{counter})
        var parts = result.CheckpointId.Split('-');
        Assert.Equal(3, parts.Length);
        Assert.True(long.TryParse(parts[1], out _)); // Timestamp should be parseable
        
        _memoryServiceMock.Verify(x => x.StoreMemoryAsync(It.Is<FlexibleMemoryEntry>(
            entry => entry.Id.StartsWith("CHECKPOINT-") && 
                     entry.Type == "WorkSession" &&
                     entry.Content.Contains("Test checkpoint content"))), Times.Once);
    }

    [Fact]
    public async Task GetLatestCheckpointAsync_NoCheckpoints_ReturnsNotFound()
    {
        // Arrange
        var emptySearchResult = new FlexibleMemorySearchResult
        {
            Memories = new List<FlexibleMemoryEntry>(),
            TotalFound = 0
        };
        
        _memoryServiceMock.Setup(x => x.SearchMemoriesAsync(It.IsAny<FlexibleMemorySearchRequest>()))
            .ReturnsAsync(emptySearchResult);

        // Act
        var result = await _checkpointService.GetLatestCheckpointAsync();

        // Assert
        Assert.False(result.Success);
        Assert.Equal("No checkpoints found", result.Message);
    }

    [Fact]
    public async Task GetLatestCheckpointAsync_WithCheckpoints_ReturnsLatest()
    {
        // Arrange
        var checkpoint1 = new FlexibleMemoryEntry
        {
            Id = "CHECKPOINT-1754271000000-000001",
            Type = "WorkSession",
            Content = "First checkpoint",
            Created = DateTime.UtcNow.AddMinutes(-10)
        };
        
        var checkpoint2 = new FlexibleMemoryEntry
        {
            Id = "CHECKPOINT-1754271060000-000002", // 60 seconds later
            Type = "WorkSession",
            Content = "Second checkpoint",
            Created = DateTime.UtcNow.AddMinutes(-5)
        };
        
        var searchResult = new FlexibleMemorySearchResult
        {
            Memories = new List<FlexibleMemoryEntry> { checkpoint1, checkpoint2 },
            TotalFound = 2
        };
        
        _memoryServiceMock.Setup(x => x.SearchMemoriesAsync(It.IsAny<FlexibleMemorySearchRequest>()))
            .ReturnsAsync((FlexibleMemorySearchRequest req) =>
            {
                // Simulate proper sorting by ID descending (latest first)
                var sorted = searchResult.Memories
                    .Where(m => m.Id.StartsWith("CHECKPOINT-"))
                    .OrderByDescending(m => m.Id)
                    .Take(req.MaxResults)
                    .ToList();
                
                return new FlexibleMemorySearchResult
                {
                    Memories = sorted,
                    TotalFound = sorted.Count
                };
            });

        // Act
        var result = await _checkpointService.GetLatestCheckpointAsync();

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Checkpoint);
        Assert.Equal(checkpoint2.Id, result.Checkpoint.Id); // Should return the latest
    }

    [Fact]
    public async Task GetLatestCheckpointAsync_SearchThrows_ReturnsError()
    {
        // Arrange
        _memoryServiceMock.Setup(x => x.SearchMemoriesAsync(It.IsAny<FlexibleMemorySearchRequest>()))
            .ThrowsAsync(new Exception("Search failed"));

        // Act
        var result = await _checkpointService.GetLatestCheckpointAsync();

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Failed to get latest checkpoint", result.Message);
    }

    [Fact]
    public async Task StoreCheckpointAsync_StoreMemoryFails_ReturnsError()
    {
        // Arrange
        _memoryServiceMock.Setup(x => x.StoreMemoryAsync(It.IsAny<FlexibleMemoryEntry>()))
            .ThrowsAsync(new Exception("Store failed"));

        // Act
        var result = await _checkpointService.StoreCheckpointAsync("Test content", "session123");

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Failed to store checkpoint", result.Message);
    }
}