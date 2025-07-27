using COA.Mcp.Protocol;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Prompt template that guides users through complex refactoring with safety checks.
/// Provides step-by-step guidance for safe code refactoring operations.
/// </summary>
public class RefactoringAssistantPrompt : BasePromptTemplate
{
    public override string Name => "refactoring-assistant";

    public override string Description => "Step-by-step guidance for safe code refactoring with analysis, planning, and validation";

    public override List<PromptArgument> Arguments => new()
    {
        new PromptArgument
        {
            Name = "target_pattern",
            Description = "What to refactor (e.g., 'UserService class', 'authentication logic', 'database layer')",
            Required = true
        },
        new PromptArgument
        {
            Name = "refactoring_type",
            Description = "Type of refactoring: extract_method, extract_class, rename, move, replace_pattern, modernize",
            Required = true
        },
        new PromptArgument
        {
            Name = "workspace_path",
            Description = "Path to the workspace or project to refactor",
            Required = true
        },
        new PromptArgument
        {
            Name = "test_coverage",
            Description = "Current test coverage percentage (if known)",
            Required = false
        },
        new PromptArgument
        {
            Name = "safety_level",
            Description = "Safety level: conservative, moderate, aggressive",
            Required = false
        }
    };

    public override async Task<GetPromptResult> RenderAsync(Dictionary<string, object>? arguments = null, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask; // Satisfy async requirement
        
        var targetPattern = GetRequiredArgument<string>(arguments, "target_pattern");
        var refactoringType = GetRequiredArgument<string>(arguments, "refactoring_type");
        var workspacePath = GetRequiredArgument<string>(arguments, "workspace_path");
        var testCoverage = GetOptionalArgument<string>(arguments, "test_coverage", "unknown");
        var safetyLevel = GetOptionalArgument<string>(arguments, "safety_level", "moderate");

        var messages = new List<PromptMessage>
        {
            CreateSystemMessage(SubstituteVariables("""
                You are an expert refactoring assistant specializing in safe code transformations.
                Guide the user through a systematic refactoring process for: {{target_pattern}}
                
                Refactoring Type: {{refactoring_type}}
                Workspace: {{workspace_path}}
                Test Coverage: {{test_coverage}}
                Safety Level: {{safety_level}}
                
                Follow this systematic approach:
                
                **Phase 1: Analysis & Planning**
                1. Use search_assistant to analyze the current implementation
                   - Find all occurrences of the target pattern
                   - Identify dependencies and usage patterns
                   - Map the scope of changes needed
                
                2. Use pattern_detector to assess code quality
                   - Check for anti-patterns in the target code
                   - Identify potential risks and complications
                   - Assess architectural implications
                
                3. Create a refactoring plan memory for tracking
                
                **Phase 2: Safety Preparation**
                1. Verify test coverage around the target code
                2. Create backup memories of current implementation
                3. Identify rollback strategies
                4. Plan incremental steps to minimize risk
                
                **Phase 3: Implementation Guidance**
                1. Guide through each refactoring step
                2. Suggest intermediate testing points
                3. Monitor for breaking changes
                4. Update documentation and comments
                
                **Phase 4: Validation & Cleanup**
                1. Verify all references are updated
                2. Run comprehensive tests
                3. Update related documentation
                4. Store lessons learned as memories
                
                **Available Tools for Refactoring:**
                - search_assistant: Multi-step code discovery and analysis
                - pattern_detector: Code quality and anti-pattern detection
                - text_search: Find specific code patterns
                - file_search: Locate related files
                - store_memory: Document decisions and findings
                - similar_files: Find related implementations
                
                **Safety Guidelines by Level:**
                - Conservative: Small steps, extensive testing, full rollback plan
                - Moderate: Balanced approach, good test coverage, planned rollback
                - Aggressive: Larger steps, basic testing, quick rollback
                
                Start by analyzing the current implementation and creating a detailed refactoring plan.
                """, arguments)),

            CreateUserMessage(SubstituteVariables("""
                I need to refactor: {{target_pattern}}
                Type of refactoring: {{refactoring_type}}
                Workspace: {{workspace_path}}
                {{#if test_coverage}}Current test coverage: {{test_coverage}}{{/if}}
                Safety level: {{safety_level}}
                
                Please help me create a safe, step-by-step refactoring plan. Start by analyzing the current code structure and identifying all the components that will be affected.
                
                What should be my first step?
                """, arguments))
        };

        return new GetPromptResult
        {
            Description = $"Refactoring assistant for {refactoringType} of {targetPattern}",
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