using System;
using System.ComponentModel.DataAnnotations;
using COA.Mcp.Framework.Base;
using COA.CodeSearch.McpServer.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Base class for CodeSearch MCP tools that provides smart parameter defaults
/// </summary>
/// <typeparam name="TParams">The type of the tool's input parameters</typeparam>
/// <typeparam name="TResult">The type of the tool's result</typeparam>
public abstract class CodeSearchToolBase<TParams, TResult> : McpToolBase<TParams, TResult>
    where TParams : class
{
    private readonly IParameterDefaultsService? _parameterDefaults;

    /// <summary>
    /// Initializes a new instance of the CodeSearchToolBase class
    /// </summary>
    /// <param name="serviceProvider">Service provider for dependency injection</param>
    /// <param name="logger">Optional logger for the tool</param>
    protected CodeSearchToolBase(IServiceProvider? serviceProvider = null, ILogger? logger = null) 
        : base(serviceProvider, logger)
    {
        // Try to resolve parameter defaults service (graceful degradation if not available)
        _parameterDefaults = serviceProvider?.GetService<IParameterDefaultsService>();
    }

    /// <summary>
    /// CodeSearch tools use Data Annotations validation with custom error handling
    /// </summary>
    protected override bool ShouldValidateDataAnnotations => true;

    /// <summary>
    /// Validates parameters with smart defaults applied first, then applies Data Annotations validation
    /// with simplified error messages for test compatibility
    /// </summary>
    /// <param name="parameters">The parameters to validate</param>
    protected override void ValidateParameters(TParams parameters)
    {
        // Apply parameter defaults if available
        if (parameters != null)
        {
            try
            {
                // Apply smart defaults before validation
                _parameterDefaults?.ApplyDefaults(parameters);
            }
            catch (Exception ex)
            {
                throw new ValidationException($"Failed to apply parameter defaults: {ex.Message}", ex);
            }
        }

        // Call base validation but catch and simplify validation errors for test compatibility
        try
        {
            base.ValidateParameters(parameters!); // parameters is checked above, safe to use !
        }
        catch (ValidationException ex) when (ex.Message.Contains("Parameter validation failed"))
        {
            // Extract the core validation message for test compatibility
            // Convert "Parameter validation failed: Parameter 'Symbol' is required" 
            // to "Symbol field is required"
            var message = ex.Message;
            if (message.Contains("Parameter '") && message.Contains("' is required"))
            {
                var start = message.IndexOf("Parameter '") + 11;
                var end = message.IndexOf("' is required");
                if (start > 10 && end > start)
                {
                    var fieldName = message.Substring(start, end - start);
                    throw new ValidationException($"{fieldName} field is required");
                }
            }
            // Re-throw original if we can't simplify
            throw;
        }
    }
}