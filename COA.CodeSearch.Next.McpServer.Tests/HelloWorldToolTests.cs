using FluentAssertions;
using NUnit.Framework;
using COA.CodeSearch.Next.McpServer.Tools;

namespace COA.CodeSearch.Next.McpServer.Tests;

[TestFixture]
public class HelloWorldToolTests
{
    private HelloWorldTool _tool = null!;

    [SetUp]
    public void Setup()
    {
        _tool = new HelloWorldTool();
    }

    [Test]
    public async Task HelloWorld_WithName_ReturnsPersonalizedGreeting()
    {
        // Arrange
        var parameters = new HelloWorldParameters
        {
            Name = "Alice"
        };

        // Act
        var result = await _tool.ExecuteAsync(parameters);

        // Assert
        result.Should().NotBeNull();
        result.Greeting.Should().Be("Hello, Alice!");
    }

    [Test]
    public async Task HelloWorld_WithoutName_ReturnsDefaultGreeting()
    {
        // Arrange
        var parameters = new HelloWorldParameters();

        // Act
        var result = await _tool.ExecuteAsync(parameters);

        // Assert
        result.Should().NotBeNull();
        result.Greeting.Should().Be("Hello, World!");
    }

    [Test]
    public async Task HelloWorld_WithTime_IncludesTimeInResponse()
    {
        // Arrange
        var parameters = new HelloWorldParameters
        {
            Name = "Bob",
            IncludeTime = true
        };

        // Act
        var result = await _tool.ExecuteAsync(parameters);

        // Assert
        result.Should().NotBeNull();
        result.Greeting.Should().Contain("Bob")
            .And.Contain("UTC");
    }

    [Test]
    public async Task HelloWorld_Always_IncludesTimestamp()
    {
        // Arrange
        var parameters = new HelloWorldParameters();
        var timeBefore = DateTime.UtcNow;

        // Act
        var result = await _tool.ExecuteAsync(parameters);
        var timeAfter = DateTime.UtcNow;

        // Assert
        result.Should().NotBeNull();
        result.Timestamp.Should().BeAfter(timeBefore.AddSeconds(-1))
            .And.BeBefore(timeAfter.AddSeconds(1));
    }

    [Test]
    public async Task HelloWorld_GetDisplayText_ReturnsGreeting()
    {
        // Arrange
        var parameters = new HelloWorldParameters
        {
            Name = "Test"
        };

        // Act
        var result = await _tool.ExecuteAsync(parameters);

        // Assert
        result.GetDisplayText().Should().Be("Hello, Test!");
    }
}