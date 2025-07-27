using COA.Mcp.Protocol;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Interface for components that provide MCP prompt templates.
/// Prompt templates generate interactive workflows for complex operations.
/// </summary>
public interface IPromptTemplate
{
    /// <summary>
    /// Gets the unique name of this prompt template.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the human-readable description of what this prompt accomplishes.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Gets the list of arguments that can be provided to customize this prompt.
    /// </summary>
    List<PromptArgument> Arguments { get; }

    /// <summary>
    /// Renders the prompt with the provided arguments, generating the final messages.
    /// </summary>
    /// <param name="arguments">Arguments to customize the prompt.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The rendered prompt result with messages.</returns>
    Task<GetPromptResult> RenderAsync(Dictionary<string, object>? arguments = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates that the provided arguments are correct for this prompt.
    /// </summary>
    /// <param name="arguments">Arguments to validate.</param>
    /// <returns>Validation result with any errors.</returns>
    PromptValidationResult ValidateArguments(Dictionary<string, object>? arguments = null);
}

/// <summary>
/// Represents the result of prompt argument validation.
/// </summary>
public class PromptValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();

    public static PromptValidationResult Success() => new() { IsValid = true };
    
    public static PromptValidationResult Failure(params string[] errors) => new() 
    { 
        IsValid = false, 
        Errors = errors.ToList() 
    };
}