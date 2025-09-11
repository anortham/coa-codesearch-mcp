using System.Text.Json.Serialization;

namespace COA.CodeSearch.McpServer.Models;

/// <summary>
/// Represents the direction of call tracing
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CallDirection
{
    /// <summary>
    /// Trace upward to find callers (who calls this?)
    /// </summary>
    Up,
    
    /// <summary>
    /// Trace downward to find callees (what does this call?)
    /// </summary>
    Down,
    
    /// <summary>
    /// Trace both directions (full call graph)
    /// </summary>
    Both
}

/// <summary>
/// Represents a hierarchical call chain analysis result
/// </summary>
public class CallHierarchy
{
    /// <summary>
    /// The symbol that was traced
    /// </summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// File path where the symbol is defined
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Line number where the symbol is defined
    /// </summary>
    public int LineNumber { get; set; }

    /// <summary>
    /// Method or type signature
    /// </summary>
    public string Signature { get; set; } = string.Empty;

    /// <summary>
    /// Direction of the trace
    /// </summary>
    public CallDirection Direction { get; set; }

    /// <summary>
    /// Root nodes of the call hierarchy
    /// </summary>
    public List<CallNode> Nodes { get; set; } = new();

    /// <summary>
    /// Total number of nodes in the hierarchy
    /// </summary>
    public int TotalNodes { get; set; }

    /// <summary>
    /// Maximum depth reached during tracing
    /// </summary>
    public int MaxDepthReached { get; set; }

    /// <summary>
    /// Whether recursion was detected
    /// </summary>
    public bool HasRecursion { get; set; }

    /// <summary>
    /// Execution time for the trace operation
    /// </summary>
    public TimeSpan ExecutionTime { get; set; }
}

/// <summary>
/// Represents a single node in the call hierarchy
/// </summary>
public class CallNode
{
    /// <summary>
    /// Name of the method or function
    /// </summary>
    public string MethodName { get; set; } = string.Empty;

    /// <summary>
    /// Name of the containing class or type
    /// </summary>
    public string? ClassName { get; set; }

    /// <summary>
    /// Full qualified name (namespace.class.method)
    /// </summary>
    public string? FullName { get; set; }

    /// <summary>
    /// Method signature with parameters
    /// </summary>
    public string? Signature { get; set; }

    /// <summary>
    /// File path containing this call
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Line number of the call site
    /// </summary>
    public int LineNumber { get; set; }

    /// <summary>
    /// Column number of the call site
    /// </summary>
    public int Column { get; set; }

    /// <summary>
    /// Code context around the call site
    /// </summary>
    public string Context { get; set; } = string.Empty;

    /// <summary>
    /// Child nodes in the hierarchy
    /// </summary>
    public List<CallNode> Children { get; set; } = new();

    /// <summary>
    /// Whether this is an entry point (Main, API controller, etc.)
    /// </summary>
    public bool IsEntryPoint { get; set; }

    /// <summary>
    /// Whether this call creates a recursive loop
    /// </summary>
    public bool IsRecursive { get; set; }

    /// <summary>
    /// Depth of this node in the hierarchy
    /// </summary>
    public int Depth { get; set; }

    /// <summary>
    /// Confidence score for the match (0.0 to 1.0)
    /// </summary>
    public double MatchScore { get; set; } = 1.0;

    /// <summary>
    /// Type of call site (method call, constructor call, etc.)
    /// </summary>
    public string? CallType { get; set; }

    /// <summary>
    /// Additional metadata about the call
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Represents information about a symbol's definition
/// </summary>
public class SymbolInfo
{
    /// <summary>
    /// Symbol name
    /// </summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// File path where symbol is defined
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Line number of definition
    /// </summary>
    public int LineNumber { get; set; }

    /// <summary>
    /// Column number of definition
    /// </summary>
    public int Column { get; set; }

    /// <summary>
    /// Type of symbol (method, class, interface, etc.)
    /// </summary>
    public string SymbolType { get; set; } = string.Empty;

    /// <summary>
    /// Full signature or declaration
    /// </summary>
    public string Signature { get; set; } = string.Empty;

    /// <summary>
    /// Containing class or namespace
    /// </summary>
    public string? ContainingType { get; set; }

    /// <summary>
    /// Method start line (for methods)
    /// </summary>
    public int? StartLine { get; set; }

    /// <summary>
    /// Method end line (for methods)
    /// </summary>
    public int? EndLine { get; set; }
}

/// <summary>
/// Represents method information extracted from code
/// </summary>
public class MethodInfo
{
    /// <summary>
    /// Method name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Containing class name
    /// </summary>
    public string? ClassName { get; set; }

    /// <summary>
    /// Full qualified name
    /// </summary>
    public string? FullName { get; set; }

    /// <summary>
    /// Method signature
    /// </summary>
    public string Signature { get; set; } = string.Empty;

    /// <summary>
    /// Return type
    /// </summary>
    public string? ReturnType { get; set; }

    /// <summary>
    /// Parameter list
    /// </summary>
    public List<string> Parameters { get; set; } = new();

    /// <summary>
    /// Method start line
    /// </summary>
    public int StartLine { get; set; }

    /// <summary>
    /// Method end line
    /// </summary>
    public int EndLine { get; set; }

    /// <summary>
    /// Method modifiers (public, private, static, etc.)
    /// </summary>
    public List<string> Modifiers { get; set; } = new();

    /// <summary>
    /// Method attributes/annotations
    /// </summary>
    public List<string> Attributes { get; set; } = new();

    /// <summary>
    /// Whether this is a constructor
    /// </summary>
    public bool IsConstructor { get; set; }

    /// <summary>
    /// Whether this is static
    /// </summary>
    public bool IsStatic { get; set; }

    /// <summary>
    /// Whether this is an entry point
    /// </summary>
    public bool IsEntryPoint { get; set; }
}