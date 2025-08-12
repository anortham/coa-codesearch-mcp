using COA.Mcp.Framework.Prompts;
using COA.Mcp.Protocol;

namespace COA.CodeSearch.McpServer.Prompts;

/// <summary>
/// Intelligent refactoring assistant that identifies and guides code improvements.
/// Uses token-optimized search to analyze patterns and suggest refactorings.
/// </summary>
public class RefactoringAssistantPrompt : PromptBase
{
    public override string Name => "refactoring-assistant";

    public override string Description => 
        "Intelligent refactoring guidance with pattern analysis and improvement suggestions";

    public override List<PromptArgument> Arguments => new()
    {
        new PromptArgument
        {
            Name = "workspace_path",
            Description = "Path to the workspace to refactor",
            Required = true
        },
        new PromptArgument
        {
            Name = "refactoring_scope",
            Description = "Scope of refactoring (e.g., 'UserService.cs', 'src/auth/', 'entire codebase')",
            Required = true
        },
        new PromptArgument
        {
            Name = "refactoring_goals",
            Description = "Goals: readability, performance, maintainability, testability, solid-principles, all",
            Required = false
        },
        new PromptArgument
        {
            Name = "complexity_threshold",
            Description = "Minimum complexity to trigger refactoring suggestions (1-10)",
            Required = false
        },
        new PromptArgument
        {
            Name = "preserve_behavior",
            Description = "Ensure behavior preservation with tests (true/false)",
            Required = false
        }
    };

    public override async Task<GetPromptResult> RenderAsync(
        Dictionary<string, object>? arguments = null,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        var workspacePath = GetRequiredArgument<string>(arguments, "workspace_path");
        var refactoringScope = GetRequiredArgument<string>(arguments, "refactoring_scope");
        var refactoringGoals = GetOptionalArgument<string>(arguments, "refactoring_goals", "readability,maintainability,solid-principles");
        var complexityThreshold = GetOptionalArgument<int>(arguments, "complexity_threshold", 5);
        var preserveBehavior = GetOptionalArgument<bool>(arguments, "preserve_behavior", true);

        var messages = new List<PromptMessage>
        {
            CreateSystemMessage($"""
                You are an expert refactoring consultant specializing in code improvement.
                Use token-optimized search to analyze code patterns and suggest refactorings.
                
                Workspace: {workspacePath}
                Scope: {refactoringScope}
                Goals: {refactoringGoals}
                Complexity Threshold: {complexityThreshold}
                Preserve Behavior: {preserveBehavior}
                
                REFACTORING METHODOLOGY:
                
                **Phase 1: Scope Analysis**
                1. Index workspace if needed with mcp__codesearch-next__index_workspace
                2. Use file_search to identify files in scope '{refactoringScope}'
                3. Estimate refactoring effort based on file count and size
                
                **Phase 2: Code Smell Detection**
                
                Use text_search with ResponseMode='summary' for efficient pattern detection:
                
                READABILITY ISSUES:
                - Long methods: Search for methods > 50 lines
                - Deep nesting: Look for multiple nested if/for/while
                - Poor naming: Single letter variables, abbreviations
                - Magic numbers: Hardcoded values without constants
                - Complex conditionals: Multiple && and || chains
                - Commented code: Large commented blocks
                
                MAINTAINABILITY ISSUES:
                - Code duplication: Similar code patterns
                - God classes: Classes with too many responsibilities
                - Feature envy: Methods using other class's data excessively
                - Inappropriate intimacy: Classes knowing too much about each other
                - Divergent change: Classes changed for multiple reasons
                - Shotgun surgery: Changes requiring many small edits
                
                SOLID PRINCIPLE VIOLATIONS:
                - Single Responsibility: Classes/methods doing too much
                - Open/Closed: Hard-coded type checks, switch on type
                - Liskov Substitution: Derived classes breaking contracts
                - Interface Segregation: Fat interfaces, unused methods
                - Dependency Inversion: Direct instantiation of dependencies
                
                PERFORMANCE ISSUES:
                - Inefficient loops: Nested loops, repeated calculations
                - String concatenation: In loops without StringBuilder
                - Excessive allocations: Creating objects unnecessarily
                - N+1 queries: Database calls in loops
                - Premature optimization: Complex code without need
                
                TESTABILITY ISSUES:
                - Tight coupling: Direct dependencies without interfaces
                - Static dependencies: Heavy use of static methods
                - Hidden dependencies: Service locator pattern
                - Non-deterministic code: Random, DateTime.Now usage
                - Side effects: Methods doing I/O unexpectedly
                
                **Phase 3: Pattern Recognition**
                
                Token-efficient pattern analysis:
                1. Start with ResponseMode='summary' for pattern scanning
                2. Group related patterns: "long method" OR "complex method" OR "god function"
                3. Use caching to avoid re-scanning: NoCache=false
                4. Note Meta.Truncated for large result sets
                5. Access Meta.ResourceUri when full analysis needed
                
                Identify improvement opportunities:
                - Extract Method: Long methods → smaller focused methods
                - Extract Class: God classes → cohesive classes
                - Replace Conditional with Polymorphism: Type checks → inheritance
                - Introduce Parameter Object: Many parameters → object
                - Replace Magic Number: Literals → named constants
                - Extract Interface: Concrete dependencies → abstractions
                
                **Phase 4: Complexity Analysis**
                
                For each file/method found:
                1. Calculate cyclomatic complexity
                2. Measure coupling and cohesion
                3. Count parameters and dependencies
                4. Identify refactoring candidates where complexity > {complexityThreshold}
                
                Use Insights from search results for AI observations
                Follow Actions for related refactoring searches
                
                **Phase 5: Test Coverage Assessment**
                
                If preserveBehavior={preserveBehavior}:
                1. Search for existing tests: "*test*", "*spec*"
                2. Identify untested code in refactoring scope
                3. Suggest test creation before refactoring
                4. Recommend characterization tests for legacy code
                
                **Phase 6: Refactoring Plan Generation**
                
                Create prioritized refactoring plan:
                
                HIGH PRIORITY (Quick wins):
                - Simple renames for clarity
                - Extract constants for magic numbers
                - Remove dead code
                - Fix obvious bugs
                
                MEDIUM PRIORITY (Structural improvements):
                - Extract methods from long functions
                - Introduce interfaces for testability
                - Consolidate duplicate code
                - Simplify complex conditionals
                
                LOW PRIORITY (Major refactorings):
                - Split god classes
                - Introduce design patterns
                - Restructure inheritance hierarchies
                - Modularize tightly coupled components
                
                For each refactoring:
                1. Current Code Analysis
                   - Location and current implementation
                   - Problems identified
                   - Complexity metrics
                
                2. Proposed Solution
                   - Refactoring technique to apply
                   - Step-by-step transformation
                   - Expected improvements
                
                3. Implementation Guide
                   - Prerequisite tests needed
                   - Refactoring steps in order
                   - Validation approach
                   - Rollback strategy
                
                4. Benefits & Risks
                   - Improved metrics
                   - Potential breaking changes
                   - Migration complexity
                
                **Phase 7: Token-Optimized Execution**
                
                Efficient refactoring workflow:
                1. Use summary mode for initial analysis (low tokens)
                2. Switch to full mode only for actual refactoring code
                3. Cache analysis results for iterative refinement
                4. Batch similar refactorings together
                5. Use resource storage for large refactoring plans
                
                Token allocation strategy:
                - Analysis phase: 30% of tokens (summary mode)
                - Planning phase: 20% of tokens (pattern matching)
                - Implementation: 50% of tokens (detailed code)
                
                **Safety Checks:**
                1. Ensure tests pass before refactoring
                2. Make small, incremental changes
                3. Validate behavior preservation after each step
                4. Document assumptions and decisions
                5. Create rollback checkpoints
                
                **Success Metrics:**
                - Reduced cyclomatic complexity
                - Improved test coverage
                - Better naming and clarity
                - Reduced duplication
                - Enhanced modularity
                - Faster build/test times
                """),

            CreateUserMessage($"""
                Please analyze {refactoringScope} in {workspacePath} for refactoring opportunities.
                
                Focus on {refactoringGoals} with complexity threshold of {complexityThreshold}.
                {(preserveBehavior ? "Ensure behavior preservation with tests." : "")}
                
                Use token-optimized search to efficiently identify patterns and provide actionable refactoring suggestions.
                """)
        };

        return new GetPromptResult
        {
            Description = "Generated prompt for refactoring assistance",
            Messages = messages
        };
    }
}