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
    private readonly JsonMemoryBackupService? _backupService;
    private readonly SemanticSearchTool? _semanticSearchTool;
    private readonly HybridSearchTool? _hybridSearchTool;

    public UnifiedMemoryService(
        FlexibleMemoryService memoryService,
        ILogger<UnifiedMemoryService> logger,
        FlexibleMemoryTools? memoryTools = null,
        ChecklistTools? checklistTools = null,
        MemoryLinkingTools? linkingTools = null,
        FastFileSearchToolV2? fileSearchTool = null,
        FastTextSearchToolV2? textSearchTool = null,
        MemoryGraphNavigatorTool? graphNavigatorTool = null,
        JsonMemoryBackupService? backupService = null,
        SemanticSearchTool? semanticSearchTool = null,
        HybridSearchTool? hybridSearchTool = null)
    {
        _memoryService = memoryService;
        _logger = logger;
        _memoryTools = memoryTools;
        _checklistTools = checklistTools;
        _linkingTools = linkingTools;
        _fileSearchTool = fileSearchTool;
        _textSearchTool = textSearchTool;
        _graphNavigatorTool = graphNavigatorTool;
        _backupService = backupService;
        _semanticSearchTool = semanticSearchTool;
        _hybridSearchTool = hybridSearchTool;
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
        
        // Check for CONNECT intent first - these are very specific keywords
        if (ContainsAny(content, "connect", "link", "relate", "associate", "tie"))
        {
            // Additional confidence if it mentions "to" or "with" (common in connect commands)
            var confidence = ContainsAny(content, " to ", " with ", " and ") ? 0.9f : 0.8f;
            return (MemoryIntent.Connect, confidence);
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
        if (ContainsAny(content, "update", "delete", "archive", "change", "modify", "remove") ||
            ContainsAny(content, "mark complete", "mark done", "check off", "complete item", "finish item") ||
            ContainsAny(content, "backup", "restore", "export", "import"))
        {
            return (MemoryIntent.Manage, 0.8f);
        }

        // Strong indicators for FIND intent
        if (ContainsAny(content, "find", "search", "look for", "get", "show", "list") ||
            ContainsAny(content, "where", "what", "how many", "which"))
        {
            return (MemoryIntent.Find, 0.8f);
        }

        // Strong indicators for SAVE intent
        var saveConfidence = 0.0f;
        if (ContainsAny(content, "remember", "store", "save", "create", "note", "record"))
        {
            saveConfidence += 0.6f;
        }
        if (ContainsAny(content, "technical debt", "architectural decision", "bug", "issue", "todo"))
        {
            saveConfidence += 0.3f;
        }
        if (ContainsAny(content, "checklist", "task list", "plan", "items"))
        {
            return (MemoryIntent.Save, 0.9f); // High confidence for checklist creation
        }
        
        if (saveConfidence >= 0.5f)
        {
            return (MemoryIntent.Save, Math.Min(saveConfidence, 1.0f));
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
            var searchMode = DetermineSearchMode(content);
            
            // Choose search strategy based on content
            if (searchMode == SearchMode.Semantic && _semanticSearchTool != null)
            {
                // Use semantic search for conceptual queries
                var semanticResult = await _semanticSearchTool.ExecuteAsync(new SemanticSearchParams
                {
                    Query = command.Content,
                    MaxResults = 20,
                    Threshold = 0.2f,
                    MemoryType = null
                });
                
                if (semanticResult is JsonElement jsonResult && 
                    jsonResult.TryGetProperty("success", out var success) && success.GetBoolean() &&
                    jsonResult.TryGetProperty("results", out var resultsArray))
                {
                    foreach (var result in resultsArray.EnumerateArray())
                    {
                        if (result.TryGetProperty("memory", out var memoryObj))
                        {
                            var memory = DeserializeMemory(memoryObj);
                            if (memory != null)
                                results.Add(memory);
                        }
                    }
                }
            }
            else if (searchMode == SearchMode.Hybrid && _hybridSearchTool != null)
            {
                // Use hybrid search for balanced results
                var hybridResult = await _hybridSearchTool.ExecuteAsync(new HybridSearchParams
                {
                    Query = command.Content,
                    MaxResults = 20,
                    LuceneWeight = 0.6f,
                    SemanticWeight = 0.4f,
                    SemanticThreshold = 0.2f,
                    MergeStrategy = MergeStrategy.Linear,
                    BothFoundBoost = 1.2f
                });
                
                if (hybridResult is JsonElement jsonResult && 
                    jsonResult.TryGetProperty("success", out var success) && success.GetBoolean() &&
                    jsonResult.TryGetProperty("results", out var resultsArray))
                {
                    foreach (var result in resultsArray.EnumerateArray())
                    {
                        if (result.TryGetProperty("memory", out var memoryObj))
                        {
                            var memory = DeserializeMemory(memoryObj);
                            if (memory != null)
                                results.Add(memory);
                        }
                    }
                }
            }
            else if (_memoryTools != null)
            {
                // Default to regular text search
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
                Message = $"Found {results.Count} memories using {searchMode} search",
                Metadata = new Dictionary<string, object>
                {
                    ["searchMode"] = searchMode.ToString(),
                    ["resultCount"] = results.Count
                },
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
    private async Task<UnifiedMemoryResult> HandleConnectAsync(
        UnifiedMemoryCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            if (_linkingTools == null)
            {
                return new UnifiedMemoryResult
                {
                    Success = false,
                    Action = "linking_unavailable",
                    Message = "Memory linking tools are not available"
                };
            }

            var content = command.Content?.ToLowerInvariant() ?? "";
            
            // Try to extract memory IDs or descriptions
            // Pattern: "connect [memory1] to/with/and [memory2]"
            var patterns = new[]
            {
                @"connect\s+(.+?)\s+(?:to|with|and)\s+(.+)",
                @"link\s+(.+?)\s+(?:to|with|and)\s+(.+)",
                @"relate\s+(.+?)\s+(?:to|with|and)\s+(.+)"
            };

            string? source = null;
            string? target = null;
            
            foreach (var pattern in patterns)
            {
                var match = Regex.Match(content, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    source = match.Groups[1].Value.Trim();
                    target = match.Groups[2].Value.Trim();
                    break;
                }
            }

            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target))
            {
                return new UnifiedMemoryResult
                {
                    Success = false,
                    Action = "connect_parse_error",
                    Message = "Unable to parse memories to connect. Try: 'connect [memory description] to [other memory]'",
                    NextSteps = new List<ActionSuggestion>
                    {
                        new ActionSuggestion
                        {
                            Id = "connect_example",
                            Description = "Example connection command",
                            Command = "memory \"connect authentication bug to security audit\"",
                            Priority = "high"
                        }
                    }
                };
            }

            // First, try to find memories by the descriptions
            if (_memoryTools == null)
            {
                return new UnifiedMemoryResult
                {
                    Success = false,
                    Action = "memory_tools_unavailable",
                    Message = "Memory tools are not available"
                };
            }

            var sourceMemories = await _memoryTools.SearchMemoriesAsync(source, null, null, null, null, true, 1);
            var targetMemories = await _memoryTools.SearchMemoriesAsync(target, null, null, null, null, true, 1);

            if (sourceMemories?.Memories == null || !sourceMemories.Memories.Any())
            {
                return new UnifiedMemoryResult
                {
                    Success = false,
                    Action = "source_not_found",
                    Message = $"Could not find memory matching: '{source}'"
                };
            }

            if (targetMemories?.Memories == null || !targetMemories.Memories.Any())
            {
                return new UnifiedMemoryResult
                {
                    Success = false,
                    Action = "target_not_found",
                    Message = $"Could not find memory matching: '{target}'"
                };
            }

            var sourceMemory = sourceMemories.Memories.First();
            var targetMemory = targetMemories.Memories.First();

            // Determine relationship type from content
            var relationshipType = "relatedTo"; // default
            if (ContainsAny(content, "causes", "caused by"))
                relationshipType = "causes";
            else if (ContainsAny(content, "depends on", "requires"))
                relationshipType = "dependsOn";
            else if (ContainsAny(content, "blocks", "blocking"))
                relationshipType = "blocks";
            else if (ContainsAny(content, "implements", "implementation"))
                relationshipType = "implements";

            // Link the memories
            var linkResult = await _linkingTools.LinkMemoriesAsync(
                sourceMemory.Id,
                targetMemory.Id,
                relationshipType,
                bidirectional: ContainsAny(content, "both", "bidirectional", "mutual")
            );

            if (!linkResult.Success)
            {
                return new UnifiedMemoryResult
                {
                    Success = false,
                    Action = "link_failed",
                    Message = linkResult.Message ?? "Failed to link memories"
                };
            }

            return new UnifiedMemoryResult
            {
                Success = true,
                Action = "connected",
                Message = $"Connected '{sourceMemory.Content.Substring(0, Math.Min(50, sourceMemory.Content.Length))}...' to '{targetMemory.Content.Substring(0, Math.Min(50, targetMemory.Content.Length))}...' with relationship '{relationshipType}'",
                Memories = new List<FlexibleMemoryEntry> { sourceMemory, targetMemory },
                NextSteps = new List<ActionSuggestion>
                {
                    new ActionSuggestion
                    {
                        Id = "explore_connections",
                        Description = "Explore the connection graph",
                        Command = $"memory \"explore connections from {sourceMemory.Id}\"",
                        Priority = "medium",
                        Category = "exploration"
                    },
                    new ActionSuggestion
                    {
                        Id = "find_related",
                        Description = "Find other related memories",
                        Command = $"memory \"find memories related to {sourceMemory.Id}\"",
                        Priority = "low",
                        Category = "discovery"
                    }
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling connect command: {Content}", command.Content);
            return new UnifiedMemoryResult
            {
                Success = false,
                Action = "connect_error",
                Message = $"Error connecting memories: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Handle EXPLORE intent - navigate memory relationships
    /// </summary>
    private async Task<UnifiedMemoryResult> HandleExploreAsync(
        UnifiedMemoryCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            if (_graphNavigatorTool == null)
            {
                return new UnifiedMemoryResult
                {
                    Success = false,
                    Action = "graph_navigator_unavailable",
                    Message = "Memory graph navigator is not available"
                };
            }

            var content = command.Content?.ToLowerInvariant() ?? "";
            
            // Extract starting point - could be a memory ID or description
            // Patterns: "explore from [memory]", "explore connections around [memory]", "explore [memory] relationships"
            var patterns = new[]
            {
                @"explore\s+(?:from|connections\s+from|relationships\s+from)\s+(.+)",
                @"explore\s+(.+?)\s+(?:connections|relationships|graph)",
                @"explore\s+connections\s+(?:around|for)\s+(.+)",
                @"navigate\s+(?:from|around)\s+(.+)"
            };

            string? startPoint = null;
            
            foreach (var pattern in patterns)
            {
                var match = Regex.Match(content, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    startPoint = match.Groups[1].Value.Trim();
                    break;
                }
            }

            // If no pattern matched, try to use the whole content after "explore"
            if (string.IsNullOrEmpty(startPoint))
            {
                var exploreIndex = content.IndexOf("explore");
                if (exploreIndex >= 0)
                {
                    startPoint = content.Substring(exploreIndex + 7).Trim();
                }
            }

            if (string.IsNullOrEmpty(startPoint))
            {
                return new UnifiedMemoryResult
                {
                    Success = false,
                    Action = "explore_parse_error",
                    Message = "Unable to determine what to explore. Try: 'explore connections from [memory description]'",
                    NextSteps = new List<ActionSuggestion>
                    {
                        new ActionSuggestion
                        {
                            Id = "explore_example",
                            Description = "Example exploration command",
                            Command = "memory \"explore connections from authentication system\"",
                            Priority = "high"
                        }
                    }
                };
            }

            // Determine depth from content
            int depth = 2; // default
            if (ContainsAny(content, "deep", "all", "complete"))
                depth = 4;
            else if (ContainsAny(content, "immediate", "direct", "first"))
                depth = 1;

            // Determine filter types if specified
            string[]? filterTypes = null;
            if (ContainsAny(content, "technical debt", "debt"))
                filterTypes = new[] { "TechnicalDebt" };
            else if (ContainsAny(content, "architectural", "architecture", "decision"))
                filterTypes = new[] { "ArchitecturalDecision" };
            else if (ContainsAny(content, "security"))
                filterTypes = new[] { "SecurityRule" };

            // Execute the graph navigation
            var graphResult = await _graphNavigatorTool.ExecuteAsync(
                startPoint,
                depth,
                filterTypes,
                includeOrphans: false,
                mode: ResponseMode.Summary,
                detailRequest: null,
                cancellationToken
            );

            // Parse the result - it should be a JSON object
            if (graphResult is JsonElement jsonResult)
            {
                if (jsonResult.TryGetProperty("success", out var successProp) && !successProp.GetBoolean())
                {
                    var errorMessage = jsonResult.TryGetProperty("message", out var msgProp) 
                        ? msgProp.GetString() 
                        : "Failed to explore memory graph";
                    
                    return new UnifiedMemoryResult
                    {
                        Success = false,
                        Action = "explore_failed",
                        Message = errorMessage ?? "Unknown error occurred"
                    };
                }

                // Extract key information from the graph result
                var nodeCount = 0;
                var edgeCount = 0;
                var clusters = new List<string>();
                var insights = new List<string>();

                if (jsonResult.TryGetProperty("summary", out var summary))
                {
                    if (summary.TryGetProperty("nodeCount", out var nc))
                        nodeCount = nc.GetInt32();
                    if (summary.TryGetProperty("edgeCount", out var ec))
                        edgeCount = ec.GetInt32();
                }

                if (jsonResult.TryGetProperty("clusters", out var clustersArray))
                {
                    foreach (var cluster in clustersArray.EnumerateArray())
                    {
                        if (cluster.TryGetProperty("theme", out var theme))
                            clusters.Add(theme.GetString() ?? "Unknown");
                    }
                }

                if (jsonResult.TryGetProperty("insights", out var insightsArray))
                {
                    foreach (var insight in insightsArray.EnumerateArray())
                    {
                        insights.Add(insight.GetString() ?? "");
                    }
                }

                var message = $"Explored {nodeCount} memories with {edgeCount} connections";
                if (clusters.Any())
                    message += $". Found {clusters.Count} clusters: {string.Join(", ", clusters.Take(3))}";

                return new UnifiedMemoryResult
                {
                    Success = true,
                    Action = "explored",
                    Message = message,
                    // Store the full graph result for potential further processing
                    Metadata = new Dictionary<string, object>
                    {
                        ["graphResult"] = jsonResult,
                        ["nodeCount"] = nodeCount,
                        ["edgeCount"] = edgeCount,
                        ["clusters"] = clusters,
                        ["insights"] = insights
                    },
                    NextSteps = new List<ActionSuggestion>
                    {
                        new ActionSuggestion
                        {
                            Id = "view_details",
                            Description = "View detailed graph visualization",
                            Command = $"Use memory_graph_navigator tool directly with startPoint='{startPoint}'",
                            Priority = "high",
                            Category = "visualization"
                        },
                        new ActionSuggestion
                        {
                            Id = "explore_deeper",
                            Description = "Explore with greater depth",
                            Command = $"memory \"explore deep connections from {startPoint}\"",
                            Priority = "medium",
                            Category = "exploration"
                        }
                    }
                };
            }

            return new UnifiedMemoryResult
            {
                Success = false,
                Action = "explore_error",
                Message = "Unexpected response format from graph navigator"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling explore command: {Content}", command.Content);
            return new UnifiedMemoryResult
            {
                Success = false,
                Action = "explore_error",
                Message = $"Error exploring memory graph: {ex.Message}"
            };
        }
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
    /// Handle MANAGE intent - update, delete, archive memories and manage checklists
    /// </summary>
    private async Task<UnifiedMemoryResult> HandleManageAsync(
        UnifiedMemoryCommand command,
        CancellationToken cancellationToken)
    {
        var content = command.Content?.ToLowerInvariant() ?? "";
        
        // Check for backup/restore operations
        if (ContainsAny(content, "backup", "export"))
        {
            return await HandleBackupAsync(command, cancellationToken);
        }
        
        if (ContainsAny(content, "restore", "import"))
        {
            return await HandleRestoreAsync(command, cancellationToken);
        }
        
        // Check for checklist operations
        if (ContainsAny(content, "checklist", "item", "task", "complete", "check", "mark"))
        {
            return await HandleChecklistManageAsync(command, cancellationToken);
        }
        
        // Check for memory operations
        if (ContainsAny(content, "memory", "update memory", "delete memory", "archive memory"))
        {
            return await HandleMemoryManageAsync(command, cancellationToken);
        }
        
        return new UnifiedMemoryResult
        {
            Success = false,
            Action = "manage_ambiguous",
            Message = "Unable to determine what to manage. Try: 'update checklist [name]' or 'mark checklist item complete'",
            NextSteps = new List<ActionSuggestion>
            {
                new ActionSuggestion
                {
                    Id = "manage_checklist",
                    Description = "Manage checklist items",
                    Command = "memory \"update checklist [checklist name] - mark item [item] complete\"",
                    Priority = "medium"
                },
                new ActionSuggestion
                {
                    Id = "manage_memory",
                    Description = "Update memory content",
                    Command = "memory \"update memory [description] with [new information]\"",
                    Priority = "medium"
                }
            }
        };
    }

    /// <summary>
    /// Handle checklist management operations
    /// </summary>
    private async Task<UnifiedMemoryResult> HandleChecklistManageAsync(
        UnifiedMemoryCommand command,
        CancellationToken cancellationToken)
    {
        if (_checklistTools == null)
        {
            return new UnifiedMemoryResult
            {
                Success = false,
                Action = "checklist_unavailable",
                Message = "Checklist tools are not available"
            };
        }

        var content = command.Content?.ToLowerInvariant() ?? "";
        
        // Check for completion operations
        if (ContainsAny(content, "complete", "mark complete", "check off", "done", "finished"))
        {
            return await HandleChecklistCompletionAsync(command, cancellationToken);
        }
        
        // Check for adding items
        if (ContainsAny(content, "add item", "add task", "new item", "add to checklist"))
        {
            return await HandleChecklistAddItemAsync(command, cancellationToken);
        }
        
        // Check for updating items
        if (ContainsAny(content, "update item", "change item", "modify item", "edit item"))
        {
            return await HandleChecklistUpdateItemAsync(command, cancellationToken);
        }
        
        return new UnifiedMemoryResult
        {
            Success = false,
            Action = "checklist_operation_unclear",
            Message = "Checklist operation not clear. Try: 'mark item complete in checklist [name]' or 'add item [description] to checklist [name]'",
            NextSteps = new List<ActionSuggestion>
            {
                new ActionSuggestion
                {
                    Id = "complete_item",
                    Description = "Mark checklist item as complete",
                    Command = "memory \"mark item [item description] complete in checklist [checklist name]\"",
                    Priority = "high"
                },
                new ActionSuggestion
                {
                    Id = "add_item",
                    Description = "Add item to checklist",
                    Command = "memory \"add item [description] to checklist [checklist name]\"",
                    Priority = "medium"
                }
            }
        };
    }

    /// <summary>
    /// Handle memory management operations
    /// </summary>
    private Task<UnifiedMemoryResult> HandleMemoryManageAsync(
        UnifiedMemoryCommand command,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new UnifiedMemoryResult
        {
            Success = false,
            Action = "memory_manage_not_implemented",
            Message = "Memory management functionality not yet implemented. Use search and store commands instead."
        });
    }

    /// <summary>
    /// Handle checklist item completion
    /// </summary>
    private Task<UnifiedMemoryResult> HandleChecklistCompletionAsync(
        UnifiedMemoryCommand command,
        CancellationToken cancellationToken)
    {
        // This is a simplified implementation - in practice you'd want to parse the checklist name and item
        return Task.FromResult(new UnifiedMemoryResult
        {
            Success = false,
            Action = "checklist_completion_not_fully_implemented",
            Message = "Checklist item completion requires specific checklist ID and item index. Use the individual checklist tools for now.",
            NextSteps = new List<ActionSuggestion>
            {
                new ActionSuggestion
                {
                    Id = "list_checklists",
                    Description = "List available checklists",
                    Command = "search_memories --types [\"Checklist\"]",
                    Priority = "high"
                }
            }
        });
    }

    /// <summary>
    /// Handle adding items to checklist
    /// </summary>
    private Task<UnifiedMemoryResult> HandleChecklistAddItemAsync(
        UnifiedMemoryCommand command,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new UnifiedMemoryResult
        {
            Success = false,
            Action = "checklist_add_not_fully_implemented",
            Message = "Adding checklist items requires specific checklist ID. Use the individual checklist tools for now.",
            NextSteps = new List<ActionSuggestion>
            {
                new ActionSuggestion
                {
                    Id = "list_checklists",
                    Description = "List available checklists",
                    Command = "search_memories --types [\"Checklist\"]", 
                    Priority = "high"
                }
            }
        });
    }

    /// <summary>
    /// Handle updating checklist items
    /// </summary>
    private Task<UnifiedMemoryResult> HandleChecklistUpdateItemAsync(
        UnifiedMemoryCommand command,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new UnifiedMemoryResult
        {
            Success = false,
            Action = "checklist_update_not_fully_implemented",
            Message = "Updating checklist items requires specific checklist ID and item index. Use the individual checklist tools for now.",
            NextSteps = new List<ActionSuggestion>
            {
                new ActionSuggestion
                {
                    Id = "list_checklists",
                    Description = "List available checklists",
                    Command = "search_memories --types [\"Checklist\"]",
                    Priority = "high"
                }
            }
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

    /// <summary>
    /// Handle backup operations
    /// </summary>
    private async Task<UnifiedMemoryResult> HandleBackupAsync(
        UnifiedMemoryCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            if (_backupService == null)
            {
                return new UnifiedMemoryResult
                {
                    Success = false,
                    Action = "backup_unavailable",
                    Message = "Backup service is not available"
                };
            }

            var content = command.Content?.ToLowerInvariant() ?? "";
            
            // Determine what to backup based on content
            var includeLocal = ContainsAny(content, "all", "everything", "local", "personal", "session");
            string[]? scopes = null;
            
            if (ContainsAny(content, "project", "shared", "team"))
            {
                scopes = new[] { "ArchitecturalDecision", "CodePattern", "SecurityRule", "ProjectInsight" };
            }
            else if (ContainsAny(content, "technical debt", "debt"))
            {
                scopes = new[] { "TechnicalDebt" };
            }
            else if (ContainsAny(content, "checklist", "tasks"))
            {
                scopes = new[] { "ChecklistItem" };
            }

            // Perform the backup
            var backupResult = await _backupService.BackupMemoriesAsync(scopes, includeLocal);

            if (!backupResult.Success)
            {
                return new UnifiedMemoryResult
                {
                    Success = false,
                    Action = "backup_failed",
                    Message = backupResult.Error ?? "Backup failed" ?? "Failed to backup memories"
                };
            }

            return new UnifiedMemoryResult
            {
                Success = true,
                Action = "backed_up",
                Message = $"Backed up {backupResult.DocumentsBackedUp} memories to {backupResult.BackupPath}",
                Metadata = new Dictionary<string, object>
                {
                    ["backupFile"] = backupResult.BackupPath ?? "",
                    ["memoryCount"] = backupResult.DocumentsBackedUp,
                    ["includesLocal"] = includeLocal
                },
                NextSteps = new List<ActionSuggestion>
                {
                    new ActionSuggestion
                    {
                        Id = "commit_backup",
                        Description = "Commit backup file to git",
                        Command = $"git add {backupResult.BackupPath} && git commit -m \"Backup project memories\"",
                        Priority = "high",
                        Category = "version_control"
                    },
                    new ActionSuggestion
                    {
                        Id = "view_backup",
                        Description = "View backup file contents",
                        Command = $"Read file: {backupResult.BackupPath}",
                        Priority = "low",
                        Category = "inspection"
                    }
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling backup command: {Content}", command.Content);
            return new UnifiedMemoryResult
            {
                Success = false,
                Action = "backup_error",
                Message = $"Error backing up memories: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Handle restore operations
    /// </summary>
    private async Task<UnifiedMemoryResult> HandleRestoreAsync(
        UnifiedMemoryCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            if (_backupService == null)
            {
                return new UnifiedMemoryResult
                {
                    Success = false,
                    Action = "restore_unavailable",
                    Message = "Backup service is not available"
                };
            }

            var content = command.Content?.ToLowerInvariant() ?? "";
            
            // Determine what to restore based on content
            var includeLocal = ContainsAny(content, "all", "everything", "local", "personal", "session");
            string[]? scopes = null;
            
            if (ContainsAny(content, "project", "shared", "team"))
            {
                scopes = new[] { "ArchitecturalDecision", "CodePattern", "SecurityRule", "ProjectInsight" };
            }
            else if (ContainsAny(content, "technical debt", "debt"))
            {
                scopes = new[] { "TechnicalDebt" };
            }
            else if (ContainsAny(content, "checklist", "tasks"))
            {
                scopes = new[] { "ChecklistItem" };
            }

            // Check for specific file path in content
            string? backupFile = null;
            var filePattern = @"(?:from|file|path)[:\s]+([^\s]+\.json)";
            var match = Regex.Match(content, filePattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                backupFile = match.Groups[1].Value;
            }

            // Perform the restore
            var restoreResult = await _backupService.RestoreMemoriesAsync(backupFile, scopes, includeLocal);

            if (!restoreResult.Success)
            {
                return new UnifiedMemoryResult
                {
                    Success = false,
                    Action = "restore_failed",
                    Message = restoreResult.Error ?? "Failed to restore memories"
                };
            }

            return new UnifiedMemoryResult
            {
                Success = true,
                Action = "restored",
                Message = $"Restored {restoreResult.DocumentsRestored} memories from backup",
                Metadata = new Dictionary<string, object>
                {
                    ["restoredCount"] = restoreResult.DocumentsRestored,
                    ["backupFile"] = "most recent backup",
                    ["includesLocal"] = includeLocal
                },
                NextSteps = new List<ActionSuggestion>
                {
                    new ActionSuggestion
                    {
                        Id = "search_restored",
                        Description = "Search restored memories",
                        Command = "memory \"find all\"",
                        Priority = "high",
                        Category = "discovery"
                    },
                    new ActionSuggestion
                    {
                        Id = "recall_context",
                        Description = "Load relevant memories for current work",
                        Command = "recall_context --query \"current project\"",
                        Priority = "medium",
                        Category = "context"
                    }
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling restore command: {Content}", command.Content);
            return new UnifiedMemoryResult
            {
                Success = false,
                Action = "restore_error",
                Message = $"Error restoring memories: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Determine the best search mode based on the query content
    /// </summary>
    private SearchMode DetermineSearchMode(string content)
    {
        // Use semantic search for conceptual/meaning-based queries
        if (ContainsAny(content, "concept", "meaning", "similar", "like", "related to", "about"))
            return SearchMode.Semantic;
        
        // Use hybrid search for queries that might benefit from both approaches
        if (ContainsAny(content, "pattern", "architecture", "design", "issue", "problem", "bug"))
            return SearchMode.Hybrid;
        
        // Default to text search for specific/exact queries
        return SearchMode.Text;
    }

    /// <summary>
    /// Deserialize a JSON element into a FlexibleMemoryEntry
    /// </summary>
    private FlexibleMemoryEntry? DeserializeMemory(JsonElement memoryElement)
    {
        try
        {
            var memory = new FlexibleMemoryEntry
            {
                Id = memoryElement.GetProperty("id").GetString() ?? "",
                Type = memoryElement.GetProperty("type").GetString() ?? "",
                Content = memoryElement.GetProperty("content").GetString() ?? ""
            };

            // Parse custom fields if present
            if (memoryElement.TryGetProperty("fields", out var fields))
            {
                foreach (var field in fields.EnumerateObject())
                {
                    memory.SetField(field.Name, field.Value);
                }
            }

            return memory;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize memory from JSON");
            return null;
        }
    }

    /// <summary>
    /// Search mode enumeration
    /// </summary>
    private enum SearchMode
    {
        Text,
        Semantic,
        Hybrid
    }

    #endregion
}