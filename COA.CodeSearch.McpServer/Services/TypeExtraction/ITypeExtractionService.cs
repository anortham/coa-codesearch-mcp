using System.Text.Json;

namespace COA.CodeSearch.McpServer.Services.TypeExtraction;

/// <summary>
/// Service for extracting type information from source code files.
/// </summary>
public interface ITypeExtractionService
{
    /// <summary>
    /// Extract types and methods from the given source code.
    /// </summary>
    /// <param name="content">The source code content</param>
    /// <param name="filePath">The file path (used to determine language)</param>
    /// <returns>Extraction results including types and methods</returns>
    Task<TypeExtractionResult> ExtractTypes(string content, string filePath);
}

/// <summary>
/// Result of type extraction from a source file.
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
/// Information about a type (class, interface, struct, etc.) found in source code.
/// </summary>
public class TypeInfo
{
    public required string Name { get; set; }
    public required string Kind { get; set; }
    public required string Signature { get; set; }
    public int Line { get; set; }
    public int Column { get; set; }
    public int EndLine { get; set; }
    public int EndColumn { get; set; }
    public List<string> Modifiers { get; set; } = new();
    public string? BaseType { get; set; }
    public List<string>? Interfaces { get; set; }

    // Additional properties for enhanced language support
    public List<string> BaseTypes { get; set; } = new();
    public List<string> TypeParameters { get; set; } = new();
    public string? Namespace { get; set; }
    public bool IsExported { get; set; }
}

/// <summary>
/// Information about a method or function found in source code.
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

    // Additional properties for enhanced language support
    public bool IsAsync { get; set; }
    public bool IsStatic { get; set; }
    public bool IsGenerator { get; set; }
    public bool IsExported { get; set; }
    public string? ClassName { get; set; }
    public List<MethodParameter> DetailedParameters { get; set; } = new();
}

/// <summary>
/// Detailed information about a method parameter.
/// </summary>
public class MethodParameter
{
    public required string Name { get; set; }
    public string? Type { get; set; }
    public bool HasDefaultValue { get; set; }
    public string? DefaultValue { get; set; }
    public bool IsRestParameter { get; set; }
    public bool IsOptional { get; set; }
}