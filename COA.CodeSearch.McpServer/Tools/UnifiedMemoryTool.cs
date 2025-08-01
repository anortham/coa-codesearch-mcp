using COA.CodeSearch.McpServer.Attributes;
using COA.CodeSearch.McpServer.Configuration;
using COA.CodeSearch.McpServer.Constants;
using COA.CodeSearch.McpServer.Infrastructure;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using COA.Mcp.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Unified memory tool that provides natural language interface to all memory operations
/// Replaces the need for multiple memory tools with intelligent intent detection
/// </summary>
[McpServerToolType]
public class UnifiedMemoryTool : ClaudeOptimizedToolBase
{
    private readonly UnifiedMemoryService _unifiedMemoryService;
    private readonly ILogger<UnifiedMemoryTool> _logger;

    // ITool implementation
    public override string ToolName => ToolNames.UnifiedMemory;
    public override string Description => "Unified memory interface with natural language processing";
    public override ToolCategory Category => ToolCategory.Memory;

    public UnifiedMemoryTool(
        UnifiedMemoryService unifiedMemoryService,
        IResponseSizeEstimator sizeEstimator,
        IResultTruncator truncator,
        IOptions<ResponseLimitOptions> options,
        ILogger<UnifiedMemoryTool> logger,
        IDetailRequestCache? detailCache = null) 
        : base(sizeEstimator, truncator, options, logger, detailCache)
    {
        _unifiedMemoryService = unifiedMemoryService;
        _logger = logger;
    }

    /// <summary>
    /// Extract total result count from data for Claude optimization
    /// </summary>
    protected override int GetTotalResults<T>(T data)
    {
        return data switch
        {
            UnifiedMemoryToolResult result => result.MemoryCount > 0 ? result.MemoryCount : (result.Memory != null ? 1 : 0),
            _ => 0
        };
    }

    /// <summary>
    /// Attribute-based ExecuteAsync method for MCP registration
    /// </summary>
    [McpServerTool(Name = "unified_memory")]
    [Description(@"Unified memory interface that uses natural language to perform memory operations.
Replaces the need for multiple memory tools with intelligent intent detection.
Automatically routes commands to appropriate tools based on detected intent.

Supported intents:
- SAVE: Store memories, create checklists (""remember that UserService has performance issues"")
- FIND: Search memories, files, content (""find all authentication bugs"")  
- CONNECT: Link related memories (""connect auth bug to security audit"")
- EXPLORE: Navigate relationships (""explore authentication system connections"")
- SUGGEST: Get recommendations (""suggest improvements for authentication"")
- MANAGE: Update/delete memories (""update technical debt status to resolved"")

Examples:
- ""remember that database query in UserService.GetActiveUsers() takes 5 seconds""
- ""find all technical debt related to authentication system""
- ""create checklist for database migration project""
- ""explore relationships around user management architecture""
- ""suggest next steps for performance optimization""

Use cases: Natural language memory operations, AI agent workflows, context-aware suggestions.
AI-optimized: Provides intent detection, action suggestions, and usage guidance.")]
    public async Task<object> ExecuteAsync(UnifiedMemoryInputParams parameters)
    {
        if (parameters == null) throw new InvalidParametersException("Parameters are required");
        
        // Convert string intent to MemoryIntent enum
        MemoryIntent? intent = null;
        if (!string.IsNullOrEmpty(parameters.Intent) && parameters.Intent != "auto")
        {
            intent = parameters.Intent.ToLowerInvariant() switch
            {
                "save" => MemoryIntent.Save,
                "find" => MemoryIntent.Find,
                "connect" => MemoryIntent.Connect,
                "explore" => MemoryIntent.Explore,
                "suggest" => MemoryIntent.Suggest,
                "manage" => MemoryIntent.Manage,
                _ => null
            };
        }

        var toolParams = new UnifiedMemoryParams
        {
            Command = ValidateRequired(parameters.Command, "command"),
            Intent = intent,
            WorkingDirectory = parameters.WorkingDirectory,
            SessionId = parameters.SessionId,
            RelatedFiles = parameters.RelatedFiles?.ToList() ?? new List<string>(),
            CurrentFocus = parameters.CurrentFocus
        };

        return await ExecuteAsync(toolParams, CancellationToken.None);
    }
    
    private static string ValidateRequired(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidParametersException($"{paramName} is required");
        return value;
    }

    /// <summary>
    /// Execute a unified memory command using natural language
    /// </summary>
    /// <param name="parameters">The unified memory parameters</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Unified memory result with AI-optimized responses</returns>
    public async Task<UnifiedMemoryToolResult> ExecuteAsync(
        UnifiedMemoryParams parameters,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Executing unified memory command: {Command}", parameters.Command);

            // Build the unified command
            var command = new UnifiedMemoryCommand
            {
                Content = parameters.Command,
                Intent = parameters.Intent ?? MemoryIntent.Auto,
                Context = new CommandContext
                {
                    WorkingDirectory = parameters.WorkingDirectory ?? Directory.GetCurrentDirectory(),
                    SessionId = parameters.SessionId,
                    RelatedFiles = parameters.RelatedFiles ?? new List<string>(),
                    CurrentFocus = parameters.CurrentFocus,
                    Scope = "project" // Default to project scope
                }
            };

            // Execute the command
            var result = await _unifiedMemoryService.ExecuteAsync(command, cancellationToken);

            // Convert to tool result format
            var toolResult = new UnifiedMemoryToolResult
            {
                Success = result.Success,
                Intent = result.ExecutedIntent.ToString(),
                IntentConfidence = result.IntentConfidence,
                Action = result.Action,
                Message = result.Message,
                Metadata = result.Metadata
            };

            // Add data based on result type
            if (result.Memory != null)
            {
                toolResult.Memory = ConvertMemoryForDisplay(result.Memory);
            }

            if (result.Memories.Any())
            {
                toolResult.Memories = result.Memories.Select(ConvertMemoryForDisplay).ToList();
                toolResult.MemoryCount = result.Memories.Count;
            }

            if (result.Checklist != null)
            {
                toolResult.Checklist = result.Checklist;
            }

            if (result.Highlights?.Any() == true)
            {
                toolResult.Highlights = result.Highlights;
            }

            if (result.Facets?.Any() == true)
            {
                toolResult.Facets = result.Facets;
            }

            if (result.SpellCheck != null)
            {
                toolResult.SpellCheck = new
                {
                    DidYouMean = result.SpellCheck.DidYouMean,
                    AutoCorrected = result.SpellCheck.AutoCorrected,
                    Suggestions = result.SpellCheck.Suggestions
                };
            }

            // Add next steps
            if (result.NextSteps.Any())
            {
                toolResult.NextSteps = result.NextSteps.Select(step => (object)new
                {
                    Id = step.Id,
                    Description = step.Description,
                    Command = step.Command,
                    Priority = step.Priority,
                    Category = step.Category,
                    EstimatedTokens = step.EstimatedTokens
                }).ToList();
            }

            // Add usage guidance
            toolResult.Usage = GenerateUsageGuidance(result);

            _logger.LogInformation("Unified memory command completed successfully: {Intent} -> {Action}", 
                result.ExecutedIntent, result.Action);

            return toolResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing unified memory command: {Command}", parameters.Command);
            
            return new UnifiedMemoryToolResult
            {
                Success = false,
                Message = $"Error executing command: {ex.Message}",
                Action = "error",
                Intent = "unknown",
                IntentConfidence = 0.0f,
                Usage = new
                {
                    Error = "Command execution failed",
                    Suggestion = "Try rephrasing your command or check the logs for more details",
                    Examples = GetExampleCommands()
                }
            };
        }
    }

    /// <summary>
    /// Convert memory entry to display format
    /// </summary>
    private static object ConvertMemoryForDisplay(FlexibleMemoryEntry memory)
    {
        return new
        {
            Id = memory.Id,
            Type = memory.Type,
            Content = memory.Content,
            Created = memory.Created,
            Modified = memory.Modified,
            // Score not available on FlexibleMemoryEntry
            Files = memory.FilesInvolved,
            Fields = memory.Fields,
            Summary = memory.Content.Length > 100 ? memory.Content.Substring(0, 100) + "..." : memory.Content
        };
    }

    /// <summary>
    /// Generate usage guidance based on the result
    /// </summary>
    private static object GenerateUsageGuidance(UnifiedMemoryResult result)
    {
        return result.ExecutedIntent switch
        {
            MemoryIntent.Save => new
            {
                Intent = "Save",
                Description = "Stored new memory or created checklist",
                NextActions = new[]
                {
                    "Find related memories: memory \"find related to [topic]\"",
                    "Explore connections: memory \"explore relationships of this memory\"",
                    "Update memory: memory \"update [memory description] with [new info]\""
                }
            },
            MemoryIntent.Find => new
            {
                Intent = "Find", 
                Description = "Searched memories and found results",
                NextActions = new[]
                {
                    "Explore specific result: memory \"explore [memory id]\"",
                    "Refine search: memory \"find [topic] in [specific area]\"",
                    "Create new: memory \"save [new information about topic]\""
                }
            },
            MemoryIntent.Connect => new
            {
                Intent = "Connect",
                Description = "Linked memories together",
                NextActions = new[]
                {
                    "View connections: memory \"explore relationships\"",
                    "Find more related: memory \"find similar patterns\"",
                    "Create memory map: memory \"show knowledge graph for [topic]\""
                }
            },
            MemoryIntent.Explore => new
            {
                Intent = "Explore",
                Description = "Navigated memory relationships",
                NextActions = new[]
                {
                    "Deep dive: memory \"explore [specific aspect] further\"",
                    "Find gaps: memory \"what's missing from [topic]\"",
                    "Create summary: memory \"save overview of [topic] research\""
                }
            },
            MemoryIntent.Suggest => new
            {
                Intent = "Suggest",
                Description = "Provided recommendations",
                NextActions = new[]
                {
                    "Act on suggestion: memory \"save [implement suggestion]\"",
                    "Find more info: memory \"find details about [suggestion]\"",
                    "Track progress: memory \"create checklist for [suggestion]\""
                }
            },
            MemoryIntent.Manage => new
            {
                Intent = "Manage",
                Description = "Updated or managed existing memories",
                NextActions = new[]
                {
                    "Verify changes: memory \"find updated memories\"",
                    "Track history: memory \"show recent changes\"",
                    "Backup important: memory \"create backup of critical memories\""
                }
            },
            _ => new
            {
                Intent = "Unknown",
                Description = "Command processed",
                Examples = GetExampleCommands()
            }
        };
    }

    /// <summary>
    /// Get example commands for user guidance
    /// </summary>
    private static string[] GetExampleCommands()
    {
        return new[]
        {
            // Save examples
            "memory \"remember that UserService has a performance issue in GetActiveUsers method\"",
            "memory \"create checklist for database migration project\"",
            "memory \"save architectural decision to use microservices for user management\"",
            
            // Find examples  
            "memory \"find all technical debt related to authentication\"",
            "memory \"search for performance issues in user services\"",
            "memory \"look for architectural decisions about database design\"",
            
            // Connect examples
            "memory \"link authentication bug to security audit findings\"",
            "memory \"connect user service performance to database optimization\"",
            
            // Explore examples
            "memory \"explore relationships around authentication system\"",
            "memory \"show me the knowledge graph for user management\"",
            
            // Suggest examples
            "memory \"suggest next steps for performance optimization\"",
            "memory \"recommend improvements for authentication system\"",
            
            // Manage examples
            "memory \"update technical debt status to resolved\"",
            "memory \"archive old architectural decisions from 2023\""
        };
    }
}

/// <summary>
/// Result structure for the unified memory tool
/// </summary>
public class UnifiedMemoryToolResult
{
    /// <summary>
    /// Whether the operation was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// The detected intent that was executed
    /// </summary>
    public string Intent { get; set; } = string.Empty;

    /// <summary>
    /// Confidence level of intent detection (0.0 to 1.0)
    /// </summary>
    public float IntentConfidence { get; set; }

    /// <summary>
    /// The action that was performed
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable message about the result
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Single memory result (for save operations)
    /// </summary>
    public object? Memory { get; set; }

    /// <summary>
    /// Multiple memory results (for find operations)  
    /// </summary>
    public List<object> Memories { get; set; } = new();

    /// <summary>
    /// Count of memories found
    /// </summary>
    public int MemoryCount { get; set; }

    /// <summary>
    /// Checklist result (for checklist operations)
    /// </summary>
    public object? Checklist { get; set; }

    /// <summary>
    /// Search highlights (for find operations)
    /// </summary>
    public Dictionary<string, string[]>? Highlights { get; set; }

    /// <summary>
    /// Search facets (for find operations)
    /// </summary>
    public Dictionary<string, Dictionary<string, int>>? Facets { get; set; }

    /// <summary>
    /// Spell check information
    /// </summary>
    public object? SpellCheck { get; set; }

    /// <summary>
    /// Suggested next actions
    /// </summary>
    public List<object> NextSteps { get; set; } = new();

    /// <summary>
    /// Usage guidance and examples
    /// </summary>
    public object? Usage { get; set; }

    /// <summary>
    /// Additional metadata
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();
}