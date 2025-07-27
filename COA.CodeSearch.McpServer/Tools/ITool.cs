namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Marker interface for all MCP tools to enable easy discovery and documentation
/// </summary>
public interface ITool
{
    /// <summary>
    /// Gets the name of the tool as registered in the MCP protocol
    /// </summary>
    string ToolName { get; }
    
    /// <summary>
    /// Gets a description of what the tool does
    /// </summary>
    string Description { get; }
    
    /// <summary>
    /// Gets the category of the tool for organization
    /// </summary>
    ToolCategory Category { get; }
}

/// <summary>
/// Extended interface for tools that follow the async execution pattern
/// </summary>
/// <typeparam name="TParams">The parameter type for the tool</typeparam>
/// <typeparam name="TResult">The result type for the tool</typeparam>
public interface IExecutableTool<TParams, TResult> : ITool
{
    /// <summary>
    /// Executes the tool with the given parameters
    /// </summary>
    Task<TResult> ExecuteAsync(TParams parameters, CancellationToken cancellationToken = default);
}

/// <summary>
/// Tool categories for organization and discovery
/// </summary>
public enum ToolCategory
{
    /// <summary>Code navigation tools (go to definition, find references)</summary>
    Navigation,
    
    /// <summary>Search tools (text search, symbol search, file search)</summary>
    Search,
    
    /// <summary>Analysis tools (diagnostics, dependencies, metrics)</summary>
    Analysis,
    
    /// <summary>Memory system tools (store, search, manage memories)</summary>
    Memory,
    
    
    /// <summary>Infrastructure tools (indexing, logging, version)</summary>
    Infrastructure,
    
    /// <summary>Refactoring tools (rename, extract, etc.)</summary>
    Refactoring,
    
    /// <summary>Batch operation tools</summary>
    Batch
}