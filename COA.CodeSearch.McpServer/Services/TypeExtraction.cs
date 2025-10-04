using System.Text.Json;

namespace COA.CodeSearch.McpServer.Services.TypeExtraction;

/// <summary>
/// Interface for extracting type information from source code
/// </summary>
public interface ITypeExtractionService
{
    Task<TypeExtractionResult> ExtractTypes(string content, string filePath);
}

/// <summary>
/// Result from type extraction operation
/// </summary>
public class TypeExtractionResult
{
    public bool Success { get; set; }
    public List<TypeInfo> Types { get; set; } = new();
    public List<MethodInfo> Methods { get; set; } = new();
    public string? Language { get; set; }

    /// <summary>
    /// Shared JSON deserialization options for TypeExtractionResult.
    /// Uses case-insensitive property matching to handle varying JSON casing from different sources.
    /// </summary>
    public static readonly JsonSerializerOptions DeserializationOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}

/// <summary>
/// Type information (class, interface, struct, etc.)
/// </summary>
public class TypeInfo
{
    public required string Name { get; set; }
    public required string Kind { get; set; }
    public required string Signature { get; set; }
    public int Line { get; set; }
    public int Column { get; set; }
    public List<string> Modifiers { get; set; } = new();
    public string? BaseType { get; set; }
    public List<string>? Interfaces { get; set; }
}

/// <summary>
/// Method/function information
/// </summary>
public class MethodInfo
{
    public required string Name { get; set; }
    public required string Signature { get; set; }
    public string ReturnType { get; set; } = "void";
    public int Line { get; set; }
    public int Column { get; set; }
    public string? ContainingType { get; set; }
    public List<string> Parameters { get; set; } = new();
    public List<string> Modifiers { get; set; } = new();
}
