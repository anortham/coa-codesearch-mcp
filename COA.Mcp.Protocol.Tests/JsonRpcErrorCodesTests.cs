using COA.Mcp.Protocol;
using FluentAssertions;

namespace COA.Mcp.Protocol.Tests;

public class JsonRpcErrorCodesTests
{
    [Fact]
    public void Constants_ShouldHaveCorrectValues()
    {
        // JSON-RPC 2.0 Standard Error Codes
        JsonRpcErrorCodes.ParseError.Should().Be(-32700);
        JsonRpcErrorCodes.InvalidRequest.Should().Be(-32600);
        JsonRpcErrorCodes.MethodNotFound.Should().Be(-32601);
        JsonRpcErrorCodes.InvalidParams.Should().Be(-32602);
        JsonRpcErrorCodes.InternalError.Should().Be(-32603);

        // Server Error Range
        JsonRpcErrorCodes.ServerError.Should().Be(-32000);
        JsonRpcErrorCodes.ResourceNotFound.Should().Be(-32001);
        JsonRpcErrorCodes.ResourceAccessDenied.Should().Be(-32002);
        JsonRpcErrorCodes.OperationTimeout.Should().Be(-32003);
        JsonRpcErrorCodes.ServiceUnavailable.Should().Be(-32004);
        JsonRpcErrorCodes.OperationCancelled.Should().Be(-32005);

        // MCP-Specific Error Codes
        JsonRpcErrorCodes.ToolNotFound.Should().Be(-32100);
        JsonRpcErrorCodes.ToolExecutionError.Should().Be(-32101);
        JsonRpcErrorCodes.WorkspaceNotFound.Should().Be(-32102);
        JsonRpcErrorCodes.IndexNotAvailable.Should().Be(-32103);
        JsonRpcErrorCodes.MemoryOperationFailed.Should().Be(-32104);
    }

    [Theory]
    [InlineData(-32000, true)]  // ServerError
    [InlineData(-32001, true)]  // ResourceNotFound
    [InlineData(-32099, true)]  // Edge of server error range
    [InlineData(-32100, false)] // MCP error, not server error
    [InlineData(-32603, false)] // Standard JSON-RPC error
    [InlineData(-31999, false)] // Outside range
    [InlineData(0, false)]      // Not an error code
    public void IsServerError_ShouldReturnCorrectValue(int errorCode, bool expected)
    {
        // Act
        var result = JsonRpcErrorCodes.IsServerError(errorCode);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(-32100, true)]  // ToolNotFound
    [InlineData(-32104, true)]  // MemoryOperationFailed
    [InlineData(-32199, true)]  // Edge of MCP error range
    [InlineData(-32000, false)] // Server error, not MCP error
    [InlineData(-32603, false)] // Standard JSON-RPC error
    [InlineData(-32200, false)] // Outside range
    [InlineData(0, false)]      // Not an error code
    public void IsMcpError_ShouldReturnCorrectValue(int errorCode, bool expected)
    {
        // Act
        var result = JsonRpcErrorCodes.IsMcpError(errorCode);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(JsonRpcErrorCodes.ParseError, "Parse error - Invalid JSON was received")]
    [InlineData(JsonRpcErrorCodes.InvalidRequest, "Invalid Request - The JSON sent is not a valid Request object")]
    [InlineData(JsonRpcErrorCodes.MethodNotFound, "Method not found - The method does not exist or is not available")]
    [InlineData(JsonRpcErrorCodes.InvalidParams, "Invalid params - Invalid method parameter(s)")]
    [InlineData(JsonRpcErrorCodes.InternalError, "Internal error - Internal JSON-RPC error")]
    [InlineData(JsonRpcErrorCodes.ResourceNotFound, "Resource not found - The requested resource could not be found")]
    [InlineData(JsonRpcErrorCodes.ResourceAccessDenied, "Access denied - Access to the requested resource was denied")]
    [InlineData(JsonRpcErrorCodes.OperationTimeout, "Operation timeout - The requested operation timed out")]
    [InlineData(JsonRpcErrorCodes.ServiceUnavailable, "Service unavailable - The server is temporarily unavailable")]
    [InlineData(JsonRpcErrorCodes.OperationCancelled, "Operation cancelled - The operation was cancelled")]
    [InlineData(JsonRpcErrorCodes.ToolNotFound, "Tool not found - A tool with the requested name was not found")]
    [InlineData(JsonRpcErrorCodes.ToolExecutionError, "Tool execution error - The tool failed to execute")]
    [InlineData(JsonRpcErrorCodes.WorkspaceNotFound, "Workspace not found - The requested workspace could not be accessed")]
    [InlineData(JsonRpcErrorCodes.IndexNotAvailable, "Index not available - The workspace index is not available or corrupted")]
    [InlineData(JsonRpcErrorCodes.MemoryOperationFailed, "Memory operation failed - The requested memory operation failed")]
    public void GetStandardErrorDescription_WithKnownCodes_ShouldReturnDescription(int errorCode, string expectedDescription)
    {
        // Act
        var description = JsonRpcErrorCodes.GetStandardErrorDescription(errorCode);

        // Assert
        description.Should().Be(expectedDescription);
    }

    [Theory]
    [InlineData(-999)]    // Unknown error code
    [InlineData(0)]       // Not an error code
    [InlineData(200)]     // HTTP success code (wrong domain)
    [InlineData(-31000)]  // Outside defined ranges
    public void GetStandardErrorDescription_WithUnknownCodes_ShouldReturnNull(int errorCode)
    {
        // Act
        var description = JsonRpcErrorCodes.GetStandardErrorDescription(errorCode);

        // Assert
        description.Should().BeNull();
    }

    [Fact]
    public void ErrorRanges_ShouldNotOverlap()
    {
        // Arrange
        var standardErrorCodes = new[] 
        { 
            JsonRpcErrorCodes.ParseError, 
            JsonRpcErrorCodes.InvalidRequest, 
            JsonRpcErrorCodes.MethodNotFound, 
            JsonRpcErrorCodes.InvalidParams, 
            JsonRpcErrorCodes.InternalError 
        };

        var serverErrorCodes = new[] 
        { 
            JsonRpcErrorCodes.ServerError, 
            JsonRpcErrorCodes.ResourceNotFound, 
            JsonRpcErrorCodes.ResourceAccessDenied,
            JsonRpcErrorCodes.OperationTimeout,
            JsonRpcErrorCodes.ServiceUnavailable,
            JsonRpcErrorCodes.OperationCancelled
        };

        var mcpErrorCodes = new[] 
        { 
            JsonRpcErrorCodes.ToolNotFound, 
            JsonRpcErrorCodes.ToolExecutionError, 
            JsonRpcErrorCodes.WorkspaceNotFound,
            JsonRpcErrorCodes.IndexNotAvailable,
            JsonRpcErrorCodes.MemoryOperationFailed
        };

        // Assert - No overlap between ranges
        foreach (var code in standardErrorCodes)
        {
            JsonRpcErrorCodes.IsServerError(code).Should().BeFalse($"Standard error code {code} should not be classified as server error");
            JsonRpcErrorCodes.IsMcpError(code).Should().BeFalse($"Standard error code {code} should not be classified as MCP error");
        }

        foreach (var code in serverErrorCodes)
        {
            JsonRpcErrorCodes.IsMcpError(code).Should().BeFalse($"Server error code {code} should not be classified as MCP error");
        }

        foreach (var code in mcpErrorCodes)
        {
            JsonRpcErrorCodes.IsServerError(code).Should().BeFalse($"MCP error code {code} should not be classified as server error");
        }
    }

    [Fact]
    public void ErrorCodeRanges_ShouldFollowJsonRpcSpecification()
    {
        // JSON-RPC 2.0 specification defines:
        // -32768 to -32000: Reserved for pre-defined errors
        // -32000 to -32099: Server error range (implementation-defined)
        
        // Assert standard codes are in reserved range
        JsonRpcErrorCodes.ParseError.Should().BeInRange(-32768, -32000);
        JsonRpcErrorCodes.InvalidRequest.Should().BeInRange(-32768, -32000);
        JsonRpcErrorCodes.MethodNotFound.Should().BeInRange(-32768, -32000);
        JsonRpcErrorCodes.InvalidParams.Should().BeInRange(-32768, -32000);
        JsonRpcErrorCodes.InternalError.Should().BeInRange(-32768, -32000);

        // Assert server codes are in server error range
        JsonRpcErrorCodes.ServerError.Should().BeInRange(-32099, -32000);
        JsonRpcErrorCodes.ResourceNotFound.Should().BeInRange(-32099, -32000);
        JsonRpcErrorCodes.ResourceAccessDenied.Should().BeInRange(-32099, -32000);

        // Assert MCP codes are outside reserved ranges (custom application range)
        JsonRpcErrorCodes.ToolNotFound.Should().BeLessThan(-32099, "MCP codes should be outside server error range");
        JsonRpcErrorCodes.ToolExecutionError.Should().BeLessThan(-32099, "MCP codes should be outside server error range");
    }
}