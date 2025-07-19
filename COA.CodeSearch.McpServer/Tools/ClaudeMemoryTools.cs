using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// MCP tools for Claude's persistent memory system
/// Enables storing and retrieving architectural decisions, code patterns, and development context
/// </summary>
public class ClaudeMemoryTools
{
    private readonly ClaudeMemoryService _memoryService;
    private readonly MemoryBackupService _backupService;
    private readonly ILogger<ClaudeMemoryTools> _logger;

    public ClaudeMemoryTools(ILogger<ClaudeMemoryTools> logger, ClaudeMemoryService memoryService, MemoryBackupService backupService)
    {
        _logger = logger;
        _memoryService = memoryService;
        _backupService = backupService;
    }

    public async Task<object> RememberDecision(
        string decision,
        string reasoning,
        string[]? affectedFiles = null,
        string[]? tags = null)
    {
        try
        {
            var success = await _memoryService.StoreArchitecturalDecisionAsync(
                decision, 
                reasoning, 
                affectedFiles ?? Array.Empty<string>(), 
                tags ?? Array.Empty<string>()
            );

            if (success)
            {
                _logger.LogInformation("Stored architectural decision: {Decision}", decision);
                return new { success = true, message = $"✅ Architectural decision stored: {decision}\n\nThis decision is now part of the project's permanent knowledge base and will be available to all team members." };
            }
            else
            {
                return new { success = false, error = "Failed to store architectural decision" };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error storing architectural decision");
            return new { success = false, error = $"Error storing decision: {ex.Message}" };
        }
    }

    public async Task<object> RememberPattern(
        string pattern,
        string location,
        string usage,
        string[]? relatedFiles = null,
        string[]? tags = null)
    {
        try
        {
            var success = await _memoryService.StoreCodePatternAsync(pattern, location, usage, relatedFiles);

            if (success)
            {
                _logger.LogInformation("Stored code pattern: {Pattern}", pattern);
                return new 
                { 
                    success = true, 
                    message = $"✅ Code pattern stored: {pattern}\n\nLocation: {location}\nUsage: {usage}\n\nThis pattern is now available for future reference and consistency." 
                };
            }
            else
            {
                return new 
                { 
                    success = false, 
                    error = "Failed to store code pattern" 
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error storing code pattern");
            return new 
            { 
                success = false, 
                error = $"Error storing pattern: {ex.Message}" 
            };
        }
    }

    public async Task<object> RememberSecurityRule(
        string rule,
        string reasoning,
        string[]? affectedFiles = null,
        string? compliance = null)
    {
        try
        {
            var tags = new List<string> { "security" };
            if (!string.IsNullOrEmpty(compliance))
                tags.Add(compliance.ToLowerInvariant());

            var memory = new MemoryEntry
            {
                Content = $"SECURITY RULE: {rule}\n\nREASONING: {reasoning}" + 
                         (string.IsNullOrEmpty(compliance) ? "" : $"\n\nCOMPLIANCE: {compliance}"),
                Scope = MemoryScope.SecurityRule,
                FilesInvolved = affectedFiles ?? Array.Empty<string>(),
                Keywords = ClaudeMemoryService.ExtractKeywords($"{rule} {reasoning} {compliance}"),
                Reasoning = reasoning,
                Tags = tags.ToArray(),
                Category = "security"
            };

            var success = await _memoryService.StoreMemoryAsync(memory);

            if (success)
            {
                _logger.LogInformation("Stored security rule: {Rule}", rule);
                return new 
                { 
                    success = true, 
                    message = $"🔒 Security rule stored: {rule}\n\nCompliance: {compliance ?? "General"}\n\nThis rule is now part of the project's security knowledge base." 
                };
            }
            else
            {
                return new 
                { 
                    success = false, 
                    error = "Failed to store security rule" 
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error storing security rule");
            return new 
            { 
                success = false, 
                error = $"Error storing security rule: {ex.Message}" 
            };
        }
    }

    public async Task<object> RememberSession(
        string summary,
        string[]? filesWorkedOn = null,
        string[]? tags = null)
    {
        try
        {
            var success = await _memoryService.StoreWorkSessionAsync(summary, filesWorkedOn);

            if (success)
            {
                _logger.LogInformation("Stored work session: {Summary}", summary);
                return new 
                { 
                    success = true, 
                    message = $"📝 Work session recorded: {summary}\n\nThis session summary is stored locally for your personal reference." 
                };
            }
            else
            {
                return new 
                { 
                    success = false, 
                    error = "Failed to store work session" 
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error storing work session");
            return new 
            { 
                success = false, 
                error = $"Error storing session: {ex.Message}" 
            };
        }
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
                    response += $"{FormatMemoryContent(memory)}\n";
                    
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

    public async Task<object> ListMemoriesByType(
        MemoryScope scope,
        int maxResults = 20)
    {
        try
        {
            var memories = await _memoryService.GetMemoriesByScopeAsync(scope, maxResults);

            if (!memories.Any())
            {
                return new 
                { 
                    success = true, 
                    message = $"📋 No {scope} memories found.\n\nStart storing some memories to build up your knowledge base!" 
                };
            }

            var response = $"📋 **{FormatScopeHeader(scope)} ({memories.Count} total)**\n\n";

            foreach (var memory in memories)
            {
                response += $"• **{FormatMemoryTitle(memory)}**\n";
                response += $"  {FormatMemoryContent(memory, maxLength: 100)}\n";
                response += $"  *{memory.Timestamp:MM/dd/yyyy}*\n\n";
            }

            return new 
            { 
                success = true, 
                message = response 
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing memories by type");
            return new 
            { 
                success = false, 
                error = $"Error listing memories: {ex.Message}" 
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