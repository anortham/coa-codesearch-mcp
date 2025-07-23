using System.Text.Json;
using COA.Mcp.Protocol;
using FluentAssertions;

namespace COA.Mcp.Protocol.Tests;

public class ProgressNotificationTests
{
    [Fact]
    public void Constructor_ShouldSetMethodToProgressNotifications()
    {
        // Act
        var notification = new ProgressNotification();

        // Assert
        notification.Method.Should().Be("notifications/progress");
        notification.JsonRpc.Should().Be("2.0");
    }

    [Fact]
    public void ParameterizedConstructor_ShouldSetAllProperties()
    {
        // Arrange
        var token = "workspace-index-123";
        var progress = 45;
        var total = 100;
        var message = "Indexing UserService.cs";

        // Act
        var notification = new ProgressNotification(token, progress, total, message);

        // Assert
        notification.Method.Should().Be("notifications/progress");
        notification.ProgressToken.Should().Be(token);
        notification.Progress.Should().Be(progress);
        notification.Total.Should().Be(total);
        notification.Message.Should().Be(message);
    }

    [Fact]
    public void ParameterizedConstructor_WithOptionalNulls_ShouldSetOnlyRequiredProperties()
    {
        // Arrange
        var token = "batch-op-456";
        var progress = 3;

        // Act
        var notification = new ProgressNotification(token, progress);

        // Assert
        notification.ProgressToken.Should().Be(token);
        notification.Progress.Should().Be(progress);
        notification.Total.Should().BeNull();
        notification.Message.Should().BeNull();
    }

    [Fact]
    public void JsonSerialization_WithAllProperties_ShouldSerializeCorrectly()
    {
        // Arrange
        var notification = new ProgressNotification("test-token", 25, 50, "Processing file");

        // Act
        var json = JsonSerializer.Serialize(notification, new JsonSerializerOptions 
        { 
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
        });

        // Assert
        json.Should().Contain("\"jsonrpc\":\"2.0\"");
        json.Should().Contain("\"method\":\"notifications/progress\"");
        json.Should().Contain("\"progressToken\":\"test-token\"");
        json.Should().Contain("\"progress\":25");
        json.Should().Contain("\"total\":50");
        json.Should().Contain("\"message\":\"Processing file\"");
    }

    [Fact]
    public void JsonSerialization_WithNullOptionalProperties_ShouldExcludeNulls()
    {
        // Arrange
        var notification = new ProgressNotification("test-token", 10);

        // Act
        var json = JsonSerializer.Serialize(notification, new JsonSerializerOptions 
        { 
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
        });

        // Assert
        json.Should().Contain("\"progressToken\":\"test-token\"");
        json.Should().Contain("\"progress\":10");
        json.Should().NotContain("total");
        json.Should().NotContain("message");
    }

    [Fact]
    public void JsonDeserialization_ShouldDeserializeCorrectly()
    {
        // Arrange
        var json = """
        {
            "jsonrpc": "2.0",
            "method": "notifications/progress",
            "progressToken": "workspace-idx",
            "progress": 75,
            "total": 100,
            "message": "Almost done"
        }
        """;

        // Act
        var notification = JsonSerializer.Deserialize<ProgressNotification>(json, new JsonSerializerOptions 
        { 
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
        });

        // Assert
        notification.Should().NotBeNull();
        notification!.JsonRpc.Should().Be("2.0");
        notification.Method.Should().Be("notifications/progress");
        notification.ProgressToken.Should().Be("workspace-idx");
        notification.Progress.Should().Be(75);
        notification.Total.Should().Be(100);
        notification.Message.Should().Be("Almost done");
    }

    [Fact]
    public void JsonDeserialization_WithMissingOptionalProperties_ShouldHandleGracefully()
    {
        // Arrange
        var json = """
        {
            "jsonrpc": "2.0",
            "method": "notifications/progress",
            "progressToken": "test-op",
            "progress": 42
        }
        """;

        // Act
        var notification = JsonSerializer.Deserialize<ProgressNotification>(json, new JsonSerializerOptions 
        { 
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
        });

        // Assert
        notification.Should().NotBeNull();
        notification!.ProgressToken.Should().Be("test-op");
        notification.Progress.Should().Be(42);
        notification.Total.Should().BeNull();
        notification.Message.Should().BeNull();
    }

    [Theory]
    [InlineData(0, 100, "Starting operation")]
    [InlineData(50, 100, "Halfway done")]
    [InlineData(100, 100, "Operation complete")]
    [InlineData(25, null, "Indeterminate progress")]
    public void ProgressNotification_ShouldHandleVariousProgressScenarios(int progress, int? total, string message)
    {
        // Act
        var notification = new ProgressNotification("scenario-test", progress, total, message);

        // Assert
        notification.Progress.Should().Be(progress);
        notification.Total.Should().Be(total);
        notification.Message.Should().Be(message);
        notification.Method.Should().Be("notifications/progress");
    }
}