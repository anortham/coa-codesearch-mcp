using COA.CodeSearch.McpServer.Attributes;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Tools;
using System;
using System.Threading.Tasks;

namespace COA.CodeSearch.McpServer.Examples
{
    /// <summary>
    /// Example showing how to migrate tools to the attribute-based system.
    /// This file demonstrates the pattern but is not included in the build.
    /// </summary>
    
    // BEFORE: Tool with manual registration in AllToolRegistrations.cs
    /*
    public class TextSearchTool : ITool
    {
        public async Task<object> ExecuteAsync(
            string query,
            string workspacePath,
            string? filePattern,
            string[]? extensions,
            int? contextLines,
            int? maxResults,
            bool? caseSensitive,
            string? searchType,
            string? responseMode)
        {
            // Implementation
        }
    }
    
    // In AllToolRegistrations.cs:
    private static void RegisterTextSearch(ToolRegistry registry, TextSearchTool tool)
    {
        registry.RegisterTool<TextSearchParams>(
            name: "text_search",
            description: "Searches file contents...",
            inputSchema: new { 
                type = "object",
                properties = new {
                    query = new { type = "string", description = "..." },
                    workspacePath = new { type = "string", description = "..." },
                    // ... etc
                }
            },
            handler: async (p, ct) => await tool.ExecuteAsync(
                p.Query,
                p.WorkspacePath,
                p.FilePattern,
                // ... etc
            )
        );
    }
    */

    // AFTER: Tool with attributes
    [McpServerToolType]
    public class TextSearchToolAttributeBased : ITool
    {
        // Option 1: Create a new method that takes a parameter object
        [McpServerTool(Name = "text_search")]
        [Description("Searches file contents for text patterns (literals, wildcards, regex).")]
        public async Task<object> ExecuteAsync(TextSearchParams parameters)
        {
            // Call the existing implementation
            return await ExecuteAsync(
                parameters.Query ?? parameters.SearchQuery ?? throw new InvalidParametersException("Query is required"),
                parameters.WorkspacePath ?? throw new InvalidParametersException("WorkspacePath is required"),
                parameters.FilePattern,
                parameters.Extensions,
                parameters.ContextLines,
                parameters.MaxResults ?? 50,
                parameters.CaseSensitive ?? false,
                parameters.SearchType ?? "standard",
                parameters.ResponseMode ?? "summary"
            );
        }

        // Keep the existing method for backward compatibility
        public async Task<object> ExecuteAsync(
            string query,
            string workspacePath,
            string? filePattern,
            string[]? extensions,
            int? contextLines,
            int? maxResults,
            bool? caseSensitive,
            string? searchType,
            string? responseMode)
        {
            // Existing implementation remains unchanged
            await Task.Delay(1); // Placeholder
            return new { success = true };
        }

        public string ToolName => "text_search";
        public string Description => "Text search tool";
        public ToolCategory Category => ToolCategory.Search;
    }

    // Parameter class with descriptions for schema generation
    public class TextSearchParams
    {
        [Description("Text to search for - supports wildcards (*), fuzzy (~), and phrases (\"exact match\")")]
        public string? Query { get; set; }

        [Description("[DEPRECATED] Use 'query' instead")]
        public string? SearchQuery { get; set; }

        [Description("Directory path to search in (e.g., C:\\MyProject)")]
        public string? WorkspacePath { get; set; }

        [Description("Glob pattern to filter files")]
        public string? FilePattern { get; set; }

        [Description("Filter by file extensions")]
        public string[]? Extensions { get; set; }

        [Description("Lines of context before/after matches")]
        public int? ContextLines { get; set; }

        [Description("Maximum number of results")]
        public int? MaxResults { get; set; }

        [Description("Case sensitive search")]
        public bool? CaseSensitive { get; set; }

        [Description("Search algorithm type")]
        public string? SearchType { get; set; }

        [Description("Response mode: 'summary' or 'full'")]
        public string? ResponseMode { get; set; }
    }

    // For tools without parameters (like GetVersion), it's even simpler:
    [McpServerToolType]
    public class SimpleToolExample
    {
        [McpServerTool(Name = "simple_tool")]
        [Description("A simple tool with no parameters")]
        public async Task<object> ExecuteAsync()
        {
            await Task.Delay(1);
            return new { message = "Hello from simple tool" };
        }
    }

    public class InvalidParametersException : Exception
    {
        public InvalidParametersException(string message) : base(message) { }
    }

    /// <summary>
    /// STEP-BY-STEP MIGRATION PROCESS FOR EACH TOOL
    /// </summary>
    /// 1. ADD ATTRIBUTES to the tool class and method:
    ///    - Add [McpServerToolType] to the class
    ///    - Add [McpServerTool(Name = "exact_tool_name")] to ExecuteAsync
    ///    - Add [Description("...")] with the EXACT description from AllToolRegistrations
    ///
    /// 2. CREATE PARAMETER CLASS (if tool has parameters):
    ///    - Create a class with properties matching the inputSchema from registration
    ///    - Add [Description("...")] to each property using the schema descriptions
    ///    - Create new ExecuteAsync overload that takes the parameter class
    ///    - Have it call the existing ExecuteAsync with individual parameters
    ///
    /// 3. COMMENT OUT MANUAL REGISTRATION in AllToolRegistrations.cs:
    ///    Find the line like:
    ///    RegisterFastTextSearchV2(registry, serviceProvider.GetRequiredService<FastTextSearchToolV2>());
    ///    Comment it out:
    ///    // RegisterFastTextSearchV2(registry, serviceProvider.GetRequiredService<FastTextSearchToolV2>());
    ///
    /// 4. BUILD THE PROJECT:
    ///    dotnet build -c Debug
    ///
    /// 5. TEST THE TOOL immediately:
    ///    - For simple tools: mcp__codesearch__get_version
    ///    - For tools with params: mcp__codesearch__text_search --query "test" --workspacePath "C:\project"
    ///    - Verify the tool executes successfully
    ///    - Check JSON output matches exactly what it was before
    ///
    /// 6. VERIFY no breaking changes:
    ///    - Tool should work exactly as before
    ///    - All parameters should be recognized
    ///    - Output format should be identical
    ///
    /// 7. COMMIT the migration for this specific tool:
    ///    git add -A
    ///    git commit -m "feat: Migrate [ToolName] to attribute-based registration"
    ///
    /// IMPORTANT NOTES:
    /// - By commenting out the manual registration, we force the tool to use attributes
    /// - This allows immediate testing without waiting to migrate all tools
    /// - If the tool doesn't work, you can uncomment the registration to rollback
    /// - Always preserve the EXACT tool name and description for compatibility
}