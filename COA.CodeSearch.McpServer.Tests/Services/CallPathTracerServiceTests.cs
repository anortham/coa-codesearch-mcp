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
        var callerSymbol = new JulieSymbol
        {
            Id = callerId,
            Name = "SaveAsync",
            Kind = "method",
            FilePath = "/test/UserService.cs",
            Language = "csharp",
            StartLine = 10,
            EndLine = 20
        };

        var callIdentifier = new JulieIdentifier
        {
            Id = "identifier_456",
            Name = targetSymbolName,
            Kind = "call",
            Language = "csharp",
            FilePath = "/test/UserService.cs",
            StartLine = 15,
            StartColumn = 8,
            EndLine = 15,
            EndColumn = 18,
            ContainingSymbolId = callerId,
            CodeContext = "await UpdateUser(user);"
        };

        _mockSqliteService
            .Setup(s => s.GetIdentifiersByNameAsync(
                TestWorkspacePath,
                targetSymbolName,
                false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<JulieIdentifier> { callIdentifier });

        // Mock for recursive call (who calls SaveAsync?) - return empty to stop recursion
        _mockSqliteService
            .Setup(s => s.GetIdentifiersByNameAsync(
                TestWorkspacePath,
                "SaveAsync",
                false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<JulieIdentifier>());

        _mockSqliteService
            .Setup(s => s.GetAllSymbolsAsync(TestWorkspacePath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<JulieSymbol> { callerSymbol });

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

        _mockSqliteService
            .Setup(s => s.GetAllSymbolsAsync(TestWorkspacePath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<JulieSymbol>());

        _mockSqliteService
            .Setup(s => s.GetIdentifiersByNameAsync(
                TestWorkspacePath,
                targetSymbolName,
                false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<JulieIdentifier>());

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
        var methodA = new JulieSymbol { Id = "a", Name = "MethodA", Kind = "method", FilePath = "/test.cs", Language = "csharp", StartLine = 1, EndLine = 5 };
        var methodB = new JulieSymbol { Id = "b", Name = "MethodB", Kind = "method", FilePath = "/test.cs", Language = "csharp", StartLine = 10, EndLine = 15 };
        var methodC = new JulieSymbol { Id = "c", Name = "MethodC", Kind = "method", FilePath = "/test.cs", Language = "csharp", StartLine = 20, EndLine = 25 };

        // C calls B, B calls A
        var callCtoB = new JulieIdentifier { Id = "c_to_b", Name = "MethodB", Kind = "call", Language = "csharp", FilePath = "/test.cs", StartLine = 22, StartColumn = 1, EndLine = 22, EndColumn = 10, ContainingSymbolId = "c", CodeContext = "MethodB();" };
        var callBtoA = new JulieIdentifier { Id = "b_to_a", Name = "MethodA", Kind = "call", Language = "csharp", FilePath = "/test.cs", StartLine = 12, StartColumn = 1, EndLine = 12, EndColumn = 10, ContainingSymbolId = "b", CodeContext = "MethodA();" };

        _mockSqliteService
            .Setup(s => s.GetAllSymbolsAsync(TestWorkspacePath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<JulieSymbol> { methodA, methodB, methodC });

        // First call: who calls MethodA? -> MethodB (depth 0)
        _mockSqliteService
            .Setup(s => s.GetIdentifiersByNameAsync(TestWorkspacePath, "MethodA", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<JulieIdentifier> { callBtoA });

        // Second call: who calls MethodB? -> MethodC (depth 1)
        _mockSqliteService
            .Setup(s => s.GetIdentifiersByNameAsync(TestWorkspacePath, "MethodB", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<JulieIdentifier> { callCtoB });

        // Act: Trace with maxDepth = 1 (should stop after finding MethodC)
        var result = await _service.TraceUpwardAsync(
            TestWorkspacePath,
            "MethodA",
            maxDepth: 1,
            caseSensitive: false);

        // Assert: Should have depth 0 and depth 1, but not depth 2
        Assert.That(result, Is.Not.Null);
        var flatList = FlattenCallPaths(result);
        Assert.That(flatList.Any(n => n.Depth == 0), Is.True, "Should have depth 0 node");
        Assert.That(flatList.Any(n => n.Depth == 1), Is.True, "Should have depth 1 node");
        Assert.That(flatList.All(n => n.Depth <= 1), Is.True, "Should not exceed maxDepth");
    }

    [Test]
    public async Task TraceDownwardAsync_WithDirectCallees_ReturnsCallPathNodes()
    {
        // Arrange: "SaveAsync" calls "UpdateUser" and "LogActivity"
        var parentSymbol = new JulieSymbol
        {
            Id = "save_async",
            Name = "SaveAsync",
            Kind = "method",
            FilePath = "/test/UserService.cs",
            Language = "csharp",
            StartLine = 10,
            EndLine = 20
        };

        var call1 = new JulieIdentifier
        {
            Id = "call_1",
            Name = "UpdateUser",
            Kind = "call",
            Language = "csharp",
            FilePath = "/test/UserService.cs",
            StartLine = 15,
            StartColumn = 8,
            EndLine = 15,
            EndColumn = 18,
            ContainingSymbolId = "save_async",
            CodeContext = "await UpdateUser(user);"
        };

        var call2 = new JulieIdentifier
        {
            Id = "call_2",
            Name = "LogActivity",
            Kind = "call",
            Language = "csharp",
            FilePath = "/test/UserService.cs",
            StartLine = 16,
            StartColumn = 8,
            EndLine = 16,
            EndColumn = 19,
            ContainingSymbolId = "save_async",
            CodeContext = "await LogActivity(\"saved\");"
        };

        _mockSqliteService
            .Setup(s => s.GetAllSymbolsAsync(TestWorkspacePath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<JulieSymbol> { parentSymbol });

        _mockSqliteService
            .Setup(s => s.GetIdentifiersByContainingSymbolAsync(
                TestWorkspacePath,
                "save_async",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<JulieIdentifier> { call1, call2 });

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
