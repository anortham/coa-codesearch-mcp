using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Service for applying smart defaults to tool parameters
/// </summary>
public class ParameterDefaultsService : IParameterDefaultsService
{
    private readonly IPathResolutionService _pathResolutionService;
    private readonly ILogger<ParameterDefaultsService> _logger;

    public ParameterDefaultsService(
        IPathResolutionService pathResolutionService,
        ILogger<ParameterDefaultsService> logger)
    {
        _pathResolutionService = pathResolutionService;
        _logger = logger;
    }

    public void ApplyDefaults<T>(T parameters) where T : class
    {
        if (parameters == null) return;

        var type = typeof(T);
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var property in properties)
        {
            // Apply WorkspacePath default if empty and property is named WorkspacePath
            if (property.Name == "WorkspacePath" && 
                property.PropertyType == typeof(string) && 
                property.CanWrite)
            {
                var currentValue = (string?)property.GetValue(parameters);
                if (string.IsNullOrWhiteSpace(currentValue))
                {
                    var primaryWorkspace = _pathResolutionService.GetPrimaryWorkspacePath();
                    property.SetValue(parameters, primaryWorkspace);
                    _logger.LogDebug("Applied default WorkspacePath: {WorkspacePath} for {ParameterType}", 
                        primaryWorkspace, type.Name);
                }
            }
            
            // Apply NavigateToFirstResult default (set to true for better UX)
            else if (property.Name == "NavigateToFirstResult" && 
                     property.PropertyType == typeof(bool) && 
                     property.CanWrite)
            {
                var currentValue = (bool)property.GetValue(parameters)!;
                if (!currentValue) // Only set if it's false (default)
                {
                    property.SetValue(parameters, true);
                    _logger.LogDebug("Applied default NavigateToFirstResult: true for {ParameterType}", type.Name);
                }
            }
            
            // Apply MaxTokens default if not set
            else if (property.Name == "MaxTokens" && 
                     (property.PropertyType == typeof(int) || property.PropertyType == typeof(int?)) && 
                     property.CanWrite)
            {
                var currentValue = property.GetValue(parameters);
                if (currentValue == null || (currentValue is int intVal && intVal == 0))
                {
                    property.SetValue(parameters, 8000);
                    _logger.LogDebug("Applied default MaxTokens: 8000 for {ParameterType}", type.Name);
                }
            }
            
            // Apply MaxResults default if not set
            else if (property.Name == "MaxResults" && 
                     (property.PropertyType == typeof(int) || property.PropertyType == typeof(int?)) && 
                     property.CanWrite)
            {
                var currentValue = property.GetValue(parameters);
                if (currentValue == null || (currentValue is int intVal && intVal == 0))
                {
                    var defaultValue = property.Name.Contains("Symbol") ? 20 : 50; // SymbolSearch gets 20, others get 50
                    property.SetValue(parameters, defaultValue);
                    _logger.LogDebug("Applied default MaxResults: {MaxResults} for {ParameterType}", 
                        defaultValue, type.Name);
                }
            }
            
            // Apply ResponseMode default if not set
            else if (property.Name == "ResponseMode" && 
                     property.PropertyType == typeof(string) && 
                     property.CanWrite)
            {
                var currentValue = (string?)property.GetValue(parameters);
                if (string.IsNullOrWhiteSpace(currentValue))
                {
                    property.SetValue(parameters, "adaptive");
                    _logger.LogDebug("Applied default ResponseMode: adaptive for {ParameterType}", type.Name);
                }
            }
            
            // Apply NoCache default (generally prefer cached results)
            else if (property.Name == "NoCache" && 
                     property.PropertyType == typeof(bool) && 
                     property.CanWrite)
            {
                // NoCache defaults to false (use cache for better performance)
                var currentValue = (bool)property.GetValue(parameters)!;
                if (currentValue) // Only log if it was explicitly set to true
                {
                    _logger.LogDebug("NoCache explicitly set to true for {ParameterType}", type.Name);
                }
                // Don't override if already set to true - user explicitly wants fresh results
            }
            
            // Apply CaseSensitive default (context-aware)
            else if (property.Name == "CaseSensitive" && 
                     property.PropertyType == typeof(bool) && 
                     property.CanWrite)
            {
                // Default false for general searches, but preserve explicit true settings
                var currentValue = (bool)property.GetValue(parameters)!;
                if (!currentValue) // Only log when using default
                {
                    _logger.LogDebug("Using default CaseSensitive: false for {ParameterType}", type.Name);
                }
            }
            
            // Apply ContextLines default
            else if (property.Name == "ContextLines" && 
                     (property.PropertyType == typeof(int) || property.PropertyType == typeof(int?)) && 
                     property.CanWrite)
            {
                var currentValue = property.GetValue(parameters);
                if (currentValue == null || (currentValue is int intVal && intVal == 0))
                {
                    property.SetValue(parameters, 5);
                    _logger.LogDebug("Applied default ContextLines: 5 for {ParameterType}", type.Name);
                }
            }
            
            // Apply IncludeFullContext default (context-aware)
            else if (property.Name == "IncludeFullContext" && 
                     property.PropertyType == typeof(bool) && 
                     property.CanWrite)
            {
                var currentValue = (bool)property.GetValue(parameters)!;
                if (!currentValue)
                {
                    // Default to false for performance, but allow explicit override
                    _logger.LogDebug("Using default IncludeFullContext: false for {ParameterType}", type.Name);
                }
            }
            
            // Apply SearchType default
            else if (property.Name == "SearchType" && 
                     property.PropertyType == typeof(string) && 
                     property.CanWrite)
            {
                var currentValue = (string?)property.GetValue(parameters);
                if (string.IsNullOrWhiteSpace(currentValue))
                {
                    property.SetValue(parameters, "standard");
                    _logger.LogDebug("Applied default SearchType: standard for {ParameterType}", type.Name);
                }
            }
            
            // Apply DocumentFindings default
            else if (property.Name == "DocumentFindings" && 
                     property.PropertyType == typeof(bool) && 
                     property.CanWrite)
            {
                // Default false to avoid noise
                var currentValue = (bool)property.GetValue(parameters)!;
                if (!currentValue)
                {
                    _logger.LogDebug("Using default DocumentFindings: false for {ParameterType}", type.Name);
                }
            }
            
            // Apply AutoDetectPatterns default  
            else if (property.Name == "AutoDetectPatterns" && 
                     property.PropertyType == typeof(bool) && 
                     property.CanWrite)
            {
                // Default false to avoid noise
                var currentValue = (bool)property.GetValue(parameters)!;
                if (!currentValue)
                {
                    _logger.LogDebug("Using default AutoDetectPatterns: false for {ParameterType}", type.Name);
                }
            }
            
            // Apply IncludePotential default (for FindReferences)
            else if (property.Name == "IncludePotential" && 
                     property.PropertyType == typeof(bool) && 
                     property.CanWrite)
            {
                // Default false for precision
                var currentValue = (bool)property.GetValue(parameters)!;
                if (!currentValue)
                {
                    _logger.LogDebug("Using default IncludePotential: false for {ParameterType}", type.Name);
                }
            }
            
            // Apply GroupByFile default (for FindReferences)
            else if (property.Name == "GroupByFile" && 
                     property.PropertyType == typeof(bool) && 
                     property.CanWrite)
            {
                var currentValue = (bool)property.GetValue(parameters)!;
                if (!currentValue)
                {
                    property.SetValue(parameters, true); // Default true for better organization
                    _logger.LogDebug("Applied default GroupByFile: true for {ParameterType}", type.Name);
                }
            }
        }
    }

    public ValidationResult[] ValidateAfterDefaults<T>(T parameters) where T : class
    {
        var validationResults = new List<ValidationResult>();
        var validationContext = new ValidationContext(parameters);
        
        Validator.TryValidateObject(parameters, validationContext, validationResults, validateAllProperties: true);
        
        return validationResults.ToArray();
    }
}