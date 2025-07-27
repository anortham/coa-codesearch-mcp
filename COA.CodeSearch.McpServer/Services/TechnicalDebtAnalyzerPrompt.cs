using COA.Mcp.Protocol;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Prompt template for comprehensive technical debt assessment workflow.
/// Analyzes and prioritizes technical debt across the codebase.
/// </summary>
public class TechnicalDebtAnalyzerPrompt : BasePromptTemplate
{
    public override string Name => "technical-debt-analyzer";

    public override string Description => "Comprehensive technical debt analysis and prioritization with remediation planning";

    public override List<PromptArgument> Arguments => new()
    {
        new PromptArgument
        {
            Name = "scope",
            Description = "Directory, file pattern, or specific component to analyze (e.g., 'src/', '*.cs', 'UserService')",
            Required = true
        },
        new PromptArgument
        {
            Name = "workspace_path",
            Description = "Path to the workspace or project to analyze",
            Required = true
        },
        new PromptArgument
        {
            Name = "categories",
            Description = "Types of debt to look for: code_quality, security, performance, maintainability, testing, architecture",
            Required = false
        },
        new PromptArgument
        {
            Name = "threshold",
            Description = "Minimum severity to report: low, medium, high, critical",
            Required = false
        },
        new PromptArgument
        {
            Name = "create_action_plan",
            Description = "Whether to create an actionable remediation plan (true/false)",
            Required = false
        }
    };

    public override async Task<GetPromptResult> RenderAsync(Dictionary<string, object>? arguments = null, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask; // Satisfy async requirement
        
        var scope = GetRequiredArgument<string>(arguments, "scope");
        var workspacePath = GetRequiredArgument<string>(arguments, "workspace_path");
        var categories = GetOptionalArgument<string>(arguments, "categories", "code_quality,security,performance,maintainability");
        var threshold = GetOptionalArgument<string>(arguments, "threshold", "medium");
        var createActionPlan = GetOptionalArgument<string>(arguments, "create_action_plan", "true");

        var messages = new List<PromptMessage>
        {
            CreateSystemMessage(SubstituteVariables("""
                You are a technical debt analysis expert specializing in comprehensive codebase assessment.
                Analyze technical debt in: {{scope}}
                
                Workspace: {{workspace_path}}
                Categories: {{categories}}
                Minimum Severity: {{threshold}}
                Create Action Plan: {{create_action_plan}}
                
                Follow this systematic debt analysis workflow:
                
                **Phase 1: Code Quality Assessment**
                1. Use pattern_detector to identify anti-patterns and quality issues
                   - Run with patternTypes: ['architecture', 'security', 'performance']
                   - Set createMemories=true to capture findings
                   - Focus on high-impact issues first
                
                2. Use search_assistant to find specific debt patterns:
                   - TODO and FIXME comments
                   - Commented-out code blocks
                   - Hard-coded values and magic numbers
                   - Copy-paste duplication patterns
                
                **Phase 2: Structural Analysis**
                1. Use file_size_analysis to identify oversized components
                   - Find files that may need splitting
                   - Identify potential god classes/methods
                
                2. Use similar_files to detect code duplication
                   - Find duplicate implementations
                   - Identify refactoring opportunities
                
                3. Analyze directory structure for architectural debt
                   - Check for proper separation of concerns
                   - Identify layering violations
                
                **Phase 3: Dependency Health Check**
                1. Search for dependency-related debt:
                   - Circular dependencies
                   - Tight coupling indicators
                   - Outdated dependency patterns
                
                **Phase 4: Security & Performance Debt**
                1. Use pattern_detector with security focus
                2. Search for common security anti-patterns:
                   - Hard-coded credentials
                   - SQL injection vulnerabilities
                   - Insecure data handling
                
                3. Identify performance debt:
                   - N+1 query patterns
                   - Inefficient algorithms
                   - Resource leak patterns
                
                **Phase 5: Test Coverage & Quality**
                1. Analyze test-related debt:
                   - Missing test coverage
                   - Outdated test patterns
                   - Flaky test indicators
                
                **Phase 6: Documentation & Maintainability**
                1. Find documentation debt:
                   - Missing or outdated comments
                   - Undocumented APIs
                   - Missing architectural documentation
                
                **Phase 7: Prioritization & Action Planning**
                1. Create TechnicalDebt memories for each significant issue:
                   - Include severity, effort, and impact assessment
                   - Link related issues using memory relationships
                   - Categorize by component and type
                
                2. Generate prioritized remediation plan:
                   - High-impact, low-effort items first
                   - Critical security issues prioritized
                   - Architectural improvements for long-term value
                
                **Debt Categories to Analyze:**
                - Code Quality: Duplicated code, complex methods, god classes
                - Security: Vulnerabilities, insecure patterns, data exposure
                - Performance: Slow algorithms, resource leaks, inefficient queries  
                - Maintainability: Poor naming, lack of tests, unclear structure
                - Testing: Missing coverage, outdated patterns, flaky tests
                - Architecture: Coupling, cohesion, separation of concerns
                
                **Severity Assessment Guidelines:**
                - Critical: Security vulnerabilities, system stability risks
                - High: Significant maintainability issues, performance problems
                - Medium: Code quality issues, moderate technical debt
                - Low: Minor improvements, style inconsistencies
                
                Start with a comprehensive scan and create a prioritized debt inventory.
                """, arguments)),

            CreateUserMessage(SubstituteVariables("""
                I need to analyze technical debt in: {{scope}}
                Workspace: {{workspace_path}}
                Focus on categories: {{categories}}
                Report items at {{threshold}} severity and above
                {{#if create_action_plan}}Please create an actionable remediation plan{{/if}}
                
                Please perform a comprehensive technical debt analysis. Start by scanning for the most critical issues and work systematically through all categories.
                
                What's the first step to get a baseline assessment of the current debt situation?
                """, arguments))
        };

        return new GetPromptResult
        {
            Description = $"Technical debt analysis for {scope} focusing on {categories}",
            Messages = messages
        };
    }

    /// <summary>
    /// Simple template variable substitution supporting {{variable}} and {{#if variable}} blocks.
    /// </summary>
    private string SubstituteVariables(string template, Dictionary<string, object>? arguments = null)
    {
        if (string.IsNullOrEmpty(template) || arguments == null)
        {
            return template;
        }

        var result = base.SubstituteVariables(template, arguments);

        // Handle simple conditional blocks {{#if variable}}content{{/if}}
        while (true)
        {
            var ifStart = result.IndexOf("{{#if ");
            if (ifStart == -1) break;

            var ifVarEnd = result.IndexOf("}}", ifStart);
            if (ifVarEnd == -1) break;

            var varName = result.Substring(ifStart + 6, ifVarEnd - ifStart - 6).Trim();
            var contentStart = ifVarEnd + 2;
            var ifEnd = result.IndexOf("{{/if}}", contentStart);
            if (ifEnd == -1) break;

            var content = result.Substring(contentStart, ifEnd - contentStart);
            var replacement = "";

            if (arguments.TryGetValue(varName, out var value) && 
                value != null && 
                !string.IsNullOrEmpty(value.ToString()))
            {
                replacement = content;
            }

            result = result.Substring(0, ifStart) + replacement + result.Substring(ifEnd + 7);
        }

        return result;
    }
}