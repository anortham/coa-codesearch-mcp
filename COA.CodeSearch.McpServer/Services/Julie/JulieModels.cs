namespace COA.CodeSearch.McpServer.Services.Julie;

/// <summary>
/// Represents a symbol (definition) from julie-codesearch extraction.
/// Symbols include classes, methods, functions, interfaces, etc.
/// </summary>
public class JulieSymbol
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("file_path")]
    public string FilePath { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("start_line")]
    public int StartLine { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("start_column")]
    public int StartColumn { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("end_line")]
    public int EndLine { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("end_column")]
    public int EndColumn { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("start_byte")]
    public int? StartByte { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("end_byte")]
    public int? EndByte { get; set; }

    public string? Signature { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("doc_comment")]
    public string? DocComment { get; set; }

    public string? Visibility { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("parent_id")]
    public string? ParentId { get; set; }
}

/// <summary>
/// Represents an identifier (reference/usage) from julie-codesearch extraction.
/// Used for LSP-quality find_references functionality.
/// </summary>
public class JulieIdentifier
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty; // call, variable_ref, type_usage, member_access
    public string Language { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("file_path")]
    public string FilePath { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("start_line")]
    public int StartLine { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("start_col")]
    public int StartColumn { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("end_line")]
    public int EndLine { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("end_col")]
    public int EndColumn { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("start_byte")]
    public int? StartByte { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("end_byte")]
    public int? EndByte { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("containing_symbol_id")]
    public string? ContainingSymbolId { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("target_symbol_id")]
    public string? TargetSymbolId { get; set; }

    public float Confidence { get; set; } = 1.0f;

    [System.Text.Json.Serialization.JsonPropertyName("code_context")]
    public string? CodeContext { get; set; }
}

/// <summary>
/// Represents a relationship between two symbols from julie-codesearch extraction.
/// Relationships include inheritance (extends, implements), calls, uses, etc.
/// </summary>
public class JulieRelationship
{
    public string Id { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("from_symbol_id")]
    public string FromSymbolId { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("to_symbol_id")]
    public string ToSymbolId { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty; // extends, implements, calls, uses, etc.

    [System.Text.Json.Serialization.JsonPropertyName("file_path")]
    public string FilePath { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("line_number")]
    public int LineNumber { get; set; }

    public float Confidence { get; set; } = 1.0f;

    public string? Metadata { get; set; }
}
