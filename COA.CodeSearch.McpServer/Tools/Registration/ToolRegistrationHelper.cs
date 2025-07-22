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
        
        // Log the actual size of what we're returning to a file
        var byteCount = System.Text.Encoding.UTF8.GetByteCount(json);
        var estimatedTokens = byteCount / 4; // Rough estimate: 1 token ≈ 4 bytes
        
        try
        {
            var logPath = Path.Combine(Path.GetTempPath(), "mcp-token-trace.log");
            var logMessage = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] CreateSuccessResult: {byteCount} bytes, ~{estimatedTokens} tokens\n";
            logMessage += $"First 500 chars: {json.Substring(0, Math.Min(500, json.Length))}\n\n";
            File.AppendAllText(logPath, logMessage);
        }
        catch { /* Ignore logging errors */ }
        
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