using COA.Mcp.Protocol;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Service for managing and providing access to MCP prompts.
/// Prompts are interactive templates that guide users through complex operations
/// like advanced searches, memory creation, and code analysis workflows.
/// </summary>
public interface IPromptRegistry
{
    /// <summary>
    /// Gets a list of all available prompts.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of available prompts.</returns>
    Task<List<Prompt>> ListPromptsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific prompt by name with arguments applied.
    /// </summary>
    /// <param name="name">The name of the prompt to retrieve.</param>
    /// <param name="arguments">Arguments to customize the prompt.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The rendered prompt with messages.</returns>
    Task<GetPromptResult> GetPromptAsync(string name, Dictionary<string, object>? arguments = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers a prompt template that can be invoked by clients.
    /// </summary>
    /// <param name="template">The prompt template to register.</param>
    void RegisterPrompt(IPromptTemplate template);
}