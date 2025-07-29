using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Tools;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Unified memory service that provides natural language interface to all memory operations
/// Routes commands to appropriate existing tools based on detected intent
/// </summary>
public class UnifiedMemoryService
{
    private readonly FlexibleMemoryService _memoryService;
    private readonly ILogger<UnifiedMemoryService> _logger;

    // Tool references for routing (injected as needed)
    private readonly FlexibleMemoryTools? _memoryTools;
    private readonly ChecklistTools? _checklistTools;
    private readonly MemoryLinkingTools? _linkingTools;
    private readonly FastFileSearchToolV2? _fileSearchTool;
    private readonly FastTextSearchToolV2? _textSearchTool;
    private readonly MemoryGraphNavigatorTool? _graphNavigatorTool;

    public UnifiedMemoryService(
        FlexibleMemoryService memoryService,
        ILogger<UnifiedMemoryService> logger,
        FlexibleMemoryTools? memoryTools = null,
        ChecklistTools? checklistTools = null,
        MemoryLinkingTools? linkingTools = null,
        FastFileSearchToolV2? fileSearchTool = null,
        FastTextSearchToolV2? textSearchTool = null,
        MemoryGraphNavigatorTool? graphNavigatorTool = null)
    {
        _memoryService = memoryService;
        _logger = logger;
        _memoryTools = memoryTools;
        _checklistTools = checklistTools;
        _linkingTools = linkingTools;
        _fileSearchTool = fileSearchTool;
        _textSearchTool = textSearchTool;
        _graphNavigatorTool = graphNavigatorTool;
    }

    /// <summary>
    /// Execute a unified memory command by detecting intent and routing to appropriate tools
    /// </summary>
    public async Task<UnifiedMemoryResult> ExecuteAsync(
        UnifiedMemoryCommand command,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Detect intent if not specified
            if (command.Intent == MemoryIntent.Auto)
            {
                var detectionResult = DetectIntent(command);
                command.Intent = detectionResult.Intent;
                command.Context.Confidence = detectionResult.Confidence;
            }

            _logger.LogInformation("Executing unified memory command with intent: {Intent}, confidence: {Confidence}", 
                command.Intent, command.Context.Confidence);

            // Route to appropriate handler
            var result = command.Intent switch
            {
                MemoryIntent.Save => await HandleSaveAsync(command, cancellationToken),
                MemoryIntent.Find => await HandleFindAsync(command, cancellationToken),
                MemoryIntent.Connect => await HandleConnectAsync(command, cancellationToken),
                MemoryIntent.Explore => await HandleExploreAsync(command, cancellationToken),
                MemoryIntent.Suggest => await HandleSuggestAsync(command, cancellationToken),
                MemoryIntent.Manage => await HandleManageAsync(command, cancellationToken),
                _ => new UnifiedMemoryResult
                {
                    Success = false,
                    Message = $"Unsupported intent: {command.Intent}",
                    ExecutedIntent = command.Intent,
                    IntentConfidence = command.Context.Confidence
                }
            };

            result.ExecutedIntent = command.Intent;
            result.IntentConfidence = command.Context.Confidence;

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing unified memory command: {Content}", command.Content);
            return new UnifiedMemoryResult
            {
                Success = false,
                Message = $"Error executing command: {ex.Message}",
                ExecutedIntent = command.Intent,
                IntentConfidence = command.Context.Confidence
            };
        }
    }

    /// <summary>
    /// Detect the intent of a command using keyword patterns
    /// </summary>
    private (MemoryIntent Intent, float Confidence) DetectIntent(UnifiedMemoryCommand command)
    {
        var content = command.Content?.ToLowerInvariant() ?? "";
        var confidence = 0.0f;

        // Strong indicators for SAVE intent
        if (ContainsAny(content, "remember", "store", "save", "create", "note", "record"))
        {
            confidence += 0.6f; // Increased from 0.4f to ensure SAVE intent is detected
        }
        if (ContainsAny(content, "technical debt", "architectural decision", "bug", "issue", "todo"))
        {
            confidence += 0.3f;
            return (MemoryIntent.Save, Math.Min(confidence, 1.0f));
        }
        if (ContainsAny(content, "checklist", "task list", "plan", "items"))
        {
            return (MemoryIntent.Save, 0.9f); // High confidence for checklist creation
        }
        
        // Return early for SAVE intent if confidence is sufficient
        if (confidence >= 0.5f)
        {
            return (MemoryIntent.Save, Math.Min(confidence, 1.0f));
        }

        // Strong indicators for FIND intent
        if (ContainsAny(content, "find", "search", "look for", "get", "show", "list"))
        {
            confidence += 0.4f;
        }
        if (ContainsAny(content, "where", "what", "how many", "which"))
        {
            confidence += 0.3f;
        }
        if (confidence >= 0.5f)
        {
            return (MemoryIntent.Find, Math.Min(confidence, 1.0f));
        }

        // Strong indicators for CONNECT intent
        if (ContainsAny(content, "connect", "link", "relate", "associate", "tie"))
        {
            return (MemoryIntent.Connect, 0.8f);
        }

        // Strong indicators for EXPLORE intent
        if (ContainsAny(content, "explore", "navigate", "graph", "relationships", "connected", "related"))
        {
            return (MemoryIntent.Explore, 0.8f);
        }

        // Strong indicators for SUGGEST intent
        if (ContainsAny(content, "suggest", "recommend", "help", "what should", "advice"))
        {
            return (MemoryIntent.Suggest, 0.8f);
        }

        // Strong indicators for MANAGE intent
        if (ContainsAny(content, "update", "delete", "archive", "change", "modify", "remove"))
        {
            return (MemoryIntent.Manage, 0.8f);
        }

        // Default to FIND if no clear intent detected
        return (MemoryIntent.Find, 0.3f);
    }

    /// <summary>
    /// Handle SAVE intent - store new memories or create checklists
    /// </summary>
    private async Task<UnifiedMemoryResult> HandleSaveAsync(
        UnifiedMemoryCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            var content = command.Content.ToLowerInvariant();

            // Check if this is a checklist creation request
            if (ContainsAny(content, "checklist", "task list", "plan", "items"))
            {
                return await CreateChecklistFromCommand(command, cancellationToken);
            }

            // Check for duplicates first
            var similarMemories = await FindSimilarMemoriesAsync(command.Content);
            if (similarMemories.Any())
            {
                return new UnifiedMemoryResult
                {
                    Success = false,
                    Action = "duplicate_check",
                    Message = "Found similar existing memories. Consider updating existing memory instead.",
                    Memories = similarMemories,
                    NextSteps = similarMemories.Select((m, i) => new ActionSuggestion
                    {
                        Id = $"update_existing_{i}",
                        Description = $"Update existing: {m.Content.Substring(0, Math.Min(50, m.Content.Length))}...",
                        Command = $"memory update --id='{m.Id}' --content='{command.Content}'",
                        Priority = "high",
                        Category = "duplicate_resolution"
                    }).ToList()
                };
            }

            // Determine memory type from content
            var memoryType = InferMemoryType(command.Content);
            
            // Determine if this should be temporary or permanent
            var isTemporary = command.Context.Confidence < 0.7f || 
                             command.Context.Scope == "session" ||
                             ContainsAny(content, "temp", "temporary", "quick note", "reminder");

            FlexibleMemoryEntry? created = null;

            if (isTemporary && _memoryTools != null)
            {
                // Use temporary memory for low confidence or session-scoped items
                var fields = InferFieldsFromContent(command.Content, memoryType);
                var jsonFields = fields.ToDictionary(
                    kv => kv.Key, 
                    kv => System.Text.Json.JsonSerializer.SerializeToElement(kv.Value)
                );

                var tempResult = await _memoryTools.StoreWorkingMemoryAsync(
                    command.Content,
                    "4h", // Default expiration
                    command.Context.SessionId,
                    command.Context.RelatedFiles.ToArray(),
                    jsonFields
                );
                
                if (tempResult.Success && !string.IsNullOrEmpty(tempResult.MemoryId))
                {
                    // We need to fetch the created memory since StoreWorkingMemoryAsync only returns the ID
                    // For now, create a mock entry - this should be improved
                    created = new FlexibleMemoryEntry
                    {
                        Id = tempResult.MemoryId,
                        Type = MemoryTypes.WorkingMemory,
                        Content = command.Content,
                        Fields = jsonFields
                    };
                }
            }
            else if (_memoryTools != null)
            {
                // Use permanent memory for high confidence items
                var fields = InferFieldsFromContent(command.Content, memoryType);
                var jsonFields = fields.ToDictionary(
                    kv => kv.Key, 
                    kv => System.Text.Json.JsonSerializer.SerializeToElement(kv.Value)
                );

                var storeResult = await _memoryTools.StoreMemoryAsync(
                    memoryType,
                    command.Content,
                    command.Context.Scope == "project", // isShared
                    command.Context.SessionId,
                    command.Context.RelatedFiles.ToArray(),
                    jsonFields
                );
                
                if (storeResult.Success && !string.IsNullOrEmpty(storeResult.MemoryId))
                {
                    // Create a mock entry since StoreMemoryAsync only returns the ID
                    created = new FlexibleMemoryEntry
                    {
                        Id = storeResult.MemoryId,
                        Type = memoryType,
                        Content = command.Content,
                        Fields = jsonFields
                    };
                }
            }

            if (created == null)
            {
                return new UnifiedMemoryResult
                {
                    Success = false,
                    Action = "save_failed",
                    Message = "Unable to save memory - memory tools not available"
                };
            }

            return new UnifiedMemoryResult
            {
                Success = true,
                Action = "saved",
                Memory = created,
                Message = $"Saved as {(isTemporary ? "temporary" : "permanent")} {memoryType}",
                NextSteps = new List<ActionSuggestion>
                {
                    new ActionSuggestion
                    {
                        Id = "find_related",
                        Description = "Find related memories",
                        Command = $"memory explore --from='{created.Id}'",
                        Priority = "medium",
                        Category = "exploration"
                    }
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling save command: {Content}", command.Content);
            return new UnifiedMemoryResult
            {
                Success = false,
                Action = "save_error",
                Message = $"Error saving memory: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Handle FIND intent - search memories, files, and content
    /// </summary>
    private async Task<UnifiedMemoryResult> HandleFindAsync(
        UnifiedMemoryCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            var content = command.Content.ToLowerInvariant();
            var results = new List<FlexibleMemoryEntry>();
            var highlights = new Dictionary<string, string[]>();
            
            // Always search memories first
            if (_memoryTools != null)
            {
                var memoryResult = await _memoryTools.SearchMemoriesAsync(
                    command.Content, // query
                    null, // types
                    null, // dateRange
                    null, // facets
                    null, // orderBy
                    true, // orderDescending
                    20, // maxResults
                    false, // includeArchived
                    true, // boostRecent
                    true // boostFrequent
                );
                results.AddRange(memoryResult.Memories);
            }

            // If looking for files specifically, also search files
            if (ContainsAny(content, "file", "files", "document", "code") && 
                !string.IsNullOrEmpty(command.Context.WorkingDirectory))
            {
                // Add file search results as synthetic memories
                // This would require additional implementation
            }

            return new UnifiedMemoryResult
            {
                Success = true,
                Action = "found",
                Memories = results,
                Highlights = highlights,
                Message = $"Found {results.Count} memories",
                NextSteps = GenerateFindNextSteps(results, command)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling find command: {Content}", command.Content);
            return new UnifiedMemoryResult
            {
                Success = false,
                Action = "find_error",
                Message = $"Error searching: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Handle CONNECT intent - link memories together
    /// </summary>
    private Task<UnifiedMemoryResult> HandleConnectAsync(
        UnifiedMemoryCommand command,
        CancellationToken cancellationToken)
    {
        // Extract memory IDs or descriptions from command
        // This would require more sophisticated parsing
        return Task.FromResult(new UnifiedMemoryResult
        {
            Success = false,
            Action = "connect_not_implemented",
            Message = "Connect functionality not yet implemented"
        });
    }

    /// <summary>
    /// Handle EXPLORE intent - navigate memory relationships
    /// </summary>
    private Task<UnifiedMemoryResult> HandleExploreAsync(
        UnifiedMemoryCommand command,
        CancellationToken cancellationToken)
    {
        // Use memory graph navigator
        return Task.FromResult(new UnifiedMemoryResult
        {
            Success = false,
            Action = "explore_not_implemented", 
            Message = "Explore functionality not yet implemented"
        });
    }

    /// <summary>
    /// Handle SUGGEST intent - provide recommendations
    /// </summary>
    private Task<UnifiedMemoryResult> HandleSuggestAsync(
        UnifiedMemoryCommand command,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new UnifiedMemoryResult
        {
            Success = false,
            Action = "suggest_not_implemented",
            Message = "Suggest functionality not yet implemented"
        });
    }

    /// <summary>
    /// Handle MANAGE intent - update, delete, archive memories
    /// </summary>
    private Task<UnifiedMemoryResult> HandleManageAsync(
        UnifiedMemoryCommand command,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new UnifiedMemoryResult
        {
            Success = false,
            Action = "manage_not_implemented",
            Message = "Manage functionality not yet implemented"
        });
    }

    #region Helper Methods

    /// <summary>
    /// Check if content contains any of the specified keywords
    /// </summary>
    private static bool ContainsAny(string content, params string[] keywords)
    {
        return keywords.Any(keyword => content.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Infer memory type from content
    /// </summary>
    private static string InferMemoryType(string content)
    {
        var lower = content.ToLowerInvariant();

        if (ContainsAny(lower, "technical debt", "tech debt", "debt", "refactor", "cleanup"))
            return "TechnicalDebt";
        
        if (ContainsAny(lower, "architectural", "architecture", "design decision", "pattern"))
            return "ArchitecturalDecision";
        
        if (ContainsAny(lower, "security", "vulnerability", "auth", "permission"))
            return "SecurityRule";
        
        if (ContainsAny(lower, "performance", "slow", "optimization", "bottleneck"))
            return "PerformanceIssue";
        
        if (ContainsAny(lower, "bug", "error", "issue", "problem"))
            return "BugReport";

        return "ProjectInsight"; // Default type
    }

    /// <summary>
    /// Infer fields from content based on memory type
    /// </summary>
    private static Dictionary<string, object> InferFieldsFromContent(string content, string memoryType)
    {
        var fields = new Dictionary<string, object>();
        var lower = content.ToLowerInvariant();

        // Common field inference
        if (ContainsAny(lower, "high priority", "urgent", "critical"))
            fields["priority"] = "high";
        else if (ContainsAny(lower, "low priority", "minor"))
            fields["priority"] = "low";
        else
            fields["priority"] = "medium";

        // Type-specific field inference
        if (memoryType == "TechnicalDebt")
        {
            if (ContainsAny(lower, "days", "week", "weeks"))
                fields["effort"] = "medium";
            else if (ContainsAny(lower, "hour", "hours", "quick"))
                fields["effort"] = "low";
            else
                fields["effort"] = "high";

            fields["impact"] = ContainsAny(lower, "performance", "security", "maintainability") ? "high" : "medium";
        }

        return fields;
    }

    /// <summary>
    /// Find memories similar to the given content
    /// </summary>
    private async Task<List<FlexibleMemoryEntry>> FindSimilarMemoriesAsync(string content)
    {
        try
        {
            if (_memoryTools == null) return new List<FlexibleMemoryEntry>();

            var result = await _memoryTools.SearchMemoriesAsync(
                content, // query
                null, // types
                null, // dateRange
                null, // facets
                null, // orderBy
                true, // orderDescending
                3, // maxResults - only get top 3 similar
                false, // includeArchived
                false, // boostRecent
                false // boostFrequent
            );
            
            // Return first few results as similar (no Score property available)
            return result.Memories.Take(2).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error finding similar memories for: {Content}", content);
            return new List<FlexibleMemoryEntry>();
        }
    }

    /// <summary>
    /// Create a checklist from a command
    /// </summary>
    private async Task<UnifiedMemoryResult> CreateChecklistFromCommand(
        UnifiedMemoryCommand command,
        CancellationToken cancellationToken)
    {
        if (_checklistTools == null)
        {
            return new UnifiedMemoryResult
            {
                Success = false,
                Action = "checklist_unavailable",
                Message = "Checklist functionality not available"
            };
        }

        try
        {
            // Extract title from command - look for patterns like "create checklist for X" or "checklist X"
            var commandText = command.Content.ToLowerInvariant();
            var title = ExtractChecklistTitle(commandText);
            
            if (string.IsNullOrEmpty(title))
            {
                return new UnifiedMemoryResult
                {
                    Success = false,
                    Action = "checklist_title_missing",
                    Message = "Could not extract checklist title from command. Try: 'create checklist for [title]'"
                };
            }

            // Create the checklist
            Dictionary<string, JsonElement>? customFields = null;
            if (command.Context.RelatedFiles?.Any() == true)
            {
                customFields = new Dictionary<string, JsonElement> 
                { 
                    ["relatedFiles"] = JsonSerializer.SerializeToElement(command.Context.RelatedFiles) 
                };
            }

            var result = await _checklistTools.CreateChecklistAsync(
                title,
                description: $"Created via unified memory interface from: {command.Content}",
                isShared: true,
                sessionId: command.Context.SessionId,
                customFields: customFields
            );

            if (result.Success && !string.IsNullOrEmpty(result.ChecklistId))
            {
                return new UnifiedMemoryResult
                {
                    Success = true,
                    Action = "checklist_created",
                    Message = $"Created checklist '{title}' with ID: {result.ChecklistId}",
                    NextSteps = new List<ActionSuggestion>
                    {
                        new ActionSuggestion
                        {
                            Id = "add_items",
                            Description = "Add items to checklist",
                            Command = $"memory \"add items to checklist {result.ChecklistId}: item 1, item 2, item 3\"",
                            Priority = "high",
                            Category = "checklist_management"
                        },
                        new ActionSuggestion
                        {
                            Id = "view_checklist",
                            Description = "View checklist",
                            Command = $"memory \"view checklist {result.ChecklistId}\"",
                            Priority = "medium",
                            Category = "checklist_management"
                        }
                    },
                    Metadata = new Dictionary<string, object>
                    {
                        ["checklistId"] = result.ChecklistId,
                        ["title"] = title
                    }
                };
            }
            else
            {
                return new UnifiedMemoryResult
                {
                    Success = false,
                    Action = "checklist_creation_failed",
                    Message = result.Message ?? "Failed to create checklist"
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating checklist from command: {Content}", command.Content);
            return new UnifiedMemoryResult
            {
                Success = false,
                Action = "checklist_error",
                Message = $"Error creating checklist: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Extract checklist title from command text
    /// </summary>
    private static string ExtractChecklistTitle(string commandText)
    {
        // Patterns to match:
        // "create checklist for X" -> X
        // "checklist for X" -> X  
        // "make checklist X" -> X
        // "new checklist X" -> X
        
        var patterns = new[]
        {
            @"create\s+checklist\s+for\s+(.+)",
            @"checklist\s+for\s+(.+)",
            @"make\s+checklist\s+(.+)",
            @"new\s+checklist\s+(.+)",
            @"checklist\s+(.+)"
        };

        foreach (var pattern in patterns)
        {
            var match = System.Text.RegularExpressions.Regex.Match(commandText, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success && match.Groups.Count > 1)
            {
                var title = match.Groups[1].Value.Trim();
                // Clean up common endings
                title = System.Text.RegularExpressions.Regex.Replace(title, @"\s+(please|now|today)$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                return title;
            }
        }

        return "";
    }

    /// <summary>
    /// Generate next step suggestions for find results
    /// </summary>
    private static List<ActionSuggestion> GenerateFindNextSteps(
        List<FlexibleMemoryEntry> results,
        UnifiedMemoryCommand command)
    {
        var suggestions = new List<ActionSuggestion>();

        if (results.Any())
        {
            // Suggest exploring relationships
            suggestions.Add(new ActionSuggestion
            {
                Id = "explore_relationships",
                Description = "Explore relationships of top result",
                Command = $"memory explore --from='{results.First().Id}'",
                Priority = "medium",
                Category = "exploration"
            });

            // Suggest filtering by type if multiple types found
            var types = results.Select(r => r.Type).Distinct().ToList();
            if (types.Count > 1)
            {
                suggestions.Add(new ActionSuggestion
                {
                    Id = "filter_by_type",
                    Description = $"Filter by {types.First()} only",
                    Command = $"memory find \"{command.Content} type:{types.First()}\"",
                    Priority = "low",
                    Category = "filtering"
                });
            }
        }
        else
        {
            // No results - suggest creating new memory
            suggestions.Add(new ActionSuggestion
            {
                Id = "create_new",
                Description = "Create new memory for this topic",
                Command = $"memory save \"{command.Content}\"",
                Priority = "high",
                Category = "creation"
            });
        }

        return suggestions;
    }

    #endregion
}