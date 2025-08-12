using NUnit.Framework;
using FluentAssertions;
using Moq;
using COA.CodeSearch.McpServer.Tools;
using COA.CodeSearch.McpServer.Tests.Base;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services.Lucene;
using COA.Mcp.Framework.TokenOptimization.Models;
using COA.Mcp.Framework.TokenOptimization.Caching;
using COA.Mcp.Framework.TokenOptimization.Storage;
using Lucene.Net.Search;
using System.Threading.Tasks;
using System.Threading;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using COA.Mcp.Framework.Models;
using System.ComponentModel.DataAnnotations;
using COA.Mcp.Framework.Exceptions;

namespace COA.CodeSearch.McpServer.Tests.Tools
{
    [TestFixture]
    public class RecentFilesToolTests : CodeSearchToolTestBase<RecentFilesTool>
    {
        private RecentFilesTool _tool = null!;
        
        protected override RecentFilesTool CreateTool()
        {
            _tool = new RecentFilesTool(
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
            _tool.Name.Should().Be(ToolNames.RecentFiles);
            _tool.Description.Should().NotBeNullOrWhiteSpace();
            _tool.Category.Should().Be(COA.Mcp.Framework.ToolCategory.Query);
        }
        
        [Test]
        public async Task ExecuteAsync_Should_Return_Error_When_Workspace_Not_Indexed()
        {
            // Arrange
            SetupNoIndex();
            var parameters = new RecentFilesParameters
            {
                WorkspacePath = TestWorkspacePath,
                TimeFrame = "1d"
            };
            
            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);
            
            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeFalse();
            result.Error.Should().NotBeNull();
            result.Error!.Code.Should().Be("NO_INDEX");
            result.Error.Message.Should().Contain(TestWorkspacePath);
            
            // Should have recovery actions
            result.Actions.Should().NotBeEmpty();
            result.Actions.Should().Contain(a => a.Action == ToolNames.IndexWorkspace);
        }
        
        [Test]
        public async Task ExecuteAsync_Should_Return_Recent_Files_When_Found()
        {
            // Arrange
            var recentTime = DateTime.UtcNow.AddHours(-2);
            var oldTime = DateTime.UtcNow.AddDays(-10);
            
            SetupIndexExists(TestWorkspacePath);
            SetupSearchResultsWithHits(
                CreateSearchHit("file1.cs", recentTime, 1024),
                CreateSearchHit("file2.js", recentTime.AddMinutes(-30), 2048),
                CreateSearchHit("old-file.txt", oldTime, 512) // This should be filtered out by time
            );
            
            var parameters = new RecentFilesParameters
            {
                WorkspacePath = TestWorkspacePath,
                TimeFrame = "1d",
                MaxResults = 10
            };
            
            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);
            
            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Data.Should().NotBeNull();
            result.Data!.Results.Should().NotBeNull();
            
            // Should have recent files (old file should be filtered out by date range query)
            result.Data.Results.Files.Should().HaveCountGreaterThan(0);
            result.Data.Results.TimeFrameRequested.Should().Be("1d");
            result.Data.Results.TotalFiles.Should().BeGreaterThan(0);
            
            // Files should be sorted by modification time (most recent first)
            var files = result.Data.Results.Files;
            if (files.Count > 1)
            {
                files[0].LastModified.Should().BeAfter(files[1].LastModified);
            }
        }
        
        [Test]
        public async Task ExecuteAsync_Should_Filter_By_Extension_When_Specified()
        {
            // Arrange
            var recentTime = DateTime.UtcNow.AddHours(-1);
            
            SetupIndexExists(TestWorkspacePath);
            SetupSearchResultsWithHits(
                CreateSearchHit("file1.cs", recentTime, 1024),
                CreateSearchHit("file2.js", recentTime.AddMinutes(-15), 2048),
                CreateSearchHit("file3.txt", recentTime.AddMinutes(-30), 512)
            );
            
            var parameters = new RecentFilesParameters
            {
                WorkspacePath = TestWorkspacePath,
                TimeFrame = "1d",
                ExtensionFilter = ".cs,.js" // Should exclude .txt file
            };
            
            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);
            
            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Data!.Results.Files.Should().NotContain(f => f.Extension == ".txt");
            result.Data.Results.Files.Should().OnlyContain(f => f.Extension == ".cs" || f.Extension == ".js");
        }
        
        [Test]
        public async Task ExecuteAsync_Should_Use_Cache_When_Available()
        {
            // Arrange
            var cachedResponse = new AIOptimizedResponse<RecentFilesResult>
            {
                Success = true,
                Data = new AIResponseData<RecentFilesResult>
                {
                    Results = new RecentFilesResult
                    {
                        Files = new List<RecentFileInfo>
                        {
                            new RecentFileInfo { FilePath = "cached-file.cs", LastModified = DateTime.UtcNow.AddHours(-1) }
                        },
                        TotalFiles = 1
                    }
                },
                Meta = new AIResponseMeta()
            };
            
            ResponseCacheServiceMock
                .Setup(x => x.GetAsync<AIOptimizedResponse<RecentFilesResult>>(It.IsAny<string>()))
                .ReturnsAsync(cachedResponse);
            
            var parameters = new RecentFilesParameters
            {
                WorkspacePath = TestWorkspacePath,
                TimeFrame = "1h"
            };
            
            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);
            
            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Data!.Results.Files.Should().HaveCount(1);
            result.Data.Results.Files[0].FilePath.Should().Be("cached-file.cs");
            result.Meta!.ExtensionData.Should().ContainKey("cacheHit");
            
            // Should not have called the index service
            LuceneIndexServiceMock.Verify(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<Query>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        
        [Test]
        public async Task ExecuteAsync_Should_Skip_Cache_When_NoCache_Is_True()
        {
            // Arrange
            SetupIndexExists(TestWorkspacePath);
            SetupSearchResultsWithHits(
                CreateSearchHit("file1.cs", DateTime.UtcNow.AddMinutes(-30), 1024)
            );
            
            // Setup cache to return something, but it should be ignored
            ResponseCacheServiceMock
                .Setup(x => x.GetAsync<AIOptimizedResponse<RecentFilesResult>>(It.IsAny<string>()))
                .ReturnsAsync(new AIOptimizedResponse<RecentFilesResult> { Success = true });
            
            var parameters = new RecentFilesParameters
            {
                WorkspacePath = TestWorkspacePath,
                TimeFrame = "1h",
                NoCache = true
            };
            
            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);
            
            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            
            // Should not have checked cache
            ResponseCacheServiceMock.Verify(x => x.GetAsync<AIOptimizedResponse<RecentFilesResult>>(It.IsAny<string>()), Times.Never);
            
            // Should have called the index service
            LuceneIndexServiceMock.Verify(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<Query>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        }
        
        [Test]
        [TestCase("1h", 0.04167)] // 1 hour = 1/24 days
        [TestCase("2d", 2)]
        [TestCase("1w", 7)]
        [TestCase("30min", 0.02083)] // 30 minutes = 30/1440 days
        public async Task ExecuteAsync_Should_Parse_TimeFrame_Correctly(string timeFrame, double expectedDays)
        {
            // Arrange
            SetupIndexExists(TestWorkspacePath);
            SetupSearchResultsWithHits();
            
            var parameters = new RecentFilesParameters
            {
                WorkspacePath = TestWorkspacePath,
                TimeFrame = timeFrame
            };
            
            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);
            
            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Data!.Results!.TimeFrameRequested.Should().Be(timeFrame);
            
            // Verify the cutoff time is approximately correct (within 1 minute tolerance)
            var expectedCutoff = DateTime.UtcNow.AddDays(-expectedDays);
            var actualCutoff = result.Data.Results!.CutoffTime;
            Math.Abs((expectedCutoff - actualCutoff).TotalMinutes).Should().BeLessThan(1);
        }
        
        [Test]
        public async Task ExecuteAsync_Should_Handle_Invalid_TimeFrame_Format()
        {
            // Arrange
            var parameters = new RecentFilesParameters
            {
                WorkspacePath = TestWorkspacePath,
                TimeFrame = "invalid-format"
            };
            
            // Act & Assert  
            Assert.ThrowsAsync<COA.Mcp.Framework.Exceptions.ToolExecutionException>(async () => 
                await _tool.ExecuteAsync(parameters, CancellationToken.None));
        }
        
        [Test]
        public async Task ExecuteAsync_Should_Validate_Required_Parameters()
        {
            // Arrange
            var parameters = new RecentFilesParameters
            {
                WorkspacePath = "", // Empty workspace path
                TimeFrame = "1d"
            };
            
            // Act & Assert
            Assert.ThrowsAsync<ToolExecutionException>(async () => 
            {
                await _tool.ExecuteAsync(parameters, CancellationToken.None);
            });
        }
        
        [Test]
        public async Task ExecuteAsync_Should_Validate_MaxResults_Range()
        {
            // Arrange
            var parameters = new RecentFilesParameters
            {
                WorkspacePath = TestWorkspacePath,
                TimeFrame = "1d",
                MaxResults = 1000 // Above maximum of 500
            };
            
            // Act & Assert
            Assert.ThrowsAsync<ToolExecutionException>(async () => 
            {
                await _tool.ExecuteAsync(parameters, CancellationToken.None);
            });
        }
        
        [Test]
        public async Task ExecuteAsync_Should_Cache_Successful_Results()
        {
            // Arrange
            SetupIndexExists(TestWorkspacePath);
            SetupSearchResultsWithHits(
                CreateSearchHit("file1.cs", DateTime.UtcNow.AddHours(-1), 1024)
            );
            
            var parameters = new RecentFilesParameters
            {
                WorkspacePath = TestWorkspacePath,
                TimeFrame = "1d"
            };
            
            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);
            
            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            
            // Should have cached the result
            ResponseCacheServiceMock.Verify(
                x => x.SetAsync(
                    It.IsAny<string>(),
                    It.IsAny<AIOptimizedResponse<RecentFilesResult>>(),
                    It.Is<CacheEntryOptions>(opts => opts.AbsoluteExpiration == TimeSpan.FromMinutes(5))),
                Times.Once);
        }
        
        [Test]
        public async Task ExecuteAsync_Should_Sort_Files_By_Most_Recent_First()
        {
            // Arrange
            var time1 = DateTime.UtcNow.AddHours(-3);
            var time2 = DateTime.UtcNow.AddHours(-1); // Most recent
            var time3 = DateTime.UtcNow.AddHours(-2);
            
            SetupIndexExists(TestWorkspacePath);
            SetupSearchResultsWithHits(
                CreateSearchHit("file1.cs", time1, 1024),
                CreateSearchHit("file2.js", time2, 2048), // Should be first
                CreateSearchHit("file3.txt", time3, 512)
            );
            
            var parameters = new RecentFilesParameters
            {
                WorkspacePath = TestWorkspacePath,
                TimeFrame = "1d"
            };
            
            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);
            
            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            
            var files = result.Data!.Results.Files;
            files.Should().HaveCount(3);
            files[0].FileName.Should().Be("file2.js"); // Most recent first
            files[1].FileName.Should().Be("file3.txt");
            files[2].FileName.Should().Be("file1.cs"); // Oldest last
        }
        
        [Test]
        public async Task ExecuteAsync_Should_Populate_ModifiedAgo_TimeSpan()
        {
            // Arrange
            var modifiedTime = DateTime.UtcNow.AddHours(-2);
            
            SetupIndexExists(TestWorkspacePath);
            SetupSearchResultsWithHits(
                CreateSearchHit("file1.cs", modifiedTime, 1024)
            );
            
            var parameters = new RecentFilesParameters
            {
                WorkspacePath = TestWorkspacePath,
                TimeFrame = "1d"
            };
            
            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);
            
            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            
            var file = result.Data!.Results.Files.Should().ContainSingle().Subject;
            file.LastModified.Should().Be(modifiedTime);
            file.ModifiedAgo.Should().BeCloseTo(TimeSpan.FromHours(2), TimeSpan.FromMinutes(1));
        }
        
        // Helper method to create search hits for testing
        private SearchHit CreateSearchHit(string filePath, DateTime lastModified, long fileSize)
        {
            return new SearchHit
            {
                FilePath = Path.Combine(TestWorkspacePath, filePath),
                LastModified = lastModified,
                Fields = new Dictionary<string, string>
                {
                    ["size"] = fileSize.ToString()
                },
                Score = 1.0f
            };
        }

        // Helper method to setup search results with multiple hits
        private void SetupSearchResultsWithHits(params SearchHit[] hits)
        {
            SetupSearchResults(TestWorkspacePath, new SearchResult
            {
                Hits = hits.ToList(),
                TotalHits = hits.Length
            });
        }
    }
}