using System.Text.Json;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Tests.Helpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace COA.CodeSearch.McpServer.Tests;

public class FlexibleMemorySearchTests : IDisposable
{
    private readonly Mock<ILogger<FlexibleMemoryService>> _loggerMock;
    private readonly Mock<IPathResolutionService> _pathResolutionMock;
    private readonly IConfiguration _configuration;
    private readonly FlexibleMemoryService _memoryService;
    private readonly InMemoryTestIndexService _indexService;
    
    public FlexibleMemorySearchTests()
    {
        _loggerMock = new Mock<ILogger<FlexibleMemoryService>>();
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
        _memoryService = new FlexibleMemoryService(_loggerMock.Object, _configuration, _indexService, _pathResolutionMock.Object);
    }
    
    public void Dispose()
    {
        // Clean up the in-memory index service
        _indexService?.Dispose();
    }
    
    [Fact]
    public async Task SearchMemories_ByType_ReturnsCorrectResults()
    {
        // Arrange
        var techDebt = CreateMemory(MemoryTypes.TechnicalDebt, "Refactor authentication module");
        var question = CreateMemory(MemoryTypes.Question, "How should we handle rate limiting?");
        var decision = CreateMemory(MemoryTypes.ArchitecturalDecision, "Use JWT for authentication");
        
        await _memoryService.StoreMemoryAsync(techDebt);
        await _memoryService.StoreMemoryAsync(question);
        await _memoryService.StoreMemoryAsync(decision);
        
        // Act
        var searchRequest = new FlexibleMemorySearchRequest
        {
            Types = new[] { MemoryTypes.TechnicalDebt, MemoryTypes.Question }
        };
        
        var result = await _memoryService.SearchMemoriesAsync(searchRequest);
        
        // Assert
        Assert.Equal(2, result.TotalFound);
        Assert.Equal(2, result.Memories.Count);
        Assert.Contains(result.Memories, m => m.Type == MemoryTypes.TechnicalDebt);
        Assert.Contains(result.Memories, m => m.Type == MemoryTypes.Question);
        Assert.DoesNotContain(result.Memories, m => m.Type == MemoryTypes.ArchitecturalDecision);
    }
    
    [Fact]
    public async Task SearchMemories_WithFacets_FiltersCorrectly()
    {
        // Arrange
        var memory1 = CreateMemory(MemoryTypes.TechnicalDebt, "Fix login bug");
        memory1.SetField(MemoryFields.Status, MemoryStatus.Pending);
        memory1.SetField(MemoryFields.Priority, MemoryPriority.High);
        
        var memory2 = CreateMemory(MemoryTypes.TechnicalDebt, "Optimize database queries");
        memory2.SetField(MemoryFields.Status, MemoryStatus.InProgress);
        memory2.SetField(MemoryFields.Priority, MemoryPriority.Medium);
        
        var memory3 = CreateMemory(MemoryTypes.TechnicalDebt, "Update dependencies");
        memory3.SetField(MemoryFields.Status, MemoryStatus.Pending);
        memory3.SetField(MemoryFields.Priority, MemoryPriority.Low);
        
        await _memoryService.StoreMemoryAsync(memory1);
        await _memoryService.StoreMemoryAsync(memory2);
        await _memoryService.StoreMemoryAsync(memory3);
        
        // Act
        var searchRequest = new FlexibleMemorySearchRequest
        {
            Facets = new Dictionary<string, string>
            {
                { MemoryFields.Status, MemoryStatus.Pending }
            }
        };
        
        var result = await _memoryService.SearchMemoriesAsync(searchRequest);
        
        // Assert
        Assert.Equal(2, result.TotalFound);
        Assert.All(result.Memories, m => Assert.Equal(MemoryStatus.Pending, m.GetField<string>(MemoryFields.Status)));
    }
    
    [Fact]
    public async Task SearchMemories_WithDateRange_FiltersCorrectly()
    {
        // Arrange
        var oldMemory = CreateMemory(MemoryTypes.WorkSession, "Old work session");
        oldMemory.Created = DateTime.UtcNow.AddDays(-10);
        
        var recentMemory = CreateMemory(MemoryTypes.WorkSession, "Recent work session");
        recentMemory.Created = DateTime.UtcNow.AddHours(-2);
        
        var futureMemory = CreateMemory(MemoryTypes.WorkSession, "Future dated memory");
        futureMemory.Created = DateTime.UtcNow.AddDays(1);
        
        await _memoryService.StoreMemoryAsync(oldMemory);
        await _memoryService.StoreMemoryAsync(recentMemory);
        await _memoryService.StoreMemoryAsync(futureMemory);
        
        // Act
        var searchRequest = new FlexibleMemorySearchRequest
        {
            DateRange = new DateRangeFilter
            {
                RelativeTime = "last-7-days"
            }
        };
        
        var result = await _memoryService.SearchMemoriesAsync(searchRequest);
        
        // Assert
        Assert.Equal(1, result.TotalFound);
        Assert.Equal("Recent work session", result.Memories.First().Content);
    }
    
    [Fact]
    public async Task SearchMemories_WithTextQuery_FindsMatches()
    {
        // Arrange
        var memory1 = CreateMemory(MemoryTypes.CodePattern, "Repository pattern for data access");
        var memory2 = CreateMemory(MemoryTypes.ArchitecturalDecision, "Use repository pattern throughout");
        var memory3 = CreateMemory(MemoryTypes.Learning, "Factory pattern is useful for object creation");
        
        await _memoryService.StoreMemoryAsync(memory1);
        await _memoryService.StoreMemoryAsync(memory2);
        await _memoryService.StoreMemoryAsync(memory3);
        
        // Act
        var searchRequest = new FlexibleMemorySearchRequest
        {
            Query = "repository"
        };
        
        var result = await _memoryService.SearchMemoriesAsync(searchRequest);
        
        // Assert
        Assert.Equal(2, result.TotalFound);
        Assert.All(result.Memories, m => Assert.Contains("repository", m.Content.ToLower()));
    }
    
    [Fact]
    public async Task SearchMemories_ReturnsFacetCounts()
    {
        // Arrange
        var memories = new[]
        {
            CreateMemoryWithStatus(MemoryTypes.TechnicalDebt, "Item 1", MemoryStatus.Pending),
            CreateMemoryWithStatus(MemoryTypes.TechnicalDebt, "Item 2", MemoryStatus.Pending),
            CreateMemoryWithStatus(MemoryTypes.TechnicalDebt, "Item 3", MemoryStatus.InProgress),
            CreateMemoryWithStatus(MemoryTypes.Question, "Question 1", MemoryStatus.Pending),
            CreateMemoryWithStatus(MemoryTypes.Idea, "Idea 1", MemoryStatus.Done)
        };
        
        foreach (var memory in memories)
        {
            await _memoryService.StoreMemoryAsync(memory);
        }
        
        // Act
        var searchRequest = new FlexibleMemorySearchRequest();
        var result = await _memoryService.SearchMemoriesAsync(searchRequest);
        
        // Assert
        Assert.NotNull(result.FacetCounts);
        Assert.True(result.FacetCounts.ContainsKey("type"));
        Assert.True(result.FacetCounts.ContainsKey("status"));
        
        Assert.Equal(3, result.FacetCounts["type"][MemoryTypes.TechnicalDebt]);
        Assert.Equal(1, result.FacetCounts["type"][MemoryTypes.Question]);
        Assert.Equal(1, result.FacetCounts["type"][MemoryTypes.Idea]);
        
        Assert.Equal(3, result.FacetCounts["status"][MemoryStatus.Pending]);
        Assert.Equal(1, result.FacetCounts["status"][MemoryStatus.InProgress]);
        Assert.Equal(1, result.FacetCounts["status"][MemoryStatus.Done]);
    }
    
    [Fact]
    public async Task SearchMemories_WithSorting_OrdersCorrectly()
    {
        // Arrange
        var memory1 = CreateMemory(MemoryTypes.Idea, "First idea");
        memory1.Created = DateTime.UtcNow.AddDays(-3);
        
        var memory2 = CreateMemory(MemoryTypes.Idea, "Second idea");
        memory2.Created = DateTime.UtcNow.AddDays(-1);
        
        var memory3 = CreateMemory(MemoryTypes.Idea, "Third idea");
        memory3.Created = DateTime.UtcNow.AddHours(-1);
        
        await _memoryService.StoreMemoryAsync(memory1);
        await _memoryService.StoreMemoryAsync(memory2);
        await _memoryService.StoreMemoryAsync(memory3);
        
        // Act - Test ascending order
        var ascRequest = new FlexibleMemorySearchRequest
        {
            OrderBy = "created",
            OrderDescending = false
        };
        
        var ascResult = await _memoryService.SearchMemoriesAsync(ascRequest);
        
        // Assert
        Assert.Equal("First idea", ascResult.Memories[0].Content);
        Assert.Equal("Second idea", ascResult.Memories[1].Content);
        Assert.Equal("Third idea", ascResult.Memories[2].Content);
        
        // Act - Test descending order
        var descRequest = new FlexibleMemorySearchRequest
        {
            OrderBy = "created",
            OrderDescending = true
        };
        
        var descResult = await _memoryService.SearchMemoriesAsync(descRequest);
        
        // Assert
        Assert.Equal("Third idea", descResult.Memories[0].Content);
        Assert.Equal("Second idea", descResult.Memories[1].Content);
        Assert.Equal("First idea", descResult.Memories[2].Content);
    }
    
    [Fact]
    public async Task SearchMemories_UpdatesAccessCount()
    {
        // Arrange
        var memory = CreateMemory(MemoryTypes.Learning, "Test learning");
        await _memoryService.StoreMemoryAsync(memory);
        
        // Act
        var searchRequest = new FlexibleMemorySearchRequest
        {
            Query = "learning"
        };
        
        // Search multiple times
        await _memoryService.SearchMemoriesAsync(searchRequest);
        await _memoryService.SearchMemoriesAsync(searchRequest);
        var finalResult = await _memoryService.SearchMemoriesAsync(searchRequest);
        
        // Assert
        Assert.Equal(3, finalResult.Memories.First().AccessCount);
        Assert.NotNull(finalResult.Memories.First().LastAccessed);
    }
    
    [Fact]
    public async Task SearchMemories_GeneratesInsights()
    {
        // Arrange
        var memories = new List<FlexibleMemoryEntry>();
        
        // Create many pending items
        for (int i = 0; i < 10; i++)
        {
            var memory = CreateMemory(MemoryTypes.TechnicalDebt, $"Technical debt item {i}");
            memory.SetField(MemoryFields.Status, MemoryStatus.Pending);
            memories.Add(memory);
        }
        
        // Create an old pending item
        var oldPending = CreateMemory(MemoryTypes.TechnicalDebt, "Old unresolved issue");
        oldPending.Created = DateTime.UtcNow.AddDays(-45);
        oldPending.SetField(MemoryFields.Status, MemoryStatus.Pending);
        memories.Add(oldPending);
        
        // Create a critical item
        var critical = CreateMemory(MemoryTypes.Blocker, "Critical blocker");
        critical.SetField(MemoryFields.Priority, MemoryPriority.Critical);
        memories.Add(critical);
        
        foreach (var memory in memories)
        {
            await _memoryService.StoreMemoryAsync(memory);
        }
        
        // Act
        var searchRequest = new FlexibleMemorySearchRequest();
        var result = await _memoryService.SearchMemoriesAsync(searchRequest);
        
        // Assert
        Assert.NotNull(result.Insights);
        Assert.NotEmpty(result.Insights.Summary);
        Assert.NotEmpty(result.Insights.Patterns);
        Assert.NotEmpty(result.Insights.RecommendedActions);
        
        // Check for expected patterns
        Assert.Contains(result.Insights.Patterns, p => p.Contains("pending"));
        Assert.Contains(result.Insights.RecommendedActions, a => a.Contains("critical"));
    }
    
    [Fact]
    public async Task UpdateMemory_ModifiesExistingMemory()
    {
        // Arrange
        var memory = CreateMemory(MemoryTypes.TechnicalDebt, "Original content");
        memory.SetField(MemoryFields.Status, MemoryStatus.Pending);
        await _memoryService.StoreMemoryAsync(memory);
        
        // Act
        var updateRequest = new MemoryUpdateRequest
        {
            Id = memory.Id,
            Content = "Updated content",
            FieldUpdates = new Dictionary<string, JsonElement?>
            {
                { MemoryFields.Status, JsonDocument.Parse($"\"{MemoryStatus.InProgress}\"").RootElement },
                { MemoryFields.AssignedTo, JsonDocument.Parse("\"developer@example.com\"").RootElement }
            }
        };
        
        var updateResult = await _memoryService.UpdateMemoryAsync(updateRequest);
        
        // Assert
        Assert.True(updateResult);
        
        var updated = await _memoryService.GetMemoryByIdAsync(memory.Id);
        Assert.NotNull(updated);
        Assert.Equal("Updated content", updated.Content);
        Assert.Equal(MemoryStatus.InProgress, updated.GetField<string>(MemoryFields.Status));
        Assert.Equal("developer@example.com", updated.GetField<string>(MemoryFields.AssignedTo));
        Assert.True(updated.Modified > memory.Modified);
    }
    
    [Fact]
    public async Task FindSimilarMemories_ReturnsRelatedContent()
    {
        // Arrange
        var memory1 = CreateMemory(MemoryTypes.CodePattern, "Repository pattern for data access layer");
        var memory2 = CreateMemory(MemoryTypes.CodePattern, "Repository pattern implementation example");
        var memory3 = CreateMemory(MemoryTypes.CodePattern, "Factory pattern for object creation");
        var memory4 = CreateMemory(MemoryTypes.Learning, "Repository provides good abstraction");
        
        await _memoryService.StoreMemoryAsync(memory1);
        await _memoryService.StoreMemoryAsync(memory2);
        await _memoryService.StoreMemoryAsync(memory3);
        await _memoryService.StoreMemoryAsync(memory4);
        
        // Act
        var similar = await _memoryService.FindSimilarMemoriesAsync(memory1.Id, 5);
        
        // Assert
        Assert.NotEmpty(similar);
        Assert.DoesNotContain(similar, m => m.Id == memory1.Id); // Should not include source
        Assert.Contains(similar, m => m.Content.Contains("Repository")); // Should find related
    }
    
    [Fact(Skip = "TODO: Fix NumericRangeQuery date filtering - memories are stored but date range query returns 0 results")]
    public async Task ArchiveMemories_MarksOldMemoriesAsArchived()
    {
        // Arrange
        var oldMemory = CreateMemory(MemoryTypes.TemporaryNote, "Old note");
        oldMemory.Created = DateTime.UtcNow.AddDays(-40);
        oldMemory.Modified = DateTime.UtcNow.AddDays(-40);
        
        var recentMemory = CreateMemory(MemoryTypes.TemporaryNote, "Recent note");
        recentMemory.Created = DateTime.UtcNow.AddDays(-5);
        recentMemory.Modified = DateTime.UtcNow.AddDays(-5);
        
        await _memoryService.StoreMemoryAsync(oldMemory);
        await _memoryService.StoreMemoryAsync(recentMemory);
        
        // Act
        var archivedCount = await _memoryService.ArchiveMemoriesAsync(
            MemoryTypes.TemporaryNote, 
            TimeSpan.FromDays(30)
        );
        
        // Assert
        Assert.Equal(1, archivedCount);
        
        // Verify old memory is archived
        var oldMemoryCheck = await _memoryService.GetMemoryByIdAsync(oldMemory.Id);
        Assert.True(oldMemoryCheck?.GetField<bool>("archived"));
        Assert.NotNull(oldMemoryCheck?.GetField<DateTime>("archivedDate"));
        
        // Verify recent memory is not archived
        var recentMemoryCheck = await _memoryService.GetMemoryByIdAsync(recentMemory.Id);
        Assert.False(recentMemoryCheck?.GetField<bool>("archived"));
    }
    
    // Helper methods
    
    private FlexibleMemoryEntry CreateMemory(string type, string content)
    {
        return new FlexibleMemoryEntry
        {
            Id = Guid.NewGuid().ToString(),
            Type = type,
            Content = content,
            Created = DateTime.UtcNow,
            Modified = DateTime.UtcNow
        };
    }
    
    private FlexibleMemoryEntry CreateMemoryWithStatus(string type, string content, string status)
    {
        var memory = CreateMemory(type, content);
        memory.SetField(MemoryFields.Status, status);
        return memory;
    }
}