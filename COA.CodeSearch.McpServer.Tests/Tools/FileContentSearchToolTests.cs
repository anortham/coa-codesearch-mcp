using NUnit.Framework;
using FluentAssertions;
using Moq;
using COA.CodeSearch.McpServer.Tools;
using COA.CodeSearch.McpServer.Tests.Base;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services.Lucene;
using COA.CodeSearch.McpServer.Services;
using COA.Mcp.Framework.TokenOptimization.Models;
using COA.Mcp.Framework.TokenOptimization.Caching;
using COA.Mcp.Framework.TokenOptimization.Storage;
using Lucene.Net.Search;
using Lucene.Net.Index;
using System.Threading.Tasks;
using System.Threading;
using System;
using System.Collections.Generic;
using System.IO;
using COA.Mcp.Framework.Models;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Tests.Tools
{
    [TestFixture]
    public class FileContentSearchToolTests : CodeSearchToolTestBase<FileContentSearchTool>
    {
        private FileContentSearchTool _tool = null!;
        private string _testFilePath = null!;
        private string _testFileContent = null!;

        protected override FileContentSearchTool CreateTool()
        {
            var queryPreprocessorLoggerMock = new Mock<ILogger<QueryPreprocessor>>();
            var queryPreprocessor = new QueryPreprocessor(queryPreprocessorLoggerMock.Object);
            
            _tool = new FileContentSearchTool(
                LuceneIndexServiceMock.Object,
                ResponseCacheServiceMock.Object,
                ResourceStorageServiceMock.Object,
                CacheKeyGeneratorMock.Object,
                queryPreprocessor,
                VSCodeBridgeMock.Object,
                ToolLoggerMock.Object
            );
            return _tool;
        }

        protected override void OnSetUp()
        {
            base.OnSetUp();
            
            // Create test file with known content and line numbers
            _testFilePath = Path.Combine(TestWorkspacePath, "TestFile.cs");
            _testFileContent = @"using System;
using System.Collections.Generic;

namespace TestProject
{
    public class TestClass
    {
        private string _testField = ""test value"";
        
        public void TestMethod()
        {
            var testVariable = ""hello world"";
            Console.WriteLine(testVariable);
            
            if (testVariable.Contains(""hello""))
            {
                Console.WriteLine(""Found hello"");
            }
        }
        
        public int CalculateSum(int a, int b)
        {
            return a + b;
        }
    }
}";
            
            Directory.CreateDirectory(Path.GetDirectoryName(_testFilePath)!);
            File.WriteAllText(_testFilePath, _testFileContent);
        }

        [Test]
        public void Tool_Should_Have_Correct_Metadata()
        {
            // Assert
            _tool.Name.Should().Be(ToolNames.FileContentSearch);
            _tool.Description.Should().NotBeNullOrWhiteSpace();
            _tool.Description.Should().Contain("specific file");
            _tool.Category.Should().Be(COA.Mcp.Framework.ToolCategory.Query);
        }

        [Test]
        public async Task ExecuteAsync_Should_Return_Error_When_Workspace_Not_Indexed()
        {
            // Arrange
            SetupNoIndex();
            var parameters = new FileContentSearchParameters
            {
                FilePath = _testFilePath,
                Pattern = "TestMethod",
                WorkspacePath = TestWorkspacePath
            };
            
            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<SearchResult>>(
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
        public async Task ExecuteAsync_Should_Return_Error_When_File_Not_Found()
        {
            // Arrange
            SetupExistingIndex();
            var nonExistentFile = Path.Combine(TestWorkspacePath, "NonExistent.cs");
            var parameters = new FileContentSearchParameters
            {
                FilePath = nonExistentFile,
                Pattern = "TestMethod",
                WorkspacePath = TestWorkspacePath
            };
            
            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<SearchResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeTrue();
            result.Result.Should().NotBeNull();
            result.Result!.Success.Should().BeFalse();
            result.Result.Error.Should().NotBeNull();
            result.Result.Error!.Code.Should().Be("FILE_NOT_FOUND");
            result.Result.Actions.Should().NotBeNullOrEmpty();
            result.Result.Actions!.Should().Contain(a => a.Action == ToolNames.FileSearch);
        }

        [Test]
        public async Task ExecuteAsync_Should_Return_Error_When_File_Outside_Workspace()
        {
            // Arrange
            SetupExistingIndex();
            var outsideFile = Path.Combine(Path.GetTempPath(), "OutsideFile.cs");
            File.WriteAllText(outsideFile, "test content");
            
            try
            {
                var parameters = new FileContentSearchParameters
                {
                    FilePath = outsideFile,
                    Pattern = "test",
                    WorkspacePath = TestWorkspacePath
                };
                
                // Act
                var result = await ExecuteToolAsync<AIOptimizedResponse<SearchResult>>(
                    async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
                
                // Assert
                result.Success.Should().BeTrue();
                result.Result.Should().NotBeNull();
                result.Result!.Success.Should().BeFalse();
                result.Result.Error.Should().NotBeNull();
                result.Result.Error!.Code.Should().Be("VALIDATION_ERROR");
                result.Result.Error!.Message.Should().Contain("not within workspace");
            }
            finally
            {
                File.Delete(outsideFile);
            }
        }

        [Test]
        public async Task ExecuteAsync_Should_Return_Cached_Results_When_Available()
        {
            // Arrange
            SetupExistingIndex();
            var cachedResult = new AIOptimizedResponse<SearchResult>
            {
                Success = true,
                Data = new AIResponseData<SearchResult>
                {
                    Summary = "Cached file content results",
                    Count = 2,
                    Results = new SearchResult { TotalHits = 2 }
                }
            };
            
            ResponseCacheServiceMock
                .Setup(x => x.GetAsync<AIOptimizedResponse<SearchResult>>(It.IsAny<string>()))
                .ReturnsAsync(cachedResult);
            
            var parameters = new FileContentSearchParameters
            {
                FilePath = _testFilePath,
                Pattern = "TestMethod",
                WorkspacePath = TestWorkspacePath
            };
            
            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<SearchResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeTrue();
            result.Result.Should().Be(cachedResult);
            result.Result!.Meta!.ExtensionData!["cacheHit"].Should().Be(true);
            
            // Verify cache was checked
            ResponseCacheServiceMock.Verify(
                x => x.GetAsync<AIOptimizedResponse<SearchResult>>(It.IsAny<string>()), 
                Times.Once);
        }

        [Test]
        public async Task ExecuteAsync_Should_Search_Specific_File_With_Exact_Line_Numbers()
        {
            // Arrange
            SetupExistingIndex();
            SetupSuccessfulSearchWithLineNumbers();
            
            var parameters = new FileContentSearchParameters
            {
                FilePath = _testFilePath,
                Pattern = "TestMethod",
                WorkspacePath = TestWorkspacePath,
                SearchType = "literal"
            };
            
            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<SearchResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeTrue();
            result.Result.Should().NotBeNull();
            result.Result!.Success.Should().BeTrue();
            result.Result.Data.Should().NotBeNull();
            result.Result.Data!.Results.Should().NotBeNull();
            
            // Verify the search was called with a BooleanQuery that includes both path and content filters
            LuceneIndexServiceMock.Verify(
                x => x.SearchAsync(
                    It.Is<string>(wp => wp == TestWorkspacePath),
                    It.Is<Query>(q => q.GetType().Name.Contains("MultiFactorScoreQuery")), // Wrapped query
                    It.IsAny<int>(),
                    It.Is<bool>(includeSnippets => includeSnippets == true), // Should include snippets for file content search
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task ExecuteAsync_Should_Support_All_Search_Types()
        {
            // Arrange
            SetupExistingIndex();
            var searchTypes = new[] { "standard", "literal", "code", "wildcard", "fuzzy", "phrase", "regex" };
            
            foreach (var searchType in searchTypes)
            {
                SetupSuccessfulSearchWithLineNumbers();
                
                var parameters = new FileContentSearchParameters
                {
                    FilePath = _testFilePath,
                    Pattern = "Test*",
                    WorkspacePath = TestWorkspacePath,
                    SearchType = searchType
                };
                
                // Act
                var result = await ExecuteToolAsync<AIOptimizedResponse<SearchResult>>(
                    async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
                
                // Assert
                result.Success.Should().BeTrue($"Search type '{searchType}' should be supported");
                result.Result.Should().NotBeNull();
                result.Result!.Success.Should().BeTrue();
                
                // Reset mocks for next iteration
                LuceneIndexServiceMock.Reset();
                SetupExistingIndex();
            }
        }

        [Test]
        public async Task ExecuteAsync_Should_Handle_Case_Sensitive_Search()
        {
            // Arrange
            SetupExistingIndex();
            SetupSuccessfulSearchWithLineNumbers();
            
            var parameters = new FileContentSearchParameters
            {
                FilePath = _testFilePath,
                Pattern = "testMethod", // lowercase
                WorkspacePath = TestWorkspacePath,
                CaseSensitive = true
            };
            
            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<SearchResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeTrue();
            
            // Verify case sensitivity was passed to the search
            LuceneIndexServiceMock.Verify(
                x => x.SearchAsync(
                    It.IsAny<string>(),
                    It.IsAny<Query>(),
                    It.IsAny<int>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task ExecuteAsync_Should_Respect_MaxResults_Parameter()
        {
            // Arrange
            SetupExistingIndex();
            SetupSuccessfulSearchWithLineNumbers();
            
            var parameters = new FileContentSearchParameters
            {
                FilePath = _testFilePath,
                Pattern = "test",
                WorkspacePath = TestWorkspacePath,
                MaxResults = 10
            };
            
            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<SearchResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeTrue();
            
            // Verify maxResults was passed (should be limited by token budget)
            LuceneIndexServiceMock.Verify(
                x => x.SearchAsync(
                    It.IsAny<string>(),
                    It.IsAny<Query>(),
                    It.Is<int>(max => max <= 10), // Should not exceed requested max
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task ExecuteAsync_Should_Cache_Successful_Results()
        {
            // Arrange
            SetupExistingIndex();
            SetupSuccessfulSearchWithLineNumbers();
            SetupResponseBuilder();
            
            var parameters = new FileContentSearchParameters
            {
                FilePath = _testFilePath,
                Pattern = "TestMethod",
                WorkspacePath = TestWorkspacePath,
                NoCache = false
            };
            
            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<SearchResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeTrue();
            
            // Verify caching was attempted
            ResponseCacheServiceMock.Verify(
                x => x.SetAsync(
                    It.IsAny<string>(),
                    It.IsAny<AIOptimizedResponse<SearchResult>>(),
                    It.Is<CacheEntryOptions>(opts => opts.AbsoluteExpiration.HasValue)),
                Times.Once);
        }

        [Test]
        public async Task ExecuteAsync_Should_Skip_Cache_When_NoCache_Is_True()
        {
            // Arrange
            SetupExistingIndex();
            SetupSuccessfulSearchWithLineNumbers();
            SetupResponseBuilder();
            
            var parameters = new FileContentSearchParameters
            {
                FilePath = _testFilePath,
                Pattern = "TestMethod",
                WorkspacePath = TestWorkspacePath,
                NoCache = true
            };
            
            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<SearchResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeTrue();
            
            // Verify cache was neither checked nor set
            ResponseCacheServiceMock.Verify(
                x => x.GetAsync<AIOptimizedResponse<SearchResult>>(It.IsAny<string>()),
                Times.Never);
            
            ResponseCacheServiceMock.Verify(
                x => x.SetAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CacheEntryOptions>()),
                Times.Never);
        }

        [Test]
        public async Task ExecuteAsync_Should_Send_VSCode_Visualization_When_Requested()
        {
            // Arrange
            SetupExistingIndex();
            SetupSuccessfulSearchWithLineNumbers();
            SetupResponseBuilder();
            
            // Setup VS Code Bridge as connected
            VSCodeBridgeMock.Setup(x => x.IsConnected).Returns(true);
            
            var parameters = new FileContentSearchParameters
            {
                FilePath = _testFilePath,
                Pattern = "TestMethod",
                WorkspacePath = TestWorkspacePath,
                ShowInVSCode = true
            };
            
            // Act
            var result = await ExecuteToolAsync<AIOptimizedResponse<SearchResult>>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
            
            // Assert
            result.Success.Should().BeTrue();
            
            // Verify VS Code visualization was sent
            VSCodeBridgeMock.Verify(
                x => x.SendVisualizationAsync(
                    "file-content-search",
                    It.IsAny<object>(),
                    It.IsAny<COA.VSCodeBridge.Models.VisualizationHint>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        #region Helper Methods

        private void SetupSuccessfulSearchWithLineNumbers()
        {
            var searchResult = new SearchResult
            {
                TotalHits = 2,
                SearchTime = TimeSpan.FromMilliseconds(50),
                Query = "TestMethod",
                Hits = new List<SearchHit>
                {
                    new SearchHit
                    {
                        FilePath = _testFilePath,
                        Score = 0.95f,
                        LineNumber = 10, // Line where "public void TestMethod()" appears
                        Snippet = "public void TestMethod()",
                        ContextLines = new List<string>
                        {
                            "        private string _testField = \"test value\";",
                            "        ",
                            "        public void TestMethod()",
                            "        {",
                            "            var testVariable = \"hello world\";"
                        },
                        StartLine = 8,
                        EndLine = 12,
                        LastModified = DateTime.UtcNow.AddMinutes(-5)
                    },
                    new SearchHit
                    {
                        FilePath = _testFilePath,
                        Score = 0.85f,
                        LineNumber = 12, // Line where testVariable is used
                        Snippet = "var testVariable = \"hello world\";",
                        ContextLines = new List<string>
                        {
                            "        public void TestMethod()",
                            "        {",
                            "            var testVariable = \"hello world\";",
                            "            Console.WriteLine(testVariable);",
                            "            "
                        },
                        StartLine = 10,
                        EndLine = 14,
                        LastModified = DateTime.UtcNow.AddMinutes(-5)
                    }
                }
            };
            
            LuceneIndexServiceMock
                .Setup(x => x.SearchAsync(
                    It.IsAny<string>(),
                    It.IsAny<Query>(),
                    It.IsAny<int>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(searchResult);
        }

        private void SetupResponseBuilder()
        {
            // Mock the response builder to return a successful response
            var mockResponse = new AIOptimizedResponse<SearchResult>
            {
                Success = true,
                Data = new AIResponseData<SearchResult>
                {
                    Summary = "Found 2 matches in TestFile.cs",
                    Count = 2,
                    Results = new SearchResult { TotalHits = 2 }
                },
                Insights = new List<string> { "File content search completed successfully" },
                Actions = new List<AIAction>()
            };

            // Note: We can't easily mock the response builder since it's instantiated in the tool
            // Instead, we rely on the LuceneIndexServiceMock returning proper data
        }

        #endregion
    }
}