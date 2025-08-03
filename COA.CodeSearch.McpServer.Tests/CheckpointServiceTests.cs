using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace COA.CodeSearch.McpServer.Tests;

public class CheckpointServiceTests : IDisposable
{
    private readonly Mock<ILogger<CheckpointService>> _loggerMock;
    private readonly Mock<IPathResolutionService> _pathResolutionMock;
    private readonly Mock<IMemoryService> _memoryServiceMock;
    private readonly CheckpointService _checkpointService;
    private readonly string _testCheckpointIdPath;

    public CheckpointServiceTests()
    {
        _loggerMock = new Mock<ILogger<CheckpointService>>();
        _pathResolutionMock = new Mock<IPathResolutionService>();
        _memoryServiceMock = new Mock<IMemoryService>();
        
        _testCheckpointIdPath = Path.Combine(Path.GetTempPath(), $"test_checkpoint_{Guid.NewGuid()}.id");
        _pathResolutionMock.Setup(x => x.GetCheckpointIdPath()).Returns(_testCheckpointIdPath);
        
        _checkpointService = new CheckpointService(
            _loggerMock.Object,
            _pathResolutionMock.Object,
            _memoryServiceMock.Object);
    }

    [Fact]
    public async Task GetNextCheckpointIdAsync_FirstTime_ReturnsOne()
    {
        // Arrange
        if (File.Exists(_testCheckpointIdPath))
            File.Delete(_testCheckpointIdPath);

        // Act
        var result = await _checkpointService.GetNextCheckpointIdAsync();

        // Assert
        Assert.Equal(1, result);
        Assert.True(File.Exists(_testCheckpointIdPath));
        Assert.Equal("1", File.ReadAllText(_testCheckpointIdPath));
    }

    [Fact]
    public async Task GetNextCheckpointIdAsync_ExistingFile_IncrementsId()
    {
        // Arrange
        File.WriteAllText(_testCheckpointIdPath, "5");

        // Act
        var result = await _checkpointService.GetNextCheckpointIdAsync();

        // Assert
        Assert.Equal(6, result);
        Assert.Equal("6", File.ReadAllText(_testCheckpointIdPath));
    }

    [Fact]
    public async Task StoreCheckpointAsync_Success_ReturnsCheckpointId()
    {
        // Arrange
        if (File.Exists(_testCheckpointIdPath))
            File.Delete(_testCheckpointIdPath);
        
        _memoryServiceMock.Setup(x => x.StoreMemoryAsync(It.IsAny<FlexibleMemoryEntry>()))
            .ReturnsAsync(true);

        // Act
        var result = await _checkpointService.StoreCheckpointAsync("Test checkpoint content", "session123");

        // Assert
        Assert.True(result.Success);
        Assert.Equal("CHECKPOINT-00001", result.CheckpointId);
        Assert.Equal(1, result.SequentialId);
        
        _memoryServiceMock.Verify(x => x.StoreMemoryAsync(It.Is<FlexibleMemoryEntry>(
            entry => entry.Id == "CHECKPOINT-00001" && 
                     entry.Type == "Checkpoint" &&
                     entry.Content.Contains("Test checkpoint content"))), Times.Once);
    }

    [Fact]
    public async Task GetLatestCheckpointAsync_NoCheckpoints_ReturnsNotFound()
    {
        // Arrange
        if (File.Exists(_testCheckpointIdPath))
            File.Delete(_testCheckpointIdPath);

        // Act
        var result = await _checkpointService.GetLatestCheckpointAsync();

        // Assert
        Assert.False(result.Success);
        Assert.Equal("No checkpoints found", result.Message);
    }

    [Fact]
    public async Task GetLatestCheckpointAsync_WithCheckpoint_ReturnsLatest()
    {
        // Arrange
        File.WriteAllText(_testCheckpointIdPath, "3");
        
        var expectedCheckpoint = new FlexibleMemoryEntry
        {
            Id = "CHECKPOINT-00003",
            Type = "Checkpoint",
            Content = "Test checkpoint",
            Created = DateTime.UtcNow
        };
        
        _memoryServiceMock.Setup(x => x.GetMemoryByIdAsync("CHECKPOINT-00003"))
            .ReturnsAsync(expectedCheckpoint);

        // Act
        var result = await _checkpointService.GetLatestCheckpointAsync();

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Checkpoint);
        Assert.Equal("CHECKPOINT-00003", result.Checkpoint.Id);
    }

    [Fact]
    public async Task GetCurrentCheckpointIdAsync_NoFile_ReturnsNull()
    {
        // Arrange
        if (File.Exists(_testCheckpointIdPath))
            File.Delete(_testCheckpointIdPath);

        // Act
        var result = await _checkpointService.GetCurrentCheckpointIdAsync();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetCurrentCheckpointIdAsync_WithFile_ReturnsCurrentId()
    {
        // Arrange
        File.WriteAllText(_testCheckpointIdPath, "7");

        // Act
        var result = await _checkpointService.GetCurrentCheckpointIdAsync();

        // Assert
        Assert.Equal(7, result);
    }

    // Cleanup
    public void Dispose()
    {
        if (File.Exists(_testCheckpointIdPath))
        {
            try
            {
                File.Delete(_testCheckpointIdPath);
            }
            catch { }
        }
    }
}