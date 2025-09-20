using NUnit.Framework;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using COA.CodeSearch.McpServer.Services.TypeExtraction;
using System;
using System.Threading.Tasks;
using Moq;

namespace COA.CodeSearch.McpServer.Tests.Services.TypeExtraction
{
    [TestFixture]
    public class TreeSitterDebugTest
    {
        [Test]
        public async Task DebugTreeSitterService_BasicHealthCheck()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<BunTreeSitterService>>();

            // Setup logger to capture all messages
            loggerMock.Setup(x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()
            )).Callback<LogLevel, EventId, object, Exception?, Delegate>((level, id, state, ex, formatter) =>
            {
                var message = formatter.DynamicInvoke(state, ex);
                Console.WriteLine($"[{level}] {message}");
            });

            var configMock = new Mock<IConfiguration>();
            var configSection = new Mock<IConfigurationSection>();
            configSection.Setup(x => x.Value).Returns((string?)null);
            configMock.Setup(x => x.GetSection("CodeSearch:TreeSitterServicePath")).Returns(configSection.Object);

            Console.WriteLine("Creating BunTreeSitterService...");

            try
            {
                // Act
                using var service = new BunTreeSitterService(loggerMock.Object, configMock.Object);

                Console.WriteLine("Service created successfully!");

                // Try a simple extraction
                var result = await service.ExtractTypesAsync("const x = 1;", "javascript", "test.js");

                Console.WriteLine($"Extraction result: Success={result.Success}, Types={result.Types.Count}, Methods={result.Methods.Count}");

                // Assert
                Assert.That(result, Is.Not.Null);
                Assert.That(result.Success, Is.True, "Extraction should succeed");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception occurred: {ex.GetType().Name}: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        [Test]
        public void DebugTreeSitterService_JustCreateAndDispose()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<BunTreeSitterService>>();
            var configMock = new Mock<IConfiguration>();
            var configSection = new Mock<IConfigurationSection>();
            configSection.Setup(x => x.Value).Returns((string?)null);
            configMock.Setup(x => x.GetSection("CodeSearch:TreeSitterServicePath")).Returns(configSection.Object);

            Console.WriteLine("Creating and immediately disposing service...");

            try
            {
                // Act
                var service = new BunTreeSitterService(loggerMock.Object, configMock.Object);
                Console.WriteLine("Service created!");
                service.Dispose();
                Console.WriteLine("Service disposed!");

                // Assert - if we get here, it worked
                Assert.Pass("Service created and disposed successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed: {ex.Message}");
                throw;
            }
        }
    }
}