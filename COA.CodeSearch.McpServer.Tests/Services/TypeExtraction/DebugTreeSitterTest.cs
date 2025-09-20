using NUnit.Framework;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using COA.CodeSearch.McpServer.Services.TypeExtraction;
using System;
using System.Threading.Tasks;
using Moq;

namespace COA.CodeSearch.McpServer.Tests.Services.TypeExtraction
{
    [TestFixture]
    public class DebugTreeSitterTest
    {
        [Test]
        public async Task Debug_TreeSitterService_Startup()
        {
            // Arrange - Create real logger that outputs to console
            var loggerMock = new Mock<ILogger<BunTreeSitterService>>();

            // Log all messages to console for debugging
            loggerMock.Setup(x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
                .Callback<LogLevel, EventId, object, Exception?, Delegate>((level, eventId, state, exception, formatter) =>
                {
                    var message = formatter.DynamicInvoke(state, exception);
                    Console.WriteLine($"[{level}] {message}");
                    if (exception != null)
                    {
                        Console.WriteLine($"Exception: {exception}");
                    }
                });

            var configMock = new Mock<IConfiguration>();
            var configSection = new Mock<IConfigurationSection>();
            configSection.Setup(x => x.Value).Returns((string?)null);
            configMock.Setup(x => x.GetSection("CodeSearch:TreeSitterServicePath")).Returns(configSection.Object);

            // Act
            try
            {
                Console.WriteLine("=== Starting BunTreeSitterService ===");
                using var service = new BunTreeSitterService(loggerMock.Object, configMock.Object);

                Console.WriteLine("=== Service created, attempting extraction ===");

                const string code = @"interface TestInterface { id: number; }";
                var result = await service.ExtractTypesAsync(code, "typescript", "test.ts");

                Console.WriteLine($"=== Extraction result: Success={result.Success} ===");
                if (!result.Success)
                {
                    Console.WriteLine("Extraction failed - checking error details");
                }
                else
                {
                    Console.WriteLine($"Found {result.Types.Count} types, {result.Methods.Count} methods");
                }

                // Assert
                result.Success.Should().BeTrue("Tree-sitter service should extract TypeScript interface");
                result.Types.Should().HaveCount(1);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"=== Exception during test: {ex} ===");
                throw;
            }
        }
    }
}