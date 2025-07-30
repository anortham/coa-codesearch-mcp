using COA.CodeSearch.McpServer.Constants;
using COA.CodeSearch.McpServer.Models;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Provides workflow discovery information for AI agents to understand tool dependencies
/// </summary>
public class WorkflowDiscoveryTool : ITool
{
    private readonly ILogger<WorkflowDiscoveryTool> _logger;
    
    public string ToolName => ToolNames.WorkflowDiscovery;
    public string Description => "Discover workflow dependencies and suggested tool chains";
    public ToolCategory Category => ToolCategory.Infrastructure;
    
    public WorkflowDiscoveryTool(ILogger<WorkflowDiscoveryTool> logger)
    {
        _logger = logger;
    }
    
    /// <summary>
    /// Get workflow information for a specific tool or general workflows
    /// </summary>
    public Task<WorkflowDiscoveryResult> GetWorkflowsAsync(string? toolName = null, string? goal = null)
    {
        try
        {
            var result = new WorkflowDiscoveryResult
            {
                Success = true,
                Workflows = new List<WorkflowInfo>()
            };
            
            if (!string.IsNullOrEmpty(toolName))
            {
                // Get workflow for specific tool
                var workflow = GetWorkflowForTool(toolName);
                if (workflow != null)
                {
                    result.Workflows.Add(workflow);
                }
            }
            else if (!string.IsNullOrEmpty(goal))
            {
                // Get workflows for specific goal
                result.Workflows.AddRange(GetWorkflowsForGoal(goal));
            }
            else
            {
                // Get all common workflows
                result.Workflows.AddRange(GetAllWorkflows());
            }
            
            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting workflow information");
            return Task.FromResult(new WorkflowDiscoveryResult
            {
                Success = false,
                Error = $"Error getting workflow information: {ex.Message}"
            });
        }
    }
    
    private WorkflowInfo? GetWorkflowForTool(string toolName)
    {
        var workflows = GetAllWorkflows();
        return workflows.FirstOrDefault(w => 
            w.Steps.Any(s => s.Tool == toolName) || 
            w.Name.Contains(toolName, StringComparison.OrdinalIgnoreCase));
    }
    
    private List<WorkflowInfo> GetWorkflowsForGoal(string goal)
    {
        var allWorkflows = GetAllWorkflows();
        var dynamicWorkflows = GetDynamicWorkflowsForGoal(goal);
        var combinedWorkflows = new List<WorkflowInfo>();
        
        // Add dynamic workflows first (higher priority)
        combinedWorkflows.AddRange(dynamicWorkflows);
        
        // Then add matching predefined workflows
        var lowerGoal = goal.ToLowerInvariant();
        var keywords = ExtractKeywords(lowerGoal);
        
        var matchingWorkflows = allWorkflows.Where(w => 
        {
            // Direct match in description, name, or use cases
            if (w.Description.Contains(lowerGoal, StringComparison.OrdinalIgnoreCase) ||
                w.UseCases.Any(uc => uc.Contains(lowerGoal, StringComparison.OrdinalIgnoreCase)) ||
                w.Name.Contains(lowerGoal, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            
            // Keyword matching for better flexibility
            return keywords.Any(keyword => 
                w.Description.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                w.UseCases.Any(uc => uc.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
                w.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        });
        
        combinedWorkflows.AddRange(matchingWorkflows);
        
        // If no matches found, suggest a general search workflow
        if (!combinedWorkflows.Any())
        {
            combinedWorkflows.Add(CreateGeneralSearchWorkflow(goal));
        }
        
        return combinedWorkflows.Distinct().ToList();
    }
    
    private List<WorkflowInfo> GetAllWorkflows()
    {
        return new List<WorkflowInfo>
        {
            new WorkflowInfo
            {
                Name = "Text Search Workflow",
                Description = "Search for text content within files",
                Category = "Search",
                Steps = new List<WorkflowStep>
                {
                    new WorkflowStep
                    {
                        Tool = "index_workspace",
                        Required = true,
                        Description = "Create search index for the workspace",
                        EstimatedTime = "10-60 seconds",
                        Parameters = new Dictionary<string, object> { ["workspacePath"] = "{workspace_path}" }
                    },
                    new WorkflowStep
                    {
                        Tool = "text_search",
                        Required = true,
                        Description = "Search for text patterns",
                        Parameters = new Dictionary<string, object> 
                        { 
                            ["query"] = "{search_term}",
                            ["workspacePath"] = "{workspace_path}"
                        }
                    }
                },
                UseCases = new List<string>
                {
                    "Find code patterns",
                    "Search for error messages",
                    "Locate TODO comments",
                    "Find configuration values"
                }
            },
            
            new WorkflowInfo
            {
                Name = "File Discovery Workflow",
                Description = "Find files by name patterns",
                Category = "Search",
                Steps = new List<WorkflowStep>
                {
                    new WorkflowStep
                    {
                        Tool = "index_workspace",
                        Required = true,
                        Description = "Create search index for the workspace",
                        EstimatedTime = "10-60 seconds",
                        Parameters = new Dictionary<string, object> { ["workspacePath"] = "{workspace_path}" }
                    },
                    new WorkflowStep
                    {
                        Tool = "file_search",
                        Required = true,
                        Description = "Search for files by name",
                        Parameters = new Dictionary<string, object> 
                        { 
                            ["query"] = "{file_name}",
                            ["workspacePath"] = "{workspace_path}"
                        }
                    }
                },
                UseCases = new List<string>
                {
                    "Find specific files",
                    "Locate files with typos",
                    "Discover file patterns",
                    "Find configuration files"
                }
            },
            
            new WorkflowInfo
            {
                Name = "Code Analysis Workflow",
                Description = "Analyze codebase for patterns and issues",
                Category = "Analysis",
                Steps = new List<WorkflowStep>
                {
                    new WorkflowStep
                    {
                        Tool = "index_workspace",
                        Required = true,
                        Description = "Create search index for the workspace",
                        EstimatedTime = "10-60 seconds",
                        Parameters = new Dictionary<string, object> { ["workspacePath"] = "{workspace_path}" }
                    },
                    new WorkflowStep
                    {
                        Tool = "pattern_detector",
                        Required = true,
                        Description = "Detect code patterns and anti-patterns",
                        Parameters = new Dictionary<string, object> 
                        { 
                            ["workspacePath"] = "{workspace_path}",
                            ["patternTypes"] = new[] { "architecture", "security", "performance" }
                        }
                    }
                },
                UseCases = new List<string>
                {
                    "Code quality assessment",
                    "Security audits",
                    "Architecture reviews",
                    "Technical debt identification"
                }
            },
            
            new WorkflowInfo
            {
                Name = "Memory Management Workflow",
                Description = "Store and retrieve project knowledge",
                Category = "Memory",
                Steps = new List<WorkflowStep>
                {
                    new WorkflowStep
                    {
                        Tool = "store_memory",
                        Required = true,
                        Description = "Store important findings or decisions",
                        Parameters = new Dictionary<string, object> 
                        { 
                            ["memoryType"] = "{type}",
                            ["content"] = "{description}"
                        }
                    },
                    new WorkflowStep
                    {
                        Tool = "search_memories",
                        Required = false,
                        Description = "Find related memories",
                        Parameters = new Dictionary<string, object> 
                        { 
                            ["query"] = "{search_term}"
                        }
                    }
                },
                UseCases = new List<string>
                {
                    "Track architectural decisions",
                    "Document technical debt",
                    "Store code patterns",
                    "Maintain project insights"
                }
            },
            
            new WorkflowInfo
            {
                Name = "Multi-Operation Workflow",
                Description = "Execute multiple operations efficiently in parallel",
                Category = "Batch",
                Steps = new List<WorkflowStep>
                {
                    new WorkflowStep
                    {
                        Tool = "index_workspace",
                        Required = true,
                        Description = "Ensure workspace is indexed",
                        EstimatedTime = "10-60 seconds",
                        Parameters = new Dictionary<string, object> { ["workspacePath"] = "{workspace_path}" }
                    },
                    new WorkflowStep
                    {
                        Tool = "batch_operations",
                        Required = true,
                        Description = "Execute multiple search/analysis operations",
                        Parameters = new Dictionary<string, object> 
                        { 
                            ["operations"] = new[] { "{operation1}", "{operation2}" },
                            ["workspacePath"] = "{workspace_path}"
                        }
                    }
                },
                UseCases = new List<string>
                {
                    "Comprehensive code discovery",
                    "Multi-faceted analysis",
                    "Parallel search operations",
                    "Efficient bulk operations"
                }
            }
        };
    }
    
    private List<string> ExtractKeywords(string goal)
    {
        // Extract meaningful keywords from the goal
        var stopWords = new HashSet<string> { "find", "search", "for", "the", "a", "an", "to", "in", "of", "and", "or", "how", "where", "what", "when", "why", "all", "any" };
        var words = goal.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !stopWords.Contains(w.ToLowerInvariant()) && w.Length > 2)
            .ToList();
        
        return words;
    }
    
    private List<WorkflowInfo> GetDynamicWorkflowsForGoal(string goal)
    {
        var workflows = new List<WorkflowInfo>();
        var lowerGoal = goal.ToLowerInvariant();
        
        // Authentication/security related goals
        if (ContainsAny(lowerGoal, "auth", "login", "security", "password", "token", "oauth", "jwt"))
        {
            workflows.Add(new WorkflowInfo
            {
                Name = "Authentication Code Discovery",
                Description = "Find and analyze authentication-related code",
                Category = "Security",
                Steps = new List<WorkflowStep>
                {
                    new WorkflowStep
                    {
                        Tool = "index_workspace",
                        Required = true,
                        Description = "Index the codebase",
                        EstimatedTime = "10-60 seconds"
                    },
                    new WorkflowStep
                    {
                        Tool = "batch_operations",
                        Required = true,
                        Description = "Search for authentication patterns",
                        Parameters = new Dictionary<string, object>
                        {
                            ["operations"] = new[]
                            {
                                new { operation = "text_search", query = "authenticate OR auth OR login", searchType = "standard" },
                                new { operation = "text_search", query = "password OR token OR jwt", searchType = "standard" },
                                new { operation = "file_search", query = "*auth*", searchType = "wildcard" },
                                new { operation = "file_search", query = "*login*", searchType = "wildcard" }
                            }
                        }
                    },
                    new WorkflowStep
                    {
                        Tool = "pattern_detector",
                        Required = false,
                        Description = "Analyze for security patterns",
                        Parameters = new Dictionary<string, object>
                        {
                            ["patternTypes"] = new[] { "security" }
                        }
                    }
                },
                UseCases = new List<string>
                {
                    "Security audit",
                    "Understanding authentication flow",
                    "Finding login implementations",
                    "Reviewing security patterns"
                }
            });
        }
        
        // Performance related goals
        if (ContainsAny(lowerGoal, "performance", "slow", "optimize", "bottleneck", "speed"))
        {
            workflows.Add(new WorkflowInfo
            {
                Name = "Performance Analysis Workflow",
                Description = "Identify performance issues and optimization opportunities",
                Category = "Analysis",
                Steps = new List<WorkflowStep>
                {
                    new WorkflowStep
                    {
                        Tool = "index_workspace",
                        Required = true,
                        Description = "Index the codebase"
                    },
                    new WorkflowStep
                    {
                        Tool = "pattern_detector",
                        Required = true,
                        Description = "Detect performance anti-patterns",
                        Parameters = new Dictionary<string, object>
                        {
                            ["patternTypes"] = new[] { "performance" }
                        }
                    },
                    new WorkflowStep
                    {
                        Tool = "text_search",
                        Required = false,
                        Description = "Search for performance-related code",
                        Parameters = new Dictionary<string, object>
                        {
                            ["query"] = "async OR await OR Task OR parallel OR cache"
                        }
                    }
                },
                UseCases = new List<string>
                {
                    "Performance optimization",
                    "Finding bottlenecks",
                    "Identifying slow code",
                    "Async pattern analysis"
                }
            });
        }
        
        // Bug/error related goals
        if (ContainsAny(lowerGoal, "bug", "error", "exception", "fix", "issue", "problem"))
        {
            workflows.Add(new WorkflowInfo
            {
                Name = "Bug Investigation Workflow",
                Description = "Find and analyze potential bugs and error handling",
                Category = "Debugging",
                Steps = new List<WorkflowStep>
                {
                    new WorkflowStep
                    {
                        Tool = "index_workspace",
                        Required = true,
                        Description = "Index the codebase"
                    },
                    new WorkflowStep
                    {
                        Tool = "batch_operations",
                        Required = true,
                        Description = "Search for error patterns",
                        Parameters = new Dictionary<string, object>
                        {
                            ["operations"] = new[]
                            {
                                new { operation = "text_search", query = "TODO OR FIXME OR HACK OR BUG", searchType = "standard" },
                                new { operation = "text_search", query = "catch OR throw OR exception", searchType = "standard" },
                                new { operation = "text_search", query = "error OR fail", searchType = "standard" }
                            }
                        }
                    },
                    new WorkflowStep
                    {
                        Tool = "recent_files",
                        Required = false,
                        Description = "Check recently modified files",
                        Parameters = new Dictionary<string, object>
                        {
                            ["timeFrame"] = "7d"
                        }
                    }
                },
                UseCases = new List<string>
                {
                    "Bug hunting",
                    "Error analysis",
                    "Finding TODOs and FIXMEs",
                    "Exception handling review"
                }
            });
        }
        
        return workflows;
    }
    
    private WorkflowInfo CreateGeneralSearchWorkflow(string goal)
    {
        return new WorkflowInfo
        {
            Name = $"Custom Search: {goal}",
            Description = $"General workflow for finding information about: {goal}",
            Category = "Search",
            Steps = new List<WorkflowStep>
            {
                new WorkflowStep
                {
                    Tool = "index_workspace",
                    Required = true,
                    Description = "Ensure workspace is indexed",
                    EstimatedTime = "10-60 seconds"
                },
                new WorkflowStep
                {
                    Tool = "search_assistant",
                    Required = true,
                    Description = $"AI-guided search for: {goal}",
                    Parameters = new Dictionary<string, object>
                    {
                        ["goal"] = goal,
                        ["workspacePath"] = "{workspace_path}"
                    }
                },
                new WorkflowStep
                {
                    Tool = "store_memory",
                    Required = false,
                    Description = "Store important findings",
                    Parameters = new Dictionary<string, object>
                    {
                        ["memoryType"] = "ProjectInsight",
                        ["content"] = "{findings}"
                    }
                }
            },
            UseCases = new List<string>
            {
                "Custom searches",
                "Exploratory analysis",
                "Understanding unfamiliar codebases",
                "Finding specific patterns"
            }
        };
    }
    
    private bool ContainsAny(string text, params string[] keywords)
    {
        return keywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Result of workflow discovery
/// </summary>
public class WorkflowDiscoveryResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<WorkflowInfo> Workflows { get; set; } = new();
}

/// <summary>
/// Information about a workflow
/// </summary>
public class WorkflowInfo
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public List<WorkflowStep> Steps { get; set; } = new();
    public List<string> UseCases { get; set; } = new();
}

/// <summary>
/// A step in a workflow
/// </summary>
public class WorkflowStep
{
    public string Tool { get; set; } = "";
    public bool Required { get; set; }
    public string Description { get; set; } = "";
    public string? EstimatedTime { get; set; }
    public Dictionary<string, object> Parameters { get; set; } = new();
}