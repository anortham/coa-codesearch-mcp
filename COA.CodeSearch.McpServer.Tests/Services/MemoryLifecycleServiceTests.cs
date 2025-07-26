using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Reflection;
using Xunit;

namespace COA.CodeSearch.McpServer.Tests.Services;

public class MemoryLifecycleServiceTests
{
    private readonly Mock<ILogger<MemoryLifecycleService>> _mockLogger;
    private readonly Mock<IMemoryService> _mockMemoryService;
    private readonly IOptions<MemoryLifecycleOptions> _options;
    private readonly MemoryLifecycleService _service;
    
    public MemoryLifecycleServiceTests()
    {
        _mockLogger = new Mock<ILogger<MemoryLifecycleService>>();
        _mockMemoryService = new Mock<IMemoryService>();
        
        var options = new MemoryLifecycleOptions
        {
            AutoResolveThreshold = 0.8f,
            PendingResolutionThreshold = 0.5f,
            CheckIntervalHours = 24,
            StaleAfterDays = 30,
            ArchiveAfterDays = 90,
            ConfidenceWeights = new ConfidenceWeights
            {
                MemoryTypeWeight = 0.25f,
                FileRelevanceWeight = 0.20f,
                ChangeTypeWeight = 0.15f,
                AgeWeight = 0.15f,
                StatusWeight = 0.15f,
                ContentAnalysisWeight = 0.10f
            }
        };
        _options = Options.Create(options);
        
        _service = new MemoryLifecycleService(_mockLogger.Object, _mockMemoryService.Object, _options);
    }
    
    [Fact]
    public async Task OnFileChangedAsync_ShouldSkipProcessing_WhenFileIsInCodeSearchDirectory()
    {
        // Arrange
        var changeEvent = new MemoryLifecycleFileChangeEvent
        {
            FilePath = @"C:\Users\test\.codesearch\project-memory\memories.idx",
            ChangeType = MemoryLifecycleFileChangeType.Modified,
            Timestamp = DateTime.UtcNow
        };
        
        // Act
        await _service.OnFileChangedAsync(changeEvent);
        
        // Assert
        // Should not call SearchMemoriesAsync since we skip processing
        _mockMemoryService.Verify(
            x => x.SearchMemoriesAsync(It.IsAny<FlexibleMemorySearchRequest>()), 
            Times.Never,
            "Should not search memories when file is in .codesearch directory");
    }
    
    [Fact]
    public async Task OnFileChangedAsync_ShouldProcessNormally_WhenFileIsNotInCodeSearchDirectory()
    {
        // Arrange
        var changeEvent = new MemoryLifecycleFileChangeEvent
        {
            FilePath = @"C:\Users\test\MyProject\MyFile.cs",
            ChangeType = MemoryLifecycleFileChangeType.Modified,
            Timestamp = DateTime.UtcNow
        };
        
        var searchResult = new FlexibleMemorySearchResult
        {
            Memories = new List<FlexibleMemoryEntry>()
        };
        
        _mockMemoryService.Setup(x => x.SearchMemoriesAsync(It.IsAny<FlexibleMemorySearchRequest>()))
            .ReturnsAsync(searchResult);
        
        // Act
        await _service.OnFileChangedAsync(changeEvent);
        
        // Assert
        _mockMemoryService.Verify(
            x => x.SearchMemoriesAsync(It.IsAny<FlexibleMemorySearchRequest>()), 
            Times.Once,
            "Should search memories when file is not in .codesearch directory");
    }
    
    [Fact]
    public async Task CreatePendingResolutionAsync_ShouldSkip_WhenMemoryIsPendingResolution()
    {
        // Arrange
        var pendingResolutionMemory = new FlexibleMemoryEntry
        {
            Id = "pr-123",
            Type = "PendingResolution",
            Content = "Pending resolution for another memory",
            FilesInvolved = new[] { @"C:\project\test.cs" },
            Created = DateTime.UtcNow
        };
        
        var changeEvent = new MemoryLifecycleFileChangeEvent
        {
            FilePath = @"C:\project\test.cs",
            ChangeType = MemoryLifecycleFileChangeType.Modified,
            Timestamp = DateTime.UtcNow
        };
        
        var searchResult = new FlexibleMemorySearchResult
        {
            Memories = new List<FlexibleMemoryEntry> { pendingResolutionMemory }
        };
        
        _mockMemoryService.Setup(x => x.SearchMemoriesAsync(It.IsAny<FlexibleMemorySearchRequest>()))
            .ReturnsAsync(searchResult);
        
        // Act
        await _service.OnFileChangedAsync(changeEvent);
        
        // Assert - Should never store a new memory
        _mockMemoryService.Verify(
            x => x.StoreMemoryAsync(It.IsAny<FlexibleMemoryEntry>()), 
            Times.Never,
            "Should not create PendingResolution for a PendingResolution memory");
    }
    
    [Fact]
    public async Task CircuitBreaker_ShouldPreventRapidPendingResolutions()
    {
        // Arrange - a memory that triggers medium confidence (creates PendingResolution)
        var memory = new FlexibleMemoryEntry
        {
            Id = "test-456",
            Type = "Question",
            Content = "How does authentication work?",
            Created = DateTime.UtcNow.AddDays(-10),
            FilesInvolved = new[] { @"C:\project\auth.cs" }
        };
        memory.SetField("status", "pending");
        
        var changeEvent = new MemoryLifecycleFileChangeEvent
        {
            FilePath = @"C:\project\auth.cs",
            ChangeType = MemoryLifecycleFileChangeType.Modified,
            Timestamp = DateTime.UtcNow
        };
        
        var searchResult = new FlexibleMemorySearchResult
        {
            Memories = new List<FlexibleMemoryEntry> { memory }
        };
        
        _mockMemoryService.Setup(x => x.SearchMemoriesAsync(It.IsAny<FlexibleMemorySearchRequest>()))
            .ReturnsAsync(searchResult);
        
        // Act - Trigger file change twice rapidly
        await _service.OnFileChangedAsync(changeEvent);
        await _service.OnFileChangedAsync(changeEvent);
        
        // Assert - Should only create one PendingResolution due to circuit breaker
        _mockMemoryService.Verify(
            x => x.StoreMemoryAsync(It.Is<FlexibleMemoryEntry>(m => m.Type == "PendingResolution")), 
            Times.Once,
            "Circuit breaker should prevent multiple PendingResolutions within 1 minute");
    }
    
    [Theory]
    [InlineData("TechnicalDebt", 0.9f)]
    [InlineData("BugReport", 0.85f)]
    [InlineData("Question", 0.7f)]
    [InlineData("CodePattern", 0.5f)]
    [InlineData("ArchitecturalDecision", 0.3f)]
    [InlineData("SecurityRule", 0.2f)]
    [InlineData("Unknown", 0.5f)]
    public void GetMemoryTypeScore_ReturnsExpectedScores(string memoryType, float expectedScore)
    {
        // Use reflection to access private method
        var method = typeof(MemoryLifecycleService).GetMethod("GetMemoryTypeScore", 
            BindingFlags.NonPublic | BindingFlags.Instance);
        
        var score = (float)method!.Invoke(_service, new object[] { memoryType })!;
        
        Assert.Equal(expectedScore, score);
    }
    
    [Theory]
    [InlineData(MemoryLifecycleFileChangeType.Deleted, 0.9f)]
    [InlineData(MemoryLifecycleFileChangeType.Modified, 0.7f)]
    [InlineData(MemoryLifecycleFileChangeType.Created, 0.5f)]
    [InlineData(MemoryLifecycleFileChangeType.Renamed, 0.4f)]
    public void GetChangeTypeScore_ReturnsExpectedScores(MemoryLifecycleFileChangeType changeType, float expectedScore)
    {
        var method = typeof(MemoryLifecycleService).GetMethod("GetChangeTypeScore", 
            BindingFlags.NonPublic | BindingFlags.Instance);
        
        var score = (float)method!.Invoke(_service, new object[] { changeType })!;
        
        Assert.Equal(expectedScore, score);
    }
    
    [Theory]
    [InlineData(1, 0.3f)]    // 1 day old - recent
    [InlineData(14, 0.5f)]   // 2 weeks old - medium
    [InlineData(45, 0.7f)]   // 1.5 months old - older
    [InlineData(120, 0.9f)]  // 4 months old - very old
    public void CalculateAgeScore_ReturnsExpectedScores(int daysOld, float expectedScore)
    {
        var created = DateTime.UtcNow.AddDays(-daysOld);
        
        var method = typeof(MemoryLifecycleService).GetMethod("CalculateAgeScore", 
            BindingFlags.NonPublic | BindingFlags.Instance);
        
        var score = (float)method!.Invoke(_service, new object[] { created })!;
        
        Assert.Equal(expectedScore, score);
    }
    
    [Fact]
    public void CalculateFileRelevance_DirectMatch_ReturnsMaxScore()
    {
        var memory = new FlexibleMemoryEntry
        {
            FilesInvolved = new[] { @"C:\project\src\Service.cs", @"C:\project\src\Model.cs" }
        };
        
        var method = typeof(MemoryLifecycleService).GetMethod("CalculateFileRelevance", 
            BindingFlags.NonPublic | BindingFlags.Instance);
        
        var score = (float)method!.Invoke(_service, new object[] { memory, @"C:\project\src\Service.cs" })!;
        
        Assert.Equal(1.0f, score);
    }
    
    [Fact]
    public void CalculateFileRelevance_SameDirectory_ReturnsMediumScore()
    {
        var memory = new FlexibleMemoryEntry
        {
            FilesInvolved = new[] { @"C:\project\src\Service.cs" }
        };
        
        var method = typeof(MemoryLifecycleService).GetMethod("CalculateFileRelevance", 
            BindingFlags.NonPublic | BindingFlags.Instance);
        
        var score = (float)method!.Invoke(_service, new object[] { memory, @"C:\project\src\Model.cs" })!;
        
        Assert.Equal(0.7f, score);
    }
    
    [Fact]
    public void CalculateFileRelevance_NoFiles_ReturnsLowScore()
    {
        var memory = new FlexibleMemoryEntry
        {
            FilesInvolved = Array.Empty<string>()
        };
        
        var method = typeof(MemoryLifecycleService).GetMethod("CalculateFileRelevance", 
            BindingFlags.NonPublic | BindingFlags.Instance);
        
        var score = (float)method!.Invoke(_service, new object[] { memory, @"C:\project\src\Service.cs" })!;
        
        Assert.Equal(0.1f, score);
    }
    
    [Theory]
    [InlineData("pending", 0.8f)]
    [InlineData("in_progress", 0.6f)]
    [InlineData("blocked", 0.4f)]
    [InlineData("resolved", 0.1f)]
    [InlineData("unknown", 0.5f)]
    [InlineData(null, 0.5f)]
    public void GetStatusScore_ReturnsExpectedScores(string? status, float expectedScore)
    {
        var memory = new FlexibleMemoryEntry();
        if (status != null)
        {
            memory.SetField("status", status);
        }
        
        var method = typeof(MemoryLifecycleService).GetMethod("GetStatusScore", 
            BindingFlags.NonPublic | BindingFlags.Instance);
        
        var score = (float)method!.Invoke(_service, new object[] { memory })!;
        
        Assert.Equal(expectedScore, score);
    }
    
    [Fact]
    public void CalculateResolutionConfidence_HighConfidenceScenario()
    {
        // Arrange - a technical debt memory that should auto-resolve
        var memory = new FlexibleMemoryEntry
        {
            Id = "test-123",
            Type = "TechnicalDebt",
            Content = "TODO: Fix the broken authentication method in UserService",
            Created = DateTime.UtcNow.AddDays(-45), // Old memory
            FilesInvolved = new[] { @"C:\project\src\Services\UserService.cs" }
        };
        memory.SetField("status", "pending");
        
        var changeEvent = new MemoryLifecycleFileChangeEvent
        {
            FilePath = @"C:\project\src\Services\UserService.cs",
            ChangeType = MemoryLifecycleFileChangeType.Modified,
            Timestamp = DateTime.UtcNow
        };
        
        // Act
        var method = typeof(MemoryLifecycleService).GetMethod("CalculateResolutionConfidence", 
            BindingFlags.NonPublic | BindingFlags.Instance);
        
        var confidence = (float)method!.Invoke(_service, new object[] { memory, changeEvent })!;
        
        // Assert
        Assert.True(confidence > 0.8f, $"Expected confidence > 0.8, but got {confidence}");
    }
    
    [Fact]
    public void CalculateResolutionConfidence_LowConfidenceScenario()
    {
        // Arrange - an architectural decision that should not auto-resolve
        var memory = new FlexibleMemoryEntry
        {
            Id = "test-456",
            Type = "ArchitecturalDecision",
            Content = "Decided to use Repository pattern for data access",
            Created = DateTime.UtcNow.AddDays(-5), // Recent memory
            FilesInvolved = new[] { @"C:\project\docs\architecture.md" }
        };
        memory.SetField("status", "approved");
        
        var changeEvent = new MemoryLifecycleFileChangeEvent
        {
            FilePath = @"C:\project\src\unrelated\OtherFile.cs",
            ChangeType = MemoryLifecycleFileChangeType.Created,
            Timestamp = DateTime.UtcNow
        };
        
        // Act
        var method = typeof(MemoryLifecycleService).GetMethod("CalculateResolutionConfidence", 
            BindingFlags.NonPublic | BindingFlags.Instance);
        
        var confidence = (float)method!.Invoke(_service, new object[] { memory, changeEvent })!;
        
        // Assert
        Assert.True(confidence < 0.5f, $"Expected confidence < 0.5, but got {confidence}");
    }
    
    [Fact]
    public void AnalyzeMemoryContent_WithResolutionKeywords_IncreasesScore()
    {
        var memoryWithKeywords = new FlexibleMemoryEntry
        {
            Content = "TODO: Fix the bug in the UserService authentication method"
        };
        
        var memoryWithoutKeywords = new FlexibleMemoryEntry
        {
            Content = "Consider using dependency injection for better testability"
        };
        
        var changeEvent = new MemoryLifecycleFileChangeEvent
        {
            FilePath = @"C:\project\src\UserService.cs",
            ChangeType = MemoryLifecycleFileChangeType.Modified
        };
        
        var method = typeof(MemoryLifecycleService).GetMethod("AnalyzeMemoryContent", 
            BindingFlags.NonPublic | BindingFlags.Instance);
        
        var scoreWithKeywords = (float)method!.Invoke(_service, new object[] { memoryWithKeywords, changeEvent })!;
        var scoreWithoutKeywords = (float)method!.Invoke(_service, new object[] { memoryWithoutKeywords, changeEvent })!;
        
        Assert.True(scoreWithKeywords > scoreWithoutKeywords, 
            $"Expected score with keywords ({scoreWithKeywords}) > score without ({scoreWithoutKeywords})");
    }
    
    [Fact]
    public async Task OnFileChangedAsync_FindsAndProcessesRelatedMemories()
    {
        // Arrange
        var changeEvent = new MemoryLifecycleFileChangeEvent
        {
            FilePath = @"C:\project\src\Service.cs",
            ChangeType = MemoryLifecycleFileChangeType.Modified,
            Timestamp = DateTime.UtcNow
        };
        
        var searchResult = new FlexibleMemorySearchResult
        {
            TotalFound = 1,
            Memories = new List<FlexibleMemoryEntry>
            {
                CreateTestMemory("test-789", "TechnicalDebt", "Refactor this service", 
                    DateTime.UtcNow.AddDays(-30), new[] { changeEvent.FilePath }, "pending")
            }
        };
        
        _mockMemoryService.Setup(s => s.SearchMemoriesAsync(It.IsAny<FlexibleMemorySearchRequest>()))
            .ReturnsAsync(searchResult);
        
        _mockMemoryService.Setup(s => s.UpdateMemoryAsync(It.IsAny<MemoryUpdateRequest>()))
            .ReturnsAsync(true);
        
        // Act
        await _service.OnFileChangedAsync(changeEvent);
        
        // Assert
        _mockMemoryService.Verify(s => s.SearchMemoriesAsync(
            It.Is<FlexibleMemorySearchRequest>(r => r.Query == "*" && r.MaxResults == 1000)), 
            Times.Once);
    }
    
    private static FlexibleMemoryEntry CreateTestMemory(string id, string type, string content, 
        DateTime created, string[] files, string? status = null)
    {
        var memory = new FlexibleMemoryEntry
        {
            Id = id,
            Type = type,
            Content = content,
            Created = created,
            FilesInvolved = files,
            Modified = created,
            IsShared = true,
            AccessCount = 0
        };
        
        if (status != null)
        {
            memory.SetField("status", status);
        }
        
        return memory;
    }
}