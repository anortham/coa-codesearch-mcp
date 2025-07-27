using System.Text.Json;
using COA.Mcp.Protocol;
using FluentAssertions;

namespace COA.Mcp.Protocol.Tests;

public class PromptsCapabilityTests
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    #region PromptArgument Tests

    [Fact]
    public void PromptArgument_RequiredArgument_ShouldSerializeCorrectly()
    {
        // Arrange
        var argument = new PromptArgument
        {
            Name = "workspace_path",
            Description = "Path to the workspace directory",
            Required = true
        };

        // Act
        var json = JsonSerializer.Serialize(argument, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<PromptArgument>(json, _jsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Name.Should().Be("workspace_path");
        deserialized.Description.Should().Be("Path to the workspace directory");
        deserialized.Required.Should().BeTrue();
    }

    [Fact]
    public void PromptArgument_OptionalArgument_ShouldSerializeCorrectly()
    {
        // Arrange
        var argument = new PromptArgument
        {
            Name = "search_type",
            Description = "Type of search to perform",
            Required = false
        };

        // Act
        var json = JsonSerializer.Serialize(argument, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<PromptArgument>(json, _jsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Name.Should().Be("search_type");
        deserialized.Description.Should().Be("Type of search to perform");
        deserialized.Required.Should().BeFalse();
    }

    [Fact]
    public void PromptArgument_DefaultRequired_ShouldBeFalse()
    {
        // Arrange
        var argument = new PromptArgument
        {
            Name = "optional_param",
            Description = "An optional parameter"
        };

        // Act
        var json = JsonSerializer.Serialize(argument, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<PromptArgument>(json, _jsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Required.Should().BeFalse();
    }

    #endregion

    #region Prompt Tests

    [Fact]
    public void Prompt_WithArguments_ShouldSerializeCorrectly()
    {
        // Arrange
        var prompt = new Prompt
        {
            Name = "advanced-search-builder",
            Description = "Interactive guide for building advanced search queries",
            Arguments = new List<PromptArgument>
            {
                new()
                {
                    Name = "workspace_path",
                    Description = "Path to workspace",
                    Required = true
                },
                new()
                {
                    Name = "initial_query",
                    Description = "Starting search term",
                    Required = false
                }
            }
        };

        // Act
        var json = JsonSerializer.Serialize(prompt, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<Prompt>(json, _jsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Name.Should().Be("advanced-search-builder");
        deserialized.Description.Should().Be("Interactive guide for building advanced search queries");
        deserialized.Arguments.Should().HaveCount(2);
        deserialized.Arguments[0].Name.Should().Be("workspace_path");
        deserialized.Arguments[0].Required.Should().BeTrue();
        deserialized.Arguments[1].Name.Should().Be("initial_query");
        deserialized.Arguments[1].Required.Should().BeFalse();
    }

    [Fact]
    public void Prompt_WithoutArguments_ShouldSerializeCorrectly()
    {
        // Arrange
        var prompt = new Prompt
        {
            Name = "help",
            Description = "Get help with CodeSearch",
            Arguments = new List<PromptArgument>()
        };

        // Act
        var json = JsonSerializer.Serialize(prompt, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<Prompt>(json, _jsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Name.Should().Be("help");
        deserialized.Description.Should().Be("Get help with CodeSearch");
        deserialized.Arguments.Should().BeEmpty();
    }

    #endregion

    #region PromptContent Tests

    [Fact]
    public void PromptContent_TextContent_ShouldSerializeCorrectly()
    {
        // Arrange
        var content = new PromptContent
        {
            Type = "text",
            Text = "You are an expert at using CodeSearch tools to find code."
        };

        // Act
        var json = JsonSerializer.Serialize(content, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<PromptContent>(json, _jsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Type.Should().Be("text");
        deserialized.Text.Should().Be("You are an expert at using CodeSearch tools to find code.");
    }

    [Fact]
    public void PromptContent_TextOnly_RequiredFieldsSet()
    {
        // Arrange
        var content = new PromptContent
        {
            Type = "text",
            Text = "Search for TODO comments in the codebase."
        };

        // Assert
        content.Type.Should().Be("text");
        content.Text.Should().Be("Search for TODO comments in the codebase.");
    }

    #endregion

    #region PromptMessage Tests

    [Fact]
    public void PromptMessage_SystemMessage_ShouldSerializeCorrectly()
    {
        // Arrange
        var message = new PromptMessage
        {
            Role = "system",
            Content = new PromptContent
            {
                Type = "text",
                Text = "You are a helpful assistant for code search and analysis."
            }
        };

        // Act
        var json = JsonSerializer.Serialize(message, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<PromptMessage>(json, _jsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Role.Should().Be("system");
        deserialized.Content.Should().NotBeNull();
        deserialized.Content.Type.Should().Be("text");
        deserialized.Content.Text.Should().Be("You are a helpful assistant for code search and analysis.");
    }

    [Fact]
    public void PromptMessage_UserMessage_ShouldSerializeCorrectly()
    {
        // Arrange
        var message = new PromptMessage
        {
            Role = "user",
            Content = new PromptContent
            {
                Type = "text",
                Text = "Help me find all TODO comments in the project."
            }
        };

        // Act
        var json = JsonSerializer.Serialize(message, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<PromptMessage>(json, _jsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Role.Should().Be("user");
        deserialized.Content.Text.Should().Be("Help me find all TODO comments in the project.");
    }

    [Fact]
    public void PromptMessage_AssistantMessage_ShouldSerializeCorrectly()
    {
        // Arrange
        var message = new PromptMessage
        {
            Role = "assistant",
            Content = new PromptContent
            {
                Type = "text",
                Text = "I can help you search for TODO comments using the text_search tool."
            }
        };

        // Act
        var json = JsonSerializer.Serialize(message, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<PromptMessage>(json, _jsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Role.Should().Be("assistant");
        deserialized.Content.Text.Should().Be("I can help you search for TODO comments using the text_search tool.");
    }

    #endregion

    #region ListPromptsResult Tests

    [Fact]
    public void ListPromptsResult_MultiplePrompts_ShouldSerializeCorrectly()
    {
        // Arrange
        var result = new ListPromptsResult
        {
            Prompts = new List<Prompt>
            {
                new()
                {
                    Name = "search-builder",
                    Description = "Build advanced searches",
                    Arguments = new List<PromptArgument>
                    {
                        new() { Name = "query", Description = "Search term", Required = true }
                    }
                },
                new()
                {
                    Name = "memory-wizard",
                    Description = "Create memory entries",
                    Arguments = new List<PromptArgument>()
                }
            }
        };

        // Act
        var json = JsonSerializer.Serialize(result, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<ListPromptsResult>(json, _jsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Prompts.Should().HaveCount(2);
        deserialized.Prompts[0].Name.Should().Be("search-builder");
        deserialized.Prompts[0].Arguments.Should().HaveCount(1);
        deserialized.Prompts[1].Name.Should().Be("memory-wizard");
        deserialized.Prompts[1].Arguments.Should().BeEmpty();
    }

    [Fact]
    public void ListPromptsResult_EmptyList_ShouldSerializeCorrectly()
    {
        // Arrange
        var result = new ListPromptsResult
        {
            Prompts = new List<Prompt>()
        };

        // Act
        var json = JsonSerializer.Serialize(result, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<ListPromptsResult>(json, _jsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Prompts.Should().BeEmpty();
    }

    #endregion

    #region GetPromptRequest Tests

    [Fact]
    public void GetPromptRequest_WithArguments_ShouldSerializeCorrectly()
    {
        // Arrange
        var request = new GetPromptRequest
        {
            Name = "advanced-search-builder",
            Arguments = new Dictionary<string, object>
            {
                { "workspace_path", "/path/to/workspace" },
                { "search_type", "text" },
                { "initial_query", "TODO" }
            }
        };

        // Act
        var json = JsonSerializer.Serialize(request, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<GetPromptRequest>(json, _jsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Name.Should().Be("advanced-search-builder");
        deserialized.Arguments.Should().HaveCount(3);
        deserialized.Arguments!["workspace_path"].ToString().Should().Be("/path/to/workspace");
        deserialized.Arguments["search_type"].ToString().Should().Be("text");
        deserialized.Arguments["initial_query"].ToString().Should().Be("TODO");
    }

    [Fact]
    public void GetPromptRequest_WithoutArguments_ShouldSerializeCorrectly()
    {
        // Arrange
        var request = new GetPromptRequest
        {
            Name = "help"
        };

        // Act
        var json = JsonSerializer.Serialize(request, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<GetPromptRequest>(json, _jsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Name.Should().Be("help");
        deserialized.Arguments.Should().BeNull();
    }

    #endregion

    #region GetPromptResult Tests

    [Fact]
    public void GetPromptResult_CompletePrompt_ShouldSerializeCorrectly()
    {
        // Arrange
        var result = new GetPromptResult
        {
            Description = "Advanced search builder for CodeSearch",
            Messages = new List<PromptMessage>
            {
                new()
                {
                    Role = "system",
                    Content = new PromptContent
                    {
                        Type = "text",
                        Text = "You are an expert at building search queries."
                    }
                },
                new()
                {
                    Role = "user",
                    Content = new PromptContent
                    {
                        Type = "text",
                        Text = "Help me search for error handling patterns."
                    }
                }
            }
        };

        // Act
        var json = JsonSerializer.Serialize(result, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<GetPromptResult>(json, _jsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Description.Should().Be("Advanced search builder for CodeSearch");
        deserialized.Messages.Should().HaveCount(2);
        deserialized.Messages[0].Role.Should().Be("system");
        deserialized.Messages[0].Content.Text.Should().Contain("expert at building");
        deserialized.Messages[1].Role.Should().Be("user");
        deserialized.Messages[1].Content.Text.Should().Contain("error handling patterns");
    }

    [Fact]
    public void GetPromptResult_WithOptionalDescription_ShouldSerializeCorrectly()
    {
        // Arrange
        var result = new GetPromptResult
        {
            Messages = new List<PromptMessage>
            {
                new()
                {
                    Role = "system",
                    Content = new PromptContent { Type = "text", Text = "System prompt" }
                }
            }
        };

        // Act
        var json = JsonSerializer.Serialize(result, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<GetPromptResult>(json, _jsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Description.Should().BeNull();
        deserialized.Messages.Should().HaveCount(1);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void PromptsWorkflow_CompleteRoundTrip_ShouldWork()
    {
        // Arrange - List prompts request/response
        var listResult = new ListPromptsResult
        {
            Prompts = new List<Prompt>
            {
                new()
                {
                    Name = "search-assistant",
                    Description = "Interactive search assistant",
                    Arguments = new List<PromptArgument>
                    {
                        new() { Name = "workspace", Description = "Workspace path", Required = true },
                        new() { Name = "query", Description = "Search query", Required = false }
                    }
                }
            }
        };

        // Act & Assert - List prompts
        var listJson = JsonSerializer.Serialize(listResult, _jsonOptions);
        var deserializedList = JsonSerializer.Deserialize<ListPromptsResult>(listJson, _jsonOptions);
        
        deserializedList.Should().NotBeNull();
        deserializedList!.Prompts.Should().HaveCount(1);
        var prompt = deserializedList.Prompts[0];
        prompt.Name.Should().Be("search-assistant");
        prompt.Arguments.Should().HaveCount(2);

        // Arrange - Get specific prompt with arguments
        var getRequest = new GetPromptRequest
        {
            Name = prompt.Name,
            Arguments = new Dictionary<string, object>
            {
                { "workspace", "/home/user/project" },
                { "query", "exception handling" }
            }
        };

        var getResult = new GetPromptResult
        {
            Description = prompt.Description,
            Messages = new List<PromptMessage>
            {
                new()
                {
                    Role = "system",
                    Content = new PromptContent
                    {
                        Type = "text",
                        Text = "You are helping search in workspace: /home/user/project"
                    }
                },
                new()
                {
                    Role = "user",
                    Content = new PromptContent
                    {
                        Type = "text",
                        Text = "I want to find: exception handling"
                    }
                }
            }
        };

        // Act & Assert - Get prompt
        var getJson = JsonSerializer.Serialize(getResult, _jsonOptions);
        var deserializedGet = JsonSerializer.Deserialize<GetPromptResult>(getJson, _jsonOptions);
        
        deserializedGet.Should().NotBeNull();
        deserializedGet!.Description.Should().Be(prompt.Description);
        deserializedGet.Messages.Should().HaveCount(2);
        deserializedGet.Messages[0].Content.Text.Should().Contain("/home/user/project");
        deserializedGet.Messages[1].Content.Text.Should().Contain("exception handling");
    }

    [Fact]
    public void PromptArguments_ComplexTypes_ShouldSerializeCorrectly()
    {
        // Arrange - Test with various argument value types
        var request = new GetPromptRequest
        {
            Name = "complex-prompt",
            Arguments = new Dictionary<string, object>
            {
                { "string_value", "test string" },
                { "number_value", 42 },
                { "boolean_value", true },
                { "array_value", new[] { "item1", "item2" } },
                { "object_value", new { property = "value", nested = new { count = 5 } } }
            }
        };

        // Act
        var json = JsonSerializer.Serialize(request, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<GetPromptRequest>(json, _jsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Name.Should().Be("complex-prompt");
        deserialized.Arguments.Should().HaveCount(5);
        deserialized.Arguments!["string_value"].ToString().Should().Be("test string");
        deserialized.Arguments["number_value"].ToString().Should().Be("42");
        deserialized.Arguments["boolean_value"].ToString().Should().Be("True");
        // Note: Complex objects get serialized as JsonElement, so we check they exist
        deserialized.Arguments.Should().ContainKey("array_value");
        deserialized.Arguments.Should().ContainKey("object_value");
    }

    #endregion
}