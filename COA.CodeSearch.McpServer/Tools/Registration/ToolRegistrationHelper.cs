using COA.CodeSearch.McpServer.Services;
using COA.Mcp.Protocol;
using System.Text.Json;

namespace COA.CodeSearch.McpServer.Tools.Registration;

/// <summary>
/// Helper class for creating consistent tool result formatting
/// </summary>
public static class ToolRegistrationHelper
{
    /// <summary>
    /// Creates a successful tool result with JSON content
    /// </summary>
    public static CallToolResult CreateSuccessResult(object result)
    {
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions 
        { 
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });
        return new CallToolResult
        {
            Content = new List<ToolContent>
            {
                new ToolContent
                {
                    Type = "text",
                    Text = json
                }
            }
        };
    }

    /// <summary>
    /// Creates an error result
    /// </summary>
    public static CallToolResult CreateErrorResult(string error)
    {
        return CreateSuccessResult(new { success = false, error });
    }

    /// <summary>
    /// Validates required string parameter
    /// </summary>
    public static string ValidateRequired(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidParametersException($"{parameterName} is required");
        return value;
    }

    /// <summary>
    /// Validates required positive integer
    /// </summary>
    public static int ValidatePositive(int value, string parameterName)
    {
        if (value <= 0)
            throw new InvalidParametersException($"{parameterName} must be positive");
        return value;
    }
}