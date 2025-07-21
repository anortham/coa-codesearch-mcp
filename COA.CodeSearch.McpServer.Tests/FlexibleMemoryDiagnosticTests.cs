using System.Text.Json;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace COA.CodeSearch.McpServer.Tests;

public class FlexibleMemoryDiagnosticTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly Mock<ILogger<FlexibleMemoryService>> _loggerMock;
    private readonly Mock<ILogger<LuceneIndexService>> _indexLoggerMock;
    private readonly Mock<IPathResolutionService> _pathResolutionMock;
    private readonly IConfiguration _configuration;
    private readonly FlexibleMemoryService _memoryService;
    private readonly ILuceneIndexService _indexService;
    private readonly string _testBasePath;
    
    public FlexibleMemoryDiagnosticTests(ITestOutputHelper output)
    {
        _output = output;
        _loggerMock = new Mock<ILogger<FlexibleMemoryService>>();
        _indexLoggerMock = new Mock<ILogger<LuceneIndexService>>();
        _pathResolutionMock = new Mock<IPathResolutionService>();
        _testBasePath = Path.Combine(Path.GetTempPath(), $"memory_diag_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testBasePath);
        
        Environment.CurrentDirectory = _testBasePath;
        
        // Setup path resolution mocks
        _pathResolutionMock.Setup(x => x.GetProjectMemoryPath())
            .Returns(Path.Combine(_testBasePath, "test-project-memory"));
        _pathResolutionMock.Setup(x => x.GetLocalMemoryPath())
            .Returns(Path.Combine(_testBasePath, "test-local-memory"));
        
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
        _indexService = new LuceneIndexService(_indexLoggerMock.Object, _configuration, _pathResolutionMock.Object);
        _memoryService = new FlexibleMemoryService(_loggerMock.Object, _configuration, _indexService, _pathResolutionMock.Object);
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
            catch (Exception ex)
            {
                _output.WriteLine($"Failed to clean up test directory: {ex.Message}");
            }
        }
    }
    
    [Fact]
    public async Task BasicStoreAndSearch_Works()
    {
        // Arrange
        var memory = new FlexibleMemoryEntry
        {
            Id = "test-123",
            Type = MemoryTypes.TechnicalDebt,
            Content = "Need to refactor the authentication module",
            IsShared = true
        };
        
        _output.WriteLine($"Storing memory: {memory.Id} - {memory.Content}");
        
        // Act - Store
        var storeResult = await _memoryService.StoreMemoryAsync(memory);
        
        // Assert - Store succeeded
        Assert.True(storeResult, "Store should succeed");
        _output.WriteLine("Store succeeded");
        
        // Wait a moment for index to be ready
        await Task.Delay(100);
        
        // Act - Search by content
        var searchRequest = new FlexibleMemorySearchRequest
        {
            Query = "authentication"
        };
        
        _output.WriteLine("Searching for 'authentication'");
        var searchResult = await _memoryService.SearchMemoriesAsync(searchRequest);
        
        // Assert - Should find the memory
        _output.WriteLine($"Found {searchResult.TotalFound} memories");
        Assert.Equal(1, searchResult.TotalFound);
        Assert.Single(searchResult.Memories);
        Assert.Equal(memory.Id, searchResult.Memories[0].Id);
        
        // Act - Search by type
        searchRequest = new FlexibleMemorySearchRequest
        {
            Types = new[] { MemoryTypes.TechnicalDebt }
        };
        
        _output.WriteLine($"Searching for type {MemoryTypes.TechnicalDebt}");
        searchResult = await _memoryService.SearchMemoriesAsync(searchRequest);
        
        // Assert - Should find the memory
        _output.WriteLine($"Found {searchResult.TotalFound} memories by type");
        Assert.Equal(1, searchResult.TotalFound);
    }
    
    [Fact]
    public async Task StoreMultipleAndSearchAll_Works()
    {
        // Arrange
        var memories = new[]
        {
            new FlexibleMemoryEntry
            {
                Id = "mem-1",
                Type = MemoryTypes.TechnicalDebt,
                Content = "Refactor user service",
                IsShared = true
            },
            new FlexibleMemoryEntry
            {
                Id = "mem-2",
                Type = MemoryTypes.Question,
                Content = "How should we handle rate limiting?",
                IsShared = true
            },
            new FlexibleMemoryEntry
            {
                Id = "mem-3",
                Type = MemoryTypes.TechnicalDebt,
                Content = "Update logging framework",
                IsShared = false // Local memory
            }
        };
        
        // Act - Store all
        foreach (var memory in memories)
        {
            var result = await _memoryService.StoreMemoryAsync(memory);
            Assert.True(result, $"Failed to store {memory.Id}");
            _output.WriteLine($"Stored {memory.Id}");
        }
        
        // Wait for indexing
        await Task.Delay(100);
        
        // Act - Search all
        var searchRequest = new FlexibleMemorySearchRequest
        {
            Query = "*" // Match all
        };
        
        var searchResult = await _memoryService.SearchMemoriesAsync(searchRequest);
        
        // Assert
        _output.WriteLine($"Found {searchResult.TotalFound} total memories");
        Assert.Equal(3, searchResult.TotalFound);
        
        // Act - Search only TechnicalDebt
        searchRequest = new FlexibleMemorySearchRequest
        {
            Types = new[] { MemoryTypes.TechnicalDebt }
        };
        
        searchResult = await _memoryService.SearchMemoriesAsync(searchRequest);
        _output.WriteLine($"Found {searchResult.TotalFound} TechnicalDebt memories");
        Assert.Equal(2, searchResult.TotalFound);
    }
}