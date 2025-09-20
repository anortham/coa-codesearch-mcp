using NUnit.Framework;
using FluentAssertions;
using COA.CodeSearch.McpServer.Services.TypeExtraction;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace COA.CodeSearch.McpServer.Tests.Services.TypeExtraction
{
    [TestFixture]
    public class MinimalTypeExtractionTest
    {
        [Test]
        public void TypeExtractionService_Should_Initialize()
        {
            // Arrange
            var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<TypeExtractionService>.Instance;
            var loggerQuery = Microsoft.Extensions.Logging.Abstractions.NullLogger<QueryBasedExtractor>.Instance;

            // Act
            var languageRegistry = Substitute.For<ILanguageRegistry>();
            var queryBasedExtractor = new QueryBasedExtractor(loggerQuery);
            var service = new TypeExtractionService(logger, languageRegistry, queryBasedExtractor);

            // Assert
            service.Should().NotBeNull();
        }

        [Test]
        public async Task TypeExtractionService_Should_Handle_Empty_Content()
        {
            // Arrange
            var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<TypeExtractionService>.Instance;
            var loggerQuery = Microsoft.Extensions.Logging.Abstractions.NullLogger<QueryBasedExtractor>.Instance;
            var languageRegistry = Substitute.For<ILanguageRegistry>();
            var queryBasedExtractor = new QueryBasedExtractor(loggerQuery);
            var service = new TypeExtractionService(logger, languageRegistry, queryBasedExtractor);

            // Act
            var result = await service.ExtractTypes("", "test.cs");

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue(); // Empty content is handled successfully
            result.Types.Should().BeEmpty();
            result.Methods.Should().BeEmpty();
        }

        [Test]
        public async Task TypeExtractionService_Should_Handle_Unknown_Extension()
        {
            // Arrange
            var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<TypeExtractionService>.Instance;
            var loggerQuery = Microsoft.Extensions.Logging.Abstractions.NullLogger<QueryBasedExtractor>.Instance;
            var languageRegistry = Substitute.For<ILanguageRegistry>();
            var queryBasedExtractor = new QueryBasedExtractor(loggerQuery);
            var service = new TypeExtractionService(logger, languageRegistry, queryBasedExtractor);

            // Act
            var result = await service.ExtractTypes("some content", "test.unknown");

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeFalse();
        }
    }
}