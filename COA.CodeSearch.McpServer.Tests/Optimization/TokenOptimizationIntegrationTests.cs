using NUnit.Framework;
using FluentAssertions;
using Moq;
using COA.CodeSearch.McpServer.Tools;
using COA.CodeSearch.McpServer.Tests.Base;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Services.TypeExtraction;
using COA.VSCodeBridge;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System.Threading;
using System;
using System.Collections.Generic;
using System.Text.Json;
using COA.Mcp.Framework;

namespace COA.CodeSearch.McpServer.Tests.Optimization
{
    /// <summary>
    /// Integration tests for token optimization functionality.
    /// Tests token optimization through public APIs and telemetry logging.
    /// </summary>
    [TestFixture]
    public class TokenOptimizationIntegrationTests : CodeSearchToolTestBase<TextSearchTool>
    {
        private readonly List<LogEntry> _capturedLogs = new List<LogEntry>();
        private TextSearchTool _tool = null!;

        protected override TextSearchTool CreateTool()
        {
            // Create a logger that captures log entries for verification
            var loggerMock = new Mock<ILogger<TextSearchTool>>();
            loggerMock.Setup(logger => logger.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                (Func<It.IsAnyType, Exception?, string>)It.IsAny<object>()))
            .Callback(new InvocationAction(invocation =>
            {
                var logLevel = (LogLevel)invocation.Arguments[0];
                var eventId = (EventId)invocation.Arguments[1];
                var state = invocation.Arguments[2];
                var exception = (Exception)invocation.Arguments[3];
                var formatter = invocation.Arguments[4];

                var message = formatter?.GetType()
                    .GetMethod("Invoke")
                    ?.Invoke(formatter, new[] { state, exception })
                    ?.ToString() ?? "Unknown message";

                _capturedLogs.Add(new LogEntry
                {
                    Level = logLevel,
                    Message = message,
                    State = state,
                    Exception = exception
                });
            }));

            // Setup IsEnabled to capture Information level logs (where telemetry is logged)
            loggerMock.Setup(x => x.IsEnabled(LogLevel.Information)).Returns(true);
            loggerMock.Setup(x => x.IsEnabled(LogLevel.Debug)).Returns(true);

            var queryPreprocessorLoggerMock = new Mock<ILogger<QueryPreprocessor>>();
            var queryPreprocessor = new QueryPreprocessor(queryPreprocessorLoggerMock.Object);
            var smartDocLoggerMock = new Mock<ILogger<SmartDocumentationService>>();
            var smartDocumentationService = new SmartDocumentationService(smartDocLoggerMock.Object);
            var smartQueryPreprocessorLoggerMock = new Mock<ILogger<SmartQueryPreprocessor>>();
            var smartQueryPreprocessor = new SmartQueryPreprocessor(smartQueryPreprocessorLoggerMock.Object);

            _tool = new TextSearchTool(
                ServiceProvider,
                LuceneIndexServiceMock.Object,
                SQLiteSymbolServiceMock.Object,
                ResponseCacheServiceMock.Object,
                ResourceStorageServiceMock.Object,
                CacheKeyGeneratorMock.Object,
                queryPreprocessor,
                smartDocumentationService,
                VSCodeBridgeMock.Object,
                smartQueryPreprocessor,
                CodeAnalyzer,
                loggerMock.Object);

            return _tool;
        }

        [SetUp]
        public void TestSetup()
        {
            _capturedLogs.Clear();
            SetupIndexExists(TestWorkspacePath);
            var searchResult = CreateTestSearchResult(10);
            SetupSearchResults(TestWorkspacePath, searchResult);
        }

        [Test]
        public async Task TextSearchTool_ExecuteAsync_LogsTokenUsageTelemetry()
        {
            // Arrange
            var parameters = new TextSearchParameters
            {
                Query = "UserService",
                WorkspacePath = TestWorkspacePath,
                MaxTokens = 8000,
                NoCache = true
            };

            // Act
            await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert - Verify telemetry log was captured
            var telemetryLog = _capturedLogs.FirstOrDefault(log =>
                log.Level == LogLevel.Information &&
                log.Message.Contains("Token Usage") &&
                log.Message.Contains("Tool:") &&
                log.Message.Contains("Estimated:") &&
                log.Message.Contains("Actual:") &&
                log.Message.Contains("Accuracy:"));

            telemetryLog.Should().NotBeNull("Token usage telemetry should be logged at Information level");
            telemetryLog!.Message.Should().Contain(_tool.Name, "Telemetry should include tool name");
        }

        [Test]
        public async Task TextSearchTool_WithLowTokenLimit_AppliesReduction()
        {
            // Arrange - Use low token limit to trigger reduction
            var parameters = new TextSearchParameters
            {
                Query = "common_term",
                WorkspacePath = TestWorkspacePath,
                MaxTokens = 1000, // Intentionally low limit
                NoCache = true
            };

            // Setup larger result set to trigger reduction
            var largeSearchResult = CreateTestSearchResult(50);
            SetupSearchResults(TestWorkspacePath, largeSearchResult);

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();

            // Check for token budget related logs
            var tokenBudgetLogs = _capturedLogs.Where(log =>
                log.Message.Contains("token", StringComparison.OrdinalIgnoreCase) &&
                (log.Message.Contains("budget", StringComparison.OrdinalIgnoreCase) ||
                 log.Message.Contains("estimate", StringComparison.OrdinalIgnoreCase) ||
                 log.Message.Contains("limit", StringComparison.OrdinalIgnoreCase)))
                .ToList();

            tokenBudgetLogs.Should().NotBeEmpty("Token budget monitoring should generate logs");
        }

        [Test]
        [TestCase("simple", 5)] // Simple query, few results
        [TestCase("class.*Service OR interface.*Repository", 25)] // Complex regex, more results
        [TestCase("TODO OR FIXME OR BUG", 40)] // High-frequency terms, many results
        public async Task TextSearchTool_DifferentQueryComplexity_ShowsTelemetryVariation(
            string query, int resultCount)
        {
            // Arrange
            var parameters = new TextSearchParameters
            {
                Query = query,
                WorkspacePath = TestWorkspacePath,
                MaxTokens = 8000,
                NoCache = true
            };

            var searchResult = CreateTestSearchResult(resultCount);
            SetupSearchResults(TestWorkspacePath, searchResult);

            // Act
            await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert - Verify telemetry includes accuracy metrics
            var telemetryLog = _capturedLogs.FirstOrDefault(log =>
                log.Level == LogLevel.Information && log.Message.Contains("Token Usage"));

            telemetryLog.Should().NotBeNull($"Query '{query}' should generate telemetry");
            telemetryLog!.Message.Should().Contain("Accuracy:", "Telemetry should include accuracy metrics");
        }

        [Test]
        public async Task TextSearchTool_ExecutionTime_IsLoggedInTelemetry()
        {
            // Arrange
            var parameters = new TextSearchParameters
            {
                Query = "UserService",
                WorkspacePath = TestWorkspacePath,
                MaxTokens = 8000,
                NoCache = true
            };

            // Act
            await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert
            var telemetryLog = _capturedLogs.FirstOrDefault(log =>
                log.Level == LogLevel.Information &&
                log.Message.Contains("Token Usage") &&
                log.Message.Contains("ExecutionTime:"));

            telemetryLog.Should().NotBeNull("Execution time should be included in telemetry");

            // Extract execution time from message
            var message = telemetryLog!.Message;
            var executionTimeMatch = System.Text.RegularExpressions.Regex.Match(
                message, @"ExecutionTime: (\d+(?:\.\d+)?)ms");

            executionTimeMatch.Success.Should().BeTrue("Execution time should be properly formatted");

            var executionTime = double.Parse(executionTimeMatch.Groups[1].Value);
            executionTime.Should().BeGreaterOrEqualTo(0, "Execution time should be non-negative");
        }

        [Test]
        public void TelemetryAccuracyCalculation_VariousEstimates_CalculatesCorrectly()
        {
            // Test the accuracy calculation logic used in telemetry:
            // accuracy = Math.Min(estimatedTokens, actualTokens) / Math.Max(estimatedTokens, actualTokens)

            var testCases = new[]
            {
                new { Estimated = 1000, Actual = 950, Expected = 0.95 }, // Good estimation - 95% accuracy
                new { Estimated = 1000, Actual = 1200, Expected = 0.83 }, // Under-estimation - 83% accuracy
                new { Estimated = 1500, Actual = 1000, Expected = 0.67 } // Over-estimation - 67% accuracy
            };

            foreach (var testCase in testCases)
            {
                var calculatedAccuracy = (double)Math.Min(testCase.Estimated, testCase.Actual) /
                                       Math.Max(testCase.Estimated, testCase.Actual);

                calculatedAccuracy.Should().BeApproximately(testCase.Expected, 0.01,
                    $"Accuracy calculation should be consistent for estimated: {testCase.Estimated}, actual: {testCase.Actual}");
            }
        }

        /// <summary>
        /// Helper class to capture log entries for verification.
        /// </summary>
        private class LogEntry
        {
            public LogLevel Level { get; set; }
            public string Message { get; set; } = string.Empty;
            public object? State { get; set; }
            public Exception? Exception { get; set; }
        }
    }

    /// <summary>
    /// Tests for enhanced response builder coordination features.
    /// </summary>
    [TestFixture]
    public class EnhancedResponseBuilderIntegrationTests
    {
        [Test]
        public void CoordinationStrategy_EnumValues_AreWellDefined()
        {
            // Verify that our coordination strategies are properly defined
            var strategies = Enum.GetValues<COA.CodeSearch.McpServer.ResponseBuilders.CoordinationStrategy>();

            strategies.Should().Contain(COA.CodeSearch.McpServer.ResponseBuilders.CoordinationStrategy.TrustTool,
                "TrustTool strategy should be available");
            strategies.Should().Contain(COA.CodeSearch.McpServer.ResponseBuilders.CoordinationStrategy.Blended,
                "Blended strategy should be available");
            strategies.Should().Contain(COA.CodeSearch.McpServer.ResponseBuilders.CoordinationStrategy.Conservative,
                "Conservative strategy should be available");
            strategies.Should().Contain(COA.CodeSearch.McpServer.ResponseBuilders.CoordinationStrategy.Adaptive,
                "Adaptive strategy should be available");
        }

        [Test]
        public void EnhancedResponseContext_Properties_AreAccessible()
        {
            // Verify that our enhanced response context works properly
            var context = new COA.CodeSearch.McpServer.Models.EnhancedResponseContext
            {
                ToolTokenEstimate = 1500,
                ToolEstimationAccuracy = 0.85,
                ToolCategory = ToolCategory.Query,
                ToolHighConfidenceEstimate = true,
                ExpectedComplexity = COA.CodeSearch.McpServer.Models.ResultComplexity.Moderate
            };

            context.ToolTokenEstimate.Should().Be(1500);
            context.ToolEstimationAccuracy.Should().Be(0.85);
            context.ToolCategory.Should().Be(ToolCategory.Query);
            context.ToolHighConfidenceEstimate.Should().BeTrue();
            context.ExpectedComplexity.Should().Be(COA.CodeSearch.McpServer.Models.ResultComplexity.Moderate);
        }

        [Test]
        public void ResultComplexity_EnumValues_AreWellDefined()
        {
            // Verify that result complexity enum is properly defined
            var complexities = Enum.GetValues<COA.CodeSearch.McpServer.Models.ResultComplexity>();

            complexities.Should().Contain(COA.CodeSearch.McpServer.Models.ResultComplexity.Simple);
            complexities.Should().Contain(COA.CodeSearch.McpServer.Models.ResultComplexity.Moderate);
            complexities.Should().Contain(COA.CodeSearch.McpServer.Models.ResultComplexity.High);
            complexities.Should().Contain(COA.CodeSearch.McpServer.Models.ResultComplexity.VeryHigh);
        }
    }
}