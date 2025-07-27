using COA.Mcp.Protocol;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Prompt template for improving test coverage systematically.
/// Analyzes current test coverage and provides guidance for comprehensive testing strategies.
/// </summary>
public class TestCoverageImproverPrompt : BasePromptTemplate
{
    public override string Name => "test-coverage-improver";

    public override string Description => "Systematic test coverage improvement with gap analysis and testing strategy recommendations";

    public override List<PromptArgument> Arguments => new()
    {
        new PromptArgument
        {
            Name = "target_component",
            Description = "Component to improve test coverage for (e.g., 'UserService', 'src/auth/', 'entire codebase')",
            Required = true
        },
        new PromptArgument
        {
            Name = "workspace_path",
            Description = "Path to the workspace or project",
            Required = true
        },
        new PromptArgument
        {
            Name = "current_coverage",
            Description = "Current test coverage percentage (if known)",
            Required = false
        },
        new PromptArgument
        {
            Name = "target_coverage",
            Description = "Target test coverage percentage goal",
            Required = false
        },
        new PromptArgument
        {
            Name = "test_types",
            Description = "Types of tests to focus on: unit, integration, e2e, performance, security, all",
            Required = false
        },
        new PromptArgument
        {
            Name = "priority_areas",
            Description = "Priority areas: critical_paths, business_logic, edge_cases, error_handling",
            Required = false
        }
    };

    public override async Task<GetPromptResult> RenderAsync(Dictionary<string, object>? arguments = null, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask; // Satisfy async requirement
        
        var targetComponent = GetRequiredArgument<string>(arguments, "target_component");
        var workspacePath = GetRequiredArgument<string>(arguments, "workspace_path");
        var currentCoverage = GetOptionalArgument<string>(arguments, "current_coverage", "unknown");
        var targetCoverage = GetOptionalArgument<string>(arguments, "target_coverage", "80%");
        var testTypes = GetOptionalArgument<string>(arguments, "test_types", "unit,integration");
        var priorityAreas = GetOptionalArgument<string>(arguments, "priority_areas", "critical_paths,business_logic");

        var messages = new List<PromptMessage>
        {
            CreateSystemMessage(SubstituteVariables("""
                You are a test coverage expert specializing in comprehensive testing strategies and coverage improvement.
                Improve test coverage for: {{target_component}}
                
                Workspace: {{workspace_path}}
                Current Coverage: {{current_coverage}}
                Target Coverage: {{target_coverage}}
                Test Types: {{test_types}}
                Priority Areas: {{priority_areas}}
                
                Follow this systematic test coverage improvement workflow:
                
                **Phase 1: Current State Analysis**
                1. Use search_assistant to analyze existing test structure:
                   - Find all test files and directories
                   - Identify test frameworks and patterns in use
                   - Map test organization and naming conventions
                
                2. Use file_search to discover test-related files:
                   - *Test.cs, *Tests.cs, *.test.js, *.spec.ts patterns
                   - Test configuration files
                   - Mock and fixture files
                
                3. Analyze test-to-code ratios:
                   - Count production vs test files
                   - Identify untested components
                
                **Phase 2: Gap Analysis**
                1. Use pattern_detector to identify testing anti-patterns:
                   - Missing test coverage
                   - Flaky test patterns
                   - Over-complicated test setups
                
                2. Map production code structure:
                   - Find all public APIs and methods
                   - Identify business logic components
                   - Locate critical paths and edge cases
                
                3. Use text_search to find coverage gaps:
                   - Methods without corresponding tests
                   - Complex business logic
                   - Error handling paths
                   - Configuration and setup code
                
                **Phase 3: Risk Assessment & Prioritization**
                1. Identify high-risk, low-coverage areas:
                   - Critical business logic
                   - Security-sensitive code
                   - Data processing and validation
                   - External integrations
                
                2. Use similar_files to find patterns:
                   - Similar components with good test coverage
                   - Test patterns that can be replicated
                
                **Phase 4: Testing Strategy Development**
                1. **Unit Testing Strategy:**
                   - Pure functions and methods
                   - Business logic validation
                   - Input/output validation
                   - Edge case testing
                
                2. **Integration Testing Strategy:**
                   - Component interactions
                   - Database operations
                   - External service calls
                   - Configuration loading
                
                3. **End-to-End Testing Strategy:**
                   - User workflows
                   - Critical business paths
                   - Error scenarios
                
                4. **Performance Testing Strategy:**
                   - Load testing critical paths
                   - Memory usage patterns
                   - Database query performance
                
                5. **Security Testing Strategy:**
                   - Authentication flows
                   - Authorization checks
                   - Input validation
                   - Data sanitization
                
                **Phase 5: Test Implementation Planning**
                1. Create test implementation roadmap:
                   - Priority order based on risk and impact
                   - Test complexity estimates
                   - Dependencies and prerequisites
                
                2. Design test structure:
                   - Test organization patterns
                   - Mock and fixture strategies
                   - Test data management
                   - Setup and teardown patterns
                
                **Phase 6: Coverage Gap Remediation**
                1. Generate specific test recommendations:
                   - Missing unit tests for public methods
                   - Integration tests for component interactions
                   - Edge case and error handling tests
                   - Performance and security tests
                
                2. Provide test implementation examples:
                   - Test method templates
                   - Mock setup patterns
                   - Assertion strategies
                   - Test data creation
                
                **Testing Best Practices to Apply:**
                
                **Unit Test Guidelines:**
                - Test one thing at a time
                - Use descriptive test names
                - Follow Arrange-Act-Assert pattern
                - Mock external dependencies
                - Test both happy path and edge cases
                - Ensure tests are fast and isolated
                
                **Integration Test Guidelines:**
                - Test component interactions
                - Use realistic test data
                - Test configuration and setup
                - Verify error handling
                - Test database transactions
                - Test external service interactions
                
                **Test Organization:**
                - Mirror production code structure
                - Group related tests together
                - Use consistent naming conventions
                - Separate unit, integration, and e2e tests
                - Use shared test utilities
                
                **Coverage Targets by Component Type:**
                - Business Logic: 90-95%
                - Service Layer: 80-90%
                - Controllers/APIs: 70-80%
                - Data Access: 80-90%
                - Utilities: 85-95%
                - Configuration: 60-70%
                
                **Priority Areas for Testing:**
                
                **Critical Paths:**
                - Core business workflows
                - Data persistence operations
                - Authentication and authorization
                - Payment and financial operations
                - Data validation and sanitization
                
                **Business Logic:**
                - Calculation engines
                - Rules engines
                - Workflow orchestration
                - Data transformations
                - Business validations
                
                **Edge Cases:**
                - Null and empty inputs
                - Boundary conditions
                - Concurrent operations
                - Resource exhaustion
                - Network failures
                
                **Error Handling:**
                - Exception scenarios
                - Validation failures
                - External service failures
                - Database connection issues
                - Configuration errors
                
                Store testing insights as ProjectInsight memories and create TechnicalDebt memories for coverage gaps.
                """, arguments)),

            CreateUserMessage(SubstituteVariables("""
                I want to improve test coverage for: {{target_component}}
                Workspace: {{workspace_path}}
                {{#if current_coverage}}Current coverage: {{current_coverage}}{{/if}}
                Target coverage: {{target_coverage}}
                Focus on: {{test_types}}
                Priority areas: {{priority_areas}}
                
                Please analyze the current test situation and create a comprehensive plan to improve test coverage. Start by understanding what tests already exist and identifying the biggest gaps.
                
                What's the first step to assess the current testing landscape?
                """, arguments))
        };

        return new GetPromptResult
        {
            Description = $"Test coverage improvement assistant for {targetComponent}",
            Messages = messages
        };
    }

    /// <summary>
    /// Simple template variable substitution supporting {{variable}} and {{#if variable}} blocks.
    /// </summary>
    private new string SubstituteVariables(string template, Dictionary<string, object>? arguments = null)
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