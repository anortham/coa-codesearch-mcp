using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using NUnit.Framework;
using FluentAssertions;
using Moq;
using Microsoft.Extensions.Logging;
using COA.Mcp.Framework.TokenOptimization.Caching;
using COA.Mcp.Framework.TokenOptimization.Storage;
using COA.Mcp.Framework.TokenOptimization.Models;
using COA.Mcp.Framework.Models;
using COA.CodeSearch.Next.McpServer.Tools;
using COA.CodeSearch.Next.McpServer.Tools.Parameters;
using COA.CodeSearch.Next.McpServer.Tools.Results;
using COA.CodeSearch.Next.McpServer.Services;
using COA.CodeSearch.Next.McpServer.Tests.Base;

namespace COA.CodeSearch.Next.McpServer.Tests.Tools
{
    [TestFixture]
    public class DirectorySearchToolTests : CodeSearchToolTestBase<DirectorySearchTool>
    {
        private DirectorySearchTool _tool = null!;
        private string _testWorkspacePath = null!;
        
        protected override DirectorySearchTool CreateTool()
        {
            _tool = new DirectorySearchTool(
                PathResolutionServiceMock.Object,
                ResponseCacheServiceMock.Object,
                ResourceStorageServiceMock.Object,
                CacheKeyGeneratorMock.Object,
                ToolLoggerMock.Object
            );
            return _tool;
        }
        
        [SetUp]
        public override void SetUp()
        {
            base.SetUp();
            
            // Create test directory structure
            _testWorkspacePath = Path.Combine(Path.GetTempPath(), $"DirSearchTest_{Guid.NewGuid()}");
            CreateTestDirectoryStructure();
        }
        
        [TearDown]
        public override void TearDown()
        {
            // Clean up test directory
            try
            {
                if (Directory.Exists(_testWorkspacePath))
                {
                    Directory.Delete(_testWorkspacePath, true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
            
            base.TearDown();
        }
        
        private void CreateTestDirectoryStructure()
        {
            // Create a sample directory structure for testing
            Directory.CreateDirectory(_testWorkspacePath);
            Directory.CreateDirectory(Path.Combine(_testWorkspacePath, "src"));
            Directory.CreateDirectory(Path.Combine(_testWorkspacePath, "src", "components"));
            Directory.CreateDirectory(Path.Combine(_testWorkspacePath, "src", "services"));
            Directory.CreateDirectory(Path.Combine(_testWorkspacePath, "tests"));
            Directory.CreateDirectory(Path.Combine(_testWorkspacePath, "tests", "unit"));
            Directory.CreateDirectory(Path.Combine(_testWorkspacePath, "tests", "integration"));
            Directory.CreateDirectory(Path.Combine(_testWorkspacePath, "docs"));
            Directory.CreateDirectory(Path.Combine(_testWorkspacePath, ".hidden"));
            Directory.CreateDirectory(Path.Combine(_testWorkspacePath, "bin")); // Should be excluded
            Directory.CreateDirectory(Path.Combine(_testWorkspacePath, "node_modules")); // Should be excluded
            
            // Create some files in directories
            File.WriteAllText(Path.Combine(_testWorkspacePath, "src", "index.ts"), "// main");
            File.WriteAllText(Path.Combine(_testWorkspacePath, "src", "components", "App.tsx"), "// app");
            File.WriteAllText(Path.Combine(_testWorkspacePath, "tests", "unit", "test.spec.ts"), "// test");
        }
        
        [Test]
        public void Tool_Should_Have_Correct_Metadata()
        {
            // Assert
            _tool.Name.Should().Be(ToolNames.DirectorySearch);
            _tool.Description.Should().Contain("directories");
            _tool.Description.Should().Contain("pattern");
            _tool.Category.Should().Be(COA.Mcp.Framework.ToolCategory.Query);
        }
        
        [Test]
        public async Task ExecuteAsync_WithInvalidWorkspacePath_ShouldReturnError()
        {
            // Arrange
            var parameters = new DirectorySearchParameters
            {
                WorkspacePath = "/nonexistent/path",
                Pattern = "*test*"
            };
            
            CacheKeyGeneratorMock.Setup(x => x.GenerateKey(It.IsAny<string>(), It.IsAny<object>()))
                .Returns("test-key");
            
            // Act
            var result = await _tool.ExecuteAsync(parameters);
            
            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeFalse();
            result.Error.Should().NotBeNull();
            result.Error!.Code.Should().Be("DIRECTORY_NOT_FOUND");
        }
        
        [Test]
        public async Task ExecuteAsync_WithGlobPattern_ShouldFindMatchingDirectories()
        {
            // Arrange
            var parameters = new DirectorySearchParameters
            {
                WorkspacePath = _testWorkspacePath,
                Pattern = "*test*",
                IncludeSubdirectories = true
            };
            
            CacheKeyGeneratorMock.Setup(x => x.GenerateKey(It.IsAny<string>(), It.IsAny<object>()))
                .Returns("test-key");
            
            // Act
            var result = await _tool.ExecuteAsync(parameters);
            
            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Data.Should().NotBeNull();
            
            var searchResult = result.Data!.Results as DirectorySearchResult;
            searchResult.Should().NotBeNull();
            searchResult!.Directories.Should().NotBeEmpty();
            searchResult.Directories.Should().Contain(d => d.Name == "tests");
        }
        
        [Test]
        public async Task ExecuteAsync_WithRegexPattern_ShouldFindMatchingDirectories()
        {
            // Arrange
            var parameters = new DirectorySearchParameters
            {
                WorkspacePath = _testWorkspacePath,
                Pattern = "^(src|tests)$",
                UseRegex = true,
                IncludeSubdirectories = true
            };
            
            CacheKeyGeneratorMock.Setup(x => x.GenerateKey(It.IsAny<string>(), It.IsAny<object>()))
                .Returns("test-key");
            
            // Act
            var result = await _tool.ExecuteAsync(parameters);
            
            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            
            var searchResult = result.Data!.Results as DirectorySearchResult;
            searchResult.Should().NotBeNull();
            searchResult!.Directories.Should().Contain(d => d.Name == "src");
            searchResult.Directories.Should().Contain(d => d.Name == "tests");
            searchResult.Directories.Should().NotContain(d => d.Name == "docs");
        }
        
        [Test]
        public async Task ExecuteAsync_ShouldExcludeCommonBuildDirectories()
        {
            // Arrange
            var parameters = new DirectorySearchParameters
            {
                WorkspacePath = _testWorkspacePath,
                Pattern = "*",
                IncludeSubdirectories = true
            };
            
            CacheKeyGeneratorMock.Setup(x => x.GenerateKey(It.IsAny<string>(), It.IsAny<object>()))
                .Returns("test-key");
            
            // Act
            var result = await _tool.ExecuteAsync(parameters);
            
            // Assert
            var searchResult = result.Data!.Results as DirectorySearchResult;
            searchResult.Should().NotBeNull();
            searchResult!.Directories.Should().NotContain(d => d.Name == "bin");
            searchResult.Directories.Should().NotContain(d => d.Name == "node_modules");
        }
        
        [Test]
        public async Task ExecuteAsync_WithHiddenDirectoriesExcluded_ShouldNotReturnHidden()
        {
            // Arrange
            var parameters = new DirectorySearchParameters
            {
                WorkspacePath = _testWorkspacePath,
                Pattern = "*",
                IncludeHidden = false,
                IncludeSubdirectories = false
            };
            
            CacheKeyGeneratorMock.Setup(x => x.GenerateKey(It.IsAny<string>(), It.IsAny<object>()))
                .Returns("test-key");
            
            // Act
            var result = await _tool.ExecuteAsync(parameters);
            
            // Assert
            var searchResult = result.Data!.Results as DirectorySearchResult;
            searchResult.Should().NotBeNull();
            searchResult!.Directories.Should().NotContain(d => d.Name.StartsWith("."));
        }
        
        [Test]
        public async Task ExecuteAsync_WithHiddenDirectoriesIncluded_ShouldReturnHidden()
        {
            // Arrange
            var parameters = new DirectorySearchParameters
            {
                WorkspacePath = _testWorkspacePath,
                Pattern = ".*",
                IncludeHidden = true,
                IncludeSubdirectories = false
            };
            
            CacheKeyGeneratorMock.Setup(x => x.GenerateKey(It.IsAny<string>(), It.IsAny<object>()))
                .Returns("test-key");
            
            // Act
            var result = await _tool.ExecuteAsync(parameters);
            
            // Assert
            var searchResult = result.Data!.Results as DirectorySearchResult;
            searchResult.Should().NotBeNull();
            searchResult!.Directories.Should().Contain(d => d.Name == ".hidden");
        }
        
        [Test]
        public async Task ExecuteAsync_WithMaxResults_ShouldLimitResults()
        {
            // Arrange
            var parameters = new DirectorySearchParameters
            {
                WorkspacePath = _testWorkspacePath,
                Pattern = "*",
                IncludeSubdirectories = true,
                MaxResults = 2
            };
            
            CacheKeyGeneratorMock.Setup(x => x.GenerateKey(It.IsAny<string>(), It.IsAny<object>()))
                .Returns("test-key");
            
            // Act
            var result = await _tool.ExecuteAsync(parameters);
            
            // Assert
            var searchResult = result.Data!.Results as DirectorySearchResult;
            searchResult.Should().NotBeNull();
            searchResult!.Directories.Count.Should().BeLessThanOrEqualTo(2);
        }
        
        [Test]
        public async Task ExecuteAsync_WithCacheEnabled_ShouldCheckCache()
        {
            // Arrange
            var parameters = new DirectorySearchParameters
            {
                WorkspacePath = _testWorkspacePath,
                Pattern = "*test*",
                NoCache = false
            };
            
            var cacheKey = "cache-key";
            CacheKeyGeneratorMock.Setup(x => x.GenerateKey(It.IsAny<string>(), It.IsAny<object>()))
                .Returns(cacheKey);
            
            var cachedResponse = new AIOptimizedResponse<DirectorySearchResult>
            {
                Success = true,
                Data = new AIResponseData<DirectorySearchResult>
                {
                    Results = new DirectorySearchResult
                    {
                        Directories = new List<DirectoryMatch>(),
                        TotalMatches = 0,
                        Pattern = "*test*",
                        WorkspacePath = _testWorkspacePath
                    }
                },
                Meta = new AIResponseMeta()
            };
            
            ResponseCacheServiceMock.Setup(x => x.GetAsync<AIOptimizedResponse<DirectorySearchResult>>(cacheKey))
                .ReturnsAsync(cachedResponse);
            
            // Act
            var result = await _tool.ExecuteAsync(parameters);
            
            // Assert
            result.Should().BeSameAs(cachedResponse);
            result.Meta!.ExtensionData!["cacheHit"].Should().Be(true);
            ResponseCacheServiceMock.Verify(x => x.GetAsync<AIOptimizedResponse<DirectorySearchResult>>(cacheKey), Times.Once);
        }
        
        [Test]
        public async Task ExecuteAsync_WithCacheDisabled_ShouldNotCheckCache()
        {
            // Arrange
            var parameters = new DirectorySearchParameters
            {
                WorkspacePath = _testWorkspacePath,
                Pattern = "*test*",
                NoCache = true
            };
            
            CacheKeyGeneratorMock.Setup(x => x.GenerateKey(It.IsAny<string>(), It.IsAny<object>()))
                .Returns("test-key");
            
            // Act
            var result = await _tool.ExecuteAsync(parameters);
            
            // Assert
            ResponseCacheServiceMock.Verify(x => x.GetAsync<AIOptimizedResponse<DirectorySearchResult>>(It.IsAny<string>()), Times.Never);
        }
        
        [Test]
        public async Task ExecuteAsync_ShouldSetCorrectMetadata()
        {
            // Arrange
            var parameters = new DirectorySearchParameters
            {
                WorkspacePath = _testWorkspacePath,
                Pattern = "src"
            };
            
            CacheKeyGeneratorMock.Setup(x => x.GenerateKey(It.IsAny<string>(), It.IsAny<object>()))
                .Returns("test-key");
            
            // Act
            var result = await _tool.ExecuteAsync(parameters);
            
            // Assert
            var searchResult = result.Data!.Results as DirectorySearchResult;
            searchResult.Should().NotBeNull();
            searchResult!.Pattern.Should().Be("src");
            searchResult.WorkspacePath.Should().Be(_testWorkspacePath);
            searchResult.SearchTimeMs.Should().BeGreaterThan(0);
        }
        
        [Test]
        public async Task ExecuteAsync_WithInvalidRegexPattern_ShouldReturnError()
        {
            // Arrange
            var parameters = new DirectorySearchParameters
            {
                WorkspacePath = _testWorkspacePath,
                Pattern = "[invalid(regex",
                UseRegex = true
            };
            
            CacheKeyGeneratorMock.Setup(x => x.GenerateKey(It.IsAny<string>(), It.IsAny<object>()))
                .Returns("test-key");
            
            // Act
            var result = await _tool.ExecuteAsync(parameters);
            
            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeFalse();
            result.Error.Should().NotBeNull();
            result.Error!.Code.Should().Be("INVALID_PATTERN");
        }
        
        [Test]
        public async Task ExecuteAsync_ShouldCalculateDirectoryDepthCorrectly()
        {
            // Arrange
            var parameters = new DirectorySearchParameters
            {
                WorkspacePath = _testWorkspacePath,
                Pattern = "*",
                IncludeSubdirectories = true
            };
            
            CacheKeyGeneratorMock.Setup(x => x.GenerateKey(It.IsAny<string>(), It.IsAny<object>()))
                .Returns("test-key");
            
            // Act
            var result = await _tool.ExecuteAsync(parameters);
            
            // Assert
            var searchResult = result.Data!.Results as DirectorySearchResult;
            searchResult.Should().NotBeNull();
            
            var srcDir = searchResult!.Directories.FirstOrDefault(d => d.Name == "src");
            srcDir.Should().NotBeNull();
            srcDir!.Depth.Should().Be(1);
            
            var componentsDir = searchResult.Directories.FirstOrDefault(d => d.Name == "components");
            componentsDir?.Depth.Should().Be(2);
        }
        
        [Test]
        public async Task ExecuteAsync_ShouldStoreResultsWhenCachingEnabled()
        {
            // Arrange
            var parameters = new DirectorySearchParameters
            {
                WorkspacePath = _testWorkspacePath,
                Pattern = "src",
                NoCache = false
            };
            
            CacheKeyGeneratorMock.Setup(x => x.GenerateKey(It.IsAny<string>(), It.IsAny<object>()))
                .Returns("test-key");
            
            // Act
            var result = await _tool.ExecuteAsync(parameters);
            
            // Assert
            ResponseCacheServiceMock.Verify(x => x.SetAsync(
                It.IsAny<string>(),
                It.IsAny<AIOptimizedResponse<DirectorySearchResult>>(),
                It.IsAny<CacheEntryOptions>()), Times.Once);
        }
        
        [Test]
        public async Task ExecuteAsync_WithNoMatches_ShouldReturnEmptyResults()
        {
            // Arrange
            var parameters = new DirectorySearchParameters
            {
                WorkspacePath = _testWorkspacePath,
                Pattern = "nonexistentpattern"
            };
            
            CacheKeyGeneratorMock.Setup(x => x.GenerateKey(It.IsAny<string>(), It.IsAny<object>()))
                .Returns("test-key");
            
            // Act
            var result = await _tool.ExecuteAsync(parameters);
            
            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            
            var searchResult = result.Data!.Results as DirectorySearchResult;
            searchResult.Should().NotBeNull();
            searchResult!.Directories.Should().BeEmpty();
            searchResult.TotalMatches.Should().Be(0);
        }
    }
}