using System.Text.Json;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
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
    private readonly Mock<ILogger<LuceneIndexService>> _indexLoggerMock;
    private readonly IConfiguration _configuration;
    private readonly FlexibleMemoryService _memoryService;
    private readonly FlexibleMemoryTools _memoryTools;
    private readonly ILuceneIndexService _indexService;
    private readonly string _testBasePath;
    
    public FlexibleMemoryToolsTests()
    {
        _memoryLoggerMock = new Mock<ILogger<FlexibleMemoryService>>();
        _toolsLoggerMock = new Mock<ILogger<FlexibleMemoryTools>>();
        _indexLoggerMock = new Mock<ILogger<LuceneIndexService>>();
        _testBasePath = Path.Combine(Path.GetTempPath(), $"memory_tools_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testBasePath);
        
        Environment.CurrentDirectory = _testBasePath;
        
        var configDict = new Dictionary<string, string?>
        {
            ["LuceneIndex:BasePath"] = ".codesearch/index",
            ["MemoryConfiguration:BasePath"] = ".codesearch",
            ["MemoryConfiguration:MaxSearchResults"] = "50"
        };
        
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();
        
        // Create real services
        var pathService = new PathResolutionService(_configuration);
        _indexService = new LuceneIndexService(_indexLoggerMock.Object, _configuration, pathService);
        _memoryService = new FlexibleMemoryService(_memoryLoggerMock.Object, _configuration, _indexService);
        _memoryTools = new FlexibleMemoryTools(_toolsLoggerMock.Object, _memoryService);
    }
    
    public void Dispose()
    {
        _indexService?.Dispose();
        Thread.Sleep(200);
        
        if (Directory.Exists(_testBasePath))
        {
            try
            {
                Directory.Delete(_testBasePath, true);
            }
            catch (Exception)
            {
                // Ignore cleanup failures
            }
        }
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
    public async Task StoreTechnicalDebtAsync_WithFields_ReturnsSuccess()
    {
        // Arrange
        var description = "Fix security vulnerability in login";
        var status = MemoryStatus.Pending;
        var priority = MemoryPriority.High;
        var files = new[] { "Login.cs", "Auth.cs" };
        var tags = new[] { "security", "urgent" };
        
        // Act
        var result = await _memoryTools.StoreTechnicalDebtAsync(
            description, status, priority, "security", 8, files, tags);
        
        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.MemoryId);
        
        // Verify we can retrieve it
        var getResult = await _memoryTools.GetMemoryByIdAsync(result.MemoryId!);
        Assert.True(getResult.Success);
        Assert.NotNull(getResult.Memory);
        Assert.Equal(MemoryTypes.TechnicalDebt, getResult.Memory.Type);
        Assert.Equal(description, getResult.Memory.Content);
        Assert.Equal(status, getResult.Memory.GetField<string>(MemoryFields.Status));
        Assert.Equal(priority, getResult.Memory.GetField<string>(MemoryFields.Priority));
    }
    
    [Fact]
    public async Task StoreQuestionAsync_WithContext_ReturnsSuccess()
    {
        // Arrange
        var question = "How should we handle rate limiting?";
        var context = "User reported API slowness";
        var files = new[] { "ApiController.cs" };
        var tags = new[] { "performance", "api" };
        
        // Act
        var result = await _memoryTools.StoreQuestionAsync(
            question, context, "open", files, tags);
        
        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.MemoryId);
        
        // Verify we can retrieve it
        var getResult = await _memoryTools.GetMemoryByIdAsync(result.MemoryId!);
        Assert.True(getResult.Success);
        Assert.NotNull(getResult.Memory);
        Assert.Equal(MemoryTypes.Question, getResult.Memory.Type);
        Assert.Equal(question, getResult.Memory.Content);
        Assert.Equal("open", getResult.Memory.GetField<string>(MemoryFields.Status));
        Assert.Equal(context, getResult.Memory.GetField<string>("context"));
    }
    
    [Fact]
    public async Task StoreDeferredTaskAsync_WithDeferredDate_ReturnsSuccess()
    {
        // Arrange
        var task = "Upgrade to .NET 9";
        var reason = "Waiting for LTS release";
        var deferredUntil = DateTime.UtcNow.AddMonths(6);
        
        // Act
        var result = await _memoryTools.StoreDeferredTaskAsync(
            task, reason, deferredUntil, MemoryPriority.Medium);
        
        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.MemoryId);
        
        // Verify we can retrieve it
        var getResult = await _memoryTools.GetMemoryByIdAsync(result.MemoryId!);
        Assert.True(getResult.Success);
        Assert.NotNull(getResult.Memory);
        Assert.Equal(MemoryTypes.DeferredTask, getResult.Memory.Type);
        Assert.Equal(task, getResult.Memory.Content);
        Assert.Equal(MemoryStatus.Deferred, getResult.Memory.GetField<string>(MemoryFields.Status));
        Assert.Equal(reason, getResult.Memory.GetField<string>("reason"));
    }
    
    [Fact]
    public async Task SearchMemoriesAsync_Basic_ReturnsResults()
    {
        // Arrange - Store some test memories
        await _memoryTools.StoreTechnicalDebtAsync("Auth refactoring", MemoryStatus.Pending);
        await _memoryTools.StoreQuestionAsync("Rate limiting approach?", null, "open");
        await _memoryTools.StoreDeferredTaskAsync("Upgrade database", "Waiting for migration window");
        
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
        await _memoryTools.StoreTechnicalDebtAsync("Auth bug fix");
        await _memoryTools.StoreTechnicalDebtAsync("Performance issue");
        await _memoryTools.StoreQuestionAsync("Best practices?");
        
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
        var storeResult = await _memoryTools.StoreTechnicalDebtAsync(
            "Original description", MemoryStatus.Pending);
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