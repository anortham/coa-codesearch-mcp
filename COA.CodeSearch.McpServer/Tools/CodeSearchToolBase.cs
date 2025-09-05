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
    /// Validates parameters with smart defaults applied first
    /// </summary>
    /// <param name="parameters">The parameters to validate</param>
    protected override void ValidateParameters(TParams parameters)
    {
        // Let base class handle null validation first
        base.ValidateParameters(parameters);
        
        // If we get here, parameters is not null (base would have thrown)
        if (parameters == null) return;

        try
        {
            // Apply smart defaults before validation
            _parameterDefaults?.ApplyDefaults(parameters);
        }
        catch (Exception ex)
        {
            throw new ValidationException($"Failed to apply parameter defaults: {ex.Message}", ex);
        }

        // Now validate with defaults applied
        base.ValidateParameters(parameters!);
    }
}