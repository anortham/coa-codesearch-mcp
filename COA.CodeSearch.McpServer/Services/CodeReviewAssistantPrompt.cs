using COA.Mcp.Protocol;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Prompt template for comprehensive code review assistance.
/// Provides systematic code review guidance with quality checks and best practices.
/// </summary>
public class CodeReviewAssistantPrompt : BasePromptTemplate
{
    public override string Name => "code-review-assistant";

    public override string Description => "Comprehensive code review assistance with quality checks, security analysis, and best practices";

    public override List<PromptArgument> Arguments => new()
    {
        new PromptArgument
        {
            Name = "review_scope",
            Description = "What to review (e.g., 'recent changes', 'UserService.cs', 'feature/auth-update', 'PR #123')",
            Required = true
        },
        new PromptArgument
        {
            Name = "workspace_path",
            Description = "Path to the workspace or project to review",
            Required = true
        },
        new PromptArgument
        {
            Name = "review_focus",
            Description = "Review focus areas: security, performance, maintainability, style, architecture, all",
            Required = false
        },
        new PromptArgument
        {
            Name = "severity_filter",
            Description = "Minimum issue severity to report: info, warning, error, critical",
            Required = false
        },
        new PromptArgument
        {
            Name = "time_period",
            Description = "Time period for recent changes (e.g., '24h', '7d', '2w')",
            Required = false
        },
        new PromptArgument
        {
            Name = "create_checklist",
            Description = "Create a follow-up checklist for addressing findings (true/false)",
            Required = false
        }
    };

    public override async Task<GetPromptResult> RenderAsync(Dictionary<string, object>? arguments = null, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask; // Satisfy async requirement
        
        var reviewScope = GetRequiredArgument<string>(arguments, "review_scope");
        var workspacePath = GetRequiredArgument<string>(arguments, "workspace_path");
        var reviewFocus = GetOptionalArgument<string>(arguments, "review_focus", "security,performance,maintainability");
        var severityFilter = GetOptionalArgument<string>(arguments, "severity_filter", "warning");
        var timePeriod = GetOptionalArgument<string>(arguments, "time_period", "7d");
        var createChecklist = GetOptionalArgument<string>(arguments, "create_checklist", "true");

        var messages = new List<PromptMessage>
        {
            CreateSystemMessage(SubstituteVariables("""
                You are an expert code reviewer specializing in comprehensive quality assessment.
                Perform a thorough code review of: {{review_scope}}
                
                Workspace: {{workspace_path}}
                Focus Areas: {{review_focus}}
                Severity Filter: {{severity_filter}}
                Time Period: {{time_period}}
                Create Checklist: {{create_checklist}}
                
                Follow this systematic code review process:
                
                **Phase 1: Scope Analysis & Planning**
                1. Use search_assistant to understand the review scope:
                   - If reviewing recent changes, use recent_files with timeFrame={{time_period}}
                   - If reviewing specific files/components, use file_search and text_search
                   - If reviewing a feature branch, analyze all related files
                
                2. Create a review strategy based on scope and focus areas
                
                **Phase 2: Automated Quality Analysis**
                1. Use pattern_detector for comprehensive analysis:
                   - Run with patternTypes matching review focus areas
                   - Set createMemories=true for significant findings
                   - Focus on security, performance, and architecture patterns
                
                2. Use file_size_analysis to identify potential issues:
                   - Find oversized files that may need refactoring
                   - Identify components with high complexity
                
                **Phase 3: Security Review**
                1. Search for common security vulnerabilities:
                   - Hard-coded credentials and secrets
                   - SQL injection patterns
                   - XSS vulnerabilities
                   - Insecure data handling
                   - Authentication/authorization issues
                
                2. Use text_search with security-focused patterns:
                   - "password", "secret", "token" in code
                   - SQL query construction patterns
                   - Input validation patterns
                
                **Phase 4: Performance Review**
                1. Identify performance anti-patterns:
                   - N+1 query problems
                   - Inefficient loops and algorithms
                   - Memory leaks and resource issues
                   - Blocking operations on main thread
                
                2. Check for optimization opportunities:
                   - Caching implementation
                   - Database query efficiency
                   - Async/await usage patterns
                
                **Phase 5: Code Quality & Maintainability**
                1. Assess code structure and organization:
                   - Single Responsibility Principle adherence
                   - DRY (Don't Repeat Yourself) violations
                   - Proper abstraction levels
                   - Clear naming conventions
                
                2. Use similar_files to detect duplication:
                   - Find copy-paste code
                   - Identify refactoring opportunities
                
                **Phase 6: Architecture & Design Review**
                1. Evaluate architectural decisions:
                   - Separation of concerns
                   - Dependency direction
                   - Interface design
                   - Error handling patterns
                
                2. Check for design pattern usage:
                   - Appropriate pattern selection
                   - Proper implementation
                   - Over-engineering detection
                
                **Phase 7: Documentation & Testing**
                1. Review documentation quality:
                   - Code comments and XML docs
                   - README and API documentation
                   - Inline documentation
                
                2. Assess testing coverage and quality:
                   - Unit test presence and quality
                   - Integration test coverage
                   - Test maintainability
                
                **Phase 8: Style & Conventions**
                1. Check coding standards adherence:
                   - Naming conventions
                   - Code formatting
                   - Language-specific best practices
                
                **Review Focus Areas:**
                
                **Security Checklist:**
                - Authentication and authorization
                - Input validation and sanitization
                - SQL injection prevention
                - XSS protection
                - Secure data storage and transmission
                - Error message information disclosure
                - Dependency vulnerabilities
                
                **Performance Checklist:**
                - Algorithm efficiency
                - Database query optimization
                - Memory usage patterns
                - Resource management
                - Async/await best practices
                - Caching strategies
                - Network call optimization
                
                **Maintainability Checklist:**
                - Code readability and clarity
                - Single Responsibility Principle
                - DRY principle adherence
                - Proper abstraction
                - Error handling consistency
                - Logging and monitoring
                - Test coverage and quality
                
                **Style Checklist:**
                - Naming conventions
                - Code formatting
                - Comment quality
                - File organization
                - Import/using statement organization
                
                **Architecture Checklist:**
                - Separation of concerns
                - Dependency inversion
                - Interface design
                - Module boundaries
                - Cross-cutting concerns
                
                Store significant findings as TechnicalDebt or SecurityRule memories.
                {{#if create_checklist}}Create a checklist for addressing the findings.{{/if}}
                """, arguments)),

            CreateUserMessage(SubstituteVariables("""
                I need a comprehensive code review for: {{review_scope}}
                Workspace: {{workspace_path}}
                Focus on: {{review_focus}}
                Report {{severity_filter}} level issues and above
                {{#if time_period}}Looking at changes from the last {{time_period}}{{/if}}
                {{#if create_checklist}}Please create an action checklist for addressing findings{{/if}}
                
                Please perform a thorough code review following best practices. Start by analyzing the scope and creating a review strategy.
                
                What's the first step to begin this code review?
                """, arguments))
        };

        return new GetPromptResult
        {
            Description = $"Code review assistant for {reviewScope} focusing on {reviewFocus}",
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