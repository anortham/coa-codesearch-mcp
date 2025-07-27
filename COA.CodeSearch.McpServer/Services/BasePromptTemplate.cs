using COA.Mcp.Protocol;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Base class for implementing prompt templates with common functionality.
/// Provides argument validation and message building utilities.
/// </summary>
public abstract class BasePromptTemplate : IPromptTemplate
{
    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public abstract string Description { get; }

    /// <inheritdoc />
    public abstract List<PromptArgument> Arguments { get; }

    /// <inheritdoc />
    public abstract Task<GetPromptResult> RenderAsync(Dictionary<string, object>? arguments = null, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public virtual PromptValidationResult ValidateArguments(Dictionary<string, object>? arguments = null)
    {
        var result = new PromptValidationResult { IsValid = true };
        
        if (Arguments == null || Arguments.Count == 0)
        {
            return result; // No arguments to validate
        }

        arguments ??= new Dictionary<string, object>();

        // Check required arguments
        foreach (var arg in Arguments.Where(a => a.Required))
        {
            if (!arguments.ContainsKey(arg.Name) || arguments[arg.Name] == null)
            {
                result.IsValid = false;
                result.Errors.Add($"Required argument '{arg.Name}' is missing");
            }
        }

        // Check for unknown arguments
        var knownArguments = Arguments.Select(a => a.Name).ToHashSet();
        foreach (var providedArg in arguments.Keys)
        {
            if (!knownArguments.Contains(providedArg))
            {
                result.Warnings.Add($"Unknown argument '{providedArg}' will be ignored");
            }
        }

        return result;
    }

    /// <summary>
    /// Helper method to create a system message.
    /// </summary>
    protected PromptMessage CreateSystemMessage(string content)
    {
        return new PromptMessage
        {
            Role = "system",
            Content = new PromptContent
            {
                Type = "text",
                Text = content
            }
        };
    }

    /// <summary>
    /// Helper method to create a user message.
    /// </summary>
    protected PromptMessage CreateUserMessage(string content)
    {
        return new PromptMessage
        {
            Role = "user",
            Content = new PromptContent
            {
                Type = "text",
                Text = content
            }
        };
    }

    /// <summary>
    /// Helper method to create an assistant message.
    /// </summary>
    protected PromptMessage CreateAssistantMessage(string content)
    {
        return new PromptMessage
        {
            Role = "assistant",
            Content = new PromptContent
            {
                Type = "text",
                Text = content
            }
        };
    }

    /// <summary>
    /// Helper method to substitute variables in a template string.
    /// Variables are in the format {{variableName}}.
    /// </summary>
    protected string SubstituteVariables(string template, Dictionary<string, object>? arguments = null)
    {
        if (string.IsNullOrEmpty(template) || arguments == null || arguments.Count == 0)
        {
            return template;
        }

        var result = template;
        foreach (var kvp in arguments)
        {
            var placeholder = $"{{{{{kvp.Key}}}}}";
            var value = kvp.Value?.ToString() ?? "";
            result = result.Replace(placeholder, value);
        }

        return result;
    }

    /// <summary>
    /// Helper method to get a required argument value.
    /// </summary>
    protected T GetRequiredArgument<T>(Dictionary<string, object>? arguments, string name)
    {
        if (arguments == null || !arguments.TryGetValue(name, out var value))
        {
            throw new ArgumentException($"Required argument '{name}' is missing");
        }

        if (value is T typedValue)
        {
            return typedValue;
        }

        // Try to convert
        try
        {
            return (T)Convert.ChangeType(value, typeof(T));
        }
        catch (Exception ex)
        {
            throw new ArgumentException($"Argument '{name}' could not be converted to {typeof(T).Name}", ex);
        }
    }

    /// <summary>
    /// Helper method to get an optional argument value with a default.
    /// </summary>
    protected T GetOptionalArgument<T>(Dictionary<string, object>? arguments, string name, T defaultValue = default!)
    {
        if (arguments == null || !arguments.TryGetValue(name, out var value))
        {
            return defaultValue;
        }

        if (value is T typedValue)
        {
            return typedValue;
        }

        // Try to convert
        try
        {
            return (T)Convert.ChangeType(value, typeof(T));
        }
        catch
        {
            return defaultValue;
        }
    }
}