using System.Text.Json;
using COA.CodeSearch.McpServer.Services.TypeExtraction;

namespace COA.CodeSearch.McpServer.Models;

public class StoredTypeInfo
{
    public List<TypeInfo> Types { get; set; } = new();
    public List<MethodInfo> Methods { get; set; } = new();
    public string? Language { get; set; }
    
    /// <summary>
    /// Shared JSON deserialization options for StoredTypeInfo.
    /// Uses case-insensitive property matching to handle varying JSON casing from different sources.
    /// </summary>
    public static readonly JsonSerializerOptions DeserializationOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}