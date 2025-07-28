using COA.CodeSearch.McpServer.Models;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Quality score for a memory entry on a 0.0-1.0 scale
/// </summary>
public class MemoryQualityScore
{
    public double OverallScore { get; set; }
    public Dictionary<string, double> ComponentScores { get; set; } = new();
    public List<QualityIssue> Issues { get; set; } = new();
    public List<QualityImprovement> Suggestions { get; set; } = new();
    public bool PassesThreshold { get; set; }
    public string? SummaryExplanation { get; set; }
}

/// <summary>
/// Represents a quality issue found in a memory
/// </summary>
public class QualityIssue
{
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public QualitySeverity Severity { get; set; }
    public string? Field { get; set; }
    public string? Location { get; set; }
}

/// <summary>
/// Represents a suggested improvement for memory quality
/// </summary>
public class QualityImprovement
{
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ActionText { get; set; } = string.Empty;
    public QualityImprovementType Type { get; set; }
    public double ExpectedImpact { get; set; } // 0.0-1.0 scale
    public bool CanAutoImplement { get; set; }
}

/// <summary>
/// Severity levels for quality issues
/// </summary>
public enum QualitySeverity
{
    Info,
    Minor,
    Major,
    Critical
}

/// <summary>
/// Types of quality improvements
/// </summary>
public enum QualityImprovementType
{
    ContentExpansion,
    Restructuring,
    TypeCorrection,
    TaggingEnhancement,
    ContextEnrichment,
    DuplicateResolution,
    ConsistencyFix
}

/// <summary>
/// Options for quality validation
/// </summary>
public class QualityValidationOptions
{
    public double PassingThreshold { get; set; } = 0.7;
    public HashSet<string> EnabledValidators { get; set; } = new();
    public bool IncludeImprovementSuggestions { get; set; } = true;
    public bool AllowAutoImprovements { get; set; } = false;
    public string? ContextWorkspace { get; set; }
    public List<string>? RecentFiles { get; set; }
}

/// <summary>
/// Interface for memory quality validation services
/// </summary>
public interface IMemoryQualityValidator
{
    /// <summary>
    /// Validate the quality of a memory entry
    /// </summary>
    Task<MemoryQualityScore> ValidateQualityAsync(FlexibleMemoryEntry memory, QualityValidationOptions? options = null);
    
    /// <summary>
    /// Validate quality for multiple memories (batch operation)
    /// </summary>
    Task<Dictionary<string, MemoryQualityScore>> ValidateQualityBatchAsync(
        IEnumerable<FlexibleMemoryEntry> memories, 
        QualityValidationOptions? options = null);
    
    /// <summary>
    /// Get quality criteria for a specific memory type
    /// </summary>
    QualityCriteria GetCriteriaForType(string memoryType);
    
    /// <summary>
    /// Apply automatic improvements to a memory if possible
    /// </summary>
    Task<FlexibleMemoryEntry> ApplyImprovementsAsync(FlexibleMemoryEntry memory, List<QualityImprovement> improvements);
    
    /// <summary>
    /// Get available validator names
    /// </summary>
    IEnumerable<string> GetAvailableValidators();
}

/// <summary>
/// Quality criteria for different memory types
/// </summary>
public class QualityCriteria
{
    public string MemoryType { get; set; } = string.Empty;
    public Dictionary<string, double> RequiredComponentWeights { get; set; } = new();
    public List<string> RequiredFields { get; set; } = new();
    public List<string> RecommendedFields { get; set; } = new();
    public int MinContentLength { get; set; } = 10;
    public int RecommendedContentLength { get; set; } = 100;
    public List<string> RequiredTags { get; set; } = new();
    public Dictionary<string, object> TypeSpecificRules { get; set; } = new();
}

/// <summary>
/// Base interface for individual quality validators
/// </summary>
public interface IQualityValidator
{
    string Name { get; }
    string Description { get; }
    double Weight { get; }
    
    Task<QualityValidatorResult> ValidateAsync(FlexibleMemoryEntry memory, QualityValidationOptions options);
}

/// <summary>
/// Result from an individual quality validator
/// </summary>
public class QualityValidatorResult
{
    public double Score { get; set; }
    public List<QualityIssue> Issues { get; set; } = new();
    public List<QualityImprovement> Suggestions { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();
}