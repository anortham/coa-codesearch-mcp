using NUnit.Framework;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using COA.CodeSearch.McpServer.Services.TypeExtraction;
using System.Threading.Tasks;
using System.Linq;
using Moq;
using System;

namespace COA.CodeSearch.McpServer.Tests.Services.TypeExtraction
{
    [TestFixture]
    public class SimpleTypeScriptTest
    {
        [Test]
        public async Task ExtractTypes_SimpleInterface()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<BunTreeSitterService>>();
            var configMock = new Mock<IConfiguration>();
            var configSection = new Mock<IConfigurationSection>();
            configSection.Setup(x => x.Value).Returns((string?)null);
            configMock.Setup(x => x.GetSection("CodeSearch:TreeSitterServicePath")).Returns(configSection.Object);

            using var service = new BunTreeSitterService(loggerMock.Object, configMock.Object);

            const string code = @"
interface User {
    id: number;
    name: string;
}";

            // Act
            Console.WriteLine("Starting extraction...");
            var result = await service.ExtractTypesAsync(code, "typescript", "test.ts");
            Console.WriteLine($"Extraction complete. Success: {result.Success}");

            if (!result.Success)
            {
                Console.WriteLine("Extraction failed!");
            }
            else
            {
                Console.WriteLine($"Found {result.Types.Count} types:");
                foreach (var type in result.Types)
                {
                    Console.WriteLine($"  - {type.Kind}: {type.Name} at line {type.Line}");
                }
            }

            // Assert
            result.Success.Should().BeTrue();
            result.Types.Should().HaveCount(1);

            var userInterface = result.Types.First();
            userInterface.Name.Should().Be("User");
            userInterface.Kind.Should().Be("interface");
            userInterface.Line.Should().Be(2);
        }
    }
}