using COA.CodeSearch.McpServer.Services;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using Xunit;

namespace COA.CodeSearch.McpServer.Tests;

public class TypeDiscoveryResourceProviderTests
{
    private readonly TypeDiscoveryResourceProvider _provider;
    
    public TypeDiscoveryResourceProviderTests()
    {
        var mockLogger = new Mock<ILogger<TypeDiscoveryResourceProvider>>();
        _provider = new TypeDiscoveryResourceProvider(mockLogger.Object);
    }
    
    [Fact]
    public async Task ListResourcesAsync_ReturnsExpectedResources()
    {
        // Act
        var resources = await _provider.ListResourcesAsync();
        
        // Assert
        Assert.NotNull(resources);
        Assert.Equal(5, resources.Count);
        
        var searchTypesResource = resources.FirstOrDefault(r => r.Uri == "codesearch-types://search/searchTypes");
        Assert.NotNull(searchTypesResource);
        Assert.Equal("Search Type Options", searchTypesResource.Name);
        Assert.Equal("application/json", searchTypesResource.MimeType);
        
        var memoryTypesResource = resources.FirstOrDefault(r => r.Uri == "codesearch-types://memory/types");
        Assert.NotNull(memoryTypesResource);
        Assert.Equal("Memory Types", memoryTypesResource.Name);
    }
    
    [Fact]
    public async Task ReadResourceAsync_SearchTypes_ReturnsValidJson()
    {
        // Arrange
        var uri = "codesearch-types://search/searchTypes";
        
        // Act
        var result = await _provider.ReadResourceAsync(uri);
        
        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Contents);
        Assert.Single(result.Contents);
        
        var content = result.Contents[0];
        Assert.Equal(uri, content.Uri);
        Assert.Equal("application/json", content.MimeType);
        
        // Verify JSON is valid and contains expected structure
        var jsonDoc = JsonDocument.Parse(content.Text);
        var root = jsonDoc.RootElement;
        
        Assert.True(root.TryGetProperty("description", out _));
        Assert.True(root.TryGetProperty("types", out var types));
        Assert.Equal(JsonValueKind.Array, types.ValueKind);
        Assert.Equal(5, types.GetArrayLength()); // standard, fuzzy, wildcard, phrase, regex
        
        // Check first type
        var firstType = types[0];
        Assert.Equal("standard", firstType.GetProperty("value").GetString());
        Assert.True(firstType.TryGetProperty("description", out _));
        Assert.True(firstType.TryGetProperty("examples", out _));
        Assert.True(firstType.TryGetProperty("useCase", out _));
    }
    
    [Fact]
    public async Task ReadResourceAsync_MemoryTypes_ContainsExpectedTypes()
    {
        // Arrange
        var uri = "codesearch-types://memory/types";
        
        // Act
        var result = await _provider.ReadResourceAsync(uri);
        
        // Assert
        Assert.NotNull(result);
        var content = result.Contents[0];
        
        var jsonDoc = JsonDocument.Parse(content.Text);
        var types = jsonDoc.RootElement.GetProperty("types");
        
        Assert.Equal(5, types.GetArrayLength()); // TechnicalDebt, ArchitecturalDecision, etc.
        
        var technicalDebt = types[0];
        Assert.Equal("TechnicalDebt", technicalDebt.GetProperty("name").GetString());
        Assert.True(technicalDebt.TryGetProperty("schema", out var schema));
        Assert.True(schema.TryGetProperty("required", out _));
        Assert.True(schema.TryGetProperty("properties", out _));
    }
    
    [Fact]
    public async Task ReadResourceAsync_InvalidUri_ReturnsNull()
    {
        // Arrange
        var uri = "invalid://uri";
        
        // Act
        var result = await _provider.ReadResourceAsync(uri);
        
        // Assert
        Assert.Null(result);
    }
    
    [Fact]
    public async Task ReadResourceAsync_UnknownPath_ReturnsNull()
    {
        // Arrange
        var uri = "codesearch-types://unknown/path";
        
        // Act
        var result = await _provider.ReadResourceAsync(uri);
        
        // Assert
        Assert.Null(result);
    }
    
    [Fact]
    public void CanHandle_ValidUri_ReturnsTrue()
    {
        // Assert
        Assert.True(_provider.CanHandle("codesearch-types://search/searchTypes"));
        Assert.True(_provider.CanHandle("CODESEARCH-TYPES://memory/types"));
    }
    
    [Fact]
    public void CanHandle_InvalidUri_ReturnsFalse()
    {
        // Assert
        Assert.False(_provider.CanHandle("invalid://uri"));
        Assert.False(_provider.CanHandle("codesearch-workspace://context"));
        Assert.False(_provider.CanHandle(null));
        Assert.False(_provider.CanHandle(""));
    }
    
    [Fact]
    public async Task ReadResourceAsync_TimeFormats_ContainsExpectedFormats()
    {
        // Arrange
        var uri = "codesearch-types://time/formats";
        
        // Act
        var result = await _provider.ReadResourceAsync(uri);
        
        // Assert
        Assert.NotNull(result);
        var content = result.Contents[0];
        
        var jsonDoc = JsonDocument.Parse(content.Text);
        var formats = jsonDoc.RootElement.GetProperty("formats");
        
        Assert.True(formats.GetArrayLength() > 0);
        
        var firstFormat = formats[0];
        Assert.Equal("30m", firstFormat.GetProperty("format").GetString());
        Assert.True(firstFormat.TryGetProperty("description", out _));
        Assert.True(firstFormat.TryGetProperty("useCase", out _));
    }
}