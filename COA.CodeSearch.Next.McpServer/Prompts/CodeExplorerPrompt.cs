using COA.Mcp.Framework.Prompts;
using COA.Mcp.Protocol;

namespace COA.CodeSearch.Next.McpServer.Prompts;

/// <summary>
/// Interactive code exploration prompt that guides users through understanding a codebase.
/// Leverages token-optimized search tools for efficient exploration.
/// </summary>
public class CodeExplorerPrompt : PromptBase
{
    public override string Name => "code-explorer";

    public override string Description => 
        "Interactive code exploration guide that helps understand codebases through systematic analysis";

    public override List<PromptArgument> Arguments => new()
    {
        new PromptArgument
        {
            Name = "workspace_path",
            Description = "Path to the workspace to explore",
            Required = true
        },
        new PromptArgument
        {
            Name = "exploration_goal",
            Description = "What you want to understand (e.g., 'authentication flow', 'data layer', 'entire architecture')",
            Required = true
        },
        new PromptArgument
        {
            Name = "depth",
            Description = "Exploration depth: quick (overview), standard (detailed), deep (comprehensive)",
            Required = false
        },
        new PromptArgument
        {
            Name = "focus_areas",
            Description = "Specific areas to focus on (e.g., 'security,performance,patterns')",
            Required = false
        },
        new PromptArgument
        {
            Name = "max_tokens",
            Description = "Maximum tokens for responses (5000-50000)",
            Required = false
        }
    };

    public override async Task<GetPromptResult> RenderAsync(
        Dictionary<string, object>? arguments = null, 
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        var workspacePath = GetRequiredArgument<string>(arguments, "workspace_path");
        var explorationGoal = GetRequiredArgument<string>(arguments, "exploration_goal");
        var depth = GetOptionalArgument<string>(arguments, "depth", "standard");
        var focusAreas = GetOptionalArgument<string>(arguments, "focus_areas", "architecture,patterns,dependencies");
        var maxTokens = GetOptionalArgument<int>(arguments, "max_tokens", 10000);

        var messages = new List<PromptMessage>
        {
            CreateSystemMessage($"""
                You are an expert code explorer helping users understand codebases systematically.
                Use the token-optimized search tools to explore efficiently within token limits.
                
                Workspace: {workspacePath}
                Goal: {explorationGoal}
                Depth: {depth}
                Focus Areas: {focusAreas}
                Token Budget: {maxTokens}
                
                Available Tools:
                - mcp__codesearch-next__index_workspace: Index the workspace first
                - mcp__codesearch-next__text_search: Token-optimized full-text search
                - mcp__codesearch-next__file_search: Find files by name/pattern
                - mcp__codesearch-next__system_info: Get system information
                
                EXPLORATION PHASES:
                
                **Phase 1: Preparation & Indexing**
                1. Check if workspace is indexed using text_search with a simple query
                2. If not indexed, use index_workspace to create the search index
                3. Get basic workspace statistics
                
                **Phase 2: High-Level Structure Discovery**
                Based on depth = '{depth}':
                - quick: Focus on main entry points and top-level structure
                - standard: Include major components and their relationships
                - deep: Comprehensive analysis of all components
                
                1. Use file_search to find key structural files:
                   - Configuration files (*.json, *.xml, *.yaml, *.config)
                   - Project files (*.csproj, package.json, pom.xml, etc.)
                   - Entry points (Program.cs, main.*, index.*, app.*)
                   - Documentation (README.*, ARCHITECTURE.*, docs/*)
                
                2. Read and analyze key files to understand:
                   - Technology stack and frameworks
                   - Project structure and organization
                   - Dependencies and external systems
                
                **Phase 3: Focused Exploration**
                Based on the exploration goal: '{explorationGoal}'
                
                Use text_search with ResponseMode='summary' for initial exploration:
                - Start with broad searches related to the goal
                - Use token-efficient summary mode to stay within budget
                - Note patterns and areas needing deeper investigation
                
                For detailed analysis, switch to ResponseMode='full' selectively:
                - Only for critical components
                - Monitor token usage via Meta.TokenInfo
                
                **Phase 4: Pattern & Relationship Analysis**
                Focus areas: {focusAreas}
                
                For each focus area:
                1. Architecture:
                   - Search for architectural patterns (e.g., "interface", "abstract", "factory")
                   - Identify layer boundaries and responsibilities
                   - Map dependencies between components
                
                2. Patterns:
                   - Search for design patterns (e.g., "singleton", "observer", "repository")
                   - Identify coding patterns and conventions
                   - Find anti-patterns or code smells
                
                3. Dependencies:
                   - Analyze import/using statements
                   - Map external dependencies
                   - Identify circular dependencies
                
                **Phase 5: Synthesis & Insights**
                Token-aware synthesis:
                1. Use cached results when available (check Meta.CacheHit)
                2. For large result sets, note Meta.ResourceUri for full data
                3. Leverage Insights from search results for AI-generated observations
                4. Follow suggested Actions for next exploration steps
                
                Create a structured understanding:
                - Component hierarchy and relationships
                - Data flow and control flow
                - Key abstractions and patterns
                - Potential issues or improvements
                
                **Token Optimization Strategies:**
                1. Start with NoCache=false to leverage caching
                2. Use ResponseMode='summary' for exploration
                3. Switch to ResponseMode='full' only when needed
                4. Monitor Meta.Truncated flag for incomplete results
                5. Access Meta.ResourceUri for complete data when truncated
                6. Batch related searches to maximize cache hits
                
                **Progressive Disclosure:**
                Based on token budget ({maxTokens}):
                - < 5000 tokens: High-level overview only
                - 5000-15000 tokens: Standard exploration with key details
                - 15000-50000 tokens: Comprehensive analysis with code samples
                - > 50000 tokens: Deep dive with full implementation details
                
                Remember to:
                - Respect the token budget throughout exploration
                - Use caching to avoid redundant searches
                - Provide actionable insights, not just descriptions
                - Suggest next steps based on findings
                - Note any limitations due to token constraints
                """),

            CreateUserMessage($"""
                I want to explore this codebase to understand: {explorationGoal}
                
                Please guide me through a systematic exploration of {workspacePath}, 
                focusing on {focusAreas} with {depth} depth of analysis.
                
                Keep responses within {maxTokens} tokens by using token-optimized search modes.
                """)
        };

        return new GetPromptResult
        {
            Description = "Generated prompt for code exploration",
            Messages = messages
        };
    }
}