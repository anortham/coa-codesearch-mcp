using System.Text.Json;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using System.Reflection;
using Lucene.Net.Documents;

namespace COA.CodeSearch.McpServer.Tests;

public class FlexibleMemoryQueryTests
{
    private readonly Mock<ILogger<FlexibleMemoryService>> _loggerMock;
    private readonly Mock<IConfiguration> _configMock;
    private readonly Mock<ILuceneIndexService> _indexServiceMock;
    private readonly Mock<IPathResolutionService> _pathResolutionMock;
    private readonly FlexibleMemoryService _memoryService;
    
    public FlexibleMemoryQueryTests()
    {
        _loggerMock = new Mock<ILogger<FlexibleMemoryService>>();
        _configMock = new Mock<IConfiguration>();
        _indexServiceMock = new Mock<ILuceneIndexService>();
        _pathResolutionMock = new Mock<IPathResolutionService>();
        
        // Setup path resolution mocks
        _pathResolutionMock.Setup(x => x.GetProjectMemoryPath())
            .Returns(Path.Combine(Path.GetTempPath(), "test-project-memory"));
        _pathResolutionMock.Setup(x => x.GetLocalMemoryPath())
            .Returns(Path.Combine(Path.GetTempPath(), "test-local-memory"));
        
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
        
        // Create MemoryAnalyzer mock
        var memoryAnalyzerLoggerMock = new Mock<ILogger<MemoryAnalyzer>>();
        var memoryAnalyzer = new MemoryAnalyzer(memoryAnalyzerLoggerMock.Object);
        
        _memoryService = new FlexibleMemoryService(_loggerMock.Object, _configMock.Object, _indexServiceMock.Object, _pathResolutionMock.Object, errorHandlingMock.Object, validationMock.Object, memoryAnalyzer);
    }
    
    [Fact]
    public void BuildQuery_EmptyRequest_ReturnsMatchAllDocsQuery()
    {
        // Arrange
        var request = new FlexibleMemorySearchRequest();
        
        // Act
        var query = InvokeBuildQuery(request);
        
        // Assert
        Assert.IsType<MatchAllDocsQuery>(query);
    }
    
    [Fact]
    public void BuildQuery_WildcardQuery_ReturnsMatchAllDocsQuery()
    {
        // Arrange
        var request = new FlexibleMemorySearchRequest { Query = "*" };
        
        // Act
        var query = InvokeBuildQuery(request);
        
        // Assert
        Assert.IsType<MatchAllDocsQuery>(query);
    }
    
    [Fact]
    public void BuildQuery_WithTextQuery_CreatesBooleanQuery()
    {
        // Arrange
        var request = new FlexibleMemorySearchRequest { Query = "authentication" };
        
        // Act
        var query = InvokeBuildQuery(request);
        
        // Assert
        Assert.IsType<BooleanQuery>(query);
        var boolQuery = (BooleanQuery)query;
        Assert.Single(boolQuery.Clauses);
        Assert.Equal(Occur.MUST, boolQuery.Clauses[0].Occur);
    }
    
    [Fact]
    public void BuildQuery_WithTypes_CreatesTypeFilter()
    {
        // Arrange
        var request = new FlexibleMemorySearchRequest 
        { 
            Types = new[] { MemoryTypes.TechnicalDebt, MemoryTypes.Question }
        };
        
        // Act
        var query = InvokeBuildQuery(request);
        
        // Assert
        Assert.IsType<BooleanQuery>(query);
        var boolQuery = (BooleanQuery)query;
        Assert.Single(boolQuery.Clauses);
        
        // The type filter itself should be a boolean query with SHOULD clauses
        var typeQuery = boolQuery.Clauses[0].Query as BooleanQuery;
        Assert.NotNull(typeQuery);
        Assert.Equal(2, typeQuery.Clauses.Count);
        Assert.All(typeQuery.Clauses, c => Assert.Equal(Occur.SHOULD, c.Occur));
    }
    
    [Fact]
    public void BuildQuery_WithDateRange_CreatesNumericRangeQuery()
    {
        // Arrange
        var request = new FlexibleMemorySearchRequest 
        { 
            DateRange = new DateRangeFilter 
            { 
                From = DateTime.UtcNow.AddDays(-7),
                To = DateTime.UtcNow
            }
        };
        
        // Act
        var query = InvokeBuildQuery(request);
        
        // Assert
        Assert.IsType<BooleanQuery>(query);
        var boolQuery = (BooleanQuery)query;
        Assert.Single(boolQuery.Clauses);
        Assert.IsType<NumericRangeQuery<long>>(boolQuery.Clauses[0].Query);
    }
    
    [Fact]
    public void BuildQuery_WithFacets_CreatesFacetFilters()
    {
        // Arrange
        var request = new FlexibleMemorySearchRequest 
        { 
            Facets = new Dictionary<string, string>
            {
                { "status", "pending" },
                { "priority", "high" }
            }
        };
        
        // Act
        var query = InvokeBuildQuery(request);
        
        // Assert
        Assert.IsType<BooleanQuery>(query);
        var boolQuery = (BooleanQuery)query;
        Assert.Equal(2, boolQuery.Clauses.Count);
        
        // Each facet should create a term query
        Assert.All(boolQuery.Clauses, c => 
        {
            Assert.IsType<TermQuery>(c.Query);
            Assert.Equal(Occur.MUST, c.Occur);
        });
    }
    
    [Fact]
    public void BuildQuery_ComplexQuery_CombinesAllFilters()
    {
        // Arrange
        var request = new FlexibleMemorySearchRequest 
        { 
            Query = "refactor",
            Types = new[] { MemoryTypes.TechnicalDebt },
            DateRange = new DateRangeFilter { RelativeTime = "last-7-days" },
            Facets = new Dictionary<string, string> { { "status", "pending" } }
        };
        
        // Act
        var query = InvokeBuildQuery(request);
        
        // Assert
        Assert.IsType<BooleanQuery>(query);
        var boolQuery = (BooleanQuery)query;
        Assert.Equal(4, boolQuery.Clauses.Count); // text + type + date + facet
    }
    
    [Fact]
    public void DateRangeFilter_ParseRelativeTime_LastWeek()
    {
        // Arrange
        var filter = new DateRangeFilter { RelativeTime = "last-week" };
        var now = DateTime.UtcNow;
        
        // Act
        filter.ParseRelativeTime();
        
        // Assert
        Assert.NotNull(filter.From);
        Assert.NotNull(filter.To);
        Assert.True((now - filter.From.Value).TotalDays >= 6.9 && (now - filter.From.Value).TotalDays <= 7.1);
        Assert.True((filter.To.Value - now).TotalSeconds < 60); // Within a minute
    }
    
    [Fact]
    public void DateRangeFilter_ParseRelativeTime_CustomDays()
    {
        // Arrange
        var filter = new DateRangeFilter { RelativeTime = "last-14-days" };
        var now = DateTime.UtcNow;
        
        // Act
        filter.ParseRelativeTime();
        
        // Assert
        Assert.NotNull(filter.From);
        Assert.NotNull(filter.To);
        Assert.True((now - filter.From.Value).TotalDays >= 13.9 && (now - filter.From.Value).TotalDays <= 14.1);
    }
    
    [Fact]
    public void CreateDocument_IncludesAllCoreFields()
    {
        // Arrange
        var memory = new FlexibleMemoryEntry
        {
            Id = "test-123",
            Type = MemoryTypes.TechnicalDebt,
            Content = "Test content",
            Created = DateTime.UtcNow,
            Modified = DateTime.UtcNow,
            IsShared = true,
            AccessCount = 5,
            SessionId = "session-456",
            FilesInvolved = new[] { "file1.cs", "file2.cs" }
        };
        
        // Act
        var doc = InvokeCreateDocument(memory);
        
        // Assert
        Assert.Equal("test-123", doc.Get("id"));
        Assert.Equal(MemoryTypes.TechnicalDebt, doc.Get("type"));
        Assert.Equal("Test content", doc.Get("content"));
        Assert.Equal("True", doc.Get("is_shared"));
        Assert.Equal("5", doc.Get("access_count"));
        Assert.Equal("session-456", doc.Get("session_id"));
        
        // Files should be stored as multiple fields
        var files = doc.GetValues("file");
        Assert.Equal(2, files.Length);
        Assert.Contains("file1.cs", files);
        Assert.Contains("file2.cs", files);
    }
    
    [Fact]
    public void CreateDocument_HandlesExtendedFields()
    {
        // Arrange
        var memory = new FlexibleMemoryEntry
        {
            Id = "test-123",
            Type = MemoryTypes.TechnicalDebt,
            Content = "Test content"
        };
        
        memory.SetField("status", "pending");
        memory.SetField("priority", "high");
        memory.SetField("tags", new[] { "auth", "security" });
        memory.SetField("complexity", 8);
        
        // Act
        var doc = InvokeCreateDocument(memory);
        
        // Assert
        // Extended fields should be stored as JSON
        var fieldsJson = doc.Get("extended_fields");
        Assert.NotNull(fieldsJson);
        
        var fields = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(fieldsJson);
        Assert.NotNull(fields);
        Assert.Equal("pending", fields["status"].GetString());
        Assert.Equal("high", fields["priority"].GetString());
        Assert.Equal(8, fields["complexity"].GetInt32());
        
        // Fields should also be indexed for searching
        Assert.NotNull(doc.GetField("field_status"));
        Assert.NotNull(doc.GetField("field_priority"));
        Assert.NotNull(doc.GetField("field_complexity"));
    }
    
    [Fact]
    public void DocumentToMemory_ReconstructsMemoryCorrectly()
    {
        // Arrange
        var doc = new Document();
        doc.Add(new StringField("id", "test-123", Field.Store.YES));
        doc.Add(new StringField("type", MemoryTypes.Question, Field.Store.YES));
        doc.Add(new TextField("content", "How to handle auth?", Field.Store.YES));
        doc.Add(new Int64Field("created", DateTime.UtcNow.AddDays(-1).Ticks, Field.Store.YES));
        doc.Add(new Int64Field("modified", DateTime.UtcNow.Ticks, Field.Store.YES));
        doc.Add(new StringField("is_shared", "false", Field.Store.YES));
        doc.Add(new Int32Field("access_count", 3, Field.Store.YES));
        doc.Add(new StringField("session_id", "sess-789", Field.Store.YES));
        doc.Add(new StringField("file", "auth.cs", Field.Store.YES));
        doc.Add(new StringField("file", "login.cs", Field.Store.YES));
        
        var extendedFields = new Dictionary<string, JsonElement>
        {
            ["status"] = JsonDocument.Parse("\"answered\"").RootElement,
            ["tags"] = JsonDocument.Parse("[\"auth\", \"question\"]").RootElement
        };
        doc.Add(new StoredField("extended_fields", JsonSerializer.Serialize(extendedFields)));
        
        // Act
        var memory = InvokeDocumentToMemory(doc);
        
        // Assert
        Assert.Equal("test-123", memory.Id);
        Assert.Equal(MemoryTypes.Question, memory.Type);
        Assert.Equal("How to handle auth?", memory.Content);
        Assert.False(memory.IsShared);
        Assert.Equal(3, memory.AccessCount);
        Assert.Equal("sess-789", memory.SessionId);
        Assert.Equal(2, memory.FilesInvolved.Length);
        Assert.Contains("auth.cs", memory.FilesInvolved);
        Assert.Contains("login.cs", memory.FilesInvolved);
        
        // Check extended fields
        Assert.Equal("answered", memory.GetField<string>("status"));
        var tags = memory.GetField<string[]>("tags");
        Assert.NotNull(tags);
        Assert.Contains("auth", tags);
        Assert.Contains("question", tags);
    }
    
    [Fact]
    public void FlexibleMemoryEntry_HelperProperties_Work()
    {
        // Arrange
        var memory = new FlexibleMemoryEntry();
        memory.SetField("status", "in-progress");
        memory.SetField("priority", "critical");
        memory.SetField("tags", new[] { "bug", "urgent" });
        memory.SetField("relatedTo", new[] { "mem-123", "mem-456" });
        
        // Act & Assert
        Assert.Equal("in-progress", memory.Status);
        Assert.Equal("critical", memory.Priority);
        Assert.NotNull(memory.Tags);
        Assert.Equal(2, memory.Tags.Length);
        Assert.Contains("bug", memory.Tags);
        Assert.NotNull(memory.RelatedTo);
        Assert.Equal(2, memory.RelatedTo.Length);
    }
    
    // Helper methods to invoke private methods via reflection
    
    private Query InvokeBuildQuery(FlexibleMemorySearchRequest request)
    {
        var method = typeof(FlexibleMemoryService).GetMethod("BuildQuery", 
            BindingFlags.NonPublic | BindingFlags.Instance);
        return (Query)method!.Invoke(_memoryService, new object[] { request })!;
    }
    
    private Document InvokeCreateDocument(FlexibleMemoryEntry memory)
    {
        var method = typeof(FlexibleMemoryService).GetMethod("CreateDocument", 
            BindingFlags.NonPublic | BindingFlags.Instance);
        return (Document)method!.Invoke(_memoryService, new object[] { memory })!;
    }
    
    private FlexibleMemoryEntry InvokeDocumentToMemory(Document doc)
    {
        var method = typeof(FlexibleMemoryService).GetMethod("DocumentToMemory", 
            BindingFlags.NonPublic | BindingFlags.Instance);
        return (FlexibleMemoryEntry)method!.Invoke(_memoryService, new object[] { doc })!;
    }
}