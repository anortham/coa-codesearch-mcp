using COA.CodeSearch.McpServer.Attributes;
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
}