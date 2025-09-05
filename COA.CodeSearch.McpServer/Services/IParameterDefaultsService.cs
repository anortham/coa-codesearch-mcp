using System.ComponentModel.DataAnnotations;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Service for applying smart defaults to tool parameters
/// </summary>
public interface IParameterDefaultsService
{
    /// <summary>
    /// Applies smart defaults to tool parameters, particularly WorkspacePath
    /// </summary>
    /// <typeparam name="T">The parameter type</typeparam>
    /// <param name="parameters">The parameters to apply defaults to</param>
    void ApplyDefaults<T>(T parameters) where T : class;
    
    /// <summary>
    /// Validates that all required parameters have values after applying defaults
    /// </summary>
    /// <typeparam name="T">The parameter type</typeparam>
    /// <param name="parameters">The parameters to validate</param>
    /// <returns>Validation results</returns>
    ValidationResult[] ValidateAfterDefaults<T>(T parameters) where T : class;
}