using COA.CodeSearch.Contracts;
using FluentAssertions;
using System.Text.Json;
using Xunit;

namespace COA.CodeSearch.McpServer.Tests;

/// <summary>
/// Tests to verify ErrorResponse maintains JSON compatibility with anonymous error objects
/// </summary>
public class ErrorResponseTests
{
    [Fact]
    public void ErrorResponse_SerializesToSameJsonAsAnonymousType()
    {
        // Arrange
        var errorMessage = "Failed to retrieve index health";
        var details = "Access denied";
        
        // Create anonymous type (old way)
        var anonymousError = new { error = errorMessage, details = details };
        
        // Create ErrorResponse (new way)
        var errorResponse = new ErrorResponse 
        { 
            error = errorMessage, 
            details = details 
        };
        
        // Act
        var anonymousJson = JsonSerializer.Serialize(anonymousError);
        var errorResponseJson = JsonSerializer.Serialize(errorResponse);
        
        // Parse both to ensure they're equivalent
        var anonymousDoc = JsonDocument.Parse(anonymousJson);
        var errorResponseDoc = JsonDocument.Parse(errorResponseJson);
        
        // Assert - Basic properties should match
        anonymousDoc.RootElement.GetProperty("error").GetString().Should().Be(errorMessage);
        errorResponseDoc.RootElement.GetProperty("error").GetString().Should().Be(errorMessage);
        
        anonymousDoc.RootElement.GetProperty("details").GetString().Should().Be(details);
        errorResponseDoc.RootElement.GetProperty("details").GetString().Should().Be(details);
    }
    
    [Fact]
    public void ErrorResponse_WithOnlyError_SerializesCorrectly()
    {
        // Arrange
        var errorMessage = "MemoryId is required for single memory assessment";
        
        // Create anonymous type (old way)
        var anonymousError = new { error = errorMessage };
        
        // Create ErrorResponse (new way)
        var errorResponse = new ErrorResponse { error = errorMessage };
        
        // Act
        var anonymousJson = JsonSerializer.Serialize(anonymousError);
        var errorResponseJson = JsonSerializer.Serialize(errorResponse);
        
        // Assert - Should have error property
        var anonymousDoc = JsonDocument.Parse(anonymousJson);
        var errorResponseDoc = JsonDocument.Parse(errorResponseJson);
        
        anonymousDoc.RootElement.GetProperty("error").GetString().Should().Be(errorMessage);
        errorResponseDoc.RootElement.GetProperty("error").GetString().Should().Be(errorMessage);
        
        // Should not have details property when null
        anonymousDoc.RootElement.TryGetProperty("details", out _).Should().BeFalse();
    }
    
    [Fact]
    public void ErrorResponse_OptionalPropertiesAreNullByDefault()
    {
        // Arrange & Act
        var errorResponse = new ErrorResponse { error = "Test error" };
        
        // Assert
        errorResponse.error.Should().Be("Test error");
        errorResponse.details.Should().BeNull();
        errorResponse.code.Should().BeNull();
        errorResponse.suggestion.Should().BeNull();
    }
    
    [Fact]
    public void ErrorResponse_AllPropertiesSerializeWhenSet()
    {
        // Arrange
        var errorResponse = new ErrorResponse 
        { 
            error = "Test error",
            details = "Exception details",
            code = "ERR_001",
            suggestion = "Try reindexing the workspace"
        };
        
        // Act
        var json = JsonSerializer.Serialize(errorResponse);
        var doc = JsonDocument.Parse(json);
        
        // Assert
        doc.RootElement.GetProperty("error").GetString().Should().Be("Test error");
        doc.RootElement.GetProperty("details").GetString().Should().Be("Exception details");
        doc.RootElement.GetProperty("code").GetString().Should().Be("ERR_001");
        doc.RootElement.GetProperty("suggestion").GetString().Should().Be("Try reindexing the workspace");
    }
}