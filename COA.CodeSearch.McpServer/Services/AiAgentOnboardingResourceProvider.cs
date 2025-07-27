using COA.Mcp.Protocol;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Provides onboarding resources for AI agents to understand and effectively use the CodeSearch MCP server.
/// Offers step-by-step workflows, best practices, and interactive guides.
/// </summary>
public class AiAgentOnboardingResourceProvider : IResourceProvider
{
    private readonly ILogger<AiAgentOnboardingResourceProvider> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public string Scheme => "codesearch-onboarding";
    public string Name => "AI Agent Onboarding";
    public string Description => "Interactive onboarding and best practices for AI agents using CodeSearch";

    public AiAgentOnboardingResourceProvider(ILogger<AiAgentOnboardingResourceProvider> logger)
    {
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions 
        { 
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public async Task<List<Resource>> ListResourcesAsync(CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask; // Keep async for interface compliance
        
        return new List<Resource>
        {
            new Resource
            {
                Uri = "codesearch-onboarding://quickstart/first-time",
                Name = "First-Time Agent Setup",
                Description = "Essential first steps for AI agents new to CodeSearch",
                MimeType = "application/json"
            },
            new Resource
            {
                Uri = "codesearch-onboarding://workflows/common",
                Name = "Common Workflows",
                Description = "Step-by-step guides for the most common AI agent tasks",
                MimeType = "application/json"
            },
            new Resource
            {
                Uri = "codesearch-onboarding://best-practices/efficiency",
                Name = "Efficiency Best Practices",
                Description = "How to use CodeSearch efficiently and avoid common mistakes",
                MimeType = "application/json"
            },
            new Resource
            {
                Uri = "codesearch-onboarding://troubleshooting/common-issues",
                Name = "Common Issues & Solutions",
                Description = "Solutions to frequently encountered problems",
                MimeType = "application/json"
            },
            new Resource
            {
                Uri = "codesearch-onboarding://progressive-disclosure/guide",
                Name = "Progressive Disclosure Guide",
                Description = "How to handle large responses and detail requests effectively",
                MimeType = "application/json"
            },
            new Resource
            {
                Uri = "codesearch-onboarding://memory-system/mastery",
                Name = "Memory System Mastery",
                Description = "Advanced techniques for using the memory system effectively",
                MimeType = "application/json"
            }
        };
    }

    public async Task<ReadResourceResult?> ReadResourceAsync(string uri, CancellationToken cancellationToken = default)
    {
        if (!CanHandle(uri))
            return null;

        try
        {
            var content = uri switch
            {
                "codesearch-onboarding://quickstart/first-time" => await GenerateFirstTimeSetupAsync(),
                "codesearch-onboarding://workflows/common" => await GenerateCommonWorkflowsAsync(),
                "codesearch-onboarding://best-practices/efficiency" => await GenerateEfficiencyBestPracticesAsync(),
                "codesearch-onboarding://troubleshooting/common-issues" => await GenerateCommonIssuesAsync(),
                "codesearch-onboarding://progressive-disclosure/guide" => await GenerateProgressiveDisclosureGuideAsync(),
                "codesearch-onboarding://memory-system/mastery" => await GenerateMemorySystemMasteryAsync(),
                _ => null
            };

            if (content == null)
                return null;

            return new ReadResourceResult
            {
                Contents = new List<ResourceContent>
                {
                    new ResourceContent
                    {
                        Uri = uri,
                        MimeType = "application/json",
                        Text = content
                    }
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read onboarding resource: {Uri}", uri);
            return null;
        }
    }

    public bool CanHandle(string uri)
    {
        return uri.StartsWith("codesearch-onboarding://", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> GenerateFirstTimeSetupAsync()
    {
        await Task.CompletedTask;
        
        var setup = new Dictionary<string, object>
        {
            ["title"] = "First-Time AI Agent Setup for CodeSearch",
            ["overview"] = "Essential steps to get started with CodeSearch as an AI agent",
            ["prerequisites"] = new[]
            {
                "CodeSearch MCP server is running and connected",
                "You have access to a workspace/codebase to analyze",
                "Basic understanding of MCP tool calling"
            },
            ["quickStartSteps"] = new object[]
            {
                new Dictionary<string, object>
                {
                    ["step"] = 1,
                    ["title"] = "Test Connection",
                    ["action"] = "get_version",
                    ["purpose"] = "Verify CodeSearch is running and get version info",
                    ["expectedResult"] = "Version information with build date",
                    ["exampleCall"] = new { tool = "mcp__codesearch__get_version" }
                },
                new Dictionary<string, object>
                {
                    ["step"] = 2,
                    ["title"] = "Load Previous Context",
                    ["action"] = "recall_context",
                    ["purpose"] = "Restore knowledge from previous sessions",
                    ["parameters"] = new { query = "describe your previous work or what you're looking for" },
                    ["exampleCall"] = new 
                    { 
                        tool = "mcp__codesearch__recall_context",
                        parameters = new { query = "authentication implementation" }
                    }
                },
                new Dictionary<string, object>
                {
                    ["step"] = 3,
                    ["title"] = "Index Your Workspace",
                    ["action"] = "index_workspace",
                    ["purpose"] = "Create searchable index of all files (required for search operations)",
                    ["parameters"] = new { workspacePath = "absolute path to your project" },
                    ["exampleCall"] = new 
                    { 
                        tool = "mcp__codesearch__index_workspace",
                        parameters = new { workspacePath = "C:/MyProject" }
                    },
                    ["warning"] = "This may take 30-60 seconds for large codebases"
                },
                new Dictionary<string, object>
                {
                    ["step"] = 4,
                    ["title"] = "Explore with Search Assistant",
                    ["action"] = "search_assistant",
                    ["purpose"] = "Use AI-guided search to understand the codebase",
                    ["parameters"] = new 
                    { 
                        goal = "describe what you want to understand",
                        workspacePath = "same path from step 3"
                    },
                    ["exampleCall"] = new 
                    { 
                        tool = "mcp__codesearch__search_assistant",
                        parameters = new 
                        { 
                            goal = "Find the main entry points and understand the application architecture",
                            workspacePath = "C:/MyProject"
                        }
                    }
                }
            },
            ["nextSteps"] = new[]
            {
                "Review the memory system guide for storing important findings",
                "Explore pattern detection for code quality analysis",
                "Learn about progressive disclosure for handling large responses"
            },
            ["keyTools"] = new[]
            {
                "search_assistant - Your primary exploration tool",
                "recall_context - Always start sessions with this",
                "pattern_detector - For comprehensive code analysis",
                "store_memory - To save important discoveries"
            }
        };

        return JsonSerializer.Serialize(setup, _jsonOptions);
    }

    private async Task<string> GenerateCommonWorkflowsAsync()
    {
        await Task.CompletedTask;
        
        var workflows = new Dictionary<string, object>
        {
            ["title"] = "Common AI Agent Workflows",
            ["description"] = "Step-by-step guides for the most frequent tasks",
            ["workflows"] = new object[]
            {
                new Dictionary<string, object>
                {
                    ["name"] = "New Codebase Analysis",
                    ["description"] = "Comprehensive analysis of an unfamiliar codebase",
                    ["estimatedTime"] = "10-15 minutes",
                    ["steps"] = new object[]
                    {
                        new { Step = 1, Tool = "index_workspace", Purpose = "Create searchable index" },
                        new { Step = 2, Tool = "search_assistant", Purpose = "Find main entry points", Goal = "Find main application entry points and key services" },
                        new { Step = 3, Tool = "pattern_detector", Purpose = "Analyze architecture patterns", Parameters = "patternTypes: ['architecture', 'security']" },
                        new { Step = 4, Tool = "store_memory", Purpose = "Document key findings", Type = "ArchitecturalDecision" },
                        new { Step = 5, Tool = "search_assistant", Purpose = "Explore specific areas", Goal = "Based on findings from step 3" }
                    },
                    ["outputValue"] = "Comprehensive understanding of codebase structure and potential issues"
                }
            },
            ["bestPractices"] = new[]
            {
                "Always start with recall_context to restore previous knowledge",
                "Use search_assistant for complex, multi-step discovery",
                "Store important findings as memories for future reference",
                "Use progressive disclosure (summary mode) for large responses",
                "Combine multiple tools for comprehensive analysis"
            }
        };

        return JsonSerializer.Serialize(workflows, _jsonOptions);
    }

    private async Task<string> GenerateEfficiencyBestPracticesAsync()
    {
        await Task.CompletedTask;
        
        var practices = new Dictionary<string, object>
        {
            ["title"] = "Efficiency Best Practices for AI Agents",
            ["description"] = "How to use CodeSearch efficiently and avoid common pitfalls",
            ["keyPrinciples"] = new[]
            {
                "Progressive disclosure: Start with summaries, drill down as needed",
                "Context preservation: Use memory system to maintain state across sessions",
                "Smart search: Use search_assistant instead of individual search tools",
                "Batch operations: Use batch_operations for multiple related searches"
            },
            ["commonMistakes"] = new[]
            {
                "Not indexing workspace before searching (causes errors)",
                "Using individual search tools instead of search_assistant",
                "Requesting full details immediately instead of reviewing summaries",
                "Not using recall_context at session start (loses previous knowledge)",
                "Storing temporary information as permanent memories"
            }
        };

        return JsonSerializer.Serialize(practices, _jsonOptions);
    }

    private async Task<string> GenerateCommonIssuesAsync()
    {
        await Task.CompletedTask;
        
        var issues = new Dictionary<string, object>
        {
            ["title"] = "Common Issues & Solutions",
            ["description"] = "Solutions to frequently encountered problems",
            ["issues"] = new object[]
            {
                new Dictionary<string, object>
                {
                    ["problem"] = "All search operations return 'INDEX_NOT_FOUND' error",
                    ["cause"] = "Workspace not indexed or index corrupted",
                    ["solution"] = "Run index_workspace tool with your workspace path",
                    ["prevention"] = "Always index before searching; run index health checks periodically"
                },
                new Dictionary<string, object>
                {
                    ["problem"] = "Responses are truncated or incomplete",
                    ["cause"] = "Response exceeds token limits",
                    ["solution"] = "Use summary mode and request details selectively using detail tokens",
                    ["example"] = "Look for 'detailRequestToken' in metadata, then use it to get specific details"
                }
            }
        };

        return JsonSerializer.Serialize(issues, _jsonOptions);
    }

    private async Task<string> GenerateProgressiveDisclosureGuideAsync()
    {
        await Task.CompletedTask;
        
        var guide = new Dictionary<string, object>
        {
            ["title"] = "Progressive Disclosure Guide",
            ["description"] = "How to handle large responses and detail requests effectively",
            ["concept"] = "Progressive disclosure prevents token overload by showing summaries first, then allowing targeted detail requests",
            ["autoThreshold"] = "Tools automatically switch to summary mode when responses exceed 5,000 tokens",
            ["bestPractices"] = new[]
            {
                "Always review summaries before requesting details",
                "Use hotspot details to focus on high-impact areas",
                "Request full details only when you need comprehensive information",
                "Pay attention to token estimates to manage conversation length",
                "Use summary insights to guide your next actions"
            }
        };

        return JsonSerializer.Serialize(guide, _jsonOptions);
    }

    private async Task<string> GenerateMemorySystemMasteryAsync()
    {
        await Task.CompletedTask;
        
        var mastery = new Dictionary<string, object>
        {
            ["title"] = "Memory System Mastery",
            ["description"] = "Advanced techniques for using the memory system effectively",
            ["philosophy"] = "The memory system is your AI agent's long-term knowledge store, designed to preserve insights across sessions and enable continuous learning",
            ["memoryTypes"] = new[]
            {
                "ArchitecturalDecision - Major design choices and their rationale",
                "TechnicalDebt - Code improvements needed with priority and effort estimates", 
                "CodePattern - Reusable patterns and anti-patterns discovered",
                "SecurityRule - Security requirements and vulnerabilities",
                "ProjectInsight - High-level understanding and discoveries"
            },
            ["relationships"] = new[]
            {
                "blockedBy - Memory A cannot proceed until B is resolved",
                "implements - Memory A implements the decision/pattern in B",
                "supersedes - Memory A replaces or updates B",
                "dependsOn - Memory A requires B to function",
                "references - Memory A mentions or relates to B"
            }
        };

        return JsonSerializer.Serialize(mastery, _jsonOptions);
    }
}