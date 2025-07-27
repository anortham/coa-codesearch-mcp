using COA.Mcp.Protocol;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Prompt template that guides users through building advanced search queries.
/// Helps users discover available search operators and construct effective searches.
/// </summary>
public class AdvancedSearchBuilderPrompt : BasePromptTemplate
{
    public override string Name => "advanced-search-builder";

    public override string Description => "Interactive guide for building powerful search queries with operators, filters, and patterns";

    public override List<PromptArgument> Arguments => new()
    {
        new PromptArgument
        {
            Name = "workspace_path",
            Description = "Path to the workspace or project to search in",
            Required = true
        },
        new PromptArgument
        {
            Name = "search_type",
            Description = "Type of search to perform: text, file, recent, or pattern",
            Required = false
        },
        new PromptArgument
        {
            Name = "initial_query",
            Description = "Initial search term or pattern to start with",
            Required = false
        },
        new PromptArgument
        {
            Name = "file_extensions",
            Description = "Comma-separated list of file extensions to focus on (e.g., 'cs,js,ts')",
            Required = false
        }
    };

    public override async Task<GetPromptResult> RenderAsync(Dictionary<string, object>? arguments = null, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask; // Satisfy async requirement
        
        var workspacePath = GetRequiredArgument<string>(arguments, "workspace_path");
        var searchType = GetOptionalArgument<string>(arguments, "search_type", "text");
        var initialQuery = GetOptionalArgument<string>(arguments, "initial_query", "");
        var extensions = GetOptionalArgument<string>(arguments, "file_extensions", "");

        var messages = new List<PromptMessage>
        {
            CreateSystemMessage(SubstituteVariablesWithConditionals("""
                You are an expert at using the COA CodeSearch MCP Server tools to find code, files, and patterns.
                Help the user build an effective search query for their {{search_type}} search in workspace: {{workspace_path}}

                Available search tools and their strengths:
                
                **text_search** - Find content within files
                - Supports: literal text, wildcards (*), fuzzy matching (~), regex, phrases
                - Filters: file extensions, glob patterns, case sensitivity
                - Best for: finding specific code patterns, error messages, TODOs, configuration values
                
                **file_search** - Find files by name
                - Supports: fuzzy matching, wildcards, exact names, regex patterns
                - Best for: locating specific files, finding files with typos in names
                
                **recent_files** - Find recently modified files
                - Filters: time periods (30m, 24h, 7d), extensions, patterns
                - Best for: resuming work, reviewing recent changes, tracking progress
                
                **similar_files** - Find files with similar content
                - Uses semantic analysis to find related implementations
                - Best for: finding duplicate code, related implementations, patterns
                
                **directory_search** - Find directories/folders
                - Supports: fuzzy matching, wildcards, patterns
                - Best for: exploring project structure, finding namespaces
                
                Search operators and patterns:
                - Literal: getUserName (exact match)
                - Wildcard: get*Name (matches getFirstName, getUserName)
                - Fuzzy: getUserNam~ (finds getUserName even with typos)
                - Phrase: "get user name" (exact phrase)
                - Regex: get\\w+Name (advanced patterns)
                - Extensions: .cs,.js,.ts (filter file types)
                - Patterns: src/**/*.ts (glob patterns)
                
                Guide the user step-by-step to build an effective search.
                """, arguments)),

            CreateUserMessage(SubstituteVariablesWithConditionals("""
                I want to search in workspace: {{workspace_path}}
                {{#if initial_query}}Starting with: "{{initial_query}}"{{/if}}
                {{#if extensions}}Focusing on files: {{extensions}}{{/if}}
                {{#if search_type}}Search type: {{search_type}}{{/if}}
                
                Help me build an effective search query. What are my options and what would you recommend?
                """, arguments))
        };

        return new GetPromptResult
        {
            Description = $"Advanced search builder for {searchType} search in {workspacePath}",
            Messages = messages
        };
    }

    /// <summary>
    /// Simple template variable substitution supporting {{variable}} and {{#if variable}} blocks.
    /// </summary>
    private string SubstituteVariablesWithConditionals(string template, Dictionary<string, object>? arguments = null)
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