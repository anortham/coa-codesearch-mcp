using System.Text.Json;
using COA.Mcp.Protocol;
using FluentAssertions;

namespace COA.Mcp.Protocol.Tests;

public class ResourcesCapabilityTests
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    #region Resource Tests

    [Fact]
    public void Resource_ShouldSerializeCorrectly()
    {
        // Arrange
        var resource = new Resource
        {
            Uri = "codesearch://workspace/src/test.cs",
            Name = "test.cs",
            Description = "Test file",
            MimeType = "text/x-csharp"
        };

        // Act
        var json = JsonSerializer.Serialize(resource, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<Resource>(json, _jsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Uri.Should().Be("codesearch://workspace/src/test.cs");
        deserialized.Name.Should().Be("test.cs");
        deserialized.Description.Should().Be("Test file");
        deserialized.MimeType.Should().Be("text/x-csharp");
    }

    [Fact]
    public void Resource_WithOptionalFields_ShouldSerializeCorrectly()
    {
        // Arrange
        var resource = new Resource
        {
            Uri = "codesearch://memory/123",
            Name = "Memory 123"
        };

        // Act
        var json = JsonSerializer.Serialize(resource, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<Resource>(json, _jsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Uri.Should().Be("codesearch://memory/123");
        deserialized.Name.Should().Be("Memory 123");
        deserialized.Description.Should().BeNull();
        deserialized.MimeType.Should().BeNull();
    }

    #endregion

    #region ResourceContent Tests

    [Fact]
    public void ResourceContent_TextContent_ShouldSerializeCorrectly()
    {
        // Arrange
        var content = new ResourceContent
        {
            Uri = "codesearch://workspace/readme.md",
            MimeType = "text/markdown",
            Text = "# Project Title\n\nDescription here"
        };

        // Act
        var json = JsonSerializer.Serialize(content, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<ResourceContent>(json, _jsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Uri.Should().Be("codesearch://workspace/readme.md");
        deserialized.MimeType.Should().Be("text/markdown");
        deserialized.Text.Should().Be("# Project Title\n\nDescription here");
        deserialized.Blob.Should().BeNull();
    }

    [Fact]
    public void ResourceContent_BlobContent_ShouldSerializeCorrectly()
    {
        // Arrange
        var blobData = "SGVsbG8gV29ybGQ="u8.ToArray(); // "Hello World" in base64
        var content = new ResourceContent
        {
            Uri = "codesearch://workspace/image.png",
            MimeType = "image/png",
            Blob = Convert.ToBase64String(blobData)
        };

        // Act
        var json = JsonSerializer.Serialize(content, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<ResourceContent>(json, _jsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Uri.Should().Be("codesearch://workspace/image.png");
        deserialized.MimeType.Should().Be("image/png");
        deserialized.Blob.Should().Be(Convert.ToBase64String(blobData));
        deserialized.Text.Should().BeNull();
    }

    #endregion

    #region ListResourcesResult Tests

    [Fact]
    public void ListResourcesResult_ShouldSerializeCorrectly()
    {
        // Arrange
        var result = new ListResourcesResult
        {
            Resources = new List<Resource>
            {
                new() { Uri = "codesearch://workspace/file1.cs", Name = "file1.cs" },
                new() { Uri = "codesearch://workspace/file2.cs", Name = "file2.cs" }
            }
        };

        // Act
        var json = JsonSerializer.Serialize(result, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<ListResourcesResult>(json, _jsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Resources.Should().HaveCount(2);
        deserialized.Resources[0].Uri.Should().Be("codesearch://workspace/file1.cs");
        deserialized.Resources[1].Uri.Should().Be("codesearch://workspace/file2.cs");
    }

    [Fact]
    public void ListResourcesResult_EmptyList_ShouldSerializeCorrectly()
    {
        // Arrange
        var result = new ListResourcesResult
        {
            Resources = new List<Resource>()
        };

        // Act
        var json = JsonSerializer.Serialize(result, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<ListResourcesResult>(json, _jsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Resources.Should().BeEmpty();
    }

    #endregion

    #region ReadResourceRequest Tests

    [Fact]
    public void ReadResourceRequest_ShouldSerializeCorrectly()
    {
        // Arrange
        var request = new ReadResourceRequest
        {
            Uri = "codesearch://workspace/src/test.cs"
        };

        // Act
        var json = JsonSerializer.Serialize(request, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<ReadResourceRequest>(json, _jsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Uri.Should().Be("codesearch://workspace/src/test.cs");
    }

    #endregion

    #region ReadResourceResult Tests

    [Fact]
    public void ReadResourceResult_SingleContent_ShouldSerializeCorrectly()
    {
        // Arrange
        var result = new ReadResourceResult
        {
            Contents = new List<ResourceContent>
            {
                new()
                {
                    Uri = "codesearch://workspace/test.cs",
                    MimeType = "text/x-csharp",
                    Text = "using System;\n\nnamespace Test\n{\n    class Program\n    {\n    }\n}"
                }
            }
        };

        // Act
        var json = JsonSerializer.Serialize(result, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<ReadResourceResult>(json, _jsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Contents.Should().HaveCount(1);
        deserialized.Contents[0].Uri.Should().Be("codesearch://workspace/test.cs");
        deserialized.Contents[0].MimeType.Should().Be("text/x-csharp");
        deserialized.Contents[0].Text.Should().Contain("namespace Test");
    }

    [Fact]
    public void ReadResourceResult_MultipleContents_ShouldSerializeCorrectly()
    {
        // Arrange
        var result = new ReadResourceResult
        {
            Contents = new List<ResourceContent>
            {
                new()
                {
                    Uri = "codesearch://memory/123",
                    MimeType = "text/markdown",
                    Text = "# Memory 123\n\nContent here"
                },
                new()
                {
                    Uri = "codesearch://memory/123/metadata",
                    MimeType = "application/json",
                    Text = "{\"id\": 123, \"type\": \"TechnicalDebt\"}"
                }
            }
        };

        // Act
        var json = JsonSerializer.Serialize(result, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<ReadResourceResult>(json, _jsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Contents.Should().HaveCount(2);
        deserialized.Contents[0].Text.Should().Contain("# Memory 123");
        deserialized.Contents[1].Text.Should().Contain("\"type\": \"TechnicalDebt\"");
    }

    #endregion

    #region ResourceCapabilities Tests

    [Fact]
    public void ResourceCapabilities_ShouldSerializeCorrectly()
    {
        // Arrange
        var capabilities = new ResourceCapabilities
        {
            Subscribe = true,
            ListChanged = false
        };

        // Act
        var json = JsonSerializer.Serialize(capabilities, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<ResourceCapabilities>(json, _jsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Subscribe.Should().BeTrue();
        deserialized.ListChanged.Should().BeFalse();
    }

    [Fact]
    public void ResourceCapabilities_DefaultValues_ShouldSerializeCorrectly()
    {
        // Arrange
        var capabilities = new ResourceCapabilities();

        // Act
        var json = JsonSerializer.Serialize(capabilities, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<ResourceCapabilities>(json, _jsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Subscribe.Should().BeFalse();
        deserialized.ListChanged.Should().BeFalse();
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void ResourcesWorkflow_CompleteRoundTrip_ShouldWork()
    {
        // Arrange - List resources request/response
        var listResult = new ListResourcesResult
        {
            Resources = new List<Resource>
            {
                new()
                {
                    Uri = "codesearch://workspace/important.cs",
                    Name = "important.cs",
                    Description = "Important source file",
                    MimeType = "text/x-csharp"
                }
            }
        };

        // Act & Assert - List resources
        var listJson = JsonSerializer.Serialize(listResult, _jsonOptions);
        var deserializedList = JsonSerializer.Deserialize<ListResourcesResult>(listJson, _jsonOptions);
        
        deserializedList.Should().NotBeNull();
        deserializedList!.Resources.Should().HaveCount(1);
        var resource = deserializedList.Resources[0];
        resource.Uri.Should().Be("codesearch://workspace/important.cs");

        // Arrange - Read specific resource
        var readRequest = new ReadResourceRequest { Uri = resource.Uri };
        var readResult = new ReadResourceResult
        {
            Contents = new List<ResourceContent>
            {
                new()
                {
                    Uri = resource.Uri,
                    MimeType = resource.MimeType,
                    Text = "// Important C# code\nusing System;\n\nnamespace Important { }"
                }
            }
        };

        // Act & Assert - Read resource
        var readJson = JsonSerializer.Serialize(readResult, _jsonOptions);
        var deserializedRead = JsonSerializer.Deserialize<ReadResourceResult>(readJson, _jsonOptions);
        
        deserializedRead.Should().NotBeNull();
        deserializedRead!.Contents.Should().HaveCount(1);
        deserializedRead.Contents[0].Uri.Should().Be(resource.Uri);
        deserializedRead.Contents[0].Text.Should().Contain("namespace Important");
    }

    #endregion
}