using NUnit.Framework;
using FluentAssertions;
using Moq;
using COA.CodeSearch.Next.McpServer.Tools;
using COA.CodeSearch.Next.McpServer.Tests.Base;
using COA.CodeSearch.Next.McpServer.Models;
using COA.CodeSearch.Next.McpServer.Services;
using COA.Mcp.Framework.TokenOptimization.Models;
using COA.Mcp.Framework.TokenOptimization.Caching;
using System.Threading.Tasks;
using System.Threading;
using System;
using System.Collections.Generic;
using COA.Mcp.Framework.Models;

namespace COA.CodeSearch.Next.McpServer.Tests.Tools
{
    [TestFixture]
    public class FileSearchToolTests : CodeSearchToolTestBase<FileSearchTool>
    {
        private FileSearchTool _tool = null!;
        
        protected override FileSearchTool CreateTool()
        {
            _tool = new FileSearchTool(
                LuceneIndexServiceMock.Object,
                PathResolutionServiceMock.Object,
                ResponseCacheServiceMock.Object,
                ResourceStorageServiceMock.Object,
                CacheKeyGeneratorMock.Object,
                ToolLoggerMock.Object
            );
            return _tool;
        }
        
        [Test]
        public void Tool_Should_Have_Correct_Metadata()
        {
            // Assert
            _tool.Name.Should().Be(ToolNames.FileSearch);
            _tool.Description.Should().NotBeNullOrWhiteSpace();
            _tool.Category.Should().Be(COA.Mcp.Framework.ToolCategory.Query);
        }
        
        [Test]
        public async Task ExecuteAsync_Should_Return_Error_When_Workspace_Not_Indexed()
        {
            // Arrange
            SetupNoIndex();
            var parameters = new FileSearchParameters
            {
                Pattern = "*.cs",
                WorkspacePath = TestWorkspacePath
            };
            
            // Act
            var result = await ExecuteToolAsync<TokenOptimizedResult>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeTrue();
            result.Result.Should().NotBeNull();
            result.Result!.Success.Should().BeFalse();
            result.Result.Error.Should().NotBeNull();
            result.Result.Error!.Code.Should().Be("NO_INDEX");
            result.Result.Actions.Should().NotBeNullOrEmpty();
            result.Result.Actions!.Should().Contain(a => a.Action == ToolNames.IndexWorkspace);
        }
        
        [Test]
        public async Task ExecuteAsync_Should_Find_Files_Matching_Glob_Pattern()
        {
            // Arrange
            SetupExistingIndex();
            var searchResult = new Services.Lucene.SearchResult
            {
                Query = "*",
                TotalHits = 3,
                Hits = new List<Services.Lucene.SearchHit>
                {
                    new() { FilePath = "/test/file1.cs", Score = 1.0f },
                    new() { FilePath = "/test/file2.cs", Score = 0.9f },
                    new() { FilePath = "/test/data.json", Score = 0.8f }
                },
                SearchTime = TimeSpan.FromMilliseconds(50)
            };
            
            LuceneIndexServiceMock
                .Setup(x => x.SearchAsync(
                    It.IsAny<string>(),
                    It.IsAny<Lucene.Net.Search.Query>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(searchResult);
            
            var parameters = new FileSearchParameters
            {
                Pattern = "*.cs",
                WorkspacePath = TestWorkspacePath,
                UseRegex = false
            };
            
            // Act
            var result = await ExecuteToolAsync<TokenOptimizedResult>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeTrue();
            result.Result.Should().NotBeNull();
            result.Result!.Success.Should().BeTrue();
            result.Result.Data.Should().NotBeNull();
            result.Result.Data.Count.Should().Be(2); // Only .cs files
            result.Result.Data.Summary.Should().Contain("2 files");
        }
        
        [Test]
        public async Task ExecuteAsync_Should_Find_Files_Matching_Regex_Pattern()
        {
            // Arrange
            SetupExistingIndex();
            var searchResult = new Services.Lucene.SearchResult
            {
                Query = "*",
                TotalHits = 3,
                Hits = new List<Services.Lucene.SearchHit>
                {
                    new() { FilePath = "/test/test_file1.cs", Score = 1.0f },
                    new() { FilePath = "/test/test_file2.cs", Score = 0.9f },
                    new() { FilePath = "/test/main.cs", Score = 0.8f }
                },
                SearchTime = TimeSpan.FromMilliseconds(50)
            };
            
            LuceneIndexServiceMock
                .Setup(x => x.SearchAsync(
                    It.IsAny<string>(),
                    It.IsAny<Lucene.Net.Search.Query>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(searchResult);
            
            var parameters = new FileSearchParameters
            {
                Pattern = "^test_.*\\.cs$",
                WorkspacePath = TestWorkspacePath,
                UseRegex = true
            };
            
            // Act
            var result = await ExecuteToolAsync<TokenOptimizedResult>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeTrue();
            result.Result.Should().NotBeNull();
            result.Result!.Success.Should().BeTrue();
            result.Result.Data.Should().NotBeNull();
            result.Result.Data.Count.Should().Be(2); // Only test_*.cs files
        }
        
        [Test]
        public async Task ExecuteAsync_Should_Apply_Extension_Filter()
        {
            // Arrange
            SetupExistingIndex();
            var searchResult = new Services.Lucene.SearchResult
            {
                Query = "*",
                TotalHits = 5,
                Hits = new List<Services.Lucene.SearchHit>
                {
                    new() { FilePath = "/test/file1.cs", Score = 1.0f },
                    new() { FilePath = "/test/file2.js", Score = 0.9f },
                    new() { FilePath = "/test/file3.ts", Score = 0.8f },
                    new() { FilePath = "/test/file4.cs", Score = 0.7f },
                    new() { FilePath = "/test/file5.json", Score = 0.6f }
                },
                SearchTime = TimeSpan.FromMilliseconds(50)
            };
            
            LuceneIndexServiceMock
                .Setup(x => x.SearchAsync(
                    It.IsAny<string>(),
                    It.IsAny<Lucene.Net.Search.Query>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(searchResult);
            
            var parameters = new FileSearchParameters
            {
                Pattern = "*",
                WorkspacePath = TestWorkspacePath,
                ExtensionFilter = ".cs,.js" // Only C# and JavaScript files
            };
            
            // Act
            var result = await ExecuteToolAsync<TokenOptimizedResult>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeTrue();
            result.Result.Should().NotBeNull();
            result.Result!.Success.Should().BeTrue();
            result.Result.Data.Should().NotBeNull();
            result.Result.Data.Count.Should().Be(3); // 2 .cs and 1 .js file
        }
        
        [Test]
        public async Task ExecuteAsync_Should_Include_Directories_When_Requested()
        {
            // Arrange
            SetupExistingIndex();
            var searchResult = new Services.Lucene.SearchResult
            {
                Query = "*",
                TotalHits = 3,
                Hits = new List<Services.Lucene.SearchHit>
                {
                    new() { FilePath = "/test/src/file1.cs", Score = 1.0f },
                    new() { FilePath = "/test/lib/file2.cs", Score = 0.9f },
                    new() { FilePath = "/test/src/file3.cs", Score = 0.8f }
                },
                SearchTime = TimeSpan.FromMilliseconds(50)
            };
            
            LuceneIndexServiceMock
                .Setup(x => x.SearchAsync(
                    It.IsAny<string>(),
                    It.IsAny<Lucene.Net.Search.Query>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(searchResult);
            
            var parameters = new FileSearchParameters
            {
                Pattern = "*.cs",
                WorkspacePath = TestWorkspacePath,
                IncludeDirectories = true
            };
            
            // Act
            var result = await ExecuteToolAsync<TokenOptimizedResult>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeTrue();
            result.Result.Should().NotBeNull();
            result.Result!.Success.Should().BeTrue();
            result.Result.Data.ExtensionData.Should().ContainKey("directories");
            
            var directories = result.Result.Data.ExtensionData!["directories"] as List<string>;
            directories.Should().NotBeNull();
            directories.Should().Contain("/test/src");
            directories.Should().Contain("/test/lib");
        }
        
        [Test]
        public async Task ExecuteAsync_Should_Respect_MaxResults_Limit()
        {
            // Arrange
            SetupExistingIndex();
            var searchResult = CreateTestSearchResult(100);
            
            LuceneIndexServiceMock
                .Setup(x => x.SearchAsync(
                    It.IsAny<string>(),
                    It.IsAny<Lucene.Net.Search.Query>(),
                    It.Is<int>(max => max == 1000), // 500 * 2 for filtering
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(searchResult);
            
            var parameters = new FileSearchParameters
            {
                Pattern = "*",
                WorkspacePath = TestWorkspacePath,
                MaxResults = 1000 // Above the 500 limit
            };
            
            // Act
            var result = await ExecuteToolAsync<TokenOptimizedResult>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeTrue();
            
            // Verify search was called with adjusted limit for filtering
            LuceneIndexServiceMock.Verify(
                x => x.SearchAsync(
                    It.IsAny<string>(),
                    It.IsAny<Lucene.Net.Search.Query>(),
                    1000, // 500 * 2
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
        
        [Test]
        public async Task ExecuteAsync_Should_Cache_Results_By_Default()
        {
            // Arrange
            SetupExistingIndex();
            var searchResult = CreateTestSearchResult(5);
            
            LuceneIndexServiceMock
                .Setup(x => x.SearchAsync(
                    It.IsAny<string>(),
                    It.IsAny<Lucene.Net.Search.Query>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(searchResult);
            
            var parameters = new FileSearchParameters
            {
                Pattern = "*.cs",
                WorkspacePath = TestWorkspacePath
            };
            
            // Act
            var result = await ExecuteToolAsync<TokenOptimizedResult>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeTrue();
            
            // Verify result was cached
            ResponseCacheServiceMock.Verify(
                x => x.SetAsync(
                    It.IsAny<string>(),
                    It.IsAny<TokenOptimizedResult>(),
                    It.IsAny<CacheEntryOptions>()),
                Times.Once);
        }
        
        [Test]
        public async Task ExecuteAsync_Should_Bypass_Cache_When_NoCache_Is_True()
        {
            // Arrange
            SetupExistingIndex();
            var searchResult = CreateTestSearchResult(5);
            
            LuceneIndexServiceMock
                .Setup(x => x.SearchAsync(
                    It.IsAny<string>(),
                    It.IsAny<Lucene.Net.Search.Query>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(searchResult);
            
            var parameters = new FileSearchParameters
            {
                Pattern = "*.cs",
                WorkspacePath = TestWorkspacePath,
                NoCache = true
            };
            
            // Act
            var result = await ExecuteToolAsync<TokenOptimizedResult>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeTrue();
            
            // Verify cache was not checked
            ResponseCacheServiceMock.Verify(
                x => x.GetAsync<TokenOptimizedResult>(It.IsAny<string>()),
                Times.Never);
            
            // Verify result was not cached
            ResponseCacheServiceMock.Verify(
                x => x.SetAsync(
                    It.IsAny<string>(),
                    It.IsAny<TokenOptimizedResult>(),
                    It.IsAny<CacheEntryOptions>()),
                Times.Never);
        }
        
        [Test]
        public async Task ExecuteAsync_Should_Handle_Search_Errors_Gracefully()
        {
            // Arrange
            SetupExistingIndex();
            
            LuceneIndexServiceMock
                .Setup(x => x.SearchAsync(
                    It.IsAny<string>(),
                    It.IsAny<Lucene.Net.Search.Query>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("Index corrupted"));
            
            var parameters = new FileSearchParameters
            {
                Pattern = "*.cs",
                WorkspacePath = TestWorkspacePath
            };
            
            // Act
            var result = await ExecuteToolAsync<TokenOptimizedResult>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeTrue(); // Tool execution succeeded
            result.Result.Should().NotBeNull();
            result.Result!.Success.Should().BeFalse(); // But search failed
            result.Result.Error.Should().NotBeNull();
            result.Result.Error!.Code.Should().Be("FILE_SEARCH_ERROR");
            result.Result.Error.Message.Should().Contain("Index corrupted");
            result.Result.Error.Recovery.Should().NotBeNull();
        }
        
        [Test]
        public async Task ExecuteAsync_Should_Validate_Required_Parameters()
        {
            // Arrange
            var parameters = new FileSearchParameters
            {
                Pattern = null!, // Missing required parameter
                WorkspacePath = TestWorkspacePath
            };
            
            // Act
            var result = await ExecuteToolAsync<TokenOptimizedResult>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeFalse();
            result.Exception.Should().NotBeNull();
            result.Exception.Should().BeOfType<ArgumentNullException>();
        }
    }
}