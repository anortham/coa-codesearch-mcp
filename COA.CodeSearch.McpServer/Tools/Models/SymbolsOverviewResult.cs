using COA.CodeSearch.McpServer.Services.TypeExtraction;

namespace COA.CodeSearch.McpServer.Tools.Models;

/// <summary>
/// Result of a symbols overview operation - comprehensive symbol information from a file
/// </summary>
public class SymbolsOverviewResult
{
    /// <summary>
    /// Path to the analyzed file
    /// </summary>
    public string FilePath { get; set; } = string.Empty;
    
    /// <summary>
    /// Language detected for the file
    /// </summary>
    public string? Language { get; set; }
    
    /// <summary>
    /// Classes found in the file
    /// </summary>
    public List<TypeOverview> Classes { get; set; } = new();
    
    /// <summary>
    /// Interfaces found in the file
    /// </summary>
    public List<TypeOverview> Interfaces { get; set; } = new();
    
    /// <summary>
    /// Structs/Records found in the file
    /// </summary>
    public List<TypeOverview> Structs { get; set; } = new();
    
    /// <summary>
    /// Enums found in the file
    /// </summary>
    public List<TypeOverview> Enums { get; set; } = new();
    
    /// <summary>
    /// Methods/Functions found in the file (if not part of a type)
    /// </summary>
    public List<MethodOverview> Methods { get; set; } = new();
    
    /// <summary>
    /// Total count of all symbols
    /// </summary>
    public int TotalSymbols { get; set; }
    
    /// <summary>
    /// Time taken to extract symbols
    /// </summary>
    public TimeSpan ExtractionTime { get; set; }
    
    /// <summary>
    /// Whether the extraction was successful
    /// </summary>
    public bool Success { get; set; }
}

/// <summary>
/// Overview information for a type (class, interface, struct, enum)
/// </summary>
public class TypeOverview
{
    /// <summary>
    /// Name of the type
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Kind of type (class, interface, struct, enum)
    /// </summary>
    public string Kind { get; set; } = string.Empty;
    
    /// <summary>
    /// Full signature/declaration
    /// </summary>
    public string Signature { get; set; } = string.Empty;
    
    /// <summary>
    /// Line number where defined
    /// </summary>
    public int Line { get; set; }
    
    /// <summary>
    /// Column position where defined
    /// </summary>
    public int Column { get; set; }
    
    /// <summary>
    /// Access modifiers (public, private, etc.)
    /// </summary>
    public List<string> Modifiers { get; set; } = new();
    
    /// <summary>
    /// Base type or parent class
    /// </summary>
    public string? BaseType { get; set; }
    
    /// <summary>
    /// Implemented interfaces
    /// </summary>
    public List<string>? Interfaces { get; set; }
    
    /// <summary>
    /// Methods defined in this type
    /// </summary>
    public List<MethodOverview> Methods { get; set; } = new();
    
    /// <summary>
    /// Number of methods in this type
    /// </summary>
    public int MethodCount { get; set; }
}

/// <summary>
/// Overview information for a method or function
/// </summary>
public class MethodOverview
{
    /// <summary>
    /// Name of the method/function
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Full signature including parameters and return type
    /// </summary>
    public string Signature { get; set; } = string.Empty;
    
    /// <summary>
    /// Return type of the method
    /// </summary>
    public string ReturnType { get; set; } = "void";
    
    /// <summary>
    /// Line number where defined
    /// </summary>
    public int Line { get; set; }
    
    /// <summary>
    /// Column position where defined
    /// </summary>
    public int Column { get; set; }
    
    /// <summary>
    /// Access modifiers and other modifiers
    /// </summary>
    public List<string> Modifiers { get; set; } = new();
    
    /// <summary>
    /// Parameters of the method
    /// </summary>
    public List<string> Parameters { get; set; } = new();
    
    /// <summary>
    /// Type that contains this method (if any)
    /// </summary>
    public string? ContainingType { get; set; }
}