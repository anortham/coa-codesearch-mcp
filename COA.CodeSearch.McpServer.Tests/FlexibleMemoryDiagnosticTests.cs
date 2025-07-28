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
using Xunit.Abstractions;

namespace COA.CodeSearch.McpServer.Tests;

public class FlexibleMemoryDiagnosticTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly Mock<ILogger<FlexibleMemoryService>> _loggerMock;
    private readonly InMemoryTestIndexService _indexService;
    private readonly Mock<IPathResolutionService> _pathResolutionMock;
    private readonly IConfiguration _configuration;
    private readonly FlexibleMemoryService _memoryService;
    
    public FlexibleMemoryDiagnosticTests(ITestOutputHelper output)
    {
        _output = output;
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
        
        // Use in-memory index service for testing
        _indexService = new InMemoryTestIndexService();
        
        var errorHandlingMock = new Mock<IErrorHandlingService>();
        var validationMock = new Mock<IMemoryValidationService>();
        
        // Setup validation mock to always return valid
        validationMock.Setup(v => v.ValidateMemory(It.IsAny<FlexibleMemoryEntry>()))
            .Returns(new MemoryValidationResult { IsValid = true });
        validationMock.Setup(v => v.ValidateUpdateRequest(It.IsAny<MemoryUpdateRequest>()))
            .Returns(new MemoryValidationResult { IsValid = true });
        
        // Setup error handling mock to execute the function directly
        errorHandlingMock.Setup(e => e.ExecuteWithErrorHandlingAsync(
                It.IsAny<Func<Task<bool>>>(), 
                It.IsAny<ErrorContext>(), 
                It.IsAny<ErrorSeverity>(), 
                It.IsAny<CancellationToken>()))
            .Returns<Func<Task<bool>>, ErrorContext, ErrorSeverity, CancellationToken>((func, context, severity, ct) => func());
            
        errorHandlingMock.Setup(e => e.ExecuteWithErrorHandlingAsync(
                It.IsAny<Func<Task>>(), 
                It.IsAny<ErrorContext>(), 
                It.IsAny<ErrorSeverity>(), 
                It.IsAny<CancellationToken>()))
            .Returns<Func<Task>, ErrorContext, ErrorSeverity, CancellationToken>((func, context, severity, ct) => func());
        
        // Create MemoryAnalyzer mock
        var memoryAnalyzerLoggerMock = new Mock<ILogger<MemoryAnalyzer>>();
        var memoryAnalyzer = new MemoryAnalyzer(memoryAnalyzerLoggerMock.Object);
        
        _memoryService = new FlexibleMemoryService(_loggerMock.Object, _configuration, _indexService, _pathResolutionMock.Object, errorHandlingMock.Object, validationMock.Object, memoryAnalyzer);
    }
    
    public void Dispose()
    {
        // Clean up the in-memory index service
        _indexService?.Dispose();
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