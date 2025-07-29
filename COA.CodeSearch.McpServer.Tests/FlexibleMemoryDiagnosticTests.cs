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
        
        // MemoryAnalyzer is now managed by LuceneIndexService
        
        var facetingServiceMock = new Mock<MemoryFacetingService>(Mock.Of<ILogger<MemoryFacetingService>>(), Mock.Of<IPathResolutionService>());
        var scoringServiceMock = new Mock<IScoringService>();
        _memoryService = new FlexibleMemoryService(_loggerMock.Object, _configuration, _indexService, _pathResolutionMock.Object, errorHandlingMock.Object, validationMock.Object, facetingServiceMock.Object, scoringServiceMock.Object);
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
        
        // Act - Search by content - try multiple search terms
        var searchRequest = new FlexibleMemorySearchRequest
        {
            Query = "authentication"
        };
        
        // Also try searching for other terms that should be in the document
        var searchRequest2 = new FlexibleMemorySearchRequest
        {
            Query = "refactor"
        };
        
        var searchRequest3 = new FlexibleMemorySearchRequest
        {
            Query = "TechnicalDebt"
        };
        
        _output.WriteLine("Searching for 'authentication'");
        
        // Debug: Check index state before search
        var projectPath = _pathResolutionMock.Object.GetProjectMemoryPath();
        var indexSearcher = await _indexService.GetIndexSearcherAsync(projectPath);
        _output.WriteLine($"Index has {indexSearcher.IndexReader.NumDocs} documents before search");
        
        // Debug: Check what document content is in the index
        for (int i = 0; i < indexSearcher.IndexReader.NumDocs; i++)
        {
            var doc = indexSearcher.Doc(i);
            var content = doc.Get("content");
            var all = doc.Get("_all");
            var type = doc.Get("type");
            var id = doc.Get("id");
            _output.WriteLine($"Doc {i}: id={id}, type={type}, content='{content}', _all='{all}'");
        }
        
        var searchResult = await _memoryService.SearchMemoriesAsync(searchRequest);
        _output.WriteLine($"Found {searchResult.TotalFound} memories for 'authentication'");
        
        var searchResult2 = await _memoryService.SearchMemoriesAsync(searchRequest2);
        _output.WriteLine($"Found {searchResult2.TotalFound} memories for 'refactor'");
        
        var searchResult3 = await _memoryService.SearchMemoriesAsync(searchRequest3);
        _output.WriteLine($"Found {searchResult3.TotalFound} memories for 'TechnicalDebt'");
        
        // Assert - Should find the memory with any of these searches
        _output.WriteLine($"Found {searchResult.TotalFound} memories");
        Assert.True(searchResult.TotalFound > 0 || searchResult2.TotalFound > 0 || searchResult3.TotalFound > 0, 
            "Should find the memory with at least one search term");
        
        if (searchResult.TotalFound > 0)
        {
            Assert.Equal(memory.Id, searchResult.Memories[0].Id);
        }
        else if (searchResult2.TotalFound > 0)
        {
            Assert.Equal(memory.Id, searchResult2.Memories[0].Id);
        }
        else if (searchResult3.TotalFound > 0)
        {
            Assert.Equal(memory.Id, searchResult3.Memories[0].Id);
        }
        
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