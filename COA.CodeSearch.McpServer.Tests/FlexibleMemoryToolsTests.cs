using System.Text.Json;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Tests.Helpers;
using COA.CodeSearch.McpServer.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace COA.CodeSearch.McpServer.Tests;

public class FlexibleMemoryToolsTests : IDisposable
{
    private readonly Mock<ILogger<FlexibleMemoryService>> _memoryLoggerMock;
    private readonly Mock<ILogger<FlexibleMemoryTools>> _toolsLoggerMock;
    private readonly Mock<IPathResolutionService> _pathResolutionMock;
    private readonly IConfiguration _configuration;
    private readonly FlexibleMemoryService _memoryService;
    private readonly FlexibleMemoryTools _memoryTools;
    private readonly InMemoryTestIndexService _indexService;
    
    public FlexibleMemoryToolsTests()
    {
        _memoryLoggerMock = new Mock<ILogger<FlexibleMemoryService>>();
        _toolsLoggerMock = new Mock<ILogger<FlexibleMemoryTools>>();
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
        
        // Use in-memory index service
        _indexService = new InMemoryTestIndexService();
        _memoryService = new FlexibleMemoryService(_memoryLoggerMock.Object, _configuration, _indexService, _pathResolutionMock.Object);
        _memoryTools = new FlexibleMemoryTools(_toolsLoggerMock.Object, _memoryService, _pathResolutionMock.Object);
    }
    
    public void Dispose()
    {
        // Clean up the in-memory index service
        _indexService?.Dispose();
    }
    
    [Fact]
    public async Task StoreMemoryAsync_Basic_ReturnsSuccess()
    {
        // Arrange
        var type = MemoryTypes.TechnicalDebt;
        var content = "Need to refactor authentication";
        
        // Act
        var result = await _memoryTools.StoreMemoryAsync(type, content);
        
        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.MemoryId);
        Assert.Contains("Successfully stored", result.Message);
    }
    
    [Fact]
    public async Task SearchMemoriesAsync_Basic_ReturnsResults()
    {
        // Arrange - Store some test memories
        await _memoryTools.StoreMemoryAsync(MemoryTypes.TechnicalDebt, "Auth refactoring");
        await _memoryTools.StoreMemoryAsync(MemoryTypes.Question, "Rate limiting approach?");
        await _memoryTools.StoreMemoryAsync(MemoryTypes.DeferredTask, "Upgrade database");
        
        // Wait for indexing
        await Task.Delay(100);
        
        // Act
        var result = await _memoryTools.SearchMemoriesAsync(query: "*");
        
        // Assert
        Assert.Equal(3, result.TotalFound);
        Assert.Equal(3, result.Memories.Count);
        
        // Check facets are generated
        Assert.NotNull(result.FacetCounts);
        Assert.True(result.FacetCounts.ContainsKey("type"));
        Assert.Equal(1, result.FacetCounts["type"][MemoryTypes.TechnicalDebt]);
        Assert.Equal(1, result.FacetCounts["type"][MemoryTypes.Question]);
        Assert.Equal(1, result.FacetCounts["type"][MemoryTypes.DeferredTask]);
    }
    
    [Fact]
    public async Task SearchMemoriesAsync_WithTypeFilter_ReturnsFilteredResults()
    {
        // Arrange
        await _memoryTools.StoreMemoryAsync(MemoryTypes.TechnicalDebt, "Auth bug fix");
        await _memoryTools.StoreMemoryAsync(MemoryTypes.TechnicalDebt, "Performance issue");
        await _memoryTools.StoreMemoryAsync(MemoryTypes.Question, "Best practices?");
        
        await Task.Delay(100);
        
        // Act
        var result = await _memoryTools.SearchMemoriesAsync(
            types: new[] { MemoryTypes.TechnicalDebt });
        
        // Assert
        Assert.Equal(2, result.TotalFound);
        Assert.All(result.Memories, m => Assert.Equal(MemoryTypes.TechnicalDebt, m.Type));
    }
    
    [Fact]
    public async Task UpdateMemoryAsync_ModifiesMemory_ReturnsSuccess()
    {
        // Arrange
        var storeResult = await _memoryTools.StoreMemoryAsync(
            MemoryTypes.TechnicalDebt, "Original description");
        Assert.True(storeResult.Success);
        
        // Act
        var fieldUpdates = new Dictionary<string, JsonElement?>
        {
            [MemoryFields.Status] = JsonDocument.Parse($"\"{MemoryStatus.InProgress}\"").RootElement,
            ["priority"] = JsonDocument.Parse($"\"{MemoryPriority.High}\"").RootElement
        };
        
        var updateResult = await _memoryTools.UpdateMemoryAsync(
            storeResult.MemoryId!, 
            "Updated description",
            fieldUpdates);
        
        // Assert
        Assert.True(updateResult.Success);
        
        // Verify update
        var getResult = await _memoryTools.GetMemoryByIdAsync(storeResult.MemoryId!);
        Assert.True(getResult.Success);
        Assert.Equal("Updated description", getResult.Memory!.Content);
        Assert.Equal(MemoryStatus.InProgress, getResult.Memory.GetField<string>(MemoryFields.Status));
        Assert.Equal(MemoryPriority.High, getResult.Memory.GetField<string>("priority"));
    }
}