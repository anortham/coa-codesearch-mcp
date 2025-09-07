using NUnit.Framework;
using FluentAssertions;
using Moq;
using COA.CodeSearch.McpServer.Tools;
using COA.CodeSearch.McpServer.Tests.Base;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using COA.VSCodeBridge;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System.Threading;
using System;
using System.Text.Json;
using COA.Mcp.Framework;

namespace COA.CodeSearch.McpServer.Tests.Tools
{
    [TestFixture]
    public class BatchOperationsToolTests : CodeSearchToolTestBase<BatchOperationsTool>
    {
        private BatchOperationsTool _tool = null!;
        private Mock<TextSearchTool> _textSearchToolMock = null!;
        private Mock<FileSearchTool> _fileSearchToolMock = null!;
        private Mock<IVSCodeBridge> _vscodeBridgeMock = null!;
        
        protected override BatchOperationsTool CreateTool()
        {
            var queryPreprocessorLoggerMock = new Mock<ILogger<QueryPreprocessor>>();
            var queryPreprocessor = new QueryPreprocessor(queryPreprocessorLoggerMock.Object);
            var projectKnowledgeServiceMock = CreateMock<IProjectKnowledgeService>();
            var smartDocLoggerMock = new Mock<ILogger<SmartDocumentationService>>();
            var smartDocumentationService = new SmartDocumentationService(smartDocLoggerMock.Object);
            var smartQueryPreprocessorLoggerMock = new Mock<ILogger<SmartQueryPreprocessor>>();
            var smartQueryPreprocessor = new SmartQueryPreprocessor(smartQueryPreprocessorLoggerMock.Object);
            
            _textSearchToolMock = new Mock<TextSearchTool>(
                ServiceProvider,
                LuceneIndexServiceMock.Object,
                ResponseCacheServiceMock.Object,
                ResourceStorageServiceMock.Object,
                CacheKeyGeneratorMock.Object,
                queryPreprocessor,
                null,
                projectKnowledgeServiceMock.Object,
                smartDocumentationService,
                VSCodeBridgeMock.Object,
                smartQueryPreprocessor,
                ToolLoggerMock.Object);
                
            _fileSearchToolMock = new Mock<FileSearchTool>(
                ServiceProvider,
                LuceneIndexServiceMock.Object,
                ResponseCacheServiceMock.Object,
                ResourceStorageServiceMock.Object,
                CacheKeyGeneratorMock.Object,
                VSCodeBridgeMock.Object,
                ToolLoggerMock.Object);
                
            _vscodeBridgeMock = new Mock<IVSCodeBridge>();
            
            _tool = new BatchOperationsTool(
                ServiceProvider,
                ToolLoggerMock.Object,
                _textSearchToolMock.Object,
                _fileSearchToolMock.Object,
                _vscodeBridgeMock.Object
            );
            return _tool;
        }

        [Test]
        public async Task ExecuteAsync_EmptyOperations_ReturnsError()
        {
            // Arrange
            var parameters = new BatchOperationsParameters
            {
                WorkspacePath = "C:\\test",
                Operations = "[]", // Empty operations array
                MaxTokens = 8000,
                ResponseMode = "adaptive",
                NoCache = false
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeFalse();
            result.Error.Should().NotBeNull();
            result.Error!.Code.Should().Be("BATCH_INVALID_OPERATIONS");
        }

        [Test]
        public async Task ExecuteAsync_InvalidJson_ReturnsError()
        {
            // Arrange
            var parameters = new BatchOperationsParameters
            {
                WorkspacePath = "C:\\test",
                Operations = "invalid json", // Invalid JSON
                MaxTokens = 8000,
                ResponseMode = "adaptive",
                NoCache = false
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeFalse();
            result.Error.Should().NotBeNull();
        }

        [Test]
        public void ValidateRequired_NullWorkspacePath_ThrowsArgumentNullException()
        {
            // Arrange
            var parameters = new BatchOperationsParameters
            {
                WorkspacePath = null!,
                Operations = "[{}]",
                MaxTokens = 8000
            };

            // Act & Assert
            Assert.ThrowsAsync<ArgumentNullException>(
                async () => await _tool.ExecuteAsync(parameters, CancellationToken.None));
        }

        [Test]
        public void ToolProperties_HaveCorrectValues()
        {
            // Assert
            _tool.Name.Should().Be(ToolNames.BatchOperations);
            _tool.Description.Should().Contain("PARALLEL search for speed");
            _tool.Category.Should().Be(ToolCategory.Query);
        }

        [Test]
        public void BatchOperation_DefaultValues_AreCorrect()
        {
            // Arrange
            var operation = new BatchOperation();

            // Assert
            operation.operation.Should().Be("");
            operation.query.Should().BeNull();
            operation.pattern.Should().BeNull();
            operation.filePath.Should().BeNull();
            operation.id.Should().BeNull();
            operation.description.Should().BeNull();
        }

        [Test]
        public void BatchOperationsResult_DefaultValues_AreCorrect()
        {
            // Arrange
            var result = new BatchOperationsResult();

            // Assert
            result.Operation.Should().Be(ToolNames.BatchOperations);
            result.Operations.Should().NotBeNull().And.BeEmpty();
            result.Summary.Should().NotBeNull();
            result.TotalDurationMs.Should().Be(0);
        }
    }
}