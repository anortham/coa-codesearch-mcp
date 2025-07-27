using System.Text.Json;
using COA.Mcp.Protocol;
using FluentAssertions;

namespace COA.Mcp.Protocol.Tests;

public class ServerCapabilitiesTests
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    #region ResourceCapabilities Tests

    [Fact]
    public void ResourceCapabilities_AllFeatures_ShouldSerializeCorrectly()
    {
        // Arrange
        var capabilities = new ResourceCapabilities
        {
            Subscribe = true,
            ListChanged = true
        };

        // Act
        var json = JsonSerializer.Serialize(capabilities, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<ResourceCapabilities>(json, _jsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Subscribe.Should().BeTrue();
        deserialized.ListChanged.Should().BeTrue();
    }

    [Fact]
    public void ResourceCapabilities_PartialFeatures_ShouldSerializeCorrectly()
    {
        // Arrange
        var capabilities = new ResourceCapabilities
        {
            Subscribe = false,
            ListChanged = true
        };

        // Act
        var json = JsonSerializer.Serialize(capabilities, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<ResourceCapabilities>(json, _jsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Subscribe.Should().BeFalse();
        deserialized.ListChanged.Should().BeTrue();
    }

    [Fact]
    public void ResourceCapabilities_DefaultValues_ShouldBeFalse()
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

    #region ServerCapabilities Tests

    [Fact]
    public void ServerCapabilities_WithAllCapabilities_ShouldSerializeCorrectly()
    {
        // Arrange
        var capabilities = new ServerCapabilities
        {
            Tools = new { },
            Resources = new ResourceCapabilities
            {
                Subscribe = true,
                ListChanged = false
            },
            Prompts = new { }
        };

        // Act
        var json = JsonSerializer.Serialize(capabilities, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<ServerCapabilities>(json, _jsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Tools.Should().NotBeNull();
        deserialized.Resources.Should().NotBeNull();
        deserialized.Resources!.Subscribe.Should().BeTrue();
        deserialized.Resources.ListChanged.Should().BeFalse();
        deserialized.Prompts.Should().NotBeNull();
    }

    [Fact]
    public void ServerCapabilities_ToolsOnly_ShouldSerializeCorrectly()
    {
        // Arrange
        var capabilities = new ServerCapabilities
        {
            Tools = new { }
        };

        // Act
        var json = JsonSerializer.Serialize(capabilities, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<ServerCapabilities>(json, _jsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Tools.Should().NotBeNull();
        deserialized.Resources.Should().BeNull();
        deserialized.Prompts.Should().BeNull();
    }

    [Fact]
    public void ServerCapabilities_ResourcesOnly_ShouldSerializeCorrectly()
    {
        // Arrange
        var capabilities = new ServerCapabilities
        {
            Resources = new ResourceCapabilities
            {
                Subscribe = false,
                ListChanged = true
            }
        };

        // Act
        var json = JsonSerializer.Serialize(capabilities, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<ServerCapabilities>(json, _jsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Tools.Should().BeNull();
        deserialized.Resources.Should().NotBeNull();
        deserialized.Resources!.Subscribe.Should().BeFalse();
        deserialized.Resources.ListChanged.Should().BeTrue();
        deserialized.Prompts.Should().BeNull();
    }

    [Fact]
    public void ServerCapabilities_PromptsOnly_ShouldSerializeCorrectly()
    {
        // Arrange
        var capabilities = new ServerCapabilities
        {
            Prompts = new { }
        };

        // Act
        var json = JsonSerializer.Serialize(capabilities, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<ServerCapabilities>(json, _jsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Tools.Should().BeNull();
        deserialized.Resources.Should().BeNull();
        deserialized.Prompts.Should().NotBeNull();
    }

    #endregion

    #region InitializeResult Tests

    [Fact]
    public void InitializeResult_WithAllCapabilities_ShouldSerializeCorrectly()
    {
        // Arrange
        var result = new InitializeResult
        {
            ProtocolVersion = "2024-11-05",
            ServerInfo = new Implementation
            {
                Name = "COA CodeSearch MCP Server",
                Version = "1.5.0"
            },
            Capabilities = new ServerCapabilities
            {
                Tools = new { },
                Resources = new ResourceCapabilities
                {
                    Subscribe = false,
                    ListChanged = false
                },
                Prompts = new { }
            }
        };

        // Act
        var json = JsonSerializer.Serialize(result, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<InitializeResult>(json, _jsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.ProtocolVersion.Should().Be("2024-11-05");
        deserialized.ServerInfo.Should().NotBeNull();
        deserialized.ServerInfo!.Name.Should().Be("COA CodeSearch MCP Server");
        deserialized.ServerInfo.Version.Should().Be("1.5.0");
        deserialized.Capabilities.Should().NotBeNull();
        deserialized.Capabilities!.Tools.Should().NotBeNull();
        deserialized.Capabilities.Resources.Should().NotBeNull();
        deserialized.Capabilities.Prompts.Should().NotBeNull();
    }

    [Fact]
    public void InitializeResult_MinimalSetup_ShouldSerializeCorrectly()
    {
        // Arrange
        var result = new InitializeResult
        {
            ProtocolVersion = "2024-11-05",
            ServerInfo = new Implementation
            {
                Name = "Basic MCP Server",
                Version = "1.0.0"
            },
            Capabilities = new ServerCapabilities
            {
                Tools = new { }
            }
        };

        // Act
        var json = JsonSerializer.Serialize(result, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<InitializeResult>(json, _jsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.ProtocolVersion.Should().Be("2024-11-05");
        deserialized.ServerInfo!.Name.Should().Be("Basic MCP Server");
        deserialized.Capabilities!.Tools.Should().NotBeNull();
        deserialized.Capabilities.Resources.Should().BeNull();
        deserialized.Capabilities.Prompts.Should().BeNull();
    }

    #endregion

    #region Real-world Integration Tests

    [Fact]
    public void ServerCapabilities_CodeSearchServerConfiguration_ShouldWork()
    {
        // Arrange - Simulate actual CodeSearch server configuration
        var serverCapabilities = new ServerCapabilities
        {
            Tools = new { }, // CodeSearch provides many tools
            Resources = new ResourceCapabilities
            {
                Subscribe = false, // Not implemented yet
                ListChanged = false // Not implemented yet
            },
            Prompts = new { } // Now supported!
        };

        var initializeResult = new InitializeResult
        {
            ProtocolVersion = "2024-11-05",
            ServerInfo = new Implementation
            {
                Name = "COA CodeSearch MCP Server",
                Version = "1.5.207.1270"
            },
            Capabilities = serverCapabilities
        };

        // Act
        var json = JsonSerializer.Serialize(initializeResult, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<InitializeResult>(json, _jsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Capabilities.Should().NotBeNull();
        
        // Verify Tools capability
        deserialized.Capabilities!.Tools.Should().NotBeNull();
        
        // Verify Resources capability with correct settings
        deserialized.Capabilities.Resources.Should().NotBeNull();
        deserialized.Capabilities.Resources!.Subscribe.Should().BeFalse();
        deserialized.Capabilities.Resources.ListChanged.Should().BeFalse();
        
        // Verify Prompts capability is present
        deserialized.Capabilities.Prompts.Should().NotBeNull();
        
        // Verify server info
        deserialized.ServerInfo.Should().NotBeNull();
        deserialized.ServerInfo!.Name.Should().Be("COA CodeSearch MCP Server");
        deserialized.ServerInfo.Version.Should().StartWith("1.5.");
    }

    [Fact]
    public void ServerCapabilities_EvolutionPath_ShouldBeFlexible()
    {
        // Test that capabilities can be added incrementally without breaking existing clients
        
        // Phase 1: Tools only
        var phase1 = new ServerCapabilities { Tools = new { } };
        var phase1Json = JsonSerializer.Serialize(phase1, _jsonOptions);
        var phase1Deserialized = JsonSerializer.Deserialize<ServerCapabilities>(phase1Json, _jsonOptions);
        
        phase1Deserialized!.Tools.Should().NotBeNull();
        phase1Deserialized.Resources.Should().BeNull();
        phase1Deserialized.Prompts.Should().BeNull();

        // Phase 2: Tools + Resources  
        var phase2 = new ServerCapabilities
        {
            Tools = new { },
            Resources = new ResourceCapabilities { Subscribe = false, ListChanged = false }
        };
        var phase2Json = JsonSerializer.Serialize(phase2, _jsonOptions);
        var phase2Deserialized = JsonSerializer.Deserialize<ServerCapabilities>(phase2Json, _jsonOptions);
        
        phase2Deserialized!.Tools.Should().NotBeNull();
        phase2Deserialized.Resources.Should().NotBeNull();
        phase2Deserialized.Prompts.Should().BeNull();

        // Phase 3: Tools + Resources + Prompts
        var phase3 = new ServerCapabilities
        {
            Tools = new { },
            Resources = new ResourceCapabilities { Subscribe = false, ListChanged = false },
            Prompts = new { }
        };
        var phase3Json = JsonSerializer.Serialize(phase3, _jsonOptions);
        var phase3Deserialized = JsonSerializer.Deserialize<ServerCapabilities>(phase3Json, _jsonOptions);
        
        phase3Deserialized!.Tools.Should().NotBeNull();
        phase3Deserialized.Resources.Should().NotBeNull();
        phase3Deserialized.Prompts.Should().NotBeNull();
    }

    #endregion

    #region JSON Structure Validation

    [Fact]
    public void ServerCapabilities_JsonStructure_ShouldMatchMcpSpecification()
    {
        // Arrange
        var capabilities = new ServerCapabilities
        {
            Tools = new { },
            Resources = new ResourceCapabilities
            {
                Subscribe = true,
                ListChanged = false
            },
            Prompts = new { }
        };

        // Act
        var json = JsonSerializer.Serialize(capabilities, _jsonOptions);

        // Assert - Verify JSON structure matches MCP specification
        json.Should().Contain("\"tools\":");
        json.Should().Contain("\"resources\":");
        json.Should().Contain("\"subscribe\":true");
        json.Should().Contain("\"listChanged\":false");
        json.Should().Contain("\"prompts\":");
        
        // Verify camelCase naming
        json.Should().NotContain("\"Subscribe\"");
        json.Should().NotContain("\"ListChanged\"");
        json.Should().Contain("\"subscribe\"");
        json.Should().Contain("\"listChanged\"");
    }

    [Fact]
    public void ResourceCapabilities_JsonStructure_ShouldUseCorrectPropertyNames()
    {
        // Arrange
        var capabilities = new ResourceCapabilities
        {
            Subscribe = true,
            ListChanged = true
        };

        // Act
        var json = JsonSerializer.Serialize(capabilities, _jsonOptions);

        // Assert
        json.Should().Be("{\"subscribe\":true,\"listChanged\":true}");
    }

    #endregion
}