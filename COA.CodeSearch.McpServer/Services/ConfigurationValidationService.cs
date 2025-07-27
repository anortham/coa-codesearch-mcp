using COA.CodeSearch.McpServer.Configuration;
using COA.CodeSearch.McpServer.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.ComponentModel.DataAnnotations;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Service for validating configuration at startup to catch issues early
/// </summary>
public class ConfigurationValidationService
{
    private readonly ILogger<ConfigurationValidationService> _logger;
    private readonly IConfiguration _configuration;

    public ConfigurationValidationService(
        ILogger<ConfigurationValidationService> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Validate all configuration sections and settings
    /// </summary>
    public async Task<ConfigurationValidationResult> ValidateAllAsync()
    {
        var result = new ConfigurationValidationResult();
        var startTime = DateTime.UtcNow;

        _logger.LogInformation("Starting comprehensive configuration validation");

        try
        {
            // Validate each configuration section
            await ValidateResponseLimitsAsync(result);
            await ValidateMemoryLimitsAsync(result);
            await ValidateMemoryLifecycleAsync(result);
            await ValidateLuceneConfigurationAsync(result);
            await ValidateFileWatcherConfigurationAsync(result);
            await ValidateClaudeMemoryConfigurationAsync(result);
            await ValidatePathConfigurationAsync(result);
            await ValidateLoggingConfigurationAsync(result);

            // Overall assessment
            result.IsValid = result.Errors.Count == 0;
            result.HasWarnings = result.Warnings.Count > 0;
            result.ValidationDuration = DateTime.UtcNow - startTime;

            var status = result.IsValid ? "VALID" : "INVALID";
            var summary = $"{result.Errors.Count} errors, {result.Warnings.Count} warnings";

            _logger.LogInformation("Configuration validation completed: {Status} ({Summary}) in {Duration}ms", 
                status, summary, result.ValidationDuration.TotalMilliseconds);

            if (!result.IsValid)
            {
                _logger.LogError("Configuration validation FAILED with {ErrorCount} errors", result.Errors.Count);
                foreach (var error in result.Errors)
                {
                    _logger.LogError("CONFIG ERROR: {Error}", error);
                }
            }

            if (result.HasWarnings)
            {
                _logger.LogWarning("Configuration has {WarningCount} warnings", result.Warnings.Count);
                foreach (var warning in result.Warnings)
                {
                    _logger.LogWarning("CONFIG WARNING: {Warning}", warning);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            result.IsValid = false;
            result.Errors.Add($"Configuration validation failed with exception: {ex.Message}");
            result.ValidationDuration = DateTime.UtcNow - startTime;
            
            _logger.LogError(ex, "Configuration validation failed with exception");
            return result;
        }
    }

    private async Task ValidateResponseLimitsAsync(ConfigurationValidationResult result)
    {
        try
        {
            var section = _configuration.GetSection("ResponseLimits");
            if (!section.Exists())
            {
                result.Warnings.Add("ResponseLimits section not found - using defaults");
                return;
            }

            var config = section.Get<ResponseLimitOptions>();
            if (config != null)
            {
                var validationResults = new List<ValidationResult>();
                var context = new ValidationContext(config);
                
                if (!Validator.TryValidateObject(config, context, validationResults, true))
                {
                    foreach (var validationResult in validationResults)
                    {
                        result.Errors.Add($"ResponseLimits: {validationResult.ErrorMessage}");
                    }
                }
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            result.Errors.Add($"ResponseLimits validation failed: {ex.Message}");
        }
    }

    private async Task ValidateMemoryLimitsAsync(ConfigurationValidationResult result)
    {
        try
        {
            var section = _configuration.GetSection("MemoryLimits");
            if (!section.Exists())
            {
                result.Warnings.Add("MemoryLimits section not found - using defaults");
                return;
            }

            var config = section.Get<MemoryLimitsConfiguration>();
            if (config != null)
            {
                var validationResults = new List<ValidationResult>();
                var context = new ValidationContext(config);
                
                if (!Validator.TryValidateObject(config, context, validationResults, true))
                {
                    foreach (var validationResult in validationResults)
                    {
                        result.Errors.Add($"MemoryLimits: {validationResult.ErrorMessage}");
                    }
                }

                // Additional business logic validation
                if (config.MaxFileSize < config.LargeFileThreshold)
                {
                    result.Errors.Add("MemoryLimits: MaxFileSize must be greater than LargeFileThreshold");
                }

                if (config.MaxIndexingConcurrency > Environment.ProcessorCount * 2)
                {
                    result.Warnings.Add($"MemoryLimits: MaxIndexingConcurrency ({config.MaxIndexingConcurrency}) is very high for this system ({Environment.ProcessorCount} cores)");
                }

                if (config.MaxMemoryUsagePercent > 90)
                {
                    result.Warnings.Add("MemoryLimits: MaxMemoryUsagePercent > 90% may cause system instability");
                }
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            result.Errors.Add($"MemoryLimits validation failed: {ex.Message}");
        }
    }

    private async Task ValidateMemoryLifecycleAsync(ConfigurationValidationResult result)
    {
        try
        {
            var section = _configuration.GetSection("MemoryLifecycle");
            if (!section.Exists())
            {
                result.Warnings.Add("MemoryLifecycle section not found - using defaults");
                return;
            }

            var config = section.Get<MemoryLifecycleOptions>();
            if (config != null)
            {
                var validationResults = new List<ValidationResult>();
                var context = new ValidationContext(config);
                
                if (!Validator.TryValidateObject(config, context, validationResults, true))
                {
                    foreach (var validationResult in validationResults)
                    {
                        result.Errors.Add($"MemoryLifecycle: {validationResult.ErrorMessage}");
                    }
                }
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            result.Errors.Add($"MemoryLifecycle validation failed: {ex.Message}");
        }
    }

    private async Task ValidateLuceneConfigurationAsync(ConfigurationValidationResult result)
    {
        try
        {
            // Validate LockTimeoutMinutes
            var lockTimeout = _configuration.GetValue<int>("Lucene:LockTimeoutMinutes", 15);
            if (lockTimeout < 1 || lockTimeout > 120)
            {
                result.Warnings.Add($"Lucene:LockTimeoutMinutes ({lockTimeout}) should be between 1 and 120 minutes");
            }

            // Validate SupportedExtensions
            var extensions = _configuration.GetSection("Lucene:SupportedExtensions").Get<string[]>();
            if (extensions != null)
            {
                if (extensions.Length == 0)
                {
                    result.Errors.Add("Lucene:SupportedExtensions cannot be empty");
                }

                foreach (var ext in extensions)
                {
                    if (string.IsNullOrWhiteSpace(ext))
                    {
                        result.Errors.Add("Lucene:SupportedExtensions contains empty or whitespace entry");
                        break;
                    }

                    if (!ext.StartsWith('.'))
                    {
                        result.Warnings.Add($"Lucene:SupportedExtensions entry '{ext}' should start with a dot");
                    }
                }
            }

            // Validate ExcludedDirectories
            var excludedDirs = _configuration.GetSection("Lucene:ExcludedDirectories").Get<string[]>();
            if (excludedDirs != null)
            {
                foreach (var dir in excludedDirs)
                {
                    if (string.IsNullOrWhiteSpace(dir))
                    {
                        result.Errors.Add("Lucene:ExcludedDirectories contains empty or whitespace entry");
                        break;
                    }
                }
            }

            // Validate IndexBasePath if specified
            var indexBasePath = _configuration.GetValue<string>("Lucene:IndexBasePath");
            if (!string.IsNullOrEmpty(indexBasePath))
            {
                try
                {
                    var fullPath = Path.GetFullPath(indexBasePath);
                    // Path is valid if we get here
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Lucene:IndexBasePath '{indexBasePath}' is invalid: {ex.Message}");
                }
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Lucene configuration validation failed: {ex.Message}");
        }
    }

    private async Task ValidateFileWatcherConfigurationAsync(ConfigurationValidationResult result)
    {
        try
        {
            var excludePatterns = _configuration.GetSection("FileWatcher:ExcludePatterns").Get<string[]>();
            if (excludePatterns != null)
            {
                foreach (var pattern in excludePatterns)
                {
                    if (string.IsNullOrWhiteSpace(pattern))
                    {
                        result.Errors.Add("FileWatcher:ExcludePatterns contains empty or whitespace entry");
                        break;
                    }
                }
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            result.Errors.Add($"FileWatcher configuration validation failed: {ex.Message}");
        }
    }

    private async Task ValidateClaudeMemoryConfigurationAsync(ConfigurationValidationResult result)
    {
        try
        {
            var section = _configuration.GetSection("ClaudeMemory");
            if (section.Exists())
            {
                var config = section.Get<MemoryConfiguration>();
                if (config != null)
                {
                    // Validate business logic for ClaudeMemory settings
                    if (config.MaxSearchResults < 1 || config.MaxSearchResults > 1000)
                    {
                        result.Warnings.Add($"ClaudeMemory:MaxSearchResults ({config.MaxSearchResults}) should be between 1 and 1000");
                    }

                    if (config.TemporaryNoteRetentionDays < 1 || config.TemporaryNoteRetentionDays > 365)
                    {
                        result.Warnings.Add($"ClaudeMemory:TemporaryNoteRetentionDays ({config.TemporaryNoteRetentionDays}) should be between 1 and 365 days");
                    }

                    if (config.MinConfidenceLevel < 0 || config.MinConfidenceLevel > 100)
                    {
                        result.Errors.Add($"ClaudeMemory:MinConfidenceLevel ({config.MinConfidenceLevel}) must be between 0 and 100");
                    }

                    if (string.IsNullOrWhiteSpace(config.BasePath))
                    {
                        result.Errors.Add("ClaudeMemory:BasePath cannot be empty");
                    }

                    if (string.IsNullOrWhiteSpace(config.ProjectMemoryPath))
                    {
                        result.Errors.Add("ClaudeMemory:ProjectMemoryPath cannot be empty");
                    }

                    if (string.IsNullOrWhiteSpace(config.LocalMemoryPath))
                    {
                        result.Errors.Add("ClaudeMemory:LocalMemoryPath cannot be empty");
                    }
                }
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            result.Errors.Add($"ClaudeMemory configuration validation failed: {ex.Message}");
        }
    }

    private async Task ValidatePathConfigurationAsync(ConfigurationValidationResult result)
    {
        try
        {
            // Validate that we can resolve paths
            var basePath = Environment.CurrentDirectory;
            if (string.IsNullOrEmpty(basePath))
            {
                result.Errors.Add("Unable to determine current working directory");
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Path configuration validation failed: {ex.Message}");
        }
    }

    private async Task ValidateLoggingConfigurationAsync(ConfigurationValidationResult result)
    {
        try
        {
            var loggingSection = _configuration.GetSection("Logging");
            if (!loggingSection.Exists())
            {
                result.Warnings.Add("Logging section not found - using defaults");
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Logging configuration validation failed: {ex.Message}");
        }
    }
}

/// <summary>
/// Result of configuration validation
/// </summary>
public class ConfigurationValidationResult
{
    public bool IsValid { get; set; } = true;
    public bool HasWarnings { get; set; } = false;
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public TimeSpan ValidationDuration { get; set; }
    public DateTime ValidationTime { get; set; } = DateTime.UtcNow;
}

