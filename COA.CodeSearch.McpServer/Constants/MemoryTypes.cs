namespace COA.CodeSearch.McpServer.Constants;

/// <summary>
/// Centralized constants for all memory types used throughout the system
/// </summary>
public static class MemoryTypeConstants
{
    // Core memory types
    public const string ArchitecturalDecision = "ArchitecturalDecision";
    public const string CodePattern = "CodePattern";
    public const string SecurityRule = "SecurityRule";
    public const string ProjectInsight = "ProjectInsight";
    public const string WorkSession = "WorkSession";
    public const string Checkpoint = "Checkpoint";
    
    // Development workflow types
    public const string TechnicalDebt = "TechnicalDebt";
    public const string DeferredTask = "DeferredTask";
    public const string Question = "Question";
    public const string Assumption = "Assumption";
    public const string Experiment = "Experiment";
    public const string Learning = "Learning";
    public const string Blocker = "Blocker";
    public const string Idea = "Idea";
    public const string CodeReview = "CodeReview";
    public const string BugReport = "BugReport";
    public const string GitCommit = "GitCommit";
    
    // Quality and optimization types
    public const string PerformanceIssue = "PerformanceIssue";
    public const string PerformanceOptimization = "PerformanceOptimization";
    public const string Refactoring = "Refactoring";
    public const string RefactoringNote = "RefactoringNote";
    public const string BugFix = "BugFix";
    
    // Documentation and planning types
    public const string Documentation = "Documentation";
    public const string DocumentationTodo = "DocumentationTodo";
    public const string FeatureIdea = "FeatureIdea";
    public const string TestingNote = "TestingNote";
    
    // Infrastructure types
    public const string Dependency = "Dependency";
    public const string DependencyNote = "DependencyNote";
    public const string Configuration = "Configuration";
    public const string ConfigurationNote = "ConfigurationNote";
    public const string DeploymentNote = "DeploymentNote";
    public const string SecurityConcern = "SecurityConcern";
    
    // Collaboration types
    public const string TeamNote = "TeamNote";
    public const string PersonalNote = "PersonalNote";
    public const string LocalInsight = "LocalInsight";
    
    // Special types
    public const string WorkingMemory = "WorkingMemory";
    public const string Checklist = "Checklist";
    public const string ChecklistItem = "ChecklistItem";
    public const string PendingResolution = "PendingResolution";
    public const string CustomType = "CustomType";
    
    /// <summary>
    /// All allowed memory types for validation
    /// </summary>
    public static readonly HashSet<string> AllowedTypes = new()
    {
        // Core types
        ArchitecturalDecision,
        CodePattern,
        SecurityRule,
        ProjectInsight,
        WorkSession,
        Checkpoint,
        
        // Development workflow
        TechnicalDebt,
        DeferredTask,
        Question,
        Assumption,
        Experiment,
        Learning,
        Blocker,
        Idea,
        CodeReview,
        BugReport,
        GitCommit,
        
        // Quality and optimization
        PerformanceIssue,
        PerformanceOptimization,
        Refactoring,
        RefactoringNote,
        BugFix,
        
        // Documentation and planning
        Documentation,
        DocumentationTodo,
        FeatureIdea,
        TestingNote,
        
        // Infrastructure
        Dependency,
        DependencyNote,
        Configuration,
        ConfigurationNote,
        DeploymentNote,
        SecurityConcern,
        
        // Collaboration
        TeamNote,
        PersonalNote,
        LocalInsight,
        
        // Special types
        WorkingMemory,
        Checklist,
        ChecklistItem,
        PendingResolution,
        CustomType
    };
    
    /// <summary>
    /// Memory types that should be shared with the team by default
    /// </summary>
    public static readonly HashSet<string> DefaultSharedTypes = new()
    {
        ArchitecturalDecision,
        CodePattern,
        SecurityRule,
        ProjectInsight,
        TechnicalDebt,
        SecurityConcern,
        BugFix,
        PerformanceOptimization,
        Documentation
    };
    
    /// <summary>
    /// Memory types that are typically personal/local
    /// </summary>
    public static readonly HashSet<string> LocalTypes = new()
    {
        WorkSession,
        LocalInsight,
        PersonalNote,
        Checkpoint
    };
}