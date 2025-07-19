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
        var initHooksTool = serviceProvider.GetRequiredService<InitializeMemoryHooksTool>();
        
        RegisterRememberDecision(registry, memoryTools);
        RegisterRememberPattern(registry, memoryTools);
        RegisterRememberSecurityRule(registry, memoryTools);
        RegisterRememberSession(registry, memoryTools);
        RegisterRecallContext(registry, memoryTools);
        RegisterListMemoriesByType(registry, memoryTools);
        RegisterBackupMemories(registry, memoryTools);
        RegisterRestoreMemories(registry, memoryTools);
        RegisterInitMemoryHooks(registry, initHooksTool);
        RegisterTestMemoryHooks(registry, initHooksTool);
    }
    
    private static void RegisterRememberDecision(ToolRegistry registry, ClaudeMemoryTools tool)
    {
        registry.RegisterTool<RememberDecisionParams>(
            name: "remember_decision",
            description: "Store an architectural decision with reasoning for the entire team (version controlled)",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    decision = new { type = "string", description = "The architectural decision that was made" },
                    reasoning = new { type = "string", description = "Why this decision was made and what problems it solves" },
                    affectedFiles = new { type = "array", items = new { type = "string" }, description = "Files that are affected by or implement this decision" },
                    tags = new { type = "array", items = new { type = "string" }, description = "Optional tags for categorization (e.g., 'security', 'performance', 'hipaa')" }
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
            description: "Store a reusable code pattern with location and usage guidance (version controlled)",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    pattern = new { type = "string", description = "Description of the code pattern" },
                    location = new { type = "string", description = "Where this pattern is implemented or can be found" },
                    usage = new { type = "string", description = "When and how to use this pattern" },
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
            description: "Store a security rule or compliance requirement (version controlled)",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    rule = new { type = "string", description = "The security rule or compliance requirement" },
                    reasoning = new { type = "string", description = "Why this rule exists and what it protects against" },
                    affectedFiles = new { type = "array", items = new { type = "string" }, description = "Files that implement or are affected by this rule" },
                    compliance = new { type = "string", description = "Compliance framework (e.g., 'HIPAA', 'SOX', 'GDPR')" }
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
            description: "Store a summary of your current work session (local only)",
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
            description: "Search memories to recall relevant context for your current work",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    query = new { type = "string", description = "What you're currently working on or want to learn about" },
                    scopeFilter = new { type = "string", description = "Optional filter by memory type (ArchitecturalDecision, CodePattern, SecurityRule, etc.)" },
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
            description: "List all memories of a specific type",
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
    
    private static void RegisterInitMemoryHooks(ToolRegistry registry, InitializeMemoryHooksTool tool)
    {
        registry.RegisterTool<InitMemoryHooksParams>(
            name: "init_memory_hooks",
            description: "Initialize Claude memory system hooks for automatic context loading and session tracking",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    projectRoot = new { type = "string", description = "Project root directory (defaults to current directory)" }
                },
                required = new string[] { }
            },
            handler: async (parameters, ct) =>
            {
                var result = await tool.InitializeMemoryHooks(parameters?.ProjectRoot);
                return CreateSuccessResult(result);
            }
        );
    }
    
    private static void RegisterTestMemoryHooks(ToolRegistry registry, InitializeMemoryHooksTool tool)
    {
        registry.RegisterTool<TestMemoryHooksParams>(
            name: "test_memory_hooks",
            description: "Test that memory hooks are working correctly",
            inputSchema: new
            {
                type = "object",
                properties = new
                {
                    hookType = new { type = "string", description = "Which hook to test: tool-call, file-edit, or session-end" }
                },
                required = new[] { "hookType" }
            },
            handler: async (parameters, ct) =>
            {
                if (parameters == null) throw new InvalidParametersException("Parameters are required");
                
                var result = await tool.TestMemoryHooks(
                    ValidateRequired(parameters.HookType, "hookType"));
                    
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
    
    private class InitMemoryHooksParams
    {
        public string? ProjectRoot { get; set; }
    }
    
    private class TestMemoryHooksParams
    {
        public string? HookType { get; set; }
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
}