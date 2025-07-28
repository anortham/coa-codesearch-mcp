using System.ComponentModel;
using System.Text.Json.Serialization;

namespace COA.CodeSearch.McpServer.Models;

/// <summary>
/// Represents a unified memory command that can handle multiple intents through natural language
/// </summary>
public class UnifiedMemoryCommand
{
    /// <summary>
    /// The detected or specified intent for this command
    /// </summary>
    public MemoryIntent Intent { get; set; } = MemoryIntent.Auto;

    /// <summary>
    /// The natural language content/query from the user
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Context information for better intent detection and execution
    /// </summary>
    public CommandContext Context { get; set; } = new();

    /// <summary>
    /// Additional parameters extracted from the command
    /// </summary>
    public Dictionary<string, object> Parameters { get; set; } = new();
}

/// <summary>
/// The intent/purpose of a memory command
/// </summary>
public enum MemoryIntent
{
    /// <summary>
    /// Auto-detect intent from content (default)
    /// </summary>
    [Description("Automatically detect intent from command content")]
    Auto = 0,

    /// <summary>
    /// Save/store new memory (maps to store_memory, store_temporary_memory)
    /// </summary>
    [Description("Save or store new memory")]
    Save = 1,

    /// <summary>
    /// Find/search memories (maps to search_memories, file_search, text_search)
    /// </summary>
    [Description("Find or search existing memories")]
    Find = 2,

    /// <summary>
    /// Connect/link memories (maps to link_memories, memory relationships)
    /// </summary>
    [Description("Connect or link related memories")]
    Connect = 3,

    /// <summary>
    /// Explore relationships and memory graph (maps to memory_graph_navigator)
    /// </summary>
    [Description("Explore memory relationships and connections")]
    Explore = 4,

    /// <summary>
    /// Get suggestions and recommendations (maps to get_memory_suggestions)
    /// </summary>
    [Description("Get suggestions and recommendations")]
    Suggest = 5,

    /// <summary>
    /// Manage memories (update, delete, archive)
    /// </summary>
    [Description("Manage existing memories (update, delete, archive)")]
    Manage = 6
}

/// <summary>
/// Context information that helps with intent detection and command execution
/// </summary>
public class CommandContext
{
    /// <summary>
    /// Confidence level of intent detection (0.0 to 1.0)
    /// </summary>
    public float Confidence { get; set; } = 0.0f;

    /// <summary>
    /// Scope of the operation (project, session, local)
    /// </summary>
    public string Scope { get; set; } = "project";

    /// <summary>
    /// Current working directory for file-related operations
    /// </summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Session ID for tracking temporary memories
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// Files currently being worked on (for context)
    /// </summary>
    public List<string> RelatedFiles { get; set; } = new();

    /// <summary>
    /// Recent files accessed (for context awareness)
    /// </summary>
    public List<string> RecentFiles { get; set; } = new();

    /// <summary>
    /// Current task or focus area
    /// </summary>
    public string? CurrentFocus { get; set; }
}

/// <summary>
/// Result of executing a unified memory command
/// </summary>
public class UnifiedMemoryResult
{
    /// <summary>
    /// Whether the command executed successfully
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// The action that was performed
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable message about the result
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// The intent that was detected and executed
    /// </summary>
    public MemoryIntent ExecutedIntent { get; set; }

    /// <summary>
    /// Confidence of intent detection
    /// </summary>
    public float IntentConfidence { get; set; }

    /// <summary>
    /// Primary memory result (for save operations)
    /// </summary>
    public FlexibleMemoryEntry? Memory { get; set; }

    /// <summary>
    /// Multiple memory results (for find operations)
    /// </summary>
    public List<FlexibleMemoryEntry> Memories { get; set; } = new();

    /// <summary>
    /// Checklist result (for checklist operations)
    /// </summary>
    public object? Checklist { get; set; }

    /// <summary>
    /// Search highlights (for find operations)
    /// </summary>
    public Dictionary<string, string[]>? Highlights { get; set; }

    /// <summary>
    /// Search facets (for find operations)
    /// </summary>
    public Dictionary<string, Dictionary<string, int>>? Facets { get; set; }

    /// <summary>
    /// Spell check suggestions (for find operations)
    /// </summary>
    public SpellCheckInfo? SpellCheck { get; set; }

    /// <summary>
    /// Suggested next actions the user can take
    /// </summary>
    public List<ActionSuggestion> NextSteps { get; set; } = new();

    /// <summary>
    /// Additional metadata about the operation
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// A suggested action the user can take based on the current result
/// </summary>
public class ActionSuggestion
{
    /// <summary>
    /// Unique identifier for this suggestion
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable description of the action
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// The command to execute this action
    /// </summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>
    /// Estimated token cost of executing this action
    /// </summary>
    public int EstimatedTokens { get; set; }

    /// <summary>
    /// Priority of this suggestion (high, medium, low)
    /// </summary>
    public string Priority { get; set; } = "medium";

    /// <summary>
    /// Category of this suggestion
    /// </summary>
    public string Category { get; set; } = "general";
}

/// <summary>
/// Information about spell check suggestions
/// </summary>
public class SpellCheckInfo
{
    /// <summary>
    /// Suggested correction for the query
    /// </summary>
    public string? DidYouMean { get; set; }

    /// <summary>
    /// Whether the query was automatically corrected
    /// </summary>
    public bool AutoCorrected { get; set; }

    /// <summary>
    /// Additional spelling suggestions
    /// </summary>
    public List<string> Suggestions { get; set; } = new();
}

/// <summary>
/// Raw parameters from MCP tool input (JSON deserializes to strings)
/// </summary>
public class UnifiedMemoryInputParams
{
    /// <summary>
    /// The natural language command
    /// </summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>
    /// Optional: Force a specific intent instead of auto-detection (string from enum)
    /// </summary>
    public string? Intent { get; set; }

    /// <summary>
    /// Optional: Working directory for file operations
    /// </summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Optional: Session ID for tracking
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// Optional: Files currently being worked on
    /// </summary>
    public string[]? RelatedFiles { get; set; }

    /// <summary>
    /// Optional: Current focus or task description
    /// </summary>
    public string? CurrentFocus { get; set; }
}

/// <summary>
/// Parameters for the unified memory tool (internal use with typed enums)
/// </summary>
public class UnifiedMemoryParams
{
    /// <summary>
    /// The natural language command
    /// </summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>
    /// Optional: Force a specific intent instead of auto-detection
    /// </summary>
    public MemoryIntent? Intent { get; set; }

    /// <summary>
    /// Optional: Working directory for file operations
    /// </summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Optional: Session ID for tracking
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// Optional: Files currently being worked on
    /// </summary>
    public List<string> RelatedFiles { get; set; } = new();

    /// <summary>
    /// Optional: Current focus or task description
    /// </summary>
    public string? CurrentFocus { get; set; }
}