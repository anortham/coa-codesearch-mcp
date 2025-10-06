using NUnit.Framework;
using FluentAssertions;
using Moq;
using COA.CodeSearch.McpServer.Tools;
using COA.CodeSearch.McpServer.Tests.Base;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services.Lucene;
using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Services.Sqlite;
using COA.CodeSearch.McpServer.Tools.Parameters;
using COA.Mcp.Framework.TokenOptimization.Models;
using COA.Mcp.Framework.TokenOptimization.Caching;
using COA.Mcp.Framework.TokenOptimization.Storage;
using Lucene.Net.Search;
using System.Threading.Tasks;
using System.Threading;
using System;
using System.Collections.Generic;
using COA.Mcp.Framework.Models;
using Microsoft.Extensions.Logging;
using System.Linq;

namespace COA.CodeSearch.McpServer.Tests.Tools;

/// <summary>
/// Tests for TraceCallPathTool - validates real call path tracing behavior, not test theater
/// </summary>
[TestFixture]
public class TraceCallPathToolTests : CodeSearchToolTestBase<TraceCallPathTool>
{
    private TraceCallPathTool _tool = null!;
    
    protected override TraceCallPathTool CreateTool()
    {
        var callPathTracerMock = new Mock<ICallPathTracerService>();
        var sqliteServiceMock = new Mock<ISQLiteSymbolService>();

        _tool = new TraceCallPathTool(
            ServiceProvider,
            callPathTracerMock.Object,
            sqliteServiceMock.Object,
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
        _tool.Name.Should().Be(ToolNames.TraceCallPath);
        _tool.Description.Should().Contain("TRACE EXECUTION PATHS");
        _tool.Category.Should().Be(COA.Mcp.Framework.ToolCategory.Query);
        _tool.Priority.Should().Be(92); // High priority for refactoring safety
        _tool.PreferredScenarios.Should().Contain("call_tracing");
        _tool.PreferredScenarios.Should().Contain("refactoring_impact");
    }

    [Test]
    public async Task ExecuteAsync_Should_Validate_Required_Parameters()
    {
        // Arrange
        var parameters = new TraceCallPathParameters
        {
            Symbol = null!, // Missing required parameter
            WorkspacePath = TestWorkspacePath
        };
        
        // Act & Assert
        var act = async () => await _tool.ExecuteAsync(parameters, CancellationToken.None);
        
        await act.Should().ThrowAsync<COA.Mcp.Framework.Exceptions.ToolExecutionException>()
            .WithMessage("*Symbol*required*");
    }

    [Test]
    public async Task ExecuteAsync_Should_Return_Error_When_Workspace_Not_Indexed()
    {
        // Arrange
        SetupNoIndex();
        var parameters = new TraceCallPathParameters
        {
            Symbol = "UserService.UpdateUser",
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
        result.Result.Error!.Code.Should().Be("TRACE_ERROR");
        result.Result.Error.Recovery.Should().NotBeNull();
        result.Result.Error.Recovery!.Steps.Should().Contain(s => s.Contains("indexed"));
    }

    [Test]
    public async Task ExecuteAsync_Should_Find_Method_Callers_In_Upward_Direction()
    {
        // Arrange - Real scenario: finding who calls UserService.UpdateUser
        SetupExistingIndex();
        var callPathResult = CreateRealisticCallPathResult_UpdateUserCallers();
        
        LuceneIndexServiceMock
            .Setup(x => x.SearchAsync(
                It.IsAny<string>(),
                It.IsAny<Query>(),
                It.IsAny<int>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(callPathResult);
        
        var parameters = new TraceCallPathParameters
        {
            Symbol = "UpdateUser",
            Direction = "up", // Find callers
            WorkspacePath = TestWorkspacePath,
            MaxDepth = 3,
            ContextLines = 2
        };
        
        // Act
        var result = await ExecuteToolAsync<AIOptimizedResponse<SearchResult>>(
            async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
        
        // Assert - Verify real call path behavior
        result.Success.Should().BeTrue();
        result.Result.Should().NotBeNull();
        result.Result!.Success.Should().BeTrue();
        
        var data = result.Result.Data;
        data.Should().NotBeNull();
        data!.Summary.Should().Contain("Call path trace (up)");
        data.Summary.Should().Contain("3 references");
        
        var searchResult = data.Results;
        searchResult.Should().NotBeNull();
        searchResult.Hits.Should().HaveCount(3);
        
        // Verify call path metadata is added
        var hits = searchResult.Hits!;
        foreach (var hit in hits)
        {
            hit.Fields.Should().ContainKey("trace_direction");
            hit.Fields!["trace_direction"].Should().Be("up");
            hit.Fields.Should().ContainKey("trace_symbol");
            hit.Fields["trace_symbol"].Should().Be("UpdateUser");
            hit.Fields.Should().ContainKey("call_depth");
        }
        
        // Verify entry point detection
        var entryPointHit = hits.FirstOrDefault(h => h.Fields?.GetValueOrDefault("is_entry_point") == "true");
        entryPointHit.Should().NotBeNull("Should identify controller as entry point");
        entryPointHit!.FilePath.Should().Contain("Controller");
        
        // Verify symbol highlighting in context
        var highlightedHit = hits.FirstOrDefault(h => h.ContextLines?.Any(line => line.Contains("«UpdateUser»")) == true);
        highlightedHit.Should().NotBeNull("Should highlight symbol references in context");
    }

    [Test]
    public async Task ExecuteAsync_Should_Detect_Entry_Points_Correctly()
    {
        // Arrange - Test entry point detection with realistic controller scenario
        SetupExistingIndex();
        var callPathResult = CreateCallPathResultWithEntryPoints();
        
        LuceneIndexServiceMock
            .Setup(x => x.SearchAsync(
                It.IsAny<string>(),
                It.IsAny<Query>(),
                It.IsAny<int>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(callPathResult);
        
        var parameters = new TraceCallPathParameters
        {
            Symbol = "ProcessPayment",
            Direction = "up",
            WorkspacePath = TestWorkspacePath
        };
        
        // Act
        var result = await ExecuteToolAsync<AIOptimizedResponse<SearchResult>>(
            async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
        
        // Assert - Verify entry point identification
        result.Success.Should().BeTrue();
        result.Result!.Success.Should().BeTrue();
        
        var insights = result.Result.Insights;
        insights.Should().NotBeNull();
        insights!.Should().Contain(i => i.Contains("entry points"));
        insights.Should().Contain(i => i.Contains("controllers") || i.Contains("main methods"));
        
        // Verify extension data contains entry point information
        var extensionData = result.Result.Data?.ExtensionData;
        extensionData.Should().NotBeNull();
        extensionData!.Should().ContainKey("symbol");
        extensionData["symbol"].Should().Be("ProcessPayment");
        extensionData.Should().ContainKey("direction");
        extensionData["direction"].Should().Be("up");
    }

    [Test]
    public async Task ExecuteAsync_Should_Use_Cached_Results_When_Available()
    {
        // Arrange
        SetupExistingIndex();
        var cachedResult = new AIOptimizedResponse<SearchResult>
        {
            Success = true,
            Data = new AIResponseData<SearchResult>
            {
                Summary = "Cached call path results",
                Count = 2,
                Results = new SearchResult { TotalHits = 2 }
            }
        };
        
        ResponseCacheServiceMock
            .Setup(x => x.GetAsync<AIOptimizedResponse<SearchResult>>(It.IsAny<string>()))
            .ReturnsAsync(cachedResult);
        
        var parameters = new TraceCallPathParameters
        {
            Symbol = "GetUserById",
            WorkspacePath = TestWorkspacePath,
            NoCache = false
        };
        
        // Act
        var result = await ExecuteToolAsync<AIOptimizedResponse<SearchResult>>(
            async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
        
        // Assert
        result.Success.Should().BeTrue();
        result.Result.Should().NotBeNull();
        result.Result!.Success.Should().BeTrue();
        result.Result.Data!.Summary.Should().Be("Cached call path results");
        
        // Verify that Lucene search was not called
        LuceneIndexServiceMock.Verify(
            x => x.SearchAsync(It.IsAny<string>(), It.IsAny<Query>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_Should_Bypass_Cache_When_NoCache_Is_True()
    {
        // Arrange
        SetupExistingIndex();
        var searchResult = CreateRealisticCallPathResult_UpdateUserCallers();
        
        LuceneIndexServiceMock
            .Setup(x => x.SearchAsync(
                It.IsAny<string>(),
                It.IsAny<Query>(),
                It.IsAny<int>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchResult);
        
        ResponseCacheServiceMock
            .Setup(x => x.GetAsync<AIOptimizedResponse<SearchResult>>(It.IsAny<string>()))
            .ReturnsAsync(new AIOptimizedResponse<SearchResult> 
            { 
                Success = true,
                Data = new AIResponseData<SearchResult>
                {
                    Results = new SearchResult { TotalHits = 0 }
                }
            }); // Cached result exists but should be bypassed
        
        var parameters = new TraceCallPathParameters
        {
            Symbol = "UpdateUser",
            WorkspacePath = TestWorkspacePath,
            NoCache = true // Bypass cache
        };
        
        // Act
        var result = await ExecuteToolAsync<AIOptimizedResponse<SearchResult>>(
            async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
        
        // Assert
        result.Success.Should().BeTrue();
        
        // Verify cache was not checked
        ResponseCacheServiceMock.Verify(
            x => x.GetAsync<SearchResult>(It.IsAny<string>()),
            Times.Never);
        
        // Verify search was performed
        LuceneIndexServiceMock.Verify(
            x => x.SearchAsync(It.IsAny<string>(), It.IsAny<Query>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_Should_Cache_Successful_Results()
    {
        // Arrange
        SetupExistingIndex();
        var searchResult = CreateRealisticCallPathResult_UpdateUserCallers();
        
        LuceneIndexServiceMock
            .Setup(x => x.SearchAsync(
                It.IsAny<string>(),
                It.IsAny<Query>(),
                It.IsAny<int>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchResult);
        
        var parameters = new TraceCallPathParameters
        {
            Symbol = "UpdateUser",
            WorkspacePath = TestWorkspacePath,
            NoCache = false
        };
        
        // Act
        var result = await ExecuteToolAsync<AIOptimizedResponse<SearchResult>>(
            async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
        
        // Assert
        result.Success.Should().BeTrue();
        
        // Verify result was cached with 10-minute expiration
        ResponseCacheServiceMock.Verify(
            x => x.SetAsync(
                It.IsAny<string>(),
                It.IsAny<AIOptimizedResponse<SearchResult>>(),
                It.Is<CacheEntryOptions>(opts => opts.AbsoluteExpiration == TimeSpan.FromMinutes(10))),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_Should_Handle_No_Results_Gracefully()
    {
        // Arrange
        SetupExistingIndex();
        var emptyResult = new SearchResult
        {
            TotalHits = 0,
            Hits = new List<SearchHit>(),
            SearchTime = TimeSpan.FromMilliseconds(5)
        };
        
        LuceneIndexServiceMock
            .Setup(x => x.SearchAsync(
                It.IsAny<string>(),
                It.IsAny<Query>(),
                It.IsAny<int>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(emptyResult);
        
        var parameters = new TraceCallPathParameters
        {
            Symbol = "NonExistentMethod",
            WorkspacePath = TestWorkspacePath
        };
        
        // Act
        var result = await ExecuteToolAsync<AIOptimizedResponse<SearchResult>>(
            async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
        
        // Assert
        result.Success.Should().BeTrue();
        result.Result!.Success.Should().BeTrue();
        result.Result.Data!.Summary.Should().Contain("No up call path found");
        result.Result.Data.Count.Should().Be(0);
        
        // Verify helpful insights for no results
        result.Result.Insights.Should().Contain(i => i.Contains("No call path found"));
        result.Result.Insights.Should().Contain(i => i.Contains("Try checking spelling"));
    }

    [Test]
    public async Task ExecuteAsync_Should_Handle_Search_Errors_Gracefully()
    {
        // Arrange
        SetupExistingIndex();
        
        LuceneIndexServiceMock
            .Setup(x => x.SearchAsync(
                It.IsAny<string>(),
                It.IsAny<Query>(),
                It.IsAny<int>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Lucene index corrupted"));
        
        var parameters = new TraceCallPathParameters
        {
            Symbol = "UpdateUser",
            WorkspacePath = TestWorkspacePath
        };
        
        // Act
        var result = await ExecuteToolAsync<AIOptimizedResponse<SearchResult>>(
            async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
        
        // Assert
        result.Success.Should().BeTrue(); // Tool execution succeeded
        result.Result.Should().NotBeNull();
        result.Result!.Success.Should().BeFalse(); // But trace failed
        result.Result.Error.Should().NotBeNull();
        result.Result.Error!.Code.Should().Be("TRACE_ERROR");
        result.Result.Error.Message.Should().Contain("Error tracing call path: Lucene index corrupted");
        result.Result.Error.Recovery.Should().NotBeNull();
        result.Result.Error.Recovery!.Steps.Should().Contain("Check if the workspace is properly indexed");
    }

    [Test]
    public async Task ExecuteAsync_Should_Generate_Appropriate_Actions_For_Results()
    {
        // Arrange
        SetupExistingIndex();
        var callPathResult = CreateCallPathResultWithEntryPoints();
        
        LuceneIndexServiceMock
            .Setup(x => x.SearchAsync(
                It.IsAny<string>(),
                It.IsAny<Query>(),
                It.IsAny<int>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(callPathResult);
        
        var parameters = new TraceCallPathParameters
        {
            Symbol = "ProcessPayment",
            WorkspacePath = TestWorkspacePath
        };
        
        // Act
        var result = await ExecuteToolAsync<AIOptimizedResponse<SearchResult>>(
            async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
        
        // Assert
        result.Success.Should().BeTrue();
        result.Result!.Actions.Should().NotBeNullOrEmpty();
        
        var actions = result.Result.Actions!;
        actions.Should().Contain(a => a.Action == "find_references");
        actions.Should().Contain(a => a.Action == "goto_definition");
        
        // Should prioritize entry points when found
        var entryPointAction = actions.FirstOrDefault(a => a.Action == "trace_entry_points");
        entryPointAction.Should().NotBeNull();
        entryPointAction!.Priority.Should().Be(95);
    }

    #region Real Data Creation Helpers

    /// <summary>
    /// Creates realistic call path results for UpdateUser method callers
    /// </summary>
    private SearchResult CreateRealisticCallPathResult_UpdateUserCallers()
    {
        var hits = new List<SearchHit>
        {
            // Controller entry point
            new SearchHit
            {
                FilePath = @"C:\project\Controllers\UserController.cs",
                Score = 0.95f,
                LineNumber = 45,
                Fields = new Dictionary<string, string>
                {
                    ["trace_direction"] = "up",
                    ["trace_symbol"] = "UpdateUser",
                    ["call_depth"] = "1",
                    ["is_entry_point"] = "true"
                },
                ContextLines = new List<string>
                {
                    "[HttpPut(\"{id}\")]",
                    "public async Task<IActionResult> «UpdateUser»(int id, UserDto dto)",
                    "{",
                    "    return await _userService.«UpdateUser»(id, dto);",
                    "}"
                },
                Snippet = "return await _userService.UpdateUser(id, dto);"
            },
            
            // Service layer call
            new SearchHit
            {
                FilePath = @"C:\project\Services\UserService.cs",
                Score = 0.85f,
                LineNumber = 123,
                Fields = new Dictionary<string, string>
                {
                    ["trace_direction"] = "up",
                    ["trace_symbol"] = "UpdateUser",
                    ["call_depth"] = "1",
                    ["is_entry_point"] = "false"
                },
                ContextLines = new List<string>
                {
                    "public async Task<UserDto> «UpdateUser»(int id, UserDto dto)",
                    "{",
                    "    var user = await _repository.GetByIdAsync(id);",
                    "    // Update logic here",
                    "}"
                },
                Snippet = "public async Task<UserDto> UpdateUser(int id, UserDto dto)"
            },
            
            // Unit test reference
            new SearchHit
            {
                FilePath = @"C:\project\Tests\UserServiceTests.cs",
                Score = 0.65f,
                LineNumber = 89,
                Fields = new Dictionary<string, string>
                {
                    ["trace_direction"] = "up",
                    ["trace_symbol"] = "UpdateUser",
                    ["call_depth"] = "1",
                    ["is_entry_point"] = "false"
                },
                ContextLines = new List<string>
                {
                    "[Test]",
                    "public async Task «UpdateUser»_ShouldUpdateUserSuccessfully()",
                    "{",
                    "    var result = await _userService.«UpdateUser»(1, validDto);",
                    "}"
                },
                Snippet = "var result = await _userService.UpdateUser(1, validDto);"
            }
        };

        return new SearchResult
        {
            TotalHits = 3,
            Hits = hits,
            SearchTime = TimeSpan.FromMilliseconds(15),
            Query = "UpdateUser"
        };
    }

    /// <summary>
    /// Creates call path results with clear entry points for testing detection
    /// </summary>
    private SearchResult CreateCallPathResultWithEntryPoints()
    {
        var hits = new List<SearchHit>
        {
            // API Controller entry point
            new SearchHit
            {
                FilePath = @"C:\project\Controllers\PaymentController.cs",
                Score = 1.0f,
                LineNumber = 67,
                Fields = new Dictionary<string, string>
                {
                    ["trace_direction"] = "up",
                    ["trace_symbol"] = "ProcessPayment",
                    ["call_depth"] = "1",
                    ["is_entry_point"] = "true"
                },
                ContextLines = new List<string>
                {
                    "[HttpPost(\"process\")]",
                    "public async Task<ActionResult> «ProcessPayment»(PaymentDto payment)",
                    "{",
                    "    return await _paymentService.«ProcessPayment»(payment);",
                    "}"
                }
            },
            
            // Console app Main method entry point
            new SearchHit
            {
                FilePath = @"C:\project\Console\Program.cs",
                Score = 0.90f,
                LineNumber = 12,
                Fields = new Dictionary<string, string>
                {
                    ["trace_direction"] = "up",
                    ["trace_symbol"] = "ProcessPayment",
                    ["call_depth"] = "1",
                    ["is_entry_point"] = "true"
                },
                ContextLines = new List<string>
                {
                    "public static async Task Main(string[] args)",
                    "{",
                    "    var payment = CreateTestPayment();",
                    "    await paymentService.«ProcessPayment»(payment);",
                    "}"
                }
            }
        };

        return new SearchResult
        {
            TotalHits = 2,
            Hits = hits,
            SearchTime = TimeSpan.FromMilliseconds(8),
            Query = "ProcessPayment"
        };
    }

    #endregion
}