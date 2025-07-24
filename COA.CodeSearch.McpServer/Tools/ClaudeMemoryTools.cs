using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Essential MCP tools for Claude's persistent memory system
/// Only includes tools that don't have flexible memory equivalents
/// </summary>
public class ClaudeMemoryTools : ITool
{
    public string ToolName => "claude_memory";
    public string Description => "Essential memory system operations";
    public ToolCategory Category => ToolCategory.Memory;
    private readonly ClaudeMemoryService _memoryService;
    private readonly MemoryBackupService _backupService;
    private readonly ILogger<ClaudeMemoryTools> _logger;

    public ClaudeMemoryTools(ILogger<ClaudeMemoryTools> logger, ClaudeMemoryService memoryService, MemoryBackupService backupService)
    {
        _logger = logger;
        _memoryService = memoryService;
        _backupService = backupService;
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
            
            var result = await _backupService.BackupMemoriesAsync(scopes);
            
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
            
            var result = await _backupService.RestoreMemoriesAsync(scopes);
            
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