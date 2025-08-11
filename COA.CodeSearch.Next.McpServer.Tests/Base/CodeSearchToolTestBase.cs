using COA.Mcp.Framework.Testing.Base;
using COA.CodeSearch.Next.McpServer.Services;
using COA.CodeSearch.Next.McpServer.Services.Lucene;
using COA.CodeSearch.Next.McpServer.Models;
using COA.Mcp.Framework.TokenOptimization.Storage;
using COA.Mcp.Framework.TokenOptimization.Caching;
using COA.Mcp.Framework.TokenOptimization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;
using Lucene.Net.Search;

namespace COA.CodeSearch.Next.McpServer.Tests.Base
{
    /// <summary>
    /// Base class for CodeSearch tool tests, providing common infrastructure and mocks.
    /// </summary>
    /// <typeparam name="TTool">The type of tool being tested.</typeparam>
    public abstract class CodeSearchToolTestBase<TTool> : ToolTestBase<TTool> where TTool : class
    {
        // Service mocks
        protected Mock<ILuceneIndexService> LuceneIndexServiceMock { get; private set; } = null!;
        protected Mock<IPathResolutionService> PathResolutionServiceMock { get; private set; } = null!;
        protected Mock<IResponseCacheService> ResponseCacheServiceMock { get; private set; } = null!;
        protected Mock<IResourceStorageService> ResourceStorageServiceMock { get; private set; } = null!;
        protected Mock<ICacheKeyGenerator> CacheKeyGeneratorMock { get; private set; } = null!;
        protected Mock<IFileIndexingService> FileIndexingServiceMock { get; private set; } = null!;
        protected Mock<ICircuitBreakerService> CircuitBreakerServiceMock { get; private set; } = null!;
        protected Mock<IMemoryPressureService> MemoryPressureServiceMock { get; private set; } = null!;
        protected Mock<IQueryCacheService> QueryCacheServiceMock { get; private set; } = null!;
        
        // Test workspace paths
        protected string TestWorkspacePath { get; private set; } = null!;
        protected string TestIndexPath { get; private set; } = null!;
        
        protected override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);
            
            // Create service mocks
            LuceneIndexServiceMock = CreateMock<ILuceneIndexService>();
            PathResolutionServiceMock = CreateMock<IPathResolutionService>();
            ResponseCacheServiceMock = CreateMock<IResponseCacheService>();
            ResourceStorageServiceMock = CreateMock<IResourceStorageService>();
            CacheKeyGeneratorMock = CreateMock<ICacheKeyGenerator>();
            FileIndexingServiceMock = CreateMock<IFileIndexingService>();
            CircuitBreakerServiceMock = CreateMock<ICircuitBreakerService>();
            MemoryPressureServiceMock = CreateMock<IMemoryPressureService>();
            QueryCacheServiceMock = CreateMock<IQueryCacheService>();
            
            // Add real services that don't need mocking
            services.AddMemoryCache();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            
            // Setup default mock behaviors
            SetupDefaultMockBehaviors();
        }
        
        protected override void OnSetUp()
        {
            base.OnSetUp();
            
            // Setup test paths
            TestWorkspacePath = Path.Combine(Path.GetTempPath(), "codesearch-test", Guid.NewGuid().ToString());
            TestIndexPath = Path.Combine(TestWorkspacePath, ".coa", "indexes");
            
            // Ensure test directory exists
            Directory.CreateDirectory(TestWorkspacePath);
        }
        
        protected override void OnTearDown()
        {
            // Clean up test directories
            if (Directory.Exists(TestWorkspacePath))
            {
                try
                {
                    Directory.Delete(TestWorkspacePath, true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
            
            base.OnTearDown();
        }
        
        /// <summary>
        /// Sets up default mock behaviors for common scenarios.
        /// </summary>
        protected virtual void SetupDefaultMockBehaviors()
        {
            // Path resolution service defaults
            PathResolutionServiceMock
                .Setup(x => x.GetIndexPath(It.IsAny<string>()))
                .Returns<string>(workspace => Path.Combine(TestIndexPath, "test-hash"));
                
            PathResolutionServiceMock
                .Setup(x => x.EnsureDirectoryExists(It.IsAny<string>()))
                .Callback<string>(path => Directory.CreateDirectory(path));
            
            // Cache key generator defaults
            CacheKeyGeneratorMock
                .Setup(x => x.GenerateKey(It.IsAny<string>(), It.IsAny<object>()))
                .Returns<string, object>((tool, param) => $"{tool}:{param?.GetHashCode()}");
            
            // Memory pressure defaults
            MemoryPressureServiceMock
                .Setup(x => x.GetCurrentPressureLevel())
                .Returns(MemoryPressureLevel.Normal);
            MemoryPressureServiceMock
                .Setup(x => x.ShouldThrottleOperation(It.IsAny<string>()))
                .Returns(false);
            
            // Circuit breaker defaults
            CircuitBreakerServiceMock
                .Setup(x => x.ExecuteAsync(
                    It.IsAny<string>(),
                    It.IsAny<Func<Task>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<string, Func<Task>, CancellationToken>(
                    async (key, func, ct) => await func());
        }
        
        /// <summary>
        /// Helper method to setup index exists mock behavior.
        /// </summary>
        protected void SetupIndexExists(string workspacePath, bool exists = true)
        {
            LuceneIndexServiceMock
                .Setup(x => x.IndexExistsAsync(workspacePath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(exists);
        }
        
        /// <summary>
        /// Helper method to setup search results mock behavior.
        /// </summary>
        protected void SetupSearchResults(string workspacePath, SearchResult result)
        {
            LuceneIndexServiceMock
                .Setup(x => x.SearchAsync(workspacePath, It.IsAny<Query>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(result);
        }
        
        /// <summary>
        /// Sets up the Lucene index service to simulate an existing index.
        /// </summary>
        protected void SetupExistingIndex(int documentCount = 100)
        {
            LuceneIndexServiceMock
                .Setup(x => x.IndexExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
                
            LuceneIndexServiceMock
                .Setup(x => x.GetDocumentCountAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(documentCount);
                
            LuceneIndexServiceMock
                .Setup(x => x.InitializeIndexAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Services.Lucene.IndexInitResult
                {
                    Success = true,
                    IsNewIndex = false,
                    WorkspaceHash = "test-hash",
                    IndexPath = TestIndexPath
                });
                
            LuceneIndexServiceMock
                .Setup(x => x.GetStatisticsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Services.Lucene.IndexStatistics
                {
                    DocumentCount = documentCount,
                    DeletedDocumentCount = 0,
                    SegmentCount = 1,
                    IndexSizeBytes = documentCount * 1024,
                    FileTypeDistribution = new Dictionary<string, int>
                    {
                        [".cs"] = documentCount / 2,
                        [".json"] = documentCount / 4,
                        [".xml"] = documentCount / 4
                    }
                });
        }
        
        /// <summary>
        /// Sets up the Lucene index service to simulate no existing index.
        /// </summary>
        protected void SetupNoIndex()
        {
            LuceneIndexServiceMock
                .Setup(x => x.IndexExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
        }
        
        /// <summary>
        /// Creates a test search result.
        /// </summary>
        protected Services.Lucene.SearchResult CreateTestSearchResult(int hitCount = 10)
        {
            var hits = new List<Services.Lucene.SearchHit>();
            for (int i = 0; i < hitCount; i++)
            {
                hits.Add(new Services.Lucene.SearchHit
                {
                    FilePath = $"/test/file{i}.cs",
                    Score = 1.0f - (i * 0.1f),
                    Content = $"Test content for file {i}"
                    // Note: SearchHit in Services.Lucene doesn't have LineNumber or MatchedField
                });
            }
            
            return new Services.Lucene.SearchResult
            {
                Query = "test query",
                TotalHits = hitCount,
                Hits = hits,
                SearchTime = TimeSpan.FromMilliseconds(100)
            };
        }
    }
}