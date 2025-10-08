using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Services.Julie;
using COA.CodeSearch.McpServer.Services.Sqlite;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace COA.CodeSearch.McpServer.Tests.Services;

[TestFixture]
public class CallPathTracerServiceTests
{
    private Mock<ISQLiteSymbolService> _mockSqliteService;
    private Mock<IReferenceResolverService> _mockReferenceResolver;
    private Mock<ILogger<CallPathTracerService>> _mockLogger;
    private CallPathTracerService _service;
    private const string TestWorkspacePath = "/test/workspace";

    [SetUp]
    public void SetUp()
    {
        _mockSqliteService = new Mock<ISQLiteSymbolService>();
        _mockReferenceResolver = new Mock<IReferenceResolverService>();
        _mockLogger = new Mock<ILogger<CallPathTracerService>>();
        _service = new CallPathTracerService(
            _mockSqliteService.Object,
            _mockReferenceResolver.Object,
            _mockLogger.Object);
    }

    [Test]
    public async Task TraceUpwardAsync_WithDirectCaller_ReturnsCallPathNode()
    {
        // Arrange: Symbol "UpdateUser" is called by "UserService.SaveAsync"
        var targetSymbolName = "UpdateUser";
        var callerId = "caller_symbol_123";

        var cteResult = new CallPathCTEResult
        {
            IdentifierId = "identifier_456",
            Name = targetSymbolName,
            Kind = "call",
            FilePath = "/test/UserService.cs",
            StartLine = 15,
            StartColumn = 8,
            EndLine = 15,
            EndColumn = 18,
            CodeContext = "await UpdateUser(user);",
            ContainingSymbolId = callerId,
            ContainingSymbolName = "SaveAsync",
            ContainingSymbolKind = "method",
            Depth = 0,
            Path = "identifier_456"
        };

        // Mock the CTE method that the service actually calls
        _mockSqliteService
            .Setup(s => s.TraceCallPathUpwardAsync(
                TestWorkspacePath,
                targetSymbolName,
                1,
                false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CallPathCTEResult> { cteResult });

        // Mock semantic search availability (return false to skip semantic bridge search)
        _mockSqliteService
            .Setup(s => s.IsSemanticSearchAvailable())
            .Returns(false);

        // Act
        var result = await _service.TraceUpwardAsync(
            TestWorkspacePath,
            targetSymbolName,
            maxDepth: 1,
            caseSensitive: false);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Count, Is.EqualTo(1));
        var node = result[0];
        Assert.That(node.Identifier.Name, Is.EqualTo(targetSymbolName));
        Assert.That(node.ContainingSymbol?.Name, Is.EqualTo("SaveAsync"));
        Assert.That(node.Depth, Is.EqualTo(0));
        Assert.That(node.Direction, Is.EqualTo(CallDirection.Upward));
    }

    [Test]
    public async Task TraceUpwardAsync_WithNoCallers_ReturnsEmptyList()
    {
        // Arrange
        var targetSymbolName = "UnusedMethod";

        // Mock the CTE method to return empty list (no callers found)
        _mockSqliteService
            .Setup(s => s.TraceCallPathUpwardAsync(
                TestWorkspacePath,
                targetSymbolName,
                1,
                false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CallPathCTEResult>());

        // Act
        var result = await _service.TraceUpwardAsync(
            TestWorkspacePath,
            targetSymbolName,
            maxDepth: 1,
            caseSensitive: false);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Count, Is.EqualTo(0));
    }

    [Test]
    public async Task TraceUpwardAsync_WithRecursiveCalls_RespectsMaxDepth()
    {
        // Arrange: A -> B -> C (depth 2)
        // B calls A (depth 0), C calls B (depth 1)
        var cteResult1 = new CallPathCTEResult
        {
            IdentifierId = "b_to_a",
            Name = "MethodA",
            Kind = "call",
            FilePath = "/test.cs",
            StartLine = 12,
            StartColumn = 1,
            EndLine = 12,
            EndColumn = 10,
            CodeContext = "MethodA();",
            ContainingSymbolId = "b",
            ContainingSymbolName = "MethodB",
            ContainingSymbolKind = "method",
            Depth = 0,
            Path = "b_to_a"
        };

        var cteResult2 = new CallPathCTEResult
        {
            IdentifierId = "c_to_b",
            Name = "MethodB",
            Kind = "call",
            FilePath = "/test.cs",
            StartLine = 22,
            StartColumn = 1,
            EndLine = 22,
            EndColumn = 10,
            CodeContext = "MethodB();",
            ContainingSymbolId = "c",
            ContainingSymbolName = "MethodC",
            ContainingSymbolKind = "method",
            Depth = 1,
            Path = "b_to_a|c_to_b"
        };

        // Mock CTE to return results respecting maxDepth = 1
        _mockSqliteService
            .Setup(s => s.TraceCallPathUpwardAsync(
                TestWorkspacePath,
                "MethodA",
                1,
                false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CallPathCTEResult> { cteResult1, cteResult2 });

        // Mock semantic search availability
        _mockSqliteService
            .Setup(s => s.IsSemanticSearchAvailable())
            .Returns(false);

        // Act: Trace with maxDepth = 1 (should stop after finding MethodC)
        var result = await _service.TraceUpwardAsync(
            TestWorkspacePath,
            "MethodA",
            maxDepth: 1,
            caseSensitive: false);

        // Assert: Should have depth 0 and depth 1, but not depth 2
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Count, Is.EqualTo(2));
        Assert.That(result.Any(n => n.Depth == 0), Is.True, "Should have depth 0 node");
        Assert.That(result.Any(n => n.Depth == 1), Is.True, "Should have depth 1 node");
        Assert.That(result.All(n => n.Depth <= 1), Is.True, "Should not exceed maxDepth");
    }

    [Test]
    public async Task TraceDownwardAsync_WithDirectCallees_ReturnsCallPathNodes()
    {
        // Arrange: "SaveAsync" calls "UpdateUser" and "LogActivity"
        var cteResult1 = new CallPathCTEResult
        {
            IdentifierId = "call_1",
            Name = "UpdateUser",
            Kind = "call",
            FilePath = "/test/UserService.cs",
            StartLine = 15,
            StartColumn = 8,
            EndLine = 15,
            EndColumn = 18,
            CodeContext = "await UpdateUser(user);",
            ContainingSymbolId = "save_async",
            ContainingSymbolName = "SaveAsync",
            ContainingSymbolKind = "method",
            Depth = 0,
            Path = "call_1"
        };

        var cteResult2 = new CallPathCTEResult
        {
            IdentifierId = "call_2",
            Name = "LogActivity",
            Kind = "call",
            FilePath = "/test/UserService.cs",
            StartLine = 16,
            StartColumn = 8,
            EndLine = 16,
            EndColumn = 19,
            CodeContext = "await LogActivity(\"saved\");",
            ContainingSymbolId = "save_async",
            ContainingSymbolName = "SaveAsync",
            ContainingSymbolKind = "method",
            Depth = 0,
            Path = "call_2"
        };

        // Mock the CTE method that the service actually calls
        _mockSqliteService
            .Setup(s => s.TraceCallPathDownwardAsync(
                TestWorkspacePath,
                "SaveAsync",
                1,
                false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CallPathCTEResult> { cteResult1, cteResult2 });

        // Mock semantic search availability (return false to skip semantic bridge search)
        _mockSqliteService
            .Setup(s => s.IsSemanticSearchAvailable())
            .Returns(false);

        // Act
        var result = await _service.TraceDownwardAsync(
            TestWorkspacePath,
            "SaveAsync",
            maxDepth: 1,
            caseSensitive: false);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Count, Is.EqualTo(2));
        Assert.That(result.Any(n => n.Identifier.Name == "UpdateUser"), Is.True);
        Assert.That(result.Any(n => n.Identifier.Name == "LogActivity"), Is.True);
        Assert.That(result.All(n => n.Direction == CallDirection.Downward), Is.True);
    }

    // Helper method to flatten hierarchical call paths for testing
    private List<CallPathNode> FlattenCallPaths(List<CallPathNode> nodes)
    {
        var result = new List<CallPathNode>();
        foreach (var node in nodes)
        {
            result.Add(node);
            if (node.Children.Any())
            {
                result.AddRange(FlattenCallPaths(node.Children));
            }
        }
        return result;
    }
}
