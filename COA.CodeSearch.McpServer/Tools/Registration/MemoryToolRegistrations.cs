using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using COA.Directus.Mcp.Protocol;
using Microsoft.Extensions.DependencyInjection;
using static COA.CodeSearch.McpServer.Tools.Registration.ToolRegistrationHelper;

namespace COA.CodeSearch.McpServer.Tools.Registration;

/// <summary>
/// Registers Claude Memory System tools with the MCP server
/// </summary>
public static class MemoryToolRegistrations
{
    public static void RegisterMemoryTools(ToolRegistry registry, IServiceProvider serviceProvider)
    {
        var memoryTools = serviceProvider.GetRequiredService<ClaudeMemoryTools>();
        
        RegisterRememberDecision(registry, memoryTools);
        RegisterRememberPattern(registry, memoryTools);
        RegisterRememberSecurityRule(registry, memoryTools);
        RegisterRememberSession(registry, memoryTools);
        RegisterRecallContext(registry, memoryTools);
        RegisterListMemoriesByType(registry, memoryTools);
        RegisterBackupMemories(registry, memoryTools);
        RegisterRestoreMemories(registry, memoryTools);
        
        // Migration tool
        RegisterMigrateMemories(registry, serviceProvider.GetRequiredService<MigrateMemoriesTool>());
        
        // Diagnostic tool
        RegisterDiagnoseMemoryIndex(registry, serviceProvider.GetRequiredService<DiagnoseMemoryIndexTool>());
    }
    
    private static void RegisterRememberDecision(ToolRegistry registry, ClaudeMemoryTools tool)
    {
        registry.RegisterTool<RememberDecisionParams>(
            name: "remember_decision",
            description: "Document a key architectural decision that affects the codebase - persists across sessions and shares with team via version control. Use when making technology choices, design patterns, or structural changes.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    decision = new { type = "string", description = "The architectural decision - example: 'Use Repository pattern for data access layer'" },
                    reasoning = new { type = "string", description = "Why this decision was made - example: 'Provides testability and abstracts database implementation details'" },
                    affectedFiles = new { type = "array", items = new { type = "string" }, description = "Files that are affected by or implement this decision" },
                    tags = new { type = "array", items = new { type = "string" }, description = "Tags for categorization - examples: ['architecture', 'data-access'], ['security', 'authentication'], ['performance']" }
                },
                required = new[] { "decision", "reasoning" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var result = await tool.RememberDecision(
                    ValidateRequired(parameters.Decision, "decision"),
                    ValidateRequired(parameters.Reasoning, "reasoning"),
                    parameters.AffectedFiles,
                    parameters.Tags);
                    
                return CreateSuccessResult(result);
            }
        );
    }
    
    private static void RegisterRememberPattern(ToolRegistry registry, ClaudeMemoryTools tool)
    {
        registry.RegisterTool<RememberPatternParams>(
            name: "remember_pattern",
            description: "Save a reusable code pattern or best practice discovered in the codebase - helps maintain consistency across the team. Persists and shares via version control.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    pattern = new { type = "string", description = "The code pattern - example: 'Async repository methods with cancellation token'" },
                    location = new { type = "string", description = "Where to find it - example: 'Services/UserRepository.cs:45-89' or 'All repository classes in Data layer'" },
                    usage = new { type = "string", description = "When to use - example: 'Use for all data access methods to ensure proper cancellation and error handling'" },
                    relatedFiles = new { type = "array", items = new { type = "string" }, description = "Files that demonstrate or contain this pattern" },
                    tags = new { type = "array", items = new { type = "string" }, description = "Optional tags for categorization" }
                },
                required = new[] { "pattern", "location", "usage" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var result = await tool.RememberPattern(
                    ValidateRequired(parameters.Pattern, "pattern"),
                    ValidateRequired(parameters.Location, "location"),
                    ValidateRequired(parameters.Usage, "usage"),
                    parameters.RelatedFiles,
                    parameters.Tags);
                    
                return CreateSuccessResult(result);
            }
        );
    }
    
    private static void RegisterRememberSecurityRule(ToolRegistry registry, ClaudeMemoryTools tool)
    {
        registry.RegisterTool<RememberSecurityRuleParams>(
            name: "remember_security_rule",
            description: "Record security requirements or compliance rules that must be followed - critical for maintaining security posture. Persists and shares with team via version control.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    rule = new { type = "string", description = "The security rule or compliance requirement" },
                    reasoning = new { type = "string", description = "Why this rule exists and what it protects against" },
                    affectedFiles = new { type = "array", items = new { type = "string" }, description = "Files that implement or are affected by this rule" },
                    compliance = new { type = "string", description = "Compliance framework - examples: 'HIPAA', 'SOX', 'GDPR', 'PCI-DSS', 'ISO-27001'" },
                },
                required = new[] { "rule", "reasoning" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var result = await tool.RememberSecurityRule(
                    ValidateRequired(parameters.Rule, "rule"),
                    ValidateRequired(parameters.Reasoning, "reasoning"),
                    parameters.AffectedFiles,
                    parameters.Compliance);
                    
                return CreateSuccessResult(result);
            }
        );
    }
    
    private static void RegisterRememberSession(ToolRegistry registry, ClaudeMemoryTools tool)
    {
        registry.RegisterTool<RememberSessionParams>(
            name: "remember_session",
            description: "Save a personal work session summary - track what you accomplished and where you left off. Local only, not shared with team. Useful for resuming work later.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    summary = new { type = "string", description = "Summary of what was accomplished in this session" },
                    filesWorkedOn = new { type = "array", items = new { type = "string" }, description = "Files that were worked on or modified" },
                    tags = new { type = "array", items = new { type = "string" }, description = "Optional tags for categorization" }
                },
                required = new[] { "summary" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var result = await tool.RememberSession(
                    ValidateRequired(parameters.Summary, "summary"),
                    parameters.FilesWorkedOn,
                    parameters.Tags);
                    
                return CreateSuccessResult(result);
            }
        );
    }
    
    private static void RegisterRecallContext(ToolRegistry registry, ClaudeMemoryTools tool)
    {
        registry.RegisterTool<RecallContextParams>(
            name: "recall_context",
            description: "Load previous architectural decisions, patterns, and context relevant to your current task - essential for maintaining consistency. Use at session start or when switching tasks.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    query = new { type = "string", description = "What you're currently working on or want to learn about" },
                    scopeFilter = new { type = "string", description = "Filter by type: ArchitecturalDecision, CodePattern, SecurityRule, ProjectInsight, WorkSession, LocalInsight" },
                    maxResults = new { type = "integer", description = "Maximum number of results to return (default: 10)", @default = 10 }
                },
                required = new[] { "query" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                MemoryScope? scopeFilter = null;
                if (!string.IsNullOrEmpty(parameters.ScopeFilter))
                {
                    if (System.Enum.TryParse<MemoryScope>(parameters.ScopeFilter, out var scope))
                    {
                        scopeFilter = scope;
                    }
                }
                
                var result = await tool.RecallContext(
                    ValidateRequired(parameters.Query, "query"),
                    scopeFilter,
                    parameters.MaxResults ?? 10);
                    
                return CreateSuccessResult(result);
            }
        );
    }
    
    private static void RegisterListMemoriesByType(ToolRegistry registry, ClaudeMemoryTools tool)
    {
        registry.RegisterTool<ListMemoriesByTypeParams>(
            name: "list_memories_by_type",
            description: "Browse stored memories by category - useful for reviewing all architectural decisions, patterns, or security rules. Shows both team-shared and local memories.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    scope = new { type = "string", description = "Type of memories to list (ArchitecturalDecision, CodePattern, SecurityRule, ProjectInsight, WorkSession, etc.)" },
                    maxResults = new { type = "integer", description = "Maximum number of results (default: 20)", @default = 20 }
                },
                required = new[] { "scope" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                if (!System.Enum.TryParse<MemoryScope>(parameters.Scope, out var scope))
                {
                    throw new InvalidParametersException($"Invalid scope: {parameters.Scope}. Valid values are: {string.Join(", ", System.Enum.GetNames<MemoryScope>())}");
                }
                
                var result = await tool.ListMemoriesByType(scope, parameters.MaxResults ?? 20);
                    
                return CreateSuccessResult(result);
            }
        );
    }
    
    private static void RegisterBackupMemories(ToolRegistry registry, ClaudeMemoryTools tool)
    {
        registry.RegisterTool<BackupMemoriesParams>(
            name: "backup_memories_to_sqlite",
            description: "Backup memories from Lucene index to SQLite database for version control and sharing. By default backs up only project-level memories (architectural decisions, patterns, security rules) which can be shared with the team.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    scopes = new { type = "array", items = new { type = "string" }, description = "Memory types to backup. Defaults to project memories: ArchitecturalDecision, CodePattern, SecurityRule, ProjectInsight" },
                    includeLocal = new { type = "boolean", description = "Include local developer memories (WorkSession, LocalInsight). Default: false", @default = false }
                },
                required = new string[] { }
            },
            handler: async (parameters, ct) =>
            {
                var result = await tool.BackupMemories(
                    parameters?.Scopes,
                    parameters?.IncludeLocal ?? false);
                    
                return CreateSuccessResult(result);
            }
        );
    }
    
    private static void RegisterRestoreMemories(ToolRegistry registry, ClaudeMemoryTools tool)
    {
        registry.RegisterTool<RestoreMemoriesParams>(
            name: "restore_memories_from_sqlite",
            description: "Restore memories from SQLite database backup to Lucene index. Useful when setting up on a new machine or after losing the Lucene index. By default restores only project-level memories.",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    scopes = new { type = "array", items = new { type = "string" }, description = "Memory types to restore. Defaults to project memories: ArchitecturalDecision, CodePattern, SecurityRule, ProjectInsight" },
                    includeLocal = new { type = "boolean", description = "Include local developer memories (WorkSession, LocalInsight). Default: false", @default = false }
                },
                required = new string[] { }
            },
            handler: async (parameters, ct) =>
            {
                var result = await tool.RestoreMemories(
                    parameters?.Scopes,
                    parameters?.IncludeLocal ?? false);
                    
                return CreateSuccessResult(result);
            }
        );
    }
    
    private static void RegisterMigrateMemories(ToolRegistry registry, MigrateMemoriesTool tool)
    {
        registry.RegisterTool<EmptyParams>(
            name: "migrate_memories_add_ticks",
            description: "Migrate existing memories to add timestamp_ticks field. This is needed for memories created before the backup enhancement. Run this once to fix old memories that aren't being backed up.",
            inputSchema: new
            {
                type = "object",
                properties = new { },
                required = new string[] { }
            },
            handler: async (parameters, ct) =>
            {
                var result = await tool.MigrateAsync();
                
                return CreateSuccessResult(new
                {
                    success = true,
                    message = $"Migration completed: {result.MigratedMemories} memories migrated, {result.SkippedMemories} already had timestamp_ticks",
                    totalMemories = result.TotalMemories,
                    migratedMemories = result.MigratedMemories,
                    skippedMemories = result.SkippedMemories,
                    migratedScopes = result.MigratedScopes,
                    errors = result.Errors
                });
            }
        );
    }
    
    private static void RegisterDiagnoseMemoryIndex(ToolRegistry registry, DiagnoseMemoryIndexTool tool)
    {
        registry.RegisterTool<DiagnoseMemoryIndexParams>(
            name: "diagnose_memory_index",
            description: "Diagnostic tool to inspect the memory index and see what documents are stored",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    workspace = new { type = "string", description = "Workspace to diagnose (default: project-memory)", @default = "project-memory" }
                },
                required = new string[] { }
            },
            handler: async (parameters, ct) =>
            {
                var result = await tool.DiagnoseMemoryIndex(parameters?.Workspace ?? "project-memory");
                return CreateSuccessResult(result);
            }
        );
    }
    
    // Parameter classes
    private class RememberDecisionParams
    {
        public string? Decision { get; set; }
        public string? Reasoning { get; set; }
        public string[]? AffectedFiles { get; set; }
        public string[]? Tags { get; set; }
    }
    
    private class RememberPatternParams
    {
        public string? Pattern { get; set; }
        public string? Location { get; set; }
        public string? Usage { get; set; }
        public string[]? RelatedFiles { get; set; }
        public string[]? Tags { get; set; }
    }
    
    private class RememberSecurityRuleParams
    {
        public string? Rule { get; set; }
        public string? Reasoning { get; set; }
        public string[]? AffectedFiles { get; set; }
        public string? Compliance { get; set; }
    }
    
    private class RememberSessionParams
    {
        public string? Summary { get; set; }
        public string[]? FilesWorkedOn { get; set; }
        public string[]? Tags { get; set; }
    }
    
    private class RecallContextParams
    {
        public string? Query { get; set; }
        public string? ScopeFilter { get; set; }
        public int? MaxResults { get; set; }
    }
    
    private class ListMemoriesByTypeParams
    {
        public string? Scope { get; set; }
        public int? MaxResults { get; set; }
    }
    
    private class BackupMemoriesParams
    {
        public string[]? Scopes { get; set; }
        public bool? IncludeLocal { get; set; }
    }
    
    private class RestoreMemoriesParams
    {
        public string[]? Scopes { get; set; }
        public bool? IncludeLocal { get; set; }
    }
    
    private class DiagnoseMemoryIndexParams
    {
        public string? Workspace { get; set; }
    }
    
    private class EmptyParams
    {
    }
}