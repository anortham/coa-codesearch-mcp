using System.ComponentModel;

namespace COA.CodeSearch.McpServer.Models;

/// <summary>
/// Defines the scope and persistence level of Claude memory entries
/// </summary>
public enum MemoryScope
{
    /// <summary>
    /// Architectural decisions that affect the entire team (version controlled)
    /// Examples: "Use Repository pattern", "HIPAA requires encryption"
    /// </summary>
    [Description("Architectural decisions that affect the entire team")]
    ArchitecturalDecision,
    
    /// <summary>
    /// Reusable code patterns and implementation approaches (version controlled)
    /// Examples: "Patient validation pattern", "Service registration pattern"
    /// </summary>
    [Description("Reusable code patterns and implementation approaches")]
    CodePattern,
    
    /// <summary>
    /// Security rules and compliance requirements (version controlled)
    /// Examples: "HIPAA audit logging", "Authentication requirements"
    /// </summary>
    [Description("Security rules and compliance requirements")]
    SecurityRule,
    
    /// <summary>
    /// High-level project insights and domain knowledge (version controlled)
    /// Examples: "DDD architecture", "Hospital workflow understanding"
    /// </summary>
    [Description("High-level project insights and domain knowledge")]
    ProjectInsight,
    
    /// <summary>
    /// Individual work sessions and progress tracking (local only)
    /// Examples: "Worked on auth module", "Fixed performance issue"
    /// </summary>
    [Description("Individual work sessions and progress tracking")]
    WorkSession,
    
    /// <summary>
    /// Compressed conversation summaries for context preservation (local only)
    /// Examples: Session summaries, decision reasoning, discussion outcomes
    /// </summary>
    [Description("Compressed conversation summaries for context preservation")]
    ConversationSummary,
    
    /// <summary>
    /// Developer-specific insights and personal context (local only)
    /// Examples: Personal coding preferences, local environment notes
    /// </summary>
    [Description("Developer-specific insights and personal context")]
    PersonalContext,
    
    /// <summary>
    /// Short-term reminders and temporary notes (local only)
    /// Examples: "TODO: Update docs", "Check with John about API changes"
    /// </summary>
    [Description("Short-term reminders and temporary notes")]
    TemporaryNote
}

/// <summary>
/// Represents a single memory entry in Claude's persistent memory system
/// </summary>
public class MemoryEntry
{
    /// <summary>
    /// Unique identifier for the memory entry
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    /// <summary>
    /// The actual content/knowledge being stored
    /// </summary>
    public string Content { get; set; } = string.Empty;
    
    /// <summary>
    /// The scope/type of this memory (determines storage location)
    /// </summary>
    public MemoryScope Scope { get; set; }
    
    /// <summary>
    /// Searchable keywords extracted from content
    /// </summary>
    public string[] Keywords { get; set; } = Array.Empty<string>();
    
    /// <summary>
    /// Files that are related to or affected by this memory
    /// </summary>
    public string[] FilesInvolved { get; set; } = Array.Empty<string>();
    
    /// <summary>
    /// When this memory was created
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Session ID when this memory was created (for correlation)
    /// </summary>
    public string SessionId { get; set; } = string.Empty;
    
    /// <summary>
    /// Confidence level that this memory is still relevant (0-100)
    /// </summary>
    public int Confidence { get; set; } = 100;
    
    /// <summary>
    /// Optional category for grouping related memories
    /// </summary>
    public string? Category { get; set; }
    
    /// <summary>
    /// Optional reasoning or context for why this memory was created
    /// </summary>
    public string? Reasoning { get; set; }
    
    /// <summary>
    /// Tags for flexible categorization and filtering
    /// </summary>
    public string[] Tags { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Response model for memory search operations
/// </summary>
public class MemorySearchResult
{
    /// <summary>
    /// The memory entries that matched the search
    /// </summary>
    public List<MemoryEntry> Memories { get; set; } = new();
    
    /// <summary>
    /// Total number of memories found (before any limiting)
    /// </summary>
    public int TotalFound { get; set; }
    
    /// <summary>
    /// The search query used
    /// </summary>
    public string Query { get; set; } = string.Empty;
    
    /// <summary>
    /// Scope filter applied (if any)
    /// </summary>
    public MemoryScope? ScopeFilter { get; set; }
    
    /// <summary>
    /// Suggested related searches based on found memories
    /// </summary>
    public string[] SuggestedQueries { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Configuration for memory storage and retrieval
/// </summary>
public class MemoryConfiguration
{
    /// <summary>
    /// Base path for memory storage
    /// </summary>
    public string BasePath { get; set; } = ".codesearch";
    
    /// <summary>
    /// Directory for project-level memories (version controlled)
    /// </summary>
    public string ProjectMemoryPath { get; set; } = "project-memory";
    
    /// <summary>
    /// Directory for local-only memories
    /// </summary>
    public string LocalMemoryPath { get; set; } = "local-memory";
    
    /// <summary>
    /// Maximum number of memories to return in a single search
    /// </summary>
    public int MaxSearchResults { get; set; } = 50;
    
    /// <summary>
    /// How long to keep temporary notes (in days)
    /// </summary>
    public int TemporaryNoteRetentionDays { get; set; } = 7;
    
    /// <summary>
    /// Minimum confidence level to include in search results
    /// </summary>
    public int MinConfidenceLevel { get; set; } = 50;
}