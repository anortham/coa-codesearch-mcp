using System.Text.Json;
using COA.Mcp.Protocol;
using FluentAssertions;

namespace COA.Mcp.Protocol.Tests;

public class TypedJsonRpcTests
{
    // Test parameter types for strongly-typed tests
    public class TestParams
    {
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
        public bool IsActive { get; set; }
    }

    public class TestResult
    {
        public string Status { get; set; } = string.Empty;
        public string[] Items { get; set; } = Array.Empty<string>();
    }

    #region TypedJsonRpcRequest Tests

    [Fact]
    public void TypedJsonRpcRequest_DefaultConstructor_ShouldSetJsonRpcVersion()
    {
        // Act
        var request = new TypedJsonRpcRequest<TestParams>();

        // Assert
        request.JsonRpc.Should().Be("2.0");
        request.Params.Should().BeNull();
    }

    [Fact]
    public void TypedJsonRpcRequest_ParameterizedConstructor_ShouldSetAllProperties()
    {
        // Arrange
        var id = "test-123";
        var method = "test/method";
        var parameters = new TestParams { Name = "Test", Value = 42, IsActive = true };

        // Act
        var request = new TypedJsonRpcRequest<TestParams>(id, method, parameters);

        // Assert
        request.Id.Should().Be(id);
        request.Method.Should().Be(method);
        request.Params.Should().Be(parameters);
        request.JsonRpc.Should().Be("2.0");
    }

    [Fact]
    public void TypedJsonRpcRequest_JsonSerialization_ShouldSerializeWithStrongTypes()
    {
        // Arrange
        var request = new TypedJsonRpcRequest<TestParams>(
            "req-1", 
            "test/execute", 
            new TestParams { Name = "Sample", Value = 123, IsActive = false }
        );

        // Act
        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions 
        { 
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
        });

        // Assert
        json.Should().Contain("\"jsonrpc\":\"2.0\"");
        json.Should().Contain("\"id\":\"req-1\"");
        json.Should().Contain("\"method\":\"test/execute\"");
        json.Should().Contain("\"name\":\"Sample\"");
        json.Should().Contain("\"value\":123");
        json.Should().Contain("\"isActive\":false");
    }

    [Fact]
    public void TypedJsonRpcRequest_JsonDeserialization_ShouldDeserializeWithStrongTypes()
    {
        // Arrange
        var json = """
        {
            "jsonrpc": "2.0",
            "id": "req-2",
            "method": "test/process",
            "params": {
                "name": "Deserialized",
                "value": 456,
                "isActive": true
            }
        }
        """;

        // Act
        var request = JsonSerializer.Deserialize<TypedJsonRpcRequest<TestParams>>(json, new JsonSerializerOptions 
        { 
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
        });

        // Assert
        request.Should().NotBeNull();
        request!.Id.ToString().Should().Be("req-2");
        request.Method.Should().Be("test/process");
        request.Params.Should().NotBeNull();
        request.Params!.Name.Should().Be("Deserialized");
        request.Params.Value.Should().Be(456);
        request.Params.IsActive.Should().BeTrue();
    }

    #endregion

    #region TypedJsonRpcResponse Tests

    [Fact]
    public void TypedJsonRpcResponse_SuccessConstructor_ShouldSetResult()
    {
        // Arrange
        var id = 42;
        var result = new TestResult { Status = "Success", Items = new[] { "Item1", "Item2" } };

        // Act
        var response = new TypedJsonRpcResponse<TestResult>(id, result);

        // Assert
        response.Id.Should().Be(id);
        response.Result.Should().Be(result);
        response.Error.Should().BeNull();
        response.IsError.Should().BeFalse();
        response.JsonRpc.Should().Be("2.0");
    }

    [Fact]
    public void TypedJsonRpcResponse_ErrorConstructor_ShouldSetError()
    {
        // Arrange
        var id = "error-test";
        var error = new JsonRpcError 
        { 
            Code = JsonRpcErrorCodes.InvalidParams, 
            Message = "Invalid parameters provided" 
        };

        // Act
        var response = new TypedJsonRpcResponse<TestResult>(id, error);

        // Assert
        response.Id.Should().Be(id);
        response.Result.Should().BeNull();
        response.Error.Should().Be(error);
        response.IsError.Should().BeTrue();
    }

    [Fact]
    public void TypedJsonRpcResponse_JsonSerialization_WithResult_ShouldSerializeCorrectly()
    {
        // Arrange
        var response = new TypedJsonRpcResponse<TestResult>(
            "resp-1", 
            new TestResult { Status = "Complete", Items = new[] { "A", "B" } }
        );

        // Act
        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions 
        { 
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
        });

        // Assert
        json.Should().Contain("\"jsonrpc\":\"2.0\"");
        json.Should().Contain("\"id\":\"resp-1\"");
        json.Should().Contain("\"status\":\"Complete\"");
        json.Should().Contain("[\"A\",\"B\"]");
        json.Should().NotContain("error");
    }

    [Fact]
    public void TypedJsonRpcResponse_JsonSerialization_WithError_ShouldSerializeError()
    {
        // Arrange
        var error = new JsonRpcError 
        { 
            Code = JsonRpcErrorCodes.MethodNotFound, 
            Message = "Method not found" 
        };
        var response = new TypedJsonRpcResponse<TestResult>("error-resp", error);

        // Act
        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions 
        { 
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
        });

        // Assert
        json.Should().Contain("\"jsonrpc\":\"2.0\"");
        json.Should().Contain("\"id\":\"error-resp\"");
        json.Should().Contain("\"code\":-32601");
        json.Should().Contain("\"message\":\"Method not found\"");
        json.Should().NotContain("result");
    }

    #endregion

    #region TypedJsonRpcNotification Tests

    [Fact]
    public void TypedJsonRpcNotification_Constructor_ShouldSetProperties()
    {
        // Arrange
        var method = "notification/test";
        var parameters = new TestParams { Name = "NotifyTest", Value = 789 };

        // Act
        var notification = new TypedJsonRpcNotification<TestParams>(method, parameters);

        // Assert
        notification.Method.Should().Be(method);
        notification.Params.Should().Be(parameters);
        notification.JsonRpc.Should().Be("2.0");
    }

    [Fact]
    public void TypedJsonRpcNotification_JsonSerialization_ShouldNotIncludeId()
    {
        // Arrange
        var notification = new TypedJsonRpcNotification<TestParams>(
            "notification/progress",
            new TestParams { Name = "Progress", Value = 50, IsActive = true }
        );

        // Act
        var json = JsonSerializer.Serialize(notification, new JsonSerializerOptions 
        { 
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
        });

        // Assert
        json.Should().Contain("\"jsonrpc\":\"2.0\"");
        json.Should().Contain("\"method\":\"notification/progress\"");
        json.Should().Contain("\"name\":\"Progress\"");
        json.Should().Contain("\"value\":50");
        json.Should().NotContain("\"id\""); // Notifications don't have IDs
    }

    #endregion

    #region TypedToolRequest Tests

    // Concrete test implementation
    public class TestToolRequest : TypedToolRequest<TestParams>
    {
        public override string ToolName => "test_tool";
    }

    [Fact]
    public void TypedToolRequest_Constructor_ShouldSetMethodToToolsCall()
    {
        // Act
        var request = new TestToolRequest();

        // Assert
        request.Method.Should().Be("tools/call");
        request.ToolName.Should().Be("test_tool");
        request.JsonRpc.Should().Be("2.0");
    }

    [Fact]
    public void TypedToolRequest_ParameterizedConstructor_ShouldSetProperties()
    {
        // Arrange
        var id = "tool-req-1";
        var parameters = new TestParams { Name = "ToolTest", Value = 999 };

        // Act
        var request = new TestToolRequest();
        request.Id = id;
        request.Params = parameters;

        // Assert
        request.Id.Should().Be(id);
        request.Params.Should().Be(parameters);
        request.Method.Should().Be("tools/call");
    }

    #endregion

    #region TypedToolResponse Tests

    [Fact]
    public void TypedToolResponse_WithToolResult_ShouldWrapInCallToolResult()
    {
        // Arrange
        var id = "tool-resp-1";
        var toolResult = new TestResult { Status = "ToolSuccess", Items = new[] { "Result1" } };

        // Act
        var response = new TypedToolResponse<TestResult>(id, toolResult);

        // Assert
        response.Id.Should().Be(id);
        response.Result.Should().NotBeNull();
        response.Result!.Content.Should().HaveCount(1);
        response.ToolResult.Should().NotBeNull();
        response.IsError.Should().BeFalse();
    }

    [Fact]
    public void TypedToolResponse_WithError_ShouldSetError()
    {
        // Arrange
        var id = "tool-error-1";
        var error = new JsonRpcError 
        { 
            Code = JsonRpcErrorCodes.ToolExecutionError, 
            Message = "Tool failed to execute" 
        };

        // Act
        var response = new TypedToolResponse<TestResult>(id, error);

        // Assert
        response.Id.Should().Be(id);
        response.Result.Should().BeNull();
        response.Error.Should().Be(error);
        response.IsError.Should().BeTrue();
        response.ToolResult.Should().BeNull();
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void RoundTripSerialization_TypedRequest_ShouldPreserveTypes()
    {
        // Arrange
        var originalRequest = new TypedJsonRpcRequest<TestParams>(
            123,
            "test/roundtrip",
            new TestParams { Name = "RoundTrip", Value = 2024, IsActive = true }
        );

        // Act - Serialize and deserialize
        var json = JsonSerializer.Serialize(originalRequest, new JsonSerializerOptions 
        { 
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
        });
        var deserializedRequest = JsonSerializer.Deserialize<TypedJsonRpcRequest<TestParams>>(json, new JsonSerializerOptions 
        { 
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
        });

        // Assert
        deserializedRequest.Should().NotBeNull();
        deserializedRequest!.Id.ToString().Should().Be(originalRequest.Id.ToString());
        deserializedRequest.Method.Should().Be(originalRequest.Method);
        deserializedRequest.Params.Should().NotBeNull();
        deserializedRequest.Params!.Name.Should().Be(originalRequest.Params!.Name);
        deserializedRequest.Params.Value.Should().Be(originalRequest.Params.Value);
        deserializedRequest.Params.IsActive.Should().Be(originalRequest.Params.IsActive);
    }

    [Fact]
    public void RoundTripSerialization_TypedResponse_ShouldPreserveTypes()
    {
        // Arrange
        var originalResponse = new TypedJsonRpcResponse<TestResult>(
            "resp-roundtrip",
            new TestResult { Status = "RoundTripSuccess", Items = new[] { "Item1", "Item2", "Item3" } }
        );

        // Act - Serialize and deserialize
        var json = JsonSerializer.Serialize(originalResponse, new JsonSerializerOptions 
        { 
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
        });
        var deserializedResponse = JsonSerializer.Deserialize<TypedJsonRpcResponse<TestResult>>(json, new JsonSerializerOptions 
        { 
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
        });

        // Assert
        deserializedResponse.Should().NotBeNull();
        deserializedResponse!.Id.ToString().Should().Be(originalResponse.Id.ToString());
        deserializedResponse.Result.Should().NotBeNull();
        deserializedResponse.Result!.Status.Should().Be(originalResponse.Result!.Status);
        deserializedResponse.Result.Items.Should().BeEquivalentTo(originalResponse.Result.Items);
        deserializedResponse.IsError.Should().BeFalse();
    }

    #endregion
}