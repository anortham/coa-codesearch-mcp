using COA.Mcp.Protocol;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Provides prompt template discovery resources for AI agents to understand available
/// prompt templates, their usage patterns, and workflow integration.
/// </summary>
public class PromptTemplateResourceProvider : IResourceProvider
{
    private readonly ILogger<PromptTemplateResourceProvider> _logger;
    private readonly IPromptRegistry _promptRegistry;
    private readonly JsonSerializerOptions _jsonOptions;

    public string Scheme => "codesearch-prompts";
    public string Name => "Prompt Template Discovery";
    public string Description => "Discover available prompt templates and their usage patterns";

    public PromptTemplateResourceProvider(
        ILogger<PromptTemplateResourceProvider> logger,
        IPromptRegistry promptRegistry)
    {
        _logger = logger;
        _promptRegistry = promptRegistry;
        _jsonOptions = new JsonSerializerOptions 
        { 
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public Task<List<Resource>> ListResourcesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new List<Resource>
        {
            new Resource
            {
                Uri = "codesearch-prompts://catalog/all",
                Name = "Prompt Template Catalog",
                Description = "Complete catalog of available prompt templates with descriptions and arguments",
                MimeType = "application/json"
            },
            new Resource
            {
                Uri = "codesearch-prompts://workflows/development",
                Name = "Development Workflows",
                Description = "Common development workflows using prompt templates",
                MimeType = "application/json"
            },
            new Resource
            {
                Uri = "codesearch-prompts://categories/quality",
                Name = "Code Quality Prompts",
                Description = "Prompt templates for code quality improvement",
                MimeType = "application/json"
            },
            new Resource
            {
                Uri = "codesearch-prompts://categories/analysis",
                Name = "Analysis Prompts",
                Description = "Prompt templates for code and architecture analysis",
                MimeType = "application/json"
            },
            new Resource
            {
                Uri = "codesearch-prompts://categories/documentation",
                Name = "Documentation Prompts",
                Description = "Prompt templates for generating documentation",
                MimeType = "application/json"
            },
            new Resource
            {
                Uri = "codesearch-prompts://usage/examples",
                Name = "Usage Examples",
                Description = "Example usage patterns and argument combinations",
                MimeType = "application/json"
            },
            new Resource
            {
                Uri = "codesearch-prompts://integration/tools",
                Name = "Tool Integration",
                Description = "How prompt templates integrate with other tools",
                MimeType = "application/json"
            }
        });
    }

    public async Task<ReadResourceResult?> ReadResourceAsync(string uri, CancellationToken cancellationToken = default)
    {
        if (!CanHandle(uri))
            return null;

        try
        {
            var content = uri switch
            {
                "codesearch-prompts://catalog/all" => await GeneratePromptCatalogAsync(),
                "codesearch-prompts://workflows/development" => await GenerateDevelopmentWorkflowsAsync(),
                "codesearch-prompts://categories/quality" => await GenerateQualityPromptsAsync(),
                "codesearch-prompts://categories/analysis" => await GenerateAnalysisPromptsAsync(),
                "codesearch-prompts://categories/documentation" => await GenerateDocumentationPromptsAsync(),
                "codesearch-prompts://usage/examples" => await GenerateUsageExamplesAsync(),
                "codesearch-prompts://integration/tools" => await GenerateToolIntegrationAsync(),
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
            _logger.LogError(ex, "Failed to read prompt template resource: {Uri}", uri);
            return null;
        }
    }

    public bool CanHandle(string uri)
    {
        return uri.StartsWith("codesearch-prompts://", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> GeneratePromptCatalogAsync()
    {
        var prompts = await _promptRegistry.ListPromptsAsync();
        
        var catalog = new
        {
            TotalPrompts = prompts.Count,
            Categories = new[]
            {
                new { Name = "Quality", Count = 2, Description = "Code quality improvement and review" },
                new { Name = "Analysis", Count = 2, Description = "Code and architecture analysis" },
                new { Name = "Documentation", Count = 1, Description = "Documentation generation" },
                new { Name = "Search", Count = 1, Description = "Advanced search assistance" }
            },
            Prompts = prompts.Select(p => new
            {
                p.Name,
                p.Description,
                Arguments = p.Arguments?.Select(a => new
                {
                    a.Name,
                    a.Description,
                    a.Required
                }).ToArray(),
                Category = GetPromptCategory(p.Name),
                UseCases = GetPromptUseCases(p.Name),
                ComplexityLevel = GetPromptComplexity(p.Name)
            }).ToArray(),
            LastUpdated = DateTime.UtcNow,
            Version = "Phase 4 - AI Optimized Prompts"
        };

        return JsonSerializer.Serialize(catalog, _jsonOptions);
    }

    private async Task<string> GenerateDevelopmentWorkflowsAsync()
    {
        await Task.CompletedTask;
        
        var workflows = new
        {
            CommonWorkflows = new[]
            {
                new 
                {
                    Name = "Code Quality Assessment",
                    Description = "Comprehensive code quality review and improvement",
                    Steps = new[]
                    {
                        new { Step = 1, Prompt = "technical-debt-analyzer", Purpose = "Identify debt and quality issues" },
                        new { Step = 2, Prompt = "code-review-assistant", Purpose = "Perform detailed code review" },
                        new { Step = 3, Prompt = "refactoring-assistant", Purpose = "Plan and execute improvements" }
                    },
                    EstimatedTime = "30-60 minutes",
                    OutputValue = "Prioritized quality improvement plan with actionable steps"
                },
                new 
                {
                    Name = "Test Coverage Improvement",
                    Description = "Systematic test coverage analysis and improvement",
                    Steps = new[]
                    {
                        new { Step = 1, Prompt = "test-coverage-improver", Purpose = "Analyze current test coverage" },
                        new { Step = 2, Prompt = "code-review-assistant", Purpose = "Review existing tests for quality" },
                        new { Step = 3, Prompt = "technical-debt-analyzer", Purpose = "Identify testing-related debt" }
                    },
                    EstimatedTime = "45-90 minutes",
                    OutputValue = "Comprehensive testing strategy with coverage gaps identified"
                },
                new 
                {
                    Name = "Architecture Documentation",
                    Description = "Generate comprehensive architecture documentation",
                    Steps = new[]
                    {
                        new { Step = 1, Prompt = "architecture-documenter", Purpose = "Generate system documentation" },
                        new { Step = 2, Prompt = "technical-debt-analyzer", Purpose = "Identify architectural debt" },
                        new { Step = 3, Prompt = "refactoring-assistant", Purpose = "Plan architectural improvements" }
                    },
                    EstimatedTime = "60-120 minutes",
                    OutputValue = "Complete architecture documentation with improvement roadmap"
                },
                new 
                {
                    Name = "Legacy Code Modernization",
                    Description = "Systematic approach to modernizing legacy codebases",
                    Steps = new[]
                    {
                        new { Step = 1, Prompt = "architecture-documenter", Purpose = "Document current architecture" },
                        new { Step = 2, Prompt = "technical-debt-analyzer", Purpose = "Assess modernization needs" },
                        new { Step = 3, Prompt = "test-coverage-improver", Purpose = "Improve test safety net" },
                        new { Step = 4, Prompt = "refactoring-assistant", Purpose = "Execute modernization steps" }
                    },
                    EstimatedTime = "2-4 hours",
                    OutputValue = "Modernization plan with risk mitigation strategies"
                }
            },
            WorkflowTips = new[]
            {
                "Start with analysis prompts before making changes",
                "Use technical-debt-analyzer to prioritize improvements",
                "Always run test-coverage-improver before major refactoring",
                "Document decisions using architecture-documenter",
                "Use code-review-assistant for quality gates"
            }
        };

        return JsonSerializer.Serialize(workflows, _jsonOptions);
    }

    private async Task<string> GenerateQualityPromptsAsync()
    {
        await Task.CompletedTask;
        
        var qualityPrompts = new
        {
            Category = "Code Quality",
            Description = "Prompt templates for improving code quality and maintainability",
            Prompts = new object[]
            {
                new
                {
                    Name = "technical-debt-analyzer",
                    Purpose = "Comprehensive technical debt assessment",
                    BestUsedFor = new[] { "Quality audits", "Technical debt prioritization", "Code health assessment" },
                    ArgumentTips = new
                    {
                        scope = "Start with specific components before analyzing entire codebase",
                        categories = "Focus on 2-3 categories for actionable results",
                        threshold = "Use 'medium' for balanced reporting"
                    }
                },
                new
                {
                    Name = "code-review-assistant",
                    Purpose = "Systematic code review with quality checks",
                    BestUsedFor = new[] { "Pre-commit reviews", "Pull request analysis", "Quality gate enforcement" },
                    ArgumentTips = new
                    {
                        review_scope = "Be specific about what to review for focused results",
                        review_focus = "Prioritize security and maintainability for most impact",
                        create_checklist = "Always create checklists for follow-up tracking"
                    }
                },
                new
                {
                    Name = "refactoring-assistant",
                    Purpose = "Safe refactoring guidance with risk mitigation",
                    BestUsedFor = new[] { "Large refactoring projects", "Legacy code improvement", "Architecture evolution" },
                    ArgumentTips = new
                    {
                        safety_level = "Use 'conservative' for critical systems",
                        test_coverage = "Provide actual coverage numbers for better planning",
                        refactoring_type = "Be specific about the type of refactoring needed"
                    }
                }
            }
        };

        return JsonSerializer.Serialize(qualityPrompts, _jsonOptions);
    }

    private async Task<string> GenerateAnalysisPromptsAsync()
    {
        await Task.CompletedTask;
        
        var analysisPrompts = new
        {
            Category = "Analysis",
            Description = "Prompt templates for code and architecture analysis",
            Prompts = new object[]
            {
                new
                {
                    Name = "architecture-documenter",
                    Purpose = "Generate comprehensive architecture documentation",
                    BestUsedFor = new[] { "System documentation", "Onboarding materials", "Architecture reviews" },
                    ArgumentTips = new
                    {
                        detail_level = "Use 'detailed' for team documentation, 'comprehensive' for external stakeholders",
                        diagram_types = "Include 'component' and 'service' for most projects",
                        entry_points = "Specify main application entry points for accurate analysis"
                    }
                },
                new
                {
                    Name = "test-coverage-improver",
                    Purpose = "Systematic test coverage analysis and improvement",
                    BestUsedFor = new[] { "Test strategy planning", "Coverage gap analysis", "Quality assurance" },
                    ArgumentTips = new
                    {
                        test_types = "Focus on 'unit,integration' for best ROI",
                        priority_areas = "Always include 'critical_paths' and 'business_logic'",
                        target_coverage = "Set realistic targets based on component criticality"
                    }
                }
            }
        };

        return JsonSerializer.Serialize(analysisPrompts, _jsonOptions);
    }

    private async Task<string> GenerateDocumentationPromptsAsync()
    {
        await Task.CompletedTask;
        
        var docPrompts = new
        {
            Category = "Documentation",
            Description = "Prompt templates for generating comprehensive documentation",
            Prompts = new[]
            {
                new
                {
                    Name = "architecture-documenter",
                    Purpose = "Auto-generate architecture documentation from code",
                    DocumentationTypes = new[]
                    {
                        "System overview and purpose",
                        "Component architecture diagrams",
                        "Service interaction patterns",
                        "Data flow documentation",
                        "Deployment architecture",
                        "Integration specifications"
                    },
                    OutputFormats = new[] { "Markdown", "Mermaid diagrams", "PlantUML" },
                    BestPractices = new[]
                    {
                        "Start with entry point analysis",
                        "Generate multiple diagram types for different audiences",
                        "Include both current state and recommendations",
                        "Link documentation to actual code locations"
                    }
                }
            }
        };

        return JsonSerializer.Serialize(docPrompts, _jsonOptions);
    }

    private async Task<string> GenerateUsageExamplesAsync()
    {
        await Task.CompletedTask;
        
        var examples = new
        {
            UsageExamples = new object[]
            {
                new
                {
                    Prompt = "technical-debt-analyzer",
                    Scenario = "Analyzing a specific service for security debt",
                    Arguments = new
                    {
                        scope = "src/services/UserService.cs",
                        workspace_path = "/project/root",
                        categories = "security,performance",
                        threshold = "medium",
                        create_action_plan = "true"
                    },
                    ExpectedOutcome = "Prioritized list of security and performance issues with remediation plan"
                },
                new
                {
                    Prompt = "refactoring-assistant",
                    Scenario = "Safely refactoring a legacy authentication system",
                    Arguments = new
                    {
                        target_pattern = "legacy authentication system",
                        refactoring_type = "modernize",
                        workspace_path = "/project/root",
                        test_coverage = "45%",
                        safety_level = "conservative"
                    },
                    ExpectedOutcome = "Step-by-step refactoring plan with safety checks and rollback strategies"
                },
                new
                {
                    Prompt = "code-review-assistant",
                    Scenario = "Reviewing recent changes before deployment",
                    Arguments = new
                    {
                        review_scope = "recent changes",
                        workspace_path = "/project/root",
                        review_focus = "security,performance",
                        time_period = "24h",
                        create_checklist = "true"
                    },
                    ExpectedOutcome = "Security and performance review with deployment checklist"
                }
            },
            CommonPatterns = new string[]
            {
                "Always specify workspace_path for proper context",
                "Use specific scopes before analyzing entire codebases",
                "Combine multiple focus areas for comprehensive analysis",
                "Create checklists for follow-up tracking",
                "Set realistic coverage and quality targets"
            }
        };

        return JsonSerializer.Serialize(examples, _jsonOptions);
    }

    private async Task<string> GenerateToolIntegrationAsync()
    {
        await Task.CompletedTask;
        
        var integration = new
        {
            ToolIntegration = new
            {
                Description = "How prompt templates integrate with CodeSearch MCP tools",
                Workflow = new[]
                {
                    "Prompts guide users through systematic tool usage",
                    "Each prompt leverages multiple tools for comprehensive analysis",
                    "Results are stored as memories for future reference",
                    "Checklists track progress and ensure completeness"
                },
                ToolMapping = new
                {
                    SearchTools = new[]
                    {
                        "search_assistant: Multi-step guided search operations",
                        "text_search: Find specific patterns and code",
                        "file_search: Locate files and components",
                        "pattern_detector: Identify architectural patterns and issues"
                    },
                    AnalysisTools = new[]
                    {
                        "pattern_detector: Code quality and anti-pattern detection",
                        "file_size_analysis: Identify oversized components",
                        "similar_files: Find code duplication and patterns"
                    },
                    MemoryTools = new[]
                    {
                        "store_memory: Document findings and decisions",
                        "search_memories: Recall previous insights",
                        "link_memories: Create knowledge relationships"
                    }
                }
            },
            BestPractices = new string[]
            {
                "Use prompts as entry points for complex workflows",
                "Let prompts guide tool selection and sequencing",
                "Store important findings as memories during prompt execution",
                "Link related findings for comprehensive knowledge graphs",
                "Create checklists for tracking multi-step processes"
            }
        };

        return JsonSerializer.Serialize(integration, _jsonOptions);
    }

    private string GetPromptCategory(string promptName)
    {
        return promptName switch
        {
            "technical-debt-analyzer" => "Quality",
            "code-review-assistant" => "Quality", 
            "refactoring-assistant" => "Quality",
            "architecture-documenter" => "Documentation",
            "test-coverage-improver" => "Analysis",
            "advanced-search-builder" => "Search",
            _ => "Other"
        };
    }

    private string[] GetPromptUseCases(string promptName)
    {
        return promptName switch
        {
            "technical-debt-analyzer" => new[] { "Quality audits", "Debt prioritization", "Code health assessment" },
            "code-review-assistant" => new[] { "Pull request reviews", "Quality gates", "Pre-deployment checks" },
            "refactoring-assistant" => new[] { "Safe refactoring", "Legacy modernization", "Architecture evolution" },
            "architecture-documenter" => new[] { "System documentation", "Team onboarding", "Architecture reviews" },
            "test-coverage-improver" => new[] { "Test strategy", "Coverage analysis", "Quality assurance" },
            "advanced-search-builder" => new[] { "Complex searches", "Code discovery", "Pattern analysis" },
            _ => new[] { "General development tasks" }
        };
    }

    private string GetPromptComplexity(string promptName)
    {
        return promptName switch
        {
            "advanced-search-builder" => "Beginner",
            "code-review-assistant" => "Intermediate", 
            "technical-debt-analyzer" => "Intermediate",
            "test-coverage-improver" => "Intermediate",
            "refactoring-assistant" => "Advanced",
            "architecture-documenter" => "Advanced",
            _ => "Intermediate"
        };
    }
}