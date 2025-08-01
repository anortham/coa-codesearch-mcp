using COA.CodeSearch.McpServer.Attributes;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using COA.Mcp.Protocol;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Essential MCP tools for Claude's persistent memory system
/// Only includes tools that don't have flexible memory equivalents
/// </summary>
[McpServerToolType]
public class ClaudeMemoryTools : ITool
{
    public string ToolName => "claude_memory";
    public string Description => "Essential memory system operations";
    public ToolCategory Category => ToolCategory.Memory;
    private readonly ClaudeMemoryService _memoryService;
    private readonly JsonMemoryBackupService _backupService;
    private readonly ILogger<ClaudeMemoryTools> _logger;

    public ClaudeMemoryTools(ILogger<ClaudeMemoryTools> logger, ClaudeMemoryService memoryService, JsonMemoryBackupService backupService)
    {
        _logger = logger;
        _memoryService = memoryService;
        _backupService = backupService;
    }

    [McpServerTool(Name = "recall_context")]
    [Description("Load relevant project knowledge from previous sessions including architectural decisions, code patterns, and insights. Searches stored memories based on your current work context. Recommended at session start to restore context from past work.")]
    public async Task<object> RecallContext(RecallContextParams parameters)
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
        
        return await RecallContext(
            ValidateRequired(parameters.Query, "query"),
            scopeFilter,
            parameters.MaxResults ?? 10);
    }
    
    private static string ValidateRequired(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidParametersException($"{paramName} is required");
        return value;
    }

    public async Task<object> RecallContext(
        string query,
        MemoryScope? scopeFilter = null,
        int maxResults = 10)
    {
        try
        {
            var searchResult = await _memoryService.SearchMemoriesAsync(query, scopeFilter, maxResults);

            if (!searchResult.Memories.Any())
            {
                return new 
                { 
                    success = true, 
                    message = $"🔍 No memories found for: '{query}'\n\nTry broader search terms or check if you've stored relevant information yet." 
                };
            }

            var response = $"🧠 **RECALLED MEMORIES FOR: {query}**\n\n";
            response += $"Found {searchResult.TotalFound} relevant memories:\n\n";

            // Group by scope for better organization
            var groupedMemories = searchResult.Memories
                .GroupBy(m => m.Scope)
                .OrderBy(g => g.Key);

            foreach (var group in groupedMemories)
            {
                response += $"## {FormatScopeHeader(group.Key)}\n\n";

                foreach (var memory in group.Take(3)) // Limit per category
                {
                    response += $"**{FormatMemoryTitle(memory)}**\n";
                    response += $"{FormatMemoryContent(memory, maxLength: 500)}\n";
                    
                    if (memory.FilesInvolved.Any())
                    {
                        response += $"*Files: {string.Join(", ", memory.FilesInvolved.Take(3))}*\n";
                    }
                    
                    response += $"*{memory.Timestamp:MM/dd/yyyy}*\n\n";
                }

                if (group.Count() > 3)
                {
                    response += $"*... and {group.Count() - 3} more {group.Key} memories*\n\n";
                }
            }

            if (searchResult.SuggestedQueries.Any())
            {
                response += $"💡 **Suggested related searches:** {string.Join(", ", searchResult.SuggestedQueries)}\n";
            }

            _logger.LogInformation("Recalled {Count} memories for query: {Query}", searchResult.TotalFound, query);

            return new 
            { 
                success = true, 
                message = response 
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recalling context");
            return new 
            { 
                success = false, 
                error = $"Error recalling context: {ex.Message}" 
            };
        }
    }
    
    [McpServerTool(Name = "backup_memories")]
    [Description("Export memories to JSON file for version control and team sharing. Creates timestamped, human-readable backups. Use cases: commit to git for team collaboration, backup before major changes, transfer knowledge to new machines. By default backs up only project memories (ArchitecturalDecision, CodePattern, SecurityRule, ProjectInsight). Use includeLocal=true to include personal memories.")]
    public async Task<object> BackupMemories(BackupMemoriesParams parameters)
    {
        return await BackupMemories(
            parameters?.Scopes,
            parameters?.IncludeLocal ?? false);
    }
    
    public async Task<object> BackupMemories(
        string[]? scopes = null,
        bool includeLocal = false)  // Changed default to false - only backup project memories
    {
        try
        {
            // Default scopes if not specified (project-level memories only)
            scopes ??= new[] { "ArchitecturalDecision", "CodePattern", "SecurityRule", "ProjectInsight" };
            
            if (includeLocal)
            {
                // Only include local memories if explicitly requested
                scopes = scopes.Concat(new[] { "WorkSession", "LocalInsight" }).ToArray();
            }
            
            var result = await _backupService.BackupMemoriesAsync(scopes, includeLocal);
            
            if (result.Success)
            {
                _logger.LogInformation("Memory backup completed: {Count} documents backed up to {Path}", 
                    result.DocumentsBackedUp, result.BackupPath);
                
                return new
                {
                    success = true,
                    message = $"✅ Backed up {result.DocumentsBackedUp} memories to {result.BackupPath}",
                    documentsBackedUp = result.DocumentsBackedUp,
                    backupPath = result.BackupPath,
                    backupTime = result.BackupTime
                };
            }
            else
            {
                return new
                {
                    success = false,
                    message = $"❌ Backup failed: {result.Error}"
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to backup memories");
            return new
            {
                success = false,
                message = $"❌ Backup failed: {ex.Message}"
            };
        }
    }
    
    [McpServerTool(Name = "restore_memories")]
    [Description("Restore memories from JSON backup file. Automatically finds most recent backup if no file specified. Useful when setting up on a new machine or after losing the Lucene index. By default restores only project-level memories.")]
    public async Task<object> RestoreMemories(RestoreMemoriesParams parameters)
    {
        return await RestoreMemories(
            parameters?.Scopes,
            parameters?.IncludeLocal ?? false);
    }
    
    public async Task<object> RestoreMemories(
        string[]? scopes = null,
        bool includeLocal = false)  // Changed default to false - only restore project memories
    {
        try
        {
            // Default scopes if not specified (project-level memories only)
            scopes ??= new[] { "ArchitecturalDecision", "CodePattern", "SecurityRule", "ProjectInsight" };
            
            if (includeLocal)
            {
                // Only include local memories if explicitly requested
                scopes = scopes.Concat(new[] { "WorkSession", "LocalInsight" }).ToArray();
            }
            
            var result = await _backupService.RestoreMemoriesAsync(null, scopes, includeLocal);
            
            if (result.Success)
            {
                _logger.LogInformation("Memory restore completed: {Count} documents restored", 
                    result.DocumentsRestored);
                
                return new
                {
                    success = true,
                    message = $"✅ Restored {result.DocumentsRestored} memories from backup",
                    documentsRestored = result.DocumentsRestored,
                    restoreTime = result.RestoreTime
                };
            }
            else
            {
                return new
                {
                    success = false,
                    message = $"❌ Restore failed: {result.Error}"
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore memories");
            return new
            {
                success = false,
                message = $"❌ Restore failed: {ex.Message}"
            };
        }
    }

    private static string FormatScopeHeader(MemoryScope scope)
    {
        return scope switch
        {
            MemoryScope.ArchitecturalDecision => "🏗️ Architectural Decisions",
            MemoryScope.CodePattern => "🔧 Code Patterns",
            MemoryScope.SecurityRule => "🔒 Security Rules",
            MemoryScope.ProjectInsight => "💡 Project Insights",
            MemoryScope.WorkSession => "📝 Work Sessions",
            MemoryScope.ConversationSummary => "💬 Conversation Summaries",
            MemoryScope.PersonalContext => "👤 Personal Context",
            MemoryScope.TemporaryNote => "📌 Temporary Notes",
            _ => scope.ToString()
        };
    }

    private static string FormatMemoryTitle(MemoryEntry memory)
    {
        var lines = memory.Content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var firstLine = lines.FirstOrDefault() ?? "Untitled Memory";
        
        // Remove common prefixes for cleaner display
        firstLine = firstLine.Replace("DECISION: ", "")
                            .Replace("PATTERN: ", "")
                            .Replace("SECURITY RULE: ", "")
                            .Replace("WORK SESSION: ", "");
        
        return firstLine.Length > 60 ? firstLine[..57] + "..." : firstLine;
    }

    private static string FormatMemoryContent(MemoryEntry memory, int maxLength = 200)
    {
        var content = memory.Content;
        
        // Extract the main content, skipping prefixes
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length > 1)
        {
            content = string.Join(" ", lines.Skip(1));
        }
        
        return content.Length > maxLength ? content[..maxLength] + "..." : content;
    }
}

/// <summary>
/// Extension methods for ClaudeMemoryService to expose keyword extraction
/// </summary>
public static class ClaudeMemoryServiceExtensions
{
    public static string[] ExtractKeywords(string text)
    {
        // Simple keyword extraction - matches the private method in ClaudeMemoryService
        return text.ToLowerInvariant()
            .Split(new[] { ' ', '\n', '\r', '\t', '.', ',', ';', ':', '!', '?' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word.Length > 3)
            .Distinct()
            .Take(20)
            .ToArray();
    }
}

/// <summary>
/// Parameters for RecallContext
/// </summary>
public class RecallContextParams
{
    [Description("What you're currently working on or want to learn about")]
    public string? Query { get; set; }
    
    [Description("Filter by type: ArchitecturalDecision, CodePattern, SecurityRule, ProjectInsight, WorkSession, LocalInsight")]
    public string? ScopeFilter { get; set; }
    
    [Description("Maximum number of results to return (default: 10)")]
    public int? MaxResults { get; set; }
}

/// <summary>
/// Parameters for BackupMemories
/// </summary>
public class BackupMemoriesParams
{
    [Description("Memory types to backup. Defaults to project memories: ArchitecturalDecision, CodePattern, SecurityRule, ProjectInsight")]
    public string[]? Scopes { get; set; }
    
    [Description("Include local developer memories (WorkSession, LocalInsight). Default: false")]
    public bool? IncludeLocal { get; set; }
}

/// <summary>
/// Parameters for RestoreMemories
/// </summary>
public class RestoreMemoriesParams
{
    [Description("Memory types to restore. Defaults to project memories: ArchitecturalDecision, CodePattern, SecurityRule, ProjectInsight")]
    public string[]? Scopes { get; set; }
    
    [Description("Include local developer memories (WorkSession, LocalInsight). Default: false")]
    public bool? IncludeLocal { get; set; }
}