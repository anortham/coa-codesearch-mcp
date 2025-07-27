using COA.Mcp.Protocol;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Central registry for managing MCP prompt templates and handling prompt requests.
/// Coordinates between different prompt templates to offer a unified prompt API.
/// </summary>
public class PromptRegistry : IPromptRegistry
{
    private readonly ILogger<PromptRegistry> _logger;
    private readonly Dictionary<string, IPromptTemplate> _templates = new();

    public PromptRegistry(ILogger<PromptRegistry> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<List<Prompt>> ListPromptsAsync(CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask; // Satisfy async requirement
        
        var prompts = new List<Prompt>();

        foreach (var template in _templates.Values)
        {
            try
            {
                prompts.Add(new Prompt
                {
                    Name = template.Name,
                    Description = template.Description,
                    Arguments = template.Arguments
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating prompt definition for template {TemplateName}", template.Name);
                // Continue with other templates
            }
        }

        _logger.LogDebug("Listed {Count} prompts from {TemplateCount} templates", 
            prompts.Count, _templates.Count);

        return prompts;
    }

    /// <inheritdoc />
    public async Task<GetPromptResult> GetPromptAsync(string name, Dictionary<string, object>? arguments = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Prompt name cannot be null or empty", nameof(name));
        }

        if (!_templates.TryGetValue(name, out var template))
        {
            _logger.LogWarning("Prompt template not found: {PromptName}", name);
            throw new InvalidOperationException($"Prompt template '{name}' not found");
        }

        try
        {
            // Validate arguments
            var validation = template.ValidateArguments(arguments);
            if (!validation.IsValid)
            {
                var errorMessage = $"Invalid arguments for prompt '{name}': {string.Join(", ", validation.Errors)}";
                _logger.LogWarning(errorMessage);
                throw new ArgumentException(errorMessage);
            }

            // Log warnings if any
            foreach (var warning in validation.Warnings)
            {
                _logger.LogWarning("Prompt {PromptName} argument warning: {Warning}", name, warning);
            }

            // Render the prompt
            var result = await template.RenderAsync(arguments, cancellationToken);
            
            _logger.LogDebug("Successfully rendered prompt {PromptName} with {MessageCount} messages", 
                name, result.Messages.Count);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rendering prompt {PromptName}", name);
            throw;
        }
    }

    /// <inheritdoc />
    public void RegisterPrompt(IPromptTemplate template)
    {
        if (template == null)
        {
            throw new ArgumentNullException(nameof(template));
        }

        if (string.IsNullOrWhiteSpace(template.Name))
        {
            throw new ArgumentException("Prompt template name cannot be null or empty", nameof(template));
        }

        // Check for duplicate names
        if (_templates.ContainsKey(template.Name))
        {
            _logger.LogWarning("Replacing existing prompt template: {TemplateName}", template.Name);
        }

        _templates[template.Name] = template;
        _logger.LogInformation("Registered prompt template: {TemplateName} - {Description}", 
            template.Name, template.Description);
    }
}