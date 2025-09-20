using NUnit.Framework;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using COA.CodeSearch.McpServer.Services.TypeExtraction;
using System;
using System.Threading.Tasks;
using Moq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace COA.CodeSearch.McpServer.Tests.Services.TypeExtraction
{
    [TestFixture]
    public class DetailedExtractionTest
    {
        private Mock<ILogger<BunTreeSitterService>> _loggerMock = null!;
        private Mock<IConfiguration> _configuration = null!;
        private JsonSerializerOptions _jsonOptions = null!;

        [SetUp]
        public void Setup()
        {
            _loggerMock = new Mock<ILogger<BunTreeSitterService>>();
            _loggerMock.Setup(x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
                .Callback<LogLevel, EventId, object, Exception?, Delegate>((level, eventId, state, exception, formatter) =>
                {
                    var message = formatter.DynamicInvoke(state, exception);
                    Console.WriteLine($"[{level}] {message}");
                });

            _configuration = new Mock<IConfiguration>();
            var configSection = new Mock<IConfigurationSection>();
            configSection.Setup(x => x.Value).Returns((string?)null);
            _configuration.Setup(x => x.GetSection("CodeSearch:TreeSitterServicePath")).Returns(configSection.Object);

            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
        }

        [Test]
        public async Task ShowExtraction_TypeScript_Interface_WithExtends()
        {
            const string code = @"
export interface User {
    id: number;
    name: string;
}

export interface AdminUser extends User {
    adminLevel: number;
    permissions: string[];
}";

            using var service = new BunTreeSitterService(_loggerMock.Object, _configuration.Object);
            var result = await service.ExtractTypesAsync(code, "typescript", "test.ts");

            Console.WriteLine("\n=== EXTRACTION RESULT ===");
            Console.WriteLine($"Success: {result.Success}");
            Console.WriteLine($"Language: {result.Language}");
            Console.WriteLine($"\nTypes ({result.Types.Count}):");
            foreach (var type in result.Types)
            {
                Console.WriteLine($"  {type.Name}:");
                Console.WriteLine($"    Kind: {type.Kind}");
                Console.WriteLine($"    Line: {type.Line}");
                Console.WriteLine($"    IsExported: {type.IsExported}");
                Console.WriteLine($"    BaseTypes: [{string.Join(", ", type.BaseTypes)}]");
                Console.WriteLine($"    TypeParameters: [{string.Join(", ", type.TypeParameters)}]");
                Console.WriteLine($"    Signature: {type.Signature?.Replace("\n", "\\n")}");
            }

            result.Success.Should().BeTrue();
            result.Types.Should().HaveCount(2);
        }

        [Test]
        public async Task ShowExtraction_JavaScript_Functions()
        {
            const string code = @"
function calculateTotal(items) {
    return items.reduce((sum, item) => sum + item.price, 0);
}

async function fetchUser(id) {
    const response = await fetch(`/api/users/${id}`);
    return response.json();
}

const processData = (data) => {
    return data.map(item => item.value);
};";

            using var service = new BunTreeSitterService(_loggerMock.Object, _configuration.Object);
            var result = await service.ExtractTypesAsync(code, "javascript", "test.js");

            Console.WriteLine("\n=== EXTRACTION RESULT ===");
            Console.WriteLine($"Success: {result.Success}");
            Console.WriteLine($"Language: {result.Language}");
            Console.WriteLine($"\nMethods ({result.Methods.Count}):");
            foreach (var method in result.Methods)
            {
                Console.WriteLine($"  {method.Name}:");
                Console.WriteLine($"    Line: {method.Line}");
                Console.WriteLine($"    IsAsync: {method.IsAsync}");
                Console.WriteLine($"    IsStatic: {method.IsStatic}");
                Console.WriteLine($"    IsGenerator: {method.IsGenerator}");
                Console.WriteLine($"    IsExported: {method.IsExported}");
                Console.WriteLine($"    Parameters: [{string.Join(", ", method.Parameters)}]");
                Console.WriteLine($"    DetailedParameters: {method.DetailedParameters.Count} items");
                Console.WriteLine($"    Signature: {method.Signature?.Replace("\n", "\\n")}");
            }

            result.Success.Should().BeTrue();
            // Don't assert count yet - just see what we get
        }
    }
}