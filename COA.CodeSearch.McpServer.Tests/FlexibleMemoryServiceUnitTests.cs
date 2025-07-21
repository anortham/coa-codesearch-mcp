using System.Text.Json;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Tests.Helpers;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace COA.CodeSearch.McpServer.Tests;

public class FlexibleMemoryServiceUnitTests : IDisposable
{
    private readonly Mock<ILogger<FlexibleMemoryService>> _memoryLoggerMock;
    private readonly InMemoryTestIndexService _indexService;
    private readonly Mock<IPathResolutionService> _pathResolutionMock;
    private readonly IConfiguration _configuration;
    private readonly FlexibleMemoryService _memoryService;
    
    public FlexibleMemoryServiceUnitTests()
    {
        _memoryLoggerMock = new Mock<ILogger<FlexibleMemoryService>>();
        _pathResolutionMock = new Mock<IPathResolutionService>();
        
        // Setup path resolution mocks
        _pathResolutionMock.Setup(x => x.GetProjectMemoryPath())
            .Returns("test-project-memory");
        _pathResolutionMock.Setup(x => x.GetLocalMemoryPath())
            .Returns("test-local-memory");
        _pathResolutionMock.Setup(x => x.GetIndexPath(It.IsAny<string>()))
            .Returns<string>(workspace => $"test-index-{workspace}");
        
        var configDict = new Dictionary<string, string?>
        {
            ["MemoryConfiguration:MaxSearchResults"] = "50"
        };
        
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();
        
        // Use in-memory index service for testing
        _indexService = new InMemoryTestIndexService();
        
        _memoryService = new FlexibleMemoryService(_memoryLoggerMock.Object, _configuration, _indexService, _pathResolutionMock.Object);
    }
    
    public void Dispose()
    {
        // Clean up the in-memory index service
        _indexService?.Dispose();
    }
    
    [Fact]
    public async Task StoreMemoryAsync_ValidMemory_ReturnsTrue()
    {
        // Arrange
        var memory = new FlexibleMemoryEntry
        {
            Id = "test-123",
            Type = MemoryTypes.TechnicalDebt,
            Content = "Refactor authentication",
            IsShared = true
        };
        
        // Act
        var result = await _memoryService.StoreMemoryAsync(memory);
        
        // Assert
        Assert.True(result);
        
        // Verify the memory can be retrieved
        var retrieved = await _memoryService.GetMemoryByIdAsync("test-123");
        Assert.NotNull(retrieved);
        Assert.Equal("test-123", retrieved.Id);
        Assert.Equal(MemoryTypes.TechnicalDebt, retrieved.Type);
        Assert.Equal("Refactor authentication", retrieved.Content);
    }
    
    [Fact]
    public async Task StoreMemoryAsync_WithExtendedFields_ReturnsTrue()
    {
        // Arrange
        var memory = new FlexibleMemoryEntry
        {
            Id = "test-456",
            Type = MemoryTypes.Question,
            Content = "How to handle caching?",
            IsShared = false
        };
        
        memory.SetField(MemoryFields.Status, MemoryStatus.Pending);
        memory.SetField(MemoryFields.Priority, MemoryPriority.High);
        memory.SetField(MemoryFields.Tags, new[] { "performance", "cache" });
        
        // Act
        var result = await _memoryService.StoreMemoryAsync(memory);
        
        // Assert
        Assert.True(result);
        
        // Verify the memory was stored with extended fields
        var retrieved = await _memoryService.GetMemoryByIdAsync("test-456");
        Assert.NotNull(retrieved);
        Assert.Equal("test-456", retrieved.Id);
        Assert.Equal(MemoryTypes.Question, retrieved.Type);
        Assert.Equal("How to handle caching?", retrieved.Content);
        Assert.False(retrieved.IsShared); // Should be in local memory
        
        // Verify extended fields
        Assert.Equal(MemoryStatus.Pending, retrieved.GetField<string>(MemoryFields.Status));
        Assert.Equal(MemoryPriority.High, retrieved.GetField<string>(MemoryFields.Priority));
        var tags = retrieved.GetField<string[]>(MemoryFields.Tags);
        Assert.NotNull(tags);
        Assert.Contains("performance", tags);
        Assert.Contains("cache", tags);
    }
    
    [Fact]
    public async Task SearchMemoriesAsync_EmptyQuery_ReturnsResults()
    {
        // Arrange
        var memory1 = new FlexibleMemoryEntry
        {
            Type = MemoryTypes.TechnicalDebt,
            Content = "Fix bug in login",
            IsShared = true
        };
        
        var memory2 = new FlexibleMemoryEntry
        {
            Type = MemoryTypes.Question,
            Content = "Best practices for testing?",
            IsShared = true
        };
        
        await _memoryService.StoreMemoryAsync(memory1);
        await _memoryService.StoreMemoryAsync(memory2);
        
        // Wait for indexing
        await Task.Delay(100);
        
        // Act
        var searchRequest = new FlexibleMemorySearchRequest();
        var result = await _memoryService.SearchMemoriesAsync(searchRequest);
        
        // Assert
        Assert.True(result.TotalFound >= 2);
        Assert.NotNull(result.FacetCounts);
    }
    
    [Fact]
    public async Task SearchMemoriesAsync_WithTypeFilter_FiltersCorrectly()
    {
        // Arrange
        var techDebt = new FlexibleMemoryEntry
        {
            Type = MemoryTypes.TechnicalDebt,
            Content = "Refactor service layer",
            IsShared = true
        };
        
        var question = new FlexibleMemoryEntry
        {
            Type = MemoryTypes.Question,
            Content = "How to optimize queries?",
            IsShared = true
        };
        
        await _memoryService.StoreMemoryAsync(techDebt);
        await _memoryService.StoreMemoryAsync(question);
        
        // Act
        var searchRequest = new FlexibleMemorySearchRequest
        {
            Types = new[] { MemoryTypes.TechnicalDebt }
        };
        var result = await _memoryService.SearchMemoriesAsync(searchRequest);
        
        // Assert
        Assert.True(result.TotalFound >= 1);
        Assert.All(result.Memories, m => Assert.Equal(MemoryTypes.TechnicalDebt, m.Type));
    }
    
    [Fact]
    public async Task GetMemoryByIdAsync_ExistingMemory_ReturnsMemory()
    {
        // Arrange
        var memory = new FlexibleMemoryEntry
        {
            Id = "get-test-123",
            Type = MemoryTypes.CodePattern,
            Content = "Repository pattern implementation",
            IsShared = true
        };
        
        await _memoryService.StoreMemoryAsync(memory);
        await Task.Delay(100);
        
        // Act
        var result = await _memoryService.GetMemoryByIdAsync("get-test-123");
        
        // Assert
        Assert.NotNull(result);
        Assert.Equal("get-test-123", result.Id);
        Assert.Equal(MemoryTypes.CodePattern, result.Type);
        Assert.Equal("Repository pattern implementation", result.Content);
    }
    
    [Fact]
    public async Task UpdateMemoryAsync_ValidUpdate_ReturnsTrue()
    {
        // Arrange
        var memory = new FlexibleMemoryEntry
        {
            Id = "update-test-123",
            Type = MemoryTypes.TechnicalDebt,
            Content = "Original content",
            IsShared = true
        };
        
        await _memoryService.StoreMemoryAsync(memory);
        await Task.Delay(100);
        
        var updateRequest = new MemoryUpdateRequest
        {
            Id = "update-test-123",
            Content = "Updated content",
            FieldUpdates = new Dictionary<string, JsonElement?>
            {
                [MemoryFields.Status] = JsonDocument.Parse($"\"{MemoryStatus.InProgress}\"").RootElement
            }
        };
        
        // Act
        var result = await _memoryService.UpdateMemoryAsync(updateRequest);
        
        // Assert
        Assert.True(result);
        
        // Verify update
        var updated = await _memoryService.GetMemoryByIdAsync("update-test-123");
        Assert.NotNull(updated);
        Assert.Equal("Updated content", updated.Content);
        Assert.Equal(MemoryStatus.InProgress, updated.GetField<string>(MemoryFields.Status));
    }
    
    [Fact(Skip = "TODO: Fix NumericRangeQuery date filtering - memories are stored but date range query returns 0 results")]
    public async Task ArchiveMemoriesAsync_OldMemories_ReturnsCount()
    {
        // Arrange
        var oldMemory = new FlexibleMemoryEntry
        {
            Type = MemoryTypes.TemporaryNote,
            Content = "Old temporary note",
            Created = DateTime.UtcNow.AddDays(-35),
            Modified = DateTime.UtcNow.AddDays(-35),
            IsShared = false
        };
        
        var recentMemory = new FlexibleMemoryEntry
        {
            Type = MemoryTypes.TemporaryNote,
            Content = "Recent temporary note",
            Created = DateTime.UtcNow.AddDays(-5),
            IsShared = false
        };
        
        await _memoryService.StoreMemoryAsync(oldMemory);
        await _memoryService.StoreMemoryAsync(recentMemory);
        await Task.Delay(100);
        
        // Act
        var archivedCount = await _memoryService.ArchiveMemoriesAsync(
            MemoryTypes.TemporaryNote, 
            TimeSpan.FromDays(30));
        
        // Assert
        Assert.Equal(1, archivedCount);
    }
    
    [Fact]
    public async Task FindSimilarMemoriesAsync_ExistingMemory_ReturnsResults()
    {
        // Arrange
        var sourceMemory = new FlexibleMemoryEntry
        {
            Id = "similar-source",
            Type = MemoryTypes.CodePattern,
            Content = "Repository pattern for data access",
            IsShared = true
        };
        
        var similarMemory = new FlexibleMemoryEntry
        {
            Type = MemoryTypes.CodePattern,
            Content = "Repository pattern implementation example",
            IsShared = true
        };
        
        var differentMemory = new FlexibleMemoryEntry
        {
            Type = MemoryTypes.Question,
            Content = "How to setup logging?",
            IsShared = true
        };
        
        await _memoryService.StoreMemoryAsync(sourceMemory);
        await _memoryService.StoreMemoryAsync(similarMemory);
        await _memoryService.StoreMemoryAsync(differentMemory);
        await Task.Delay(100);
        
        // Act
        var similar = await _memoryService.FindSimilarMemoriesAsync("similar-source", 5);
        
        // Assert
        Assert.NotEmpty(similar);
        // Should not include the source memory itself
        Assert.DoesNotContain(similar, m => m.Id == "similar-source");
    }
}