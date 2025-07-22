using System.Text.Json;

namespace COA.CodeSearch.McpServer.Models;

/// <summary>
/// Represents a reusable template for creating memories with predefined structure
/// </summary>
public class MemoryTemplate
{
    /// <summary>
    /// Unique identifier for the template
    /// </summary>
    public required string Id { get; set; }
    
    /// <summary>
    /// Display name for the template
    /// </summary>
    public required string Name { get; set; }
    
    /// <summary>
    /// Description of when to use this template
    /// </summary>
    public required string Description { get; set; }
    
    /// <summary>
    /// The memory type this template creates
    /// </summary>
    public required string MemoryType { get; set; }
    
    /// <summary>
    /// Template string for the content, supports placeholders like {file}, {description}, {reason}
    /// </summary>
    public required string ContentTemplate { get; set; }
    
    /// <summary>
    /// Default fields to include in memories created from this template
    /// </summary>
    public Dictionary<string, JsonElement> DefaultFields { get; set; } = new();
    
    /// <summary>
    /// List of placeholder names that must be provided when using this template
    /// </summary>
    public List<string> RequiredPlaceholders { get; set; } = new();
    
    /// <summary>
    /// Optional placeholder descriptions for better UX
    /// </summary>
    public Dictionary<string, string> PlaceholderDescriptions { get; set; } = new();
    
    /// <summary>
    /// Tags to automatically apply to memories created from this template
    /// </summary>
    public List<string> DefaultTags { get; set; } = new();
}

/// <summary>
/// Built-in memory templates
/// </summary>
public static class MemoryTemplates
{
    public static readonly MemoryTemplate CodeReview = new()
    {
        Id = "code-review",
        Name = "Code Review Finding",
        Description = "Document issues or improvements found during code review",
        MemoryType = "CodeReview",
        ContentTemplate = "Code Review Finding for {file}\n\nIssue: {issue}\n\nSuggested Fix: {suggestion}\n\nSeverity: {severity}",
        RequiredPlaceholders = new() { "file", "issue", "suggestion", "severity" },
        PlaceholderDescriptions = new()
        {
            ["file"] = "The file being reviewed",
            ["issue"] = "Description of the issue found",
            ["suggestion"] = "How to fix or improve the code",
            ["severity"] = "low, medium, high, or critical"
        },
        DefaultFields = new()
        {
            ["status"] = JsonSerializer.SerializeToElement("pending"),
            ["category"] = JsonSerializer.SerializeToElement("review")
        }
    };
    
    public static readonly MemoryTemplate PerformanceIssue = new()
    {
        Id = "performance-issue",
        Name = "Performance Issue",
        Description = "Track performance problems that need optimization",
        MemoryType = "TechnicalDebt",
        ContentTemplate = "Performance Issue: {title}\n\nLocation: {location}\n\nCurrent Behavior: {current}\n\nExpected Improvement: {expected}\n\nMeasurement: {measurement}",
        RequiredPlaceholders = new() { "title", "location", "current", "expected", "measurement" },
        PlaceholderDescriptions = new()
        {
            ["title"] = "Brief title of the performance issue",
            ["location"] = "Where the issue occurs (file, method, etc.)",
            ["current"] = "Current performance characteristics",
            ["expected"] = "Target performance after optimization",
            ["measurement"] = "How performance is measured (time, memory, etc.)"
        },
        DefaultFields = new()
        {
            ["status"] = JsonSerializer.SerializeToElement("pending"),
            ["priority"] = JsonSerializer.SerializeToElement("high"),
            ["category"] = JsonSerializer.SerializeToElement("performance")
        },
        DefaultTags = new() { "performance", "optimization" }
    };
    
    public static readonly MemoryTemplate SecurityAudit = new()
    {
        Id = "security-audit",
        Name = "Security Audit Finding",
        Description = "Document security vulnerabilities or concerns",
        MemoryType = "SecurityRule",
        ContentTemplate = "Security Finding: {title}\n\nVulnerability: {vulnerability}\n\nRisk Level: {risk}\n\nRecommendation: {recommendation}\n\nAffected Components: {components}",
        RequiredPlaceholders = new() { "title", "vulnerability", "risk", "recommendation", "components" },
        PlaceholderDescriptions = new()
        {
            ["title"] = "Brief title of the security issue",
            ["vulnerability"] = "Description of the vulnerability",
            ["risk"] = "Risk level: low, medium, high, critical",
            ["recommendation"] = "How to fix the vulnerability",
            ["components"] = "List of affected components or files"
        },
        DefaultFields = new()
        {
            ["status"] = JsonSerializer.SerializeToElement("pending"),
            ["priority"] = JsonSerializer.SerializeToElement("critical"),
            ["category"] = JsonSerializer.SerializeToElement("security")
        },
        DefaultTags = new() { "security", "vulnerability" }
    };
    
    public static readonly MemoryTemplate RefactoringOpportunity = new()
    {
        Id = "refactoring",
        Name = "Refactoring Opportunity",
        Description = "Identify code that could benefit from refactoring",
        MemoryType = "TechnicalDebt",
        ContentTemplate = "Refactoring Opportunity: {title}\n\nCurrent Code Location: {location}\n\nIssue: {issue}\n\nProposed Refactoring: {proposal}\n\nBenefits: {benefits}",
        RequiredPlaceholders = new() { "title", "location", "issue", "proposal", "benefits" },
        PlaceholderDescriptions = new()
        {
            ["title"] = "Brief title for the refactoring",
            ["location"] = "Where the code is located",
            ["issue"] = "What's wrong with the current code",
            ["proposal"] = "How to refactor the code",
            ["benefits"] = "Expected benefits of the refactoring"
        },
        DefaultFields = new()
        {
            ["status"] = JsonSerializer.SerializeToElement("pending"),
            ["priority"] = JsonSerializer.SerializeToElement("medium"),
            ["category"] = JsonSerializer.SerializeToElement("refactoring")
        },
        DefaultTags = new() { "refactoring", "code-quality" }
    };
    
    public static readonly MemoryTemplate ApiDesignDecision = new()
    {
        Id = "api-design",
        Name = "API Design Decision",
        Description = "Document API design choices and rationale",
        MemoryType = "ArchitecturalDecision",
        ContentTemplate = "API Design Decision: {endpoint}\n\nDesign Choice: {choice}\n\nAlternatives Considered: {alternatives}\n\nRationale: {rationale}\n\nImpact: {impact}",
        RequiredPlaceholders = new() { "endpoint", "choice", "alternatives", "rationale", "impact" },
        PlaceholderDescriptions = new()
        {
            ["endpoint"] = "API endpoint or component",
            ["choice"] = "The design decision made",
            ["alternatives"] = "Other options that were considered",
            ["rationale"] = "Why this choice was made",
            ["impact"] = "How this affects the system"
        },
        DefaultFields = new()
        {
            ["category"] = JsonSerializer.SerializeToElement("api-design")
        },
        DefaultTags = new() { "api", "design", "architecture" }
    };
    
    /// <summary>
    /// Get all built-in templates
    /// </summary>
    public static Dictionary<string, MemoryTemplate> GetAll()
    {
        return new Dictionary<string, MemoryTemplate>
        {
            [CodeReview.Id] = CodeReview,
            [PerformanceIssue.Id] = PerformanceIssue,
            [SecurityAudit.Id] = SecurityAudit,
            [RefactoringOpportunity.Id] = RefactoringOpportunity,
            [ApiDesignDecision.Id] = ApiDesignDecision
        };
    }
}