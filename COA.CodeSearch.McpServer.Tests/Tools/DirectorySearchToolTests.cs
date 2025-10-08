using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using FluentAssertions;
using Moq;
using Microsoft.Extensions.Logging;
using COA.Mcp.Framework.TokenOptimization.Models;
using COA.CodeSearch.McpServer.Tools;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Tools.Parameters;
using COA.CodeSearch.McpServer.Tools.Results;
using COA.CodeSearch.McpServer.Services.Lucene;
using COA.CodeSearch.McpServer.Tests.Base;
using Lucene.Net.Search;

namespace COA.CodeSearch.McpServer.Tests.Tools
{
    [TestFixture]
    public class DirectorySearchToolTests : CodeSearchToolTestBase<SearchFilesTool>
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
            // Assert - DirectorySearchToolTests now uses unified SearchFilesTool
            _tool.Name.Should().Be(ToolNames.SearchFiles);
            _tool.Description.Should().Contain("file"); // Unified tool searches files/directories
            _tool.Category.Should().Be(COA.Mcp.Framework.ToolCategory.Query);
        }
        
        [Test]
        public async Task ExecuteAsync_WithNoIndex_ShouldReturnIndexNotFoundError()
        {
            // Arrange
            SetupNoIndex();
            var parameters = new SearchFilesParameters
            {
                WorkspacePath = TestWorkspacePath,
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
            result.Error!.Code.Should().Be("INDEX_NOT_FOUND");
            result.Error.Message.Should().Contain("index");
        }
        
        [Test]
        public void TestGlobPatternMatching()
        {
            // Test the glob pattern matching logic
            var pattern = "*";
            var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";
            
            Console.WriteLine($"Pattern: {pattern}");
            Console.WriteLine($"Regex: {regexPattern}");
            
            var testNames = new[] { "src", "components", "tests", "unit" };
            foreach (var name in testNames)
            {
                var matches = System.Text.RegularExpressions.Regex.IsMatch(name, regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                Console.WriteLine($"  {name}: {matches}");
            }
            
            // All should match
            Assert.That(System.Text.RegularExpressions.Regex.IsMatch("src", regexPattern), Is.True);
            Assert.That(System.Text.RegularExpressions.Regex.IsMatch("components", regexPattern), Is.True);
        }
        
        [Test]
        public void TestPathProcessing()
        {
            // Simple test to understand the path processing logic
            var filePath = "/workspace/src/components/App.tsx";
            var normalizedPath = filePath.Replace('\\', '/').TrimStart('/');
            var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            
            Console.WriteLine($"Original: {filePath}");
            Console.WriteLine($"Normalized: {normalizedPath}");
            Console.WriteLine($"Segments: {string.Join(", ", segments)}");
            Console.WriteLine($"Segment count: {segments.Length}");
            
            var directories = new List<string>();
            var currentPath = "";
            for (int i = 0; i < segments.Length - 1; i++)
            {
                var segment = segments[i];
                currentPath = currentPath == "" ? segment : currentPath + "/" + segment;
                var fullPath = "/" + currentPath;
                directories.Add($"{segment}@{fullPath}");
                Console.WriteLine($"  i={i}: segment={segment}, currentPath={currentPath}, fullPath={fullPath}");
            }
            
            Console.WriteLine($"Directories found: {string.Join(", ", directories)}");
            
            // This should produce: workspace, src, components
            Assert.That(directories.Count, Is.EqualTo(3));
        }
        
        [Test]
        public async Task ExecuteAsync_WithIndexedFiles_ShouldExtractDirectories()
        {
            // Arrange
            SetupExistingIndex();
            var searchResult = new SearchResult
            {
                TotalHits = 3,
                Hits = new List<SearchHit>
                {
                    new() 
                    { 
                        FilePath = "/workspace/src/components/App.tsx", 
                        Score = 1.0f,
                        Fields = new Dictionary<string, string>
                        {
                            ["directory"] = "/workspace/src/components",
                            ["relativeDirectory"] = "src/components",
                            ["directoryName"] = "components"
                        }
                    },
                    new() 
                    { 
                        FilePath = "/workspace/src/services/Api.ts", 
                        Score = 0.9f,
                        Fields = new Dictionary<string, string>
                        {
                            ["directory"] = "/workspace/src/services",
                            ["relativeDirectory"] = "src/services",
                            ["directoryName"] = "services"
                        }
                    },
                    new() 
                    { 
                        FilePath = "/workspace/tests/unit/App.test.tsx", 
                        Score = 0.8f,
                        Fields = new Dictionary<string, string>
                        {
                            ["directory"] = "/workspace/tests/unit",
                            ["relativeDirectory"] = "tests/unit",
                            ["directoryName"] = "unit"
                        }
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
            
            CacheKeyGeneratorMock.Setup(x => x.GenerateKey(It.IsAny<string>(), It.IsAny<object>()))
                .Returns("test-key");
            
            var parameters = new SearchFilesParameters
            {
                WorkspacePath = "/workspace",
                Pattern = "*",
                IncludeSubdirectories = true,
                MaxTokens = 25000
            };
            
            // Act
            var result = await _tool.ExecuteAsync(parameters);
            
            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Data.Should().NotBeNull();
            
            var searchResultData = result.Data!.Results as SearchFilesResult;
            searchResultData.Should().NotBeNull();
            searchResultData!.Directories.Should().NotBeEmpty();
            
            // Should have extracted unique directories
            var dirNames = searchResultData.Directories.Select(d => d.DirectoryName).ToList();
            
            // Now the test should work with the directory fields properly set
            dirNames.Should().Contain("src");
            dirNames.Should().Contain("tests");
            dirNames.Should().Contain("components");
            dirNames.Should().Contain("services");
            dirNames.Should().Contain("unit");
        }
        
        [Test]
        public async Task ExecuteAsync_WithPattern_ShouldFilterDirectories()
        {
            // Arrange
            SetupExistingIndex();
            var searchResult = new SearchResult
            {
                TotalHits = 5,
                Hits = new List<SearchHit>
                {
                    new() 
                    { 
                        FilePath = "/workspace/src/index.ts",
                        Fields = new Dictionary<string, string>
                        {
                            ["directory"] = "/workspace/src",
                            ["relativeDirectory"] = "src",
                            ["directoryName"] = "src"
                        }
                    },
                    new() 
                    { 
                        FilePath = "/workspace/tests/test.spec.ts",
                        Fields = new Dictionary<string, string>
                        {
                            ["directory"] = "/workspace/tests",
                            ["relativeDirectory"] = "tests",
                            ["directoryName"] = "tests"
                        }
                    },
                    new() 
                    { 
                        FilePath = "/workspace/docs/readme.md",
                        Fields = new Dictionary<string, string>
                        {
                            ["directory"] = "/workspace/docs",
                            ["relativeDirectory"] = "docs",
                            ["directoryName"] = "docs"
                        }
                    },
                    new() 
                    { 
                        FilePath = "/workspace/bin/output.dll",
                        Fields = new Dictionary<string, string>
                        {
                            ["directory"] = "/workspace/bin",
                            ["relativeDirectory"] = "bin",
                            ["directoryName"] = "bin"
                        }
                    },
                    new() 
                    { 
                        FilePath = "/workspace/node_modules/package/index.js",
                        Fields = new Dictionary<string, string>
                        {
                            ["directory"] = "/workspace/node_modules/package",
                            ["relativeDirectory"] = "node_modules/package",
                            ["directoryName"] = "package"
                        }
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
            
            CacheKeyGeneratorMock.Setup(x => x.GenerateKey(It.IsAny<string>(), It.IsAny<object>()))
                .Returns("test-key");
            
            var parameters = new SearchFilesParameters
            {
                WorkspacePath = "/workspace",
                Pattern = "*test*"
            };
            
            // Act
            var result = await _tool.ExecuteAsync(parameters);
            
            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            
            var searchResultData = result.Data!.Results as SearchFilesResult;
            searchResultData.Should().NotBeNull();
            searchResultData!.Directories.Should().NotBeEmpty();
            
            // Should only match "tests" directory
            searchResultData.Directories.Should().HaveCount(1);
            searchResultData.Directories[0].DirectoryName.Should().Be("tests");
        }
        
        [Test]
        public async Task ExecuteAsync_ShouldExcludeBuildDirectories()
        {
            // Arrange
            SetupExistingIndex();
            var searchResult = new SearchResult
            {
                TotalHits = 4,
                Hits = new List<SearchHit>
                {
                    new() 
                    { 
                        FilePath = "/workspace/src/app.ts",
                        Fields = new Dictionary<string, string>
                        {
                            ["directory"] = "/workspace/src",
                            ["relativeDirectory"] = "src",
                            ["directoryName"] = "src"
                        }
                    },
                    new() 
                    { 
                        FilePath = "/workspace/bin/debug/app.dll",
                        Fields = new Dictionary<string, string>
                        {
                            ["directory"] = "/workspace/bin/debug",
                            ["relativeDirectory"] = "bin/debug",
                            ["directoryName"] = "debug"
                        }
                    },
                    new() 
                    { 
                        FilePath = "/workspace/obj/temp.obj",
                        Fields = new Dictionary<string, string>
                        {
                            ["directory"] = "/workspace/obj",
                            ["relativeDirectory"] = "obj",
                            ["directoryName"] = "obj"
                        }
                    },
                    new() 
                    { 
                        FilePath = "/workspace/node_modules/lib/index.js",
                        Fields = new Dictionary<string, string>
                        {
                            ["directory"] = "/workspace/node_modules/lib",
                            ["relativeDirectory"] = "node_modules/lib",
                            ["directoryName"] = "lib"
                        }
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
            
            CacheKeyGeneratorMock.Setup(x => x.GenerateKey(It.IsAny<string>(), It.IsAny<object>()))
                .Returns("test-key");
            
            var parameters = new SearchFilesParameters
            {
                WorkspacePath = "/workspace",
                Pattern = "*"
            };
            
            // Act
            var result = await _tool.ExecuteAsync(parameters);
            
            // Assert
            var searchResultData = result.Data!.Results as SearchFilesResult;
            searchResultData.Should().NotBeNull();
            
            // Should only have "src" directory, not bin, obj, or node_modules
            var dirNames = searchResultData!.Directories.Select(d => d.DirectoryName).ToList();
            dirNames.Should().Contain("src");
            dirNames.Should().NotContain("bin");
            dirNames.Should().NotContain("obj");
            dirNames.Should().NotContain("node_modules");
        }
        
        [Test]
        public async Task ExecuteAsync_WithRegexPattern_ShouldMatchCorrectly()
        {
            // Arrange
            SetupExistingIndex();
            var searchResult = new SearchResult
            {
                TotalHits = 3,
                Hits = new List<SearchHit>
                {
                    new() 
                    { 
                        FilePath = "/workspace/src/index.ts",
                        Fields = new Dictionary<string, string>
                        {
                            ["directory"] = "/workspace/src",
                            ["relativeDirectory"] = "src",
                            ["directoryName"] = "src"
                        }
                    },
                    new() 
                    { 
                        FilePath = "/workspace/tests/test.ts",
                        Fields = new Dictionary<string, string>
                        {
                            ["directory"] = "/workspace/tests",
                            ["relativeDirectory"] = "tests",
                            ["directoryName"] = "tests"
                        }
                    },
                    new() 
                    { 
                        FilePath = "/workspace/docs/readme.md",
                        Fields = new Dictionary<string, string>
                        {
                            ["directory"] = "/workspace/docs",
                            ["relativeDirectory"] = "docs",
                            ["directoryName"] = "docs"
                        }
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
            
            CacheKeyGeneratorMock.Setup(x => x.GenerateKey(It.IsAny<string>(), It.IsAny<object>()))
                .Returns("test-key");
            
            var parameters = new SearchFilesParameters
            {
                WorkspacePath = "/workspace",
                Pattern = "^(src|tests)$",
                UseRegex = true,
                MaxTokens = 25000
            };
            
            // Act
            var result = await _tool.ExecuteAsync(parameters);
            
            // Assert
            var searchResultData = result.Data!.Results as SearchFilesResult;
            searchResultData.Should().NotBeNull();
            
            var dirNames = searchResultData!.Directories.Select(d => d.DirectoryName).ToList();
            dirNames.Should().Contain("src");
            dirNames.Should().Contain("tests");
            dirNames.Should().NotContain("docs");
        }
        
        [Test]
        public async Task ExecuteAsync_WithCacheEnabled_ShouldUseCachedResults()
        {
            // Arrange
            SetupExistingIndex();
            var cachedResponse = new AIOptimizedResponse<SearchFilesResult>
            {
                Success = true,
                Data = new AIResponseData<SearchFilesResult>
                {
                    Results = new SearchFilesResult
                    {
                        Directories = new List<COA.CodeSearch.McpServer.Models.DirectoryMatch>
                        {
                            new() { DirectoryName = "cached-dir", DirectoryPath = "/cached" }
                        },
                        TotalMatches = 1
                    }
                },
                Meta = new AIResponseMeta()
            };
            
            ResponseCacheServiceMock
                .Setup(x => x.GetAsync<AIOptimizedResponse<SearchFilesResult>>(It.IsAny<string>()))
                .ReturnsAsync(cachedResponse);
            
            CacheKeyGeneratorMock.Setup(x => x.GenerateKey(It.IsAny<string>(), It.IsAny<object>()))
                .Returns("test-key");
            
            var parameters = new SearchFilesParameters
            {
                WorkspacePath = "/workspace",
                Pattern = "*",
                NoCache = false
            };
            
            // Act
            var result = await _tool.ExecuteAsync(parameters);
            
            // Assert
            result.Should().BeSameAs(cachedResponse);
            result.Meta!.ExtensionData!["cacheHit"].Should().Be(true);
            
            // Verify Lucene was not called
            LuceneIndexServiceMock.Verify(
                x => x.SearchAsync(It.IsAny<string>(), It.IsAny<Query>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
        
        [Test]
        [Ignore("Depth property not available in unified SearchFilesTool - feature removed")]
        public async Task ExecuteAsync_ShouldCalculateDepthCorrectly()
        {
            // Arrange
            SetupExistingIndex();
            var searchResult = new SearchResult
            {
                TotalHits = 2,
                Hits = new List<SearchHit>
                {
                    new() 
                    { 
                        FilePath = "/workspace/src/components/deep/nested/file.ts",
                        Fields = new Dictionary<string, string>
                        {
                            ["directory"] = "/workspace/src/components/deep/nested",
                            ["relativeDirectory"] = "src/components/deep/nested",
                            ["directoryName"] = "nested"
                        }
                    },
                    new() 
                    { 
                        FilePath = "/workspace/tests/file.ts",
                        Fields = new Dictionary<string, string>
                        {
                            ["directory"] = "/workspace/tests",
                            ["relativeDirectory"] = "tests",
                            ["directoryName"] = "tests"
                        }
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
            
            CacheKeyGeneratorMock.Setup(x => x.GenerateKey(It.IsAny<string>(), It.IsAny<object>()))
                .Returns("test-key");
            
            var parameters = new SearchFilesParameters
            {
                WorkspacePath = "/workspace",
                Pattern = "*",
                MaxTokens = 25000
            };
            
            // Act
            var result = await _tool.ExecuteAsync(parameters);
            
            // Assert
            var searchResultData = result.Data!.Results as SearchFilesResult;
            searchResultData.Should().NotBeNull();
            
            var dirs = searchResultData!.Directories;
            
            // Check depths - commented out because Depth property no longer exists in unified tool
            var srcDir = dirs.FirstOrDefault(d => d.DirectoryName == "src");
            srcDir.Should().NotBeNull();
            // srcDir!.Depth.Should().Be(1);

            var componentsDir = dirs.FirstOrDefault(d => d.DirectoryName == "components");
            componentsDir.Should().NotBeNull();
            // componentsDir!.Depth.Should().Be(2);

            var deepDir = dirs.FirstOrDefault(d => d.DirectoryName == "deep");
            deepDir.Should().NotBeNull();
            // deepDir!.Depth.Should().Be(3);
        }
        
        [Test]
        [Ignore("FileCount property not available in unified SearchFilesTool - feature removed")]
        public async Task ExecuteAsync_ShouldCalculateFileCounts()
        {
            // Arrange
            SetupExistingIndex();
            var searchResult = new SearchResult
            {
                TotalHits = 4,
                Hits = new List<SearchHit>
                {
                    new() 
                    { 
                        FilePath = "/workspace/src/file1.ts",
                        Fields = new Dictionary<string, string>
                        {
                            ["directory"] = "/workspace/src",
                            ["relativeDirectory"] = "src",
                            ["directoryName"] = "src"
                        }
                    },
                    new() 
                    { 
                        FilePath = "/workspace/src/file2.ts",
                        Fields = new Dictionary<string, string>
                        {
                            ["directory"] = "/workspace/src",
                            ["relativeDirectory"] = "src",
                            ["directoryName"] = "src"
                        }
                    },
                    new() 
                    { 
                        FilePath = "/workspace/src/file3.ts",
                        Fields = new Dictionary<string, string>
                        {
                            ["directory"] = "/workspace/src",
                            ["relativeDirectory"] = "src",
                            ["directoryName"] = "src"
                        }
                    },
                    new() 
                    { 
                        FilePath = "/workspace/tests/test.ts",
                        Fields = new Dictionary<string, string>
                        {
                            ["directory"] = "/workspace/tests",
                            ["relativeDirectory"] = "tests",
                            ["directoryName"] = "tests"
                        }
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
            
            CacheKeyGeneratorMock.Setup(x => x.GenerateKey(It.IsAny<string>(), It.IsAny<object>()))
                .Returns("test-key");
            
            var parameters = new SearchFilesParameters
            {
                WorkspacePath = "/workspace",
                Pattern = "*",
                MaxTokens = 25000
            };
            
            // Act
            var result = await _tool.ExecuteAsync(parameters);
            
            // Assert
            var searchResultData = result.Data!.Results as SearchFilesResult;
            searchResultData.Should().NotBeNull();
            
            var srcDir = searchResultData!.Directories.FirstOrDefault(d => d.DirectoryName == "src");
            srcDir.Should().NotBeNull();
            // srcDir!.FileCount.Should().Be(3); // FileCount property no longer exists in unified tool

            var testsDir = searchResultData.Directories.FirstOrDefault(d => d.DirectoryName == "tests");
            testsDir.Should().NotBeNull();
            // testsDir!.FileCount.Should().Be(1); // FileCount property no longer exists in unified tool
        }
    }
}