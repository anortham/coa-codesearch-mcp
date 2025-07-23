namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Service for tracking and managing context awareness for intelligent memory search
/// </summary>
public interface IContextAwarenessService
{
    /// <summary>
    /// Get the current search context based on session activity
    /// </summary>
    Task<SearchContext> GetCurrentContextAsync();
    
    /// <summary>
    /// Update context with current file being worked on
    /// </summary>
    Task UpdateCurrentFileAsync(string? filePath);
    
    /// <summary>
    /// Track a file access (builds recent files list)
    /// </summary>
    Task TrackFileAccessAsync(string filePath);
    
    /// <summary>
    /// Track a memory search query (builds query history)
    /// </summary>
    Task TrackSearchQueryAsync(string query, int resultsFound);
    
    /// <summary>
    /// Extract context keywords from a file path
    /// </summary>
    string[] ExtractFileContextKeywords(string filePath);
    
    /// <summary>
    /// Get context-aware boost weights for search terms
    /// </summary>
    Dictionary<string, float> GetContextBoosts(SearchContext context, string[] searchTerms);
}

/// <summary>
/// Current search context with file, session, and project information
/// </summary>
public class SearchContext
{
    /// <summary>
    /// File currently being edited/viewed
    /// </summary>
    public string? CurrentFile { get; set; }
    
    /// <summary>
    /// Recently accessed files (last 10)
    /// </summary>
    public string[] RecentFiles { get; set; } = Array.Empty<string>();
    
    /// <summary>
    /// Recent search queries (last 20)
    /// </summary>
    public SearchHistoryItem[] RecentQueries { get; set; } = Array.Empty<SearchHistoryItem>();
    
    /// <summary>
    /// Active working memory items that might be relevant
    /// </summary>
    public string[] ActiveWorkingMemoryTopics { get; set; } = Array.Empty<string>();
    
    /// <summary>
    /// Detected project type and technologies
    /// </summary>
    public ProjectContext ProjectInfo { get; set; } = new();
    
    /// <summary>
    /// Keywords extracted from current context
    /// </summary>
    public string[] ContextKeywords { get; set; } = Array.Empty<string>();
    
    /// <summary>
    /// Session timestamp for tracking
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Session ID for grouping context
    /// </summary>
    public string SessionId { get; set; } = Guid.NewGuid().ToString();
}

/// <summary>
/// Historical search query information
/// </summary>
public class SearchHistoryItem
{
    public string Query { get; set; } = string.Empty;
    public int ResultsFound { get; set; }
    public DateTime Timestamp { get; set; }
    public string[] ExpandedTerms { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Project context information for domain-specific boosting
/// </summary>
public class ProjectContext
{
    /// <summary>
    /// Detected project type (web, console, library, etc.)
    /// </summary>
    public string ProjectType { get; set; } = "unknown";
    
    /// <summary>
    /// Primary technologies/frameworks detected
    /// </summary>
    public string[] Technologies { get; set; } = Array.Empty<string>();
    
    /// <summary>
    /// Programming languages in the project
    /// </summary>
    public string[] Languages { get; set; } = Array.Empty<string>();
    
    /// <summary>
    /// Common patterns/keywords found in the codebase
    /// </summary>
    public string[] CommonPatterns { get; set; } = Array.Empty<string>();
    
    /// <summary>
    /// Workspace root path
    /// </summary>
    public string? WorkspacePath { get; set; }
}

/// <summary>
/// Context boost configuration
/// </summary>
public class ContextBoostOptions
{
    /// <summary>
    /// Boost factor for current file context (default: 1.2)
    /// </summary>
    public float CurrentFileBoost { get; set; } = 1.2f;
    
    /// <summary>
    /// Boost factor for recent files context (default: 1.1)
    /// </summary>
    public float RecentFilesBoost { get; set; } = 1.1f;
    
    /// <summary>
    /// Boost factor for recent query patterns (default: 1.15)
    /// </summary>
    public float RecentQueryBoost { get; set; } = 1.15f;
    
    /// <summary>
    /// Boost factor for project technology alignment (default: 1.3)
    /// </summary>
    public float TechnologyBoost { get; set; } = 1.3f;
    
    /// <summary>
    /// Maximum number of recent files to consider
    /// </summary>
    public int MaxRecentFiles { get; set; } = 10;
    
    /// <summary>
    /// Maximum number of recent queries to consider
    /// </summary>
    public int MaxRecentQueries { get; set; } = 20;
    
    /// <summary>
    /// Minimum keyword length for context extraction
    /// </summary>
    public int MinKeywordLength { get; set; } = 3;
}