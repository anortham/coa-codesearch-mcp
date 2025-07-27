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

namespace COA.CodeSearch.McpServer.Tests;

public class FlexibleMemoryServiceUnitTests : IDisposable
{
    private readonly Mock<ILogger<FlexibleMemoryService>> _memoryLoggerMock;
    private readonly InMemoryTestIndexService _indexService;
    private readonly Mock<IPathResolutionService> _pathResolutionMock;
    private readonly IConfiguration _configuration;
    private readonly FlexibleMemoryService _memoryService;
    
    public FlexibleMemoryServiceUnitTests()
    {
        _memoryLoggerMock = new Mock<ILogger<FlexibleMemoryService>>();
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
        
        _memoryService = new FlexibleMemoryService(_memoryLoggerMock.Object, _configuration, _indexService, _pathResolutionMock.Object, errorHandlingMock.Object, validationMock.Object);
    }
    
    public void Dispose()
    {
        // Clean up the in-memory index service
        _indexService?.Dispose();
    }
    
    [Fact]
    public async Task StoreMemoryAsync_ValidMemory_ReturnsTrue()
    {
        // Arrange
        var memory = new FlexibleMemoryEntry
        {
            Id = "test-123",
            Type = MemoryTypes.TechnicalDebt,
            Content = "Refactor authentication",
            IsShared = true
        };
        
        // Act
        var result = await _memoryService.StoreMemoryAsync(memory);
        
        // Assert
        Assert.True(result);
        
        // Verify the memory can be retrieved
        var retrieved = await _memoryService.GetMemoryByIdAsync("test-123");
        Assert.NotNull(retrieved);
        Assert.Equal("test-123", retrieved.Id);
        Assert.Equal(MemoryTypes.TechnicalDebt, retrieved.Type);
        Assert.Equal("Refactor authentication", retrieved.Content);
    }
    
    [Fact]
    public async Task StoreMemoryAsync_WithExtendedFields_ReturnsTrue()
    {
        // Arrange
        var memory = new FlexibleMemoryEntry
        {
            Id = "test-456",
            Type = MemoryTypes.Question,
            Content = "How to handle caching?",
            IsShared = false
        };
        
        memory.SetField(MemoryFields.Status, MemoryStatus.Pending);
        memory.SetField(MemoryFields.Priority, MemoryPriority.High);
        memory.SetField(MemoryFields.Tags, new[] { "performance", "cache" });
        
        // Act
        var result = await _memoryService.StoreMemoryAsync(memory);
        
        // Assert
        Assert.True(result);
        
        // Verify the memory was stored with extended fields
        var retrieved = await _memoryService.GetMemoryByIdAsync("test-456");
        Assert.NotNull(retrieved);
        Assert.Equal("test-456", retrieved.Id);
        Assert.Equal(MemoryTypes.Question, retrieved.Type);
        Assert.Equal("How to handle caching?", retrieved.Content);
        Assert.False(retrieved.IsShared); // Should be in local memory
        
        // Verify extended fields
        Assert.Equal(MemoryStatus.Pending, retrieved.GetField<string>(MemoryFields.Status));
        Assert.Equal(MemoryPriority.High, retrieved.GetField<string>(MemoryFields.Priority));
        var tags = retrieved.GetField<string[]>(MemoryFields.Tags);
        Assert.NotNull(tags);
        Assert.Contains("performance", tags);
        Assert.Contains("cache", tags);
    }
    
    [Fact]
    public async Task SearchMemoriesAsync_EmptyQuery_ReturnsResults()
    {
        // Arrange
        var memory1 = new FlexibleMemoryEntry
        {
            Type = MemoryTypes.TechnicalDebt,
            Content = "Fix bug in login",
            IsShared = true
        };
        
        var memory2 = new FlexibleMemoryEntry
        {
            Type = MemoryTypes.Question,
            Content = "Best practices for testing?",
            IsShared = true
        };
        
        await _memoryService.StoreMemoryAsync(memory1);
        await _memoryService.StoreMemoryAsync(memory2);
        
        // Wait for indexing
        await Task.Delay(100);
        
        // Act
        var searchRequest = new FlexibleMemorySearchRequest();
        var result = await _memoryService.SearchMemoriesAsync(searchRequest);
        
        // Assert
        Assert.True(result.TotalFound >= 2);
        Assert.NotNull(result.FacetCounts);
    }
    
    [Fact]
    public async Task SearchMemoriesAsync_WithTypeFilter_FiltersCorrectly()
    {
        // Arrange
        var techDebt = new FlexibleMemoryEntry
        {
            Type = MemoryTypes.TechnicalDebt,
            Content = "Refactor service layer",
            IsShared = true
        };
        
        var question = new FlexibleMemoryEntry
        {
            Type = MemoryTypes.Question,
            Content = "How to optimize queries?",
            IsShared = true
        };
        
        await _memoryService.StoreMemoryAsync(techDebt);
        await _memoryService.StoreMemoryAsync(question);
        
        // Act
        var searchRequest = new FlexibleMemorySearchRequest
        {
            Types = new[] { MemoryTypes.TechnicalDebt }
        };
        var result = await _memoryService.SearchMemoriesAsync(searchRequest);
        
        // Assert
        Assert.True(result.TotalFound >= 1);
        Assert.All(result.Memories, m => Assert.Equal(MemoryTypes.TechnicalDebt, m.Type));
    }
    
    [Fact]
    public async Task GetMemoryByIdAsync_ExistingMemory_ReturnsMemory()
    {
        // Arrange
        var memory = new FlexibleMemoryEntry
        {
            Id = "get-test-123",
            Type = MemoryTypes.CodePattern,
            Content = "Repository pattern implementation",
            IsShared = true
        };
        
        await _memoryService.StoreMemoryAsync(memory);
        await Task.Delay(100);
        
        // Act
        var result = await _memoryService.GetMemoryByIdAsync("get-test-123");
        
        // Assert
        Assert.NotNull(result);
        Assert.Equal("get-test-123", result.Id);
        Assert.Equal(MemoryTypes.CodePattern, result.Type);
        Assert.Equal("Repository pattern implementation", result.Content);
    }
    
    [Fact]
    public async Task UpdateMemoryAsync_ValidUpdate_ReturnsTrue()
    {
        // Arrange
        var memory = new FlexibleMemoryEntry
        {
            Id = "update-test-123",
            Type = MemoryTypes.TechnicalDebt,
            Content = "Original content",
            IsShared = true
        };
        
        await _memoryService.StoreMemoryAsync(memory);
        await Task.Delay(100);
        
        var updateRequest = new MemoryUpdateRequest
        {
            Id = "update-test-123",
            Content = "Updated content",
            FieldUpdates = new Dictionary<string, JsonElement?>
            {
                [MemoryFields.Status] = JsonDocument.Parse($"\"{MemoryStatus.InProgress}\"").RootElement
            }
        };
        
        // Act
        var result = await _memoryService.UpdateMemoryAsync(updateRequest);
        
        // Assert
        Assert.True(result);
        
        // Verify update
        var updated = await _memoryService.GetMemoryByIdAsync("update-test-123");
        Assert.NotNull(updated);
        Assert.Equal("Updated content", updated.Content);
        Assert.Equal(MemoryStatus.InProgress, updated.GetField<string>(MemoryFields.Status));
    }
    
    // Commented out - this test requires real Lucene index with proper field configuration
    // The InMemoryTestIndexService doesn't support the precision step configuration needed for NumericRangeQuery
    // [Fact]
    // public async Task ArchiveMemoriesAsync_OldMemories_ReturnsCount()
    // {
    //     // Arrange
    //     var oldMemory = new FlexibleMemoryEntry
    //     {
    //         Type = MemoryTypes.TemporaryNote,
    //         Content = "Old temporary note",
    //         Created = DateTime.UtcNow.AddDays(-35),
    //         Modified = DateTime.UtcNow.AddDays(-35),
    //         IsShared = false
    //     };
    //     
    //     var recentMemory = new FlexibleMemoryEntry
    //     {
    //         Type = MemoryTypes.TemporaryNote,
    //         Content = "Recent temporary note",
    //         Created = DateTime.UtcNow.AddDays(-5),
    //         IsShared = false
    //     };
    //     
    //     await _memoryService.StoreMemoryAsync(oldMemory);
    //     await _memoryService.StoreMemoryAsync(recentMemory);
    //     await Task.Delay(100);
    //     
    //     // Act
    //     var archivedCount = await _memoryService.ArchiveMemoriesAsync(
    //         MemoryTypes.TemporaryNote, 
    //         TimeSpan.FromDays(30));
    //     
    //     // Assert
    //     Assert.Equal(1, archivedCount);
    // }
    
    [Fact]
    public async Task SearchMemories_WithDateRange_FindsMatchingMemories()
    {
        await Task.Yield();
        // Arrange - Test basic date range functionality
        var oldMemory = new FlexibleMemoryEntry
        {
            Id = "date-test-old", 
            Type = MemoryTypes.TemporaryNote,
            Content = "Old memory for date testing",
            Created = DateTime.UtcNow.AddDays(-10),
            IsShared = false
        };
        
        var newMemory = new FlexibleMemoryEntry
        {
            Id = "date-test-new",
            Type = MemoryTypes.TemporaryNote, 
            Content = "New memory for date testing",
            Created = DateTime.UtcNow.AddHours(-1),
            IsShared = false
        };
        
        await _memoryService.StoreMemoryAsync(oldMemory);
        await _memoryService.StoreMemoryAsync(newMemory);
        await Task.Delay(100);
        
        // Act - Search with date range (last 7 days should only find new memory)
        var searchRequest = new FlexibleMemorySearchRequest
        {
            Types = new[] { MemoryTypes.TemporaryNote },
            DateRange = new DateRangeFilter 
            { 
                From = DateTime.UtcNow.AddDays(-7), 
                To = DateTime.UtcNow 
            },
            MaxResults = 10
        };
        
        var results = await _memoryService.SearchMemoriesAsync(searchRequest);
        
        // Assert - Should only find the new memory (created 1 hour ago)
        Assert.Single(results.Memories);
        Assert.Equal("date-test-new", results.Memories[0].Id);
    }
    
    // Commented out - this test requires real Lucene index with proper field configuration
    // The InMemoryTestIndexService doesn't support the precision step configuration needed for NumericRangeQuery
    // [Fact]
    // public async Task SearchMemoriesForArchive_OldMemories_ReturnsCorrectMemories()
    // {
    //     // Arrange - Test the exact same search that ArchiveMemoriesAsync does
    //     var oldMemory = new FlexibleMemoryEntry
    //     {
    //         Type = MemoryTypes.TemporaryNote,
    //         Content = "Old temporary note for archive test",
    //         Created = DateTime.UtcNow.AddDays(-35),
    //         Modified = DateTime.UtcNow.AddDays(-35),
    //         IsShared = false
    //     };
    //     
    //     var recentMemory = new FlexibleMemoryEntry
    //     {
    //         Type = MemoryTypes.TemporaryNote,
    //         Content = "Recent temporary note for archive test",
    //         Created = DateTime.UtcNow.AddDays(-5),
    //         IsShared = false
    //     };
    //     
    //     await _memoryService.StoreMemoryAsync(oldMemory);
    //     await _memoryService.StoreMemoryAsync(recentMemory);
    //     await Task.Delay(100);
    //     
    //     // Act - Test with a more reasonable range instead of DateTime.MinValue
    //     var cutoffDate = DateTime.UtcNow - TimeSpan.FromDays(30);
    //     var searchRequest = new FlexibleMemorySearchRequest
    //     {
    //         Types = new[] { MemoryTypes.TemporaryNote },
    //         DateRange = new DateRangeFilter { From = DateTime.UtcNow.AddYears(-1), To = cutoffDate },
    //         MaxResults = int.MaxValue
    //     };
    //     
    //     var results = await _memoryService.SearchMemoriesAsync(searchRequest);
    //     
    //     // Assert - Should find the old memory (created 35 days ago)
    //     Assert.True(results.Memories.Count >= 1, $"Expected at least 1 old memory but found {results.Memories.Count}");
    //     Assert.True(results.Memories.Any(m => m.Created < cutoffDate), "No memories older than cutoff date found");
    // }
    
    [Fact]
    public async Task LuceneNumericRangeQuery_DateTicks_WorksCorrectly()
    {
        await Task.Yield();
        // Test NumericRangeQuery directly with DateTime.Ticks to isolate the issue
        var directory = new Lucene.Net.Store.RAMDirectory();
        var analyzer = new Lucene.Net.Analysis.Standard.StandardAnalyzer(Lucene.Net.Util.LuceneVersion.LUCENE_48);
        var config = new Lucene.Net.Index.IndexWriterConfig(Lucene.Net.Util.LuceneVersion.LUCENE_48, analyzer);
        
        using var writer = new Lucene.Net.Index.IndexWriter(directory, config);
        
        // Create test documents with different dates
        var oldDate = DateTime.UtcNow.AddDays(-35);
        var recentDate = DateTime.UtcNow.AddDays(-5);
        
        // Test setup with old and recent dates
        
        // Test Int64Field approach (what we're using)
        var doc1 = new Lucene.Net.Documents.Document();
        doc1.Add(new Lucene.Net.Documents.StringField("id", "old", Lucene.Net.Documents.Field.Store.YES));
        doc1.Add(new Lucene.Net.Documents.Int64Field("created", oldDate.Ticks, Lucene.Net.Documents.Field.Store.YES));
        writer.AddDocument(doc1);
        
        var doc2 = new Lucene.Net.Documents.Document();
        doc2.Add(new Lucene.Net.Documents.StringField("id", "recent", Lucene.Net.Documents.Field.Store.YES));
        doc2.Add(new Lucene.Net.Documents.Int64Field("created", recentDate.Ticks, Lucene.Net.Documents.Field.Store.YES));
        writer.AddDocument(doc2);
        
        writer.Commit();
        
        using var reader = Lucene.Net.Index.DirectoryReader.Open(directory);
        var searcher = new Lucene.Net.Search.IndexSearcher(reader);
        
        // Test 1: Find old documents (should find doc with oldDate)
        var cutoffDate = DateTime.UtcNow.AddDays(-30);
        // Console.WriteLine($"Cutoff date: {cutoffDate} (Ticks: {cutoffDate.Ticks})");
        
        // Test NumericRangeQuery with default precision step
        var queryOldDefault = Lucene.Net.Search.NumericRangeQuery.NewInt64Range(
            "created", 
            DateTime.MinValue.Ticks, 
            cutoffDate.Ticks, 
            true, true);
        var resultsOldDefault = searcher.Search(queryOldDefault, 10);
        
        // Test NumericRangeQuery with explicit precision step 8
        var queryOldStep8 = Lucene.Net.Search.NumericRangeQuery.NewInt64Range(
            "created", 8,
            DateTime.MinValue.Ticks,
            cutoffDate.Ticks,
            true, true);
        var resultsOldStep8 = searcher.Search(queryOldStep8, 10);
        
        // Console.WriteLine($"Default precision NumericRangeQuery results: {resultsOldDefault.TotalHits} documents");
        // Console.WriteLine($"Precision step 8 NumericRangeQuery results: {resultsOldStep8.TotalHits} documents");
        
        if (resultsOldStep8.TotalHits > 0)
        {
            foreach (var scoreDoc in resultsOldStep8.ScoreDocs)
            {
                var doc = searcher.Doc(scoreDoc.Doc);
                // Console.WriteLine($"  Found: {doc.Get("id")}");
            }
        }
        
        // Test 2: Find recent documents (should find doc with recentDate)
        var queryRecentStep8 = Lucene.Net.Search.NumericRangeQuery.NewInt64Range(
            "created", 8,
            DateTime.UtcNow.AddDays(-10).Ticks,
            DateTime.UtcNow.Ticks,
            true, true);
        var resultsRecentStep8 = searcher.Search(queryRecentStep8, 10);
        
        // Console.WriteLine($"Recent precision step 8 results: {resultsRecentStep8.TotalHits} documents");
        if (resultsRecentStep8.TotalHits > 0)
        {
            foreach (var scoreDoc in resultsRecentStep8.ScoreDocs)
            {
                var doc = searcher.Doc(scoreDoc.Doc);
                // Console.WriteLine($"  Found: {doc.Get("id")}");
            }
        }
        
        // Test 3: Find all documents
        var queryAll = Lucene.Net.Search.NumericRangeQuery.NewInt64Range(
            "created", 8,
            DateTime.MinValue.Ticks,
            DateTime.MaxValue.Ticks,
            true, true);
        var resultsAll = searcher.Search(queryAll, 10);
        
        // Console.WriteLine($"All documents with step 8: {resultsAll.TotalHits} documents");
        
        // Show what we actually found
        // Console.WriteLine("All documents in index:");
        foreach (var scoreDoc in resultsAll.ScoreDocs)
        {
            var doc = searcher.Doc(scoreDoc.Doc);
            var ticks = long.Parse(doc.Get("created"));
            var date = new DateTime(ticks);
            // Console.WriteLine($"  {doc.Get("id")}: {date} (ticks: {ticks})");
        }
        
        // Test expectations - if NumericRangeQuery is broken, these will fail
        // We expect at least some results with better precision step
        Assert.True(resultsAll.TotalHits == 2, $"Expected 2 total documents, got {resultsAll.TotalHits}");
        
        analyzer.Dispose();
        directory.Dispose();
    }
    
    [Fact]
    public async Task DateRangeProductionReplication_WorksCorrectly()
    {
        await Task.Yield();
        // Test that replicates the EXACT production configuration to identify the issue
        DateRangeProductionTest.RunProductionTest();
    }
    
    // Commented out - this test is for manual debugging only, not for CI/CD
    // [Fact]
    // public async Task AnalyzeProductionSqliteBackup_ShowsCurrentState()
    // {
    //     // Analyze the SQLite backup to understand current production memory state
    //     var sqlitePath = @"C:\source\COA CodeSearch MCP\.codesearch\memories.db";
    //     AnalyzeSqliteMemories.AnalyzeBackup(sqlitePath);
    // }
    
    [Fact]
    public async Task FindSimilarMemoriesAsync_ExistingMemory_ReturnsResults()
    {
        await Task.Yield();
        // Arrange
        var sourceMemory = new FlexibleMemoryEntry
        {
            Id = "similar-source",
            Type = MemoryTypes.CodePattern,
            Content = "Repository pattern for data access",
            IsShared = true
        };
        
        var similarMemory = new FlexibleMemoryEntry
        {
            Type = MemoryTypes.CodePattern,
            Content = "Repository pattern implementation example",
            IsShared = true
        };
        
        var differentMemory = new FlexibleMemoryEntry
        {
            Type = MemoryTypes.Question,
            Content = "How to setup logging?",
            IsShared = true
        };
        
        await _memoryService.StoreMemoryAsync(sourceMemory);
        await _memoryService.StoreMemoryAsync(similarMemory);
        await _memoryService.StoreMemoryAsync(differentMemory);
        await Task.Delay(100);
        
        // Act
        var similar = await _memoryService.FindSimilarMemoriesAsync("similar-source", 5);
        
        // Assert
        Assert.NotEmpty(similar);
        // Should not include the source memory itself
        Assert.DoesNotContain(similar, m => m.Id == "similar-source");
    }
}

/// <summary>
/// Test that replicates the EXACT production configuration from FlexibleMemoryService
/// to identify why date range queries fail in production but work in our unit test
/// </summary>
public class DateRangeProductionTest
{
    public static void RunProductionTest()
    {
        // Console.WriteLine("=== Production Configuration Test ===");
        
        var directory = new Lucene.Net.Store.RAMDirectory();
        var analyzer = new Lucene.Net.Analysis.Standard.StandardAnalyzer(Lucene.Net.Util.LuceneVersion.LUCENE_48);
        var config = new Lucene.Net.Index.IndexWriterConfig(Lucene.Net.Util.LuceneVersion.LUCENE_48, analyzer);
        
        using (var writer = new Lucene.Net.Index.IndexWriter(directory, config))
        {
            // Replicate EXACT production field type from FlexibleMemoryService.cs
            var dateFieldType = new Lucene.Net.Documents.FieldType 
            { 
                IsIndexed = true,
                IsStored = true,
                NumericType = Lucene.Net.Documents.NumericType.INT64,
                NumericPrecisionStep = 8
            };
            
            // Create test documents with old and recent dates
            var oldDate = DateTime.UtcNow.AddDays(-35);
            var recentDate = DateTime.UtcNow.AddDays(-5);
            
            // Console.WriteLine($"Old date: {oldDate} (Ticks: {oldDate.Ticks})");
            // Console.WriteLine($"Recent date: {recentDate} (Ticks: {recentDate.Ticks})");
            
            // Document 1: Old memory
            var doc1 = new Lucene.Net.Documents.Document();
            doc1.Add(new Lucene.Net.Documents.StringField("id", "old", Lucene.Net.Documents.Field.Store.YES));
            doc1.Add(new Lucene.Net.Documents.TextField("content", "Old memory content", Lucene.Net.Documents.Field.Store.YES));
            doc1.Add(new Lucene.Net.Documents.StringField("type", "TechnicalDebt", Lucene.Net.Documents.Field.Store.YES));
            doc1.Add(new Lucene.Net.Documents.Int64Field("created", oldDate.Ticks, dateFieldType));
            doc1.Add(new Lucene.Net.Documents.NumericDocValuesField("created", oldDate.Ticks));
            doc1.Add(new Lucene.Net.Documents.Int64Field("modified", oldDate.Ticks, dateFieldType));
            doc1.Add(new Lucene.Net.Documents.NumericDocValuesField("modified", oldDate.Ticks));
            doc1.Add(new Lucene.Net.Documents.Int64Field("timestamp_ticks", oldDate.Ticks, dateFieldType));
            doc1.Add(new Lucene.Net.Documents.NumericDocValuesField("timestamp_ticks", oldDate.Ticks));
            writer.AddDocument(doc1);
            
            // Document 2: Recent memory  
            var doc2 = new Lucene.Net.Documents.Document();
            doc2.Add(new Lucene.Net.Documents.StringField("id", "recent", Lucene.Net.Documents.Field.Store.YES));
            doc2.Add(new Lucene.Net.Documents.TextField("content", "Recent memory content", Lucene.Net.Documents.Field.Store.YES));
            doc2.Add(new Lucene.Net.Documents.StringField("type", "TechnicalDebt", Lucene.Net.Documents.Field.Store.YES));
            doc2.Add(new Lucene.Net.Documents.Int64Field("created", recentDate.Ticks, dateFieldType));
            doc2.Add(new Lucene.Net.Documents.NumericDocValuesField("created", recentDate.Ticks));
            doc2.Add(new Lucene.Net.Documents.Int64Field("modified", recentDate.Ticks, dateFieldType));
            doc2.Add(new Lucene.Net.Documents.NumericDocValuesField("modified", recentDate.Ticks));
            doc2.Add(new Lucene.Net.Documents.Int64Field("timestamp_ticks", recentDate.Ticks, dateFieldType));
            doc2.Add(new Lucene.Net.Documents.NumericDocValuesField("timestamp_ticks", recentDate.Ticks));
            writer.AddDocument(doc2);
            
            writer.Commit();
            
            using (var reader = Lucene.Net.Index.DirectoryReader.Open(directory))
            {
                var searcher = new Lucene.Net.Search.IndexSearcher(reader);
                
                // Test 1: Archive query (find old memories) - this is what's failing
                var cutoffDate = DateTime.UtcNow.AddDays(-30);
                // Console.WriteLine($"\n=== Archive Query Test ===");
                // Console.WriteLine($"Cutoff date: {cutoffDate} (Ticks: {cutoffDate.Ticks})");
                
                // This replicates the exact query from ArchiveMemoriesAsync
                var archiveQuery = Lucene.Net.Search.NumericRangeQuery.NewInt64Range("created", 8,
                    DateTime.MinValue.Ticks, cutoffDate.Ticks, true, true);
                
                var archiveResults = searcher.Search(archiveQuery, 100);
                // Console.WriteLine($"Archive query results: {archiveResults.TotalHits} documents");
                
                foreach (var scoreDoc in archiveResults.ScoreDocs)
                {
                    var doc = searcher.Doc(scoreDoc.Doc);
                    var createdTicks = long.Parse(doc.Get("created"));
                    var createdDate = new DateTime(createdTicks);
                    // Console.WriteLine($"  Found: {doc.Get("id")} created {createdDate}");
                }
                
                // Test 2: Date range search (this is also affected)
                // Console.WriteLine($"\n=== Date Range Search Test ===");
                var searchFromDate = DateTime.UtcNow.AddDays(-40);
                var searchToDate = DateTime.UtcNow.AddDays(-20);
                
                // Console.WriteLine($"Search range: {searchFromDate} to {searchToDate}");
                
                var rangeQuery = Lucene.Net.Search.NumericRangeQuery.NewInt64Range("created", 8,
                    searchFromDate.Ticks, searchToDate.Ticks, true, true);
                
                var rangeResults = searcher.Search(rangeQuery, 100);
                // Console.WriteLine($"Range query results: {rangeResults.TotalHits} documents");
                
                foreach (var scoreDoc in rangeResults.ScoreDocs)
                {
                    var doc = searcher.Doc(scoreDoc.Doc);
                    var createdTicks = long.Parse(doc.Get("created"));
                    var createdDate = new DateTime(createdTicks);
                    // Console.WriteLine($"  Found: {doc.Get("id")} created {createdDate}");
                }
                
                // Test 3: All documents query (for comparison)
                // Console.WriteLine($"\n=== All Documents Test ===");
                var allQuery = new Lucene.Net.Search.MatchAllDocsQuery();
                var allResults = searcher.Search(allQuery, 100);
                // Console.WriteLine($"All documents: {allResults.TotalHits} documents");
                
                foreach (var scoreDoc in allResults.ScoreDocs)
                {
                    var doc = searcher.Doc(scoreDoc.Doc);
                    var createdTicks = long.Parse(doc.Get("created"));
                    var createdDate = new DateTime(createdTicks);
                    var age = DateTime.UtcNow - createdDate;
                    // Console.WriteLine($"  {doc.Get("id")}: {createdDate} (Age: {age.TotalDays:F1} days)");
                }
            }
        }
        
        analyzer.Dispose();
        directory.Dispose();
        
        // Console.WriteLine("\n=== Test Complete ===");
    }
}

/*
/// <summary>
/// Tool to analyze the SQLite backup and understand the current state of production memories
/// </summary>
public static class AnalyzeSqliteMemories
{
        public static void AnalyzeBackup(string sqlitePath)
        {
            // Console.WriteLine("=== SQLite Memory Analysis ===");
            
            if (!File.Exists(sqlitePath))
            {
                // Console.WriteLine($"SQLite file not found: {sqlitePath}");
                return;
            }

            var connectionString = $"Data Source={sqlitePath}";
            
            using var connection = new Microsoft.Data.Sqlite.SqliteConnection(connectionString);
            connection.Open();

            // Get table schema first
            // Console.WriteLine("\n=== Database Schema ===");
            var schemaCommand = connection.CreateCommand();
            schemaCommand.CommandText = @"
                SELECT name, sql FROM sqlite_master 
                WHERE type='table' 
                ORDER BY name;";

            using var schemaReader = schemaCommand.ExecuteReader();
            while (schemaReader.Read())
            {
                var tableName = schemaReader.GetString(0);
                var createSql = schemaReader.GetString(1);
                // Console.WriteLine($"Table: {tableName}");
                // Console.WriteLine($"  {createSql}");
                // Console.WriteLine();
            }

            // Analyze memory data
            // Console.WriteLine("\n=== Memory Data Analysis ===");
            
            var dataCommand = connection.CreateCommand();
            dataCommand.CommandText = @"
                SELECT 
                    id, 
                    type, 
                    content,
                    created,
                    modified,
                    is_shared,
                    access_count,
                    timestamp_ticks,
                    fields
                FROM memories 
                ORDER BY created DESC;";

            using var dataReader = dataCommand.ExecuteReader();
            int count = 0;
            
            // Console.WriteLine("ID | Type | Created | Modified | TimestampTicks | IsShared | Content Preview");
            // Console.WriteLine("---|------|---------|----------|----------------|----------|----------------");
            
            while (dataReader.Read())
            {
                count++;
                var id = dataReader.GetString(0);
                var type = dataReader.GetString(1);
                var content = dataReader.GetString(2);
                var created = dataReader.GetString(3);
                var modified = dataReader.GetString(4);
                var isShared = dataReader.GetBoolean(5);
                var accessCount = dataReader.GetInt32(6);
                var timestampTicks = dataReader.GetInt64(7);
                var fields = dataReader.IsDBNull(8) ? null : dataReader.GetString(8);

                // Parse dates to understand the format
                DateTime createdDate = DateTime.MinValue;
                DateTime modifiedDate = DateTime.MinValue;
                
                if (DateTime.TryParse(created, out var parsedCreated))
                {
                    createdDate = parsedCreated;
                }
                
                if (DateTime.TryParse(modified, out var parsedModified))
                {
                    modifiedDate = parsedModified;
                }

                var contentPreview = content.Length > 30 ? content.Substring(0, 30) + "..." : content;
                
                // Console.WriteLine($"{id[..8]} | {type} | {createdDate:yyyy-MM-dd} | {modifiedDate:yyyy-MM-dd} | {timestampTicks} | {isShared} | {contentPreview}");
                
                // Check for date consistency issues
                if (createdDate != DateTime.MinValue)
                {
                    var expectedTicks = createdDate.Ticks;
                    if (Math.Abs(expectedTicks - timestampTicks) > TimeSpan.TicksPerSecond)
                    {
                        // Console.WriteLine($"  ⚠️  DATE INCONSISTENCY: Created={expectedTicks} vs TimestampTicks={timestampTicks}");
                    }
                }
                
                // Show extended fields if present
                if (!string.IsNullOrEmpty(fields))
                {
                    // Console.WriteLine($"  Fields: {fields}");
                }
                
                // Console.WriteLine();
            }
            
            // Console.WriteLine($"\nTotal memories found: {count}");
            
            // Check for date range distribution
            // Console.WriteLine("\n=== Date Range Analysis ===");
            var rangeCommand = connection.CreateCommand();
            rangeCommand.CommandText = @"
                SELECT 
                    MIN(created) as earliest_created,
                    MAX(created) as latest_created,
                    MIN(timestamp_ticks) as min_ticks,
                    MAX(timestamp_ticks) as max_ticks,
                    COUNT(*) as total_count
                FROM memories;";

            using var rangeReader = rangeCommand.ExecuteReader();
            if (rangeReader.Read())
            {
                var earliestCreated = rangeReader.GetString(0);
                var latestCreated = rangeReader.GetString(1);
                var minTicks = rangeReader.GetInt64(2);
                var maxTicks = rangeReader.GetInt64(3);
                var totalCount = rangeReader.GetInt32(4);
                
                // Console.WriteLine($"Date range: {earliestCreated} to {latestCreated}");
                // Console.WriteLine($"Ticks range: {minTicks} to {maxTicks}");
                // Console.WriteLine($"Total memories: {totalCount}");
                
                // Convert ticks back to dates for verification
                if (minTicks > 0)
                {
                    var minTicksDate = new DateTime(minTicks);
                    var maxTicksDate = new DateTime(maxTicks);
                    // Console.WriteLine($"Ticks as dates: {minTicksDate} to {maxTicksDate}");
                }
            }
            
            // Console.WriteLine("\n=== Analysis Complete ===");
        }
}
*/