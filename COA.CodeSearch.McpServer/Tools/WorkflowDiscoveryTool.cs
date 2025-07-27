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
        var lowerGoal = goal.ToLowerInvariant();
        
        return allWorkflows.Where(w => 
            w.Description.Contains(lowerGoal, StringComparison.OrdinalIgnoreCase) ||
            w.UseCases.Any(uc => uc.Contains(lowerGoal, StringComparison.OrdinalIgnoreCase)) ||
            w.Name.Contains(lowerGoal, StringComparison.OrdinalIgnoreCase)
        ).ToList();
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