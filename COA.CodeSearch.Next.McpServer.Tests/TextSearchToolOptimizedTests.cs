using FluentAssertions;
using NUnit.Framework;
using COA.CodeSearch.Next.McpServer.Tools;
using COA.CodeSearch.Next.McpServer.Models;
using COA.CodeSearch.Next.McpServer.Services.Lucene;
using COA.Mcp.Framework.TokenOptimization;
using COA.Mcp.Framework.TokenOptimization.Caching;
using COA.Mcp.Framework.TokenOptimization.Storage;
using COA.Mcp.Framework.Testing;
using Moq;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.Next.McpServer.Tests;

[TestFixture]
public class TextSearchToolOptimizedTests : ToolTestBase<TextSearchToolOptimized, TextSearchParametersOptimized, TokenOptimizedResult>
{
    private Mock<ILuceneIndexService> _luceneIndexServiceMock = null!;
    private Mock<ITokenEstimator> _tokenEstimatorMock = null!;
    private Mock<IResponseCacheService> _cacheServiceMock = null!;
    private Mock<IResourceStorageService> _storageServiceMock = null!;
    private Mock<ICacheKeyGenerator> _keyGeneratorMock = null!;
    private Mock<ILogger<TextSearchToolOptimized>> _loggerMock = null!;

    protected override TextSearchToolOptimized CreateTool()
    {
        _luceneIndexServiceMock = new Mock<ILuceneIndexService>();
        _tokenEstimatorMock = new Mock<ITokenEstimator>();
        _cacheServiceMock = new Mock<IResponseCacheService>();
        _storageServiceMock = new Mock<IResourceStorageService>();
        _keyGeneratorMock = new Mock<ICacheKeyGenerator>();
        _loggerMock = new Mock<ILogger<TextSearchToolOptimized>>();

        return new TextSearchToolOptimized(
            _luceneIndexServiceMock.Object,
            _tokenEstimatorMock.Object,
            _cacheServiceMock.Object,
            _storageServiceMock.Object,
            _keyGeneratorMock.Object,
            _loggerMock.Object
        );
    }

    [Test]
    public async Task TextSearch_WithValidQuery_ReturnsResults()
    {
        // Arrange
        var parameters = new TextSearchParametersOptimized
        {
            Query = "test query",
            WorkspacePath = @"C:\test\workspace"
        };

        var searchResult = new SearchResult
        {
            TotalHits = 2,
            Hits = new List<SearchHit>
            {
                new SearchHit { FilePath = "file1.cs", Score = 0.9f, Content = "Test content 1" },
                new SearchHit { FilePath = "file2.cs", Score = 0.8f, Content = "Test content 2" }
            }
        };

        _luceneIndexServiceMock
            .Setup(x => x.IndexExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _luceneIndexServiceMock
            .Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<Lucene.Net.Search.Query>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchResult);

        _keyGeneratorMock
            .Setup(x => x.GenerateKey(It.IsAny<string>(), It.IsAny<object>()))
            .Returns("cache-key");

        _cacheServiceMock
            .Setup(x => x.GetAsync<TokenOptimizedResult>(It.IsAny<string>()))
            .ReturnsAsync((TokenOptimizedResult?)null);

        _tokenEstimatorMock
            .Setup(x => x.EstimateObject(It.IsAny<object>()))
            .Returns(100);

        _tokenEstimatorMock
            .Setup(x => x.EstimateCollection(It.IsAny<IEnumerable<SearchHit>>(), It.IsAny<Func<SearchHit, int>>()))
            .Returns(200);

        _tokenEstimatorMock
            .Setup(x => x.EstimateString(It.IsAny<string>()))
            .Returns(10);

        // Act
        var result = await Tool.ExecuteAsync(parameters);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data.Count.Should().Be(2);
        result.Data.Results.Should().NotBeNull();
        result.Insights.Should().NotBeEmpty();
    }

    [Test]
    public async Task TextSearch_WithNoIndex_ReturnsError()
    {
        // Arrange
        var parameters = new TextSearchParametersOptimized
        {
            Query = "test query",
            WorkspacePath = @"C:\test\workspace"
        };

        _luceneIndexServiceMock
            .Setup(x => x.IndexExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _keyGeneratorMock
            .Setup(x => x.GenerateKey(It.IsAny<string>(), It.IsAny<object>()))
            .Returns("cache-key");

        _cacheServiceMock
            .Setup(x => x.GetAsync<TokenOptimizedResult>(It.IsAny<string>()))
            .ReturnsAsync((TokenOptimizedResult?)null);

        // Act
        var result = await Tool.ExecuteAsync(parameters);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Insights.Should().Contain(i => i.Contains("indexed"));
        result.Actions.Should().NotBeEmpty();
        result.Actions.Should().Contain(a => a.Action == ToolNames.IndexWorkspace);
    }

    [Test]
    public async Task TextSearch_WithCachedResult_ReturnsCachedData()
    {
        // Arrange
        var parameters = new TextSearchParametersOptimized
        {
            Query = "cached query",
            WorkspacePath = @"C:\test\workspace",
            NoCache = false
        };

        var cachedResult = new TokenOptimizedResult
        {
            Success = true,
            Data = new AIResponseData
            {
                Summary = "Cached results",
                Count = 5
            },
            Meta = new AIResponseMeta()
        };
        cachedResult.SetOperation(ToolNames.TextSearch);

        _keyGeneratorMock
            .Setup(x => x.GenerateKey(It.IsAny<string>(), It.IsAny<object>()))
            .Returns("cache-key");

        _cacheServiceMock
            .Setup(x => x.GetAsync<TokenOptimizedResult>("cache-key"))
            .ReturnsAsync(cachedResult);

        // Act
        var result = await Tool.ExecuteAsync(parameters);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Data.Summary.Should().Be("Cached results");
        result.Meta.ExtensionData.Should().ContainKey("cacheHit");
        result.Meta.ExtensionData!["cacheHit"].Should().Be(true);
        
        // Verify search was not performed
        _luceneIndexServiceMock.Verify(
            x => x.SearchAsync(It.IsAny<string>(), It.IsAny<Lucene.Net.Search.Query>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Test]
    public async Task TextSearch_WithLargeResults_StoresInResourceStorage()
    {
        // Arrange
        var parameters = new TextSearchParametersOptimized
        {
            Query = "large result set",
            WorkspacePath = @"C:\test\workspace"
        };

        var searchResult = new SearchResult
        {
            TotalHits = 1000,
            Hits = Enumerable.Range(1, 1000).Select(i => new SearchHit
            {
                FilePath = $"file{i}.cs",
                Score = 0.9f,
                Content = $"Content {i}"
            }).ToList()
        };

        _luceneIndexServiceMock
            .Setup(x => x.IndexExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _luceneIndexServiceMock
            .Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<Lucene.Net.Search.Query>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchResult);

        _keyGeneratorMock
            .Setup(x => x.GenerateKey(It.IsAny<string>(), It.IsAny<object>()))
            .Returns("cache-key");

        _cacheServiceMock
            .Setup(x => x.GetAsync<TokenOptimizedResult>(It.IsAny<string>()))
            .ReturnsAsync((TokenOptimizedResult?)null);

        // Make estimation return large value to trigger truncation
        _tokenEstimatorMock
            .Setup(x => x.EstimateCollection(It.IsAny<IEnumerable<SearchHit>>(), It.IsAny<Func<SearchHit, int>>()))
            .Returns(100000); // Way over budget

        _tokenEstimatorMock
            .Setup(x => x.EstimateObject(It.IsAny<object>()))
            .Returns(100);

        _tokenEstimatorMock
            .Setup(x => x.EstimateString(It.IsAny<string>()))
            .Returns(10);

        var resourceUri = new ResourceUri { Uri = "resource://12345" };
        _storageServiceMock
            .Setup(x => x.StoreAsync(It.IsAny<object>(), It.IsAny<ResourceStorageOptions>()))
            .ReturnsAsync(resourceUri);

        // Act
        var result = await Tool.ExecuteAsync(parameters);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Meta.Truncated.Should().BeTrue();
        result.Meta.ResourceUri.Should().NotBeNullOrEmpty();
        result.Actions.Should().Contain(a => a.Action == "retrieve_full_results");
        
        // Verify resource was stored
        _storageServiceMock.Verify(
            x => x.StoreAsync(It.IsAny<List<SearchHit>>(), It.IsAny<ResourceStorageOptions>()),
            Times.Once
        );
    }

    [Test]
    public async Task TextSearch_WithEmptyQuery_ReturnsValidationError()
    {
        // Arrange
        var parameters = new TextSearchParametersOptimized
        {
            Query = "",
            WorkspacePath = @"C:\test\workspace"
        };

        // Act & Assert
        await AssertValidationErrorAsync(
            parameters,
            "Query",
            "is required"
        );
    }

    [Test]
    public async Task TextSearch_WithEmptyWorkspace_ReturnsValidationError()
    {
        // Arrange
        var parameters = new TextSearchParametersOptimized
        {
            Query = "test",
            WorkspacePath = ""
        };

        // Act & Assert
        await AssertValidationErrorAsync(
            parameters,
            "WorkspacePath",
            "is required"
        );
    }

    [Test]
    public void TextSearch_ToolMetadata_IsCorrect()
    {
        // Assert
        Tool.Name.Should().Be(ToolNames.TextSearch);
        Tool.Description.Should().Contain("token optimization");
        Tool.Category.Should().Be(COA.Mcp.Framework.ToolCategory.Query);
    }
}