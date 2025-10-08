using NUnit.Framework;
using FluentAssertions;
using Moq;
using COA.CodeSearch.McpServer.Tools;
using COA.CodeSearch.McpServer.Tests.Base;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using COA.Mcp.Framework.TokenOptimization.Models;
using COA.Mcp.Framework.TokenOptimization.Caching;
using System.Threading.Tasks;
using System.Threading;
using System;
using System.Collections.Generic;
using COA.Mcp.Framework.Models;

namespace COA.CodeSearch.McpServer.Tests.Tools
{
    [TestFixture]
    public class FileSearchToolTests : CodeSearchToolTestBase<SearchFilesTool>
    {
        private SearchFilesTool _tool = null!;
        
        protected override SearchFilesTool CreateTool()
        {
            _tool = new SearchFilesTool(
                ServiceProvider,
                LuceneIndexServiceMock.Object,
                SQLiteSymbolServiceMock.Object,
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
            // Assert - FileSearchToolTests now uses unified SearchFilesTool
            _tool.Name.Should().Be(ToolNames.SearchFiles);
            _tool.Description.Should().NotBeNullOrWhiteSpace();
            _tool.Category.Should().Be(COA.Mcp.Framework.ToolCategory.Query);
        }
        
        [Test]
        public async Task ExecuteAsync_Should_Return_Error_When_Workspace_Not_Indexed()
        {
            // Arrange
            SetupNoIndex();
            var parameters = new SearchFilesParameters
            {
                Pattern = "*.cs",
                WorkspacePath = TestWorkspacePath
            };
            
            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<SearchFilesResult>>(
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
            var searchResult = new COA.CodeSearch.McpServer.Services.Lucene.SearchResult
            {
                Query = "*",
                TotalHits = 3,
                Hits = new List<COA.CodeSearch.McpServer.Services.Lucene.SearchHit>
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
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(searchResult);
            
            var parameters = new SearchFilesParameters
            {
                Pattern = "*.cs",
                WorkspacePath = TestWorkspacePath,
                UseRegex = false
            };
            
            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<SearchFilesResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeTrue();
            result.Result.Should().NotBeNull();
            result.Result!.Success.Should().BeTrue();
            result.Result.Data.Should().NotBeNull();
            result.Result.Data.Results.Should().NotBeNull();

            // Unified tool returns Files list for file searches
            result.Result.Data.Results.Files.Should().NotBeNull();
            result.Result.Data.Results.Files!.Count.Should().Be(2); // Only .cs files
            result.Result.Data.Results.TotalMatches.Should().Be(2);
        }
        
        [Test]
        public async Task ExecuteAsync_Should_Find_Files_Matching_Regex_Pattern()
        {
            // Arrange
            SetupExistingIndex();
            var searchResult = new COA.CodeSearch.McpServer.Services.Lucene.SearchResult
            {
                Query = "*",
                TotalHits = 3,
                Hits = new List<COA.CodeSearch.McpServer.Services.Lucene.SearchHit>
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
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(searchResult);
            
            var parameters = new SearchFilesParameters
            {
                Pattern = "^test_.*\\.cs$",
                WorkspacePath = TestWorkspacePath,
                UseRegex = true
            };
            
            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<SearchFilesResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeTrue();
            result.Result.Should().NotBeNull();
            result.Result!.Success.Should().BeTrue();
            result.Result.Data.Should().NotBeNull();
            result.Result.Data.Results.TotalMatches.Should().Be(2); // Only test_*.cs files
        }
        
        [Test]
        public async Task ExecuteAsync_Should_Apply_Extension_Filter()
        {
            // Arrange
            SetupExistingIndex();
            var searchResult = new COA.CodeSearch.McpServer.Services.Lucene.SearchResult
            {
                Query = "*",
                TotalHits = 5,
                Hits = new List<COA.CodeSearch.McpServer.Services.Lucene.SearchHit>
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
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(searchResult);
            
            var parameters = new SearchFilesParameters
            {
                Pattern = "*",
                WorkspacePath = TestWorkspacePath,
                ExtensionFilter = ".cs,.js" // Only C# and JavaScript files
            };
            
            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<SearchFilesResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeTrue();
            result.Result.Should().NotBeNull();
            result.Result!.Success.Should().BeTrue();
            result.Result.Data.Should().NotBeNull();
            result.Result.Data.Results.TotalMatches.Should().Be(3); // 2 .cs and 1 .js file
        }
        
        [Test]
        public async Task ExecuteAsync_Should_Include_Directories_When_Requested()
        {
            // Arrange
            SetupExistingIndex();
            var searchResult = new COA.CodeSearch.McpServer.Services.Lucene.SearchResult
            {
                Query = "*",
                TotalHits = 3,
                Hits = new List<COA.CodeSearch.McpServer.Services.Lucene.SearchHit>
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
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(searchResult);
            
            var parameters = new SearchFilesParameters
            {
                Pattern = "*.cs",
                WorkspacePath = TestWorkspacePath,
                IncludeDirectories = true
            };
            
            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<SearchFilesResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeTrue();
            result.Result.Should().NotBeNull();
            result.Result!.Success.Should().BeTrue();
            result.Result.Data.ExtensionData.Should().ContainKey("directories");
            
            var directories = result.Result.Data.ExtensionData!["directories"] as List<string>;
            directories.Should().NotBeNull();
            // Use platform-specific path separators
            directories.Should().Contain(d => d.Replace('\\', '/').Equals("/test/src"));
            directories.Should().Contain(d => d.Replace('\\', '/').Equals("/test/lib"));
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
            
            var parameters = new SearchFilesParameters
            {
                Pattern = "*",
                WorkspacePath = TestWorkspacePath,
                MaxResults = 1000 // Above the 500 limit
            };
            
            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<SearchFilesResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            // Tool should fail validation since max is 500
            result.Success.Should().BeFalse();
            result.Exception.Should().NotBeNull();
            
            // Verify search was NOT called due to validation failure
            LuceneIndexServiceMock.Verify(
                x => x.SearchAsync(
                    It.IsAny<string>(),
                    It.IsAny<Lucene.Net.Search.Query>(),
                    It.IsAny<int>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
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
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(searchResult);
            
            var parameters = new SearchFilesParameters
            {
                Pattern = "*.cs",
                WorkspacePath = TestWorkspacePath
            };
            
            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<SearchFilesResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeTrue();
            
            // Verify result was cached
            ResponseCacheServiceMock.Verify(
                x => x.SetAsync(
                    It.IsAny<string>(),
                    It.IsAny<AIOptimizedResponse<SearchFilesResult>>(),
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
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(searchResult);
            
            var parameters = new SearchFilesParameters
            {
                Pattern = "*.cs",
                WorkspacePath = TestWorkspacePath,
                NoCache = true
            };
            
            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<SearchFilesResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeTrue();
            
            // Verify cache was not checked
            ResponseCacheServiceMock.Verify(
                x => x.GetAsync<FileSearchResult>(It.IsAny<string>()),
                Times.Never);
            
            // Verify result was not cached
            ResponseCacheServiceMock.Verify(
                x => x.SetAsync(
                    It.IsAny<string>(),
                    It.IsAny<FileSearchResult>(),
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
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("Index corrupted"));
            
            var parameters = new SearchFilesParameters
            {
                Pattern = "*.cs",
                WorkspacePath = TestWorkspacePath
            };
            
            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<SearchFilesResult>>(
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
            var parameters = new SearchFilesParameters
            {
                Pattern = null!, // Missing required parameter
                WorkspacePath = TestWorkspacePath
            };
            
            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<SearchFilesResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeFalse();
            result.Exception.Should().NotBeNull();
            result.Exception.Should().BeOfType<COA.Mcp.Framework.Exceptions.ToolExecutionException>();
        }

        [Test]
        public async Task ExecuteAsync_Should_Support_Recursive_Glob_Patterns()
        {
            // Arrange
            SetupExistingIndex();
            var searchResult = new COA.CodeSearch.McpServer.Services.Lucene.SearchResult
            {
                Query = "**/*.csproj",
                TotalHits = 3,
                Hits = new List<COA.CodeSearch.McpServer.Services.Lucene.SearchHit>
                {
                    new() { FilePath = "C:\\source\\Project1\\Project1.csproj", Score = 1.0f },
                    new() { FilePath = "C:\\source\\Project1\\Tests\\Tests.csproj", Score = 0.9f },
                    new() { FilePath = "C:\\source\\Project2\\Project2.csproj", Score = 0.8f }
                },
                SearchTime = TimeSpan.FromMilliseconds(50)
            };
            
            LuceneIndexServiceMock
                .Setup(x => x.SearchAsync(
                    It.IsAny<string>(),
                    It.IsAny<Lucene.Net.Search.Query>(),
                    It.IsAny<int>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(searchResult);
            
            var parameters = new SearchFilesParameters
            {
                Pattern = "**/*.csproj",
                WorkspacePath = TestWorkspacePath,
                UseRegex = false
            };
            
            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<SearchFilesResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeTrue();
            result.Result.Should().NotBeNull();
            result.Result!.Success.Should().BeTrue();
            result.Result.Data.Should().NotBeNull();
            result.Result.Data.Results.TotalMatches.Should().Be(3); // All .csproj files found recursively
            
            // Verify that the search used MatchAllDocsQuery for ** patterns (since Lucene WildcardQuery doesn't support **)
            LuceneIndexServiceMock.Verify(
                x => x.SearchAsync(
                    It.IsAny<string>(),
                    It.Is<Lucene.Net.Search.Query>(q => q.ToString().Contains("*:*")),
                    It.IsAny<int>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task ExecuteAsync_Should_Use_Path_Field_For_Directory_Patterns()
        {
            // Arrange
            SetupExistingIndex();
            var searchResult = new COA.CodeSearch.McpServer.Services.Lucene.SearchResult
            {
                Query = "src/**/*.cs",
                TotalHits = 2,
                Hits = new List<COA.CodeSearch.McpServer.Services.Lucene.SearchHit>
                {
                    new() { FilePath = "C:\\source\\Project\\src\\Controllers\\HomeController.cs", Score = 1.0f },
                    new() { FilePath = "C:\\source\\Project\\src\\Services\\UserService.cs", Score = 0.9f }
                },
                SearchTime = TimeSpan.FromMilliseconds(50)
            };
            
            LuceneIndexServiceMock
                .Setup(x => x.SearchAsync(
                    It.IsAny<string>(),
                    It.IsAny<Lucene.Net.Search.Query>(),
                    It.IsAny<int>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(searchResult);
            
            var parameters = new SearchFilesParameters
            {
                Pattern = "src/**/*.cs",
                WorkspacePath = TestWorkspacePath,
                UseRegex = false
            };
            
            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<SearchFilesResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeTrue();
            result.Result.Should().NotBeNull();
            result.Result!.Success.Should().BeTrue();
            result.Result.Data.Should().NotBeNull();
            result.Result.Data.Results.TotalMatches.Should().Be(2); // Files in src directory recursively
        }

        [Test]
        public async Task ExecuteAsync_Should_Use_Filename_Field_For_Simple_Patterns()
        {
            // Arrange
            SetupExistingIndex();
            
            // Mock returns ALL files from index, FileSearchTool will filter them
            var searchResult = new COA.CodeSearch.McpServer.Services.Lucene.SearchResult
            {
                Query = "*.cs",
                TotalHits = 5,
                Hits = new List<COA.CodeSearch.McpServer.Services.Lucene.SearchHit>
                {
                    new() { FilePath = "/source/Project/Program.cs", Score = 1.0f },
                    new() { FilePath = "/source/Project/Startup.cs", Score = 0.9f },
                    new() { FilePath = "/source/Project/README.md", Score = 0.8f },
                    new() { FilePath = "/source/Project/package.json", Score = 0.7f },
                    new() { FilePath = "/source/Project/Utils.cs", Score = 0.6f }
                },
                SearchTime = TimeSpan.FromMilliseconds(50)
            };
            
            LuceneIndexServiceMock
                .Setup(x => x.SearchAsync(
                    It.IsAny<string>(),
                    It.IsAny<Lucene.Net.Search.Query>(),
                    It.IsAny<int>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(searchResult);
            
            var parameters = new SearchFilesParameters
            {
                Pattern = "*.cs",
                WorkspacePath = TestWorkspacePath,
                UseRegex = false
            };
            
            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<SearchFilesResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert - FileSearchTool should filter to only .cs files
            result.Success.Should().BeTrue();
            result.Result.Should().NotBeNull();
            result.Result!.Success.Should().BeTrue();
            result.Result.Data.Should().NotBeNull();
            
            // The tool should have filtered the results to only include .cs files
            result.Result.Data.Results.TotalMatches.Should().Be(3, "should return only the 3 .cs files from the 5 total files");
            result.Result.Data.Results.Should().NotBeNull();
            
            // Verify all returned files match the pattern
            var files = result.Result.Data.Results as System.Collections.IEnumerable;
            if (files != null)
            {
                foreach (dynamic file in files)
                {
                    string path = file.Path?.ToString() ?? "";
                    path.Should().EndWith(".cs", "all returned files should be .cs files");
                }
            }
            
            // Verify that the search used the filename_lower field for simple patterns
            LuceneIndexServiceMock.Verify(
                x => x.SearchAsync(
                    It.IsAny<string>(),
                    It.Is<Lucene.Net.Search.Query>(q => q.ToString().Contains("filename_lower")),
                    It.IsAny<int>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()),
                Times.Once,
                "Simple patterns like *.cs should use filename_lower field, not path field");
        }

        [Test]
        public async Task ExecuteAsync_Should_Handle_Mixed_Path_Separators()
        {
            // Arrange
            SetupExistingIndex();
            var searchResult = new COA.CodeSearch.McpServer.Services.Lucene.SearchResult
            {
                Query = "src\\**\\*.cs",
                TotalHits = 2,
                Hits = new List<COA.CodeSearch.McpServer.Services.Lucene.SearchHit>
                {
                    new() { FilePath = "C:\\source\\Project\\src\\Controllers\\HomeController.cs", Score = 1.0f },
                    new() { FilePath = "C:\\source\\Project\\src\\Models\\User.cs", Score = 0.9f }
                },
                SearchTime = TimeSpan.FromMilliseconds(50)
            };
            
            LuceneIndexServiceMock
                .Setup(x => x.SearchAsync(
                    It.IsAny<string>(),
                    It.IsAny<Lucene.Net.Search.Query>(),
                    It.IsAny<int>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(searchResult);
            
            var parameters = new SearchFilesParameters
            {
                Pattern = "src\\**\\*.cs", // Windows-style path separators
                WorkspacePath = TestWorkspacePath,
                UseRegex = false
            };
            
            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<SearchFilesResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeTrue();
            result.Result.Should().NotBeNull();
            result.Result!.Success.Should().BeTrue();
            result.Result.Data.Should().NotBeNull();
            result.Result.Data.Results.TotalMatches.Should().Be(2);
        }

        [Test]
        public async Task ConvertGlobToRegex_Should_Handle_Recursive_Patterns_Correctly()
        {
            // This test verifies the glob-to-regex conversion logic
            // Since ConvertGlobToRegex is private, we test it indirectly through pattern matching
            
            // Arrange
            SetupExistingIndex();
            var searchResult = new COA.CodeSearch.McpServer.Services.Lucene.SearchResult
            {
                Query = "**/*.test.js",
                TotalHits = 4,
                Hits = new List<COA.CodeSearch.McpServer.Services.Lucene.SearchHit>
                {
                    new() { FilePath = "C:\\source\\app\\tests\\unit.test.js", Score = 1.0f },
                    new() { FilePath = "C:\\source\\app\\components\\button.test.js", Score = 0.9f },
                    new() { FilePath = "C:\\source\\app\\services\\api.test.js", Score = 0.8f },
                    new() { FilePath = "C:\\source\\app\\main.js", Score = 0.7f } // Should be filtered out
                },
                SearchTime = TimeSpan.FromMilliseconds(50)
            };
            
            LuceneIndexServiceMock
                .Setup(x => x.SearchAsync(
                    It.IsAny<string>(),
                    It.IsAny<Lucene.Net.Search.Query>(),
                    It.IsAny<int>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(searchResult);
            
            var parameters = new SearchFilesParameters
            {
                Pattern = "**/*.test.js",
                WorkspacePath = TestWorkspacePath,
                UseRegex = false
            };
            
            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<SearchFilesResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeTrue();
            result.Result.Should().NotBeNull();
            result.Result!.Success.Should().BeTrue();
            result.Result.Data.Should().NotBeNull();
            result.Result.Data.Results.TotalMatches.Should().Be(3); // Only *.test.js files, not main.js
        }
    }
}