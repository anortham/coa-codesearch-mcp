using COA.CodeSearch.McpServer.Models;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Validation result for memory operations
/// </summary>
public class MemoryValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// Service for validating memory data to prevent security issues and data corruption
/// </summary>
public interface IMemoryValidationService
{
    /// <summary>
    /// Validate a memory entry before storage
    /// </summary>
    MemoryValidationResult ValidateMemory(FlexibleMemoryEntry memory);
    
    /// <summary>
    /// Validate a memory update request
    /// </summary>
    MemoryValidationResult ValidateUpdateRequest(MemoryUpdateRequest request);
    
    /// <summary>
    /// Validate custom fields
    /// </summary>
    MemoryValidationResult ValidateCustomFields(Dictionary<string, JsonElement>? fields);
    
    /// <summary>
    /// Validate file paths
    /// </summary>
    MemoryValidationResult ValidateFilePaths(IEnumerable<string>? filePaths);
}

public class MemoryValidationService : IMemoryValidationService
{
    // Configuration constants
    private const int MaxContentLength = 100_000; // 100KB
    private const int MaxFilePathsCount = 50;
    private const int MaxFilePathLength = 260; // Windows MAX_PATH
    private const int MaxCustomFieldsCount = 20;
    private const int MaxCustomFieldNameLength = 50;
    private const int MaxCustomFieldValueLength = 1000;
    
    // Reserved field names that cannot be used in custom fields
    // Only core system fields that would break the memory storage structure
    private static readonly HashSet<string> ReservedFieldNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "id", "type", "content", "created", "modified", "lastaccessed", "accesscount",
        "sessionid", "isshared", "filesinvolved", "fields"
    };
    
    // Allowed file path patterns (basic security check)
    private static readonly Regex SafeFilePathPattern = new(@"^[a-zA-Z]:[\\\/](?:[^<>:""|?*\x00-\x1f]+[\\\/])*[^<>:""|?*\x00-\x1f]*$", 
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public MemoryValidationResult ValidateMemory(FlexibleMemoryEntry memory)
    {
        var result = new MemoryValidationResult { IsValid = true };

        // Validate basic properties
        if (string.IsNullOrWhiteSpace(memory.Content))
        {
            result.Errors.Add("Content cannot be empty");
            result.IsValid = false;
        }
        else if (memory.Content.Length > MaxContentLength)
        {
            result.Errors.Add($"Content exceeds maximum length of {MaxContentLength:N0} characters");
            result.IsValid = false;
        }

        if (string.IsNullOrWhiteSpace(memory.Type))
        {
            result.Errors.Add("Type cannot be empty");
            result.IsValid = false;
        }

        // Validate content for potential security issues
        ValidateContentSecurity(memory.Content, result);

        // Validate custom fields
        var fieldsValidation = ValidateCustomFields(memory.Fields);
        result.Errors.AddRange(fieldsValidation.Errors);
        result.Warnings.AddRange(fieldsValidation.Warnings);
        if (!fieldsValidation.IsValid)
            result.IsValid = false;

        // Validate file paths
        var filePathsValidation = ValidateFilePaths(memory.FilesInvolved);
        result.Errors.AddRange(filePathsValidation.Errors);
        result.Warnings.AddRange(filePathsValidation.Warnings);
        if (!filePathsValidation.IsValid)
            result.IsValid = false;

        return result;
    }

    public MemoryValidationResult ValidateUpdateRequest(MemoryUpdateRequest request)
    {
        var result = new MemoryValidationResult { IsValid = true };

        if (string.IsNullOrWhiteSpace(request.Id))
        {
            result.Errors.Add("Memory ID cannot be empty");
            result.IsValid = false;
        }

        // Validate content if provided
        if (!string.IsNullOrEmpty(request.Content))
        {
            if (request.Content.Length > MaxContentLength)
            {
                result.Errors.Add($"Content exceeds maximum length of {MaxContentLength:N0} characters");
                result.IsValid = false;
            }
            ValidateContentSecurity(request.Content, result);
        }

        // Validate custom fields if provided
        if (request.FieldUpdates != null)
        {
            // Convert Dictionary<string, JsonElement?> to Dictionary<string, JsonElement> for validation
            var fieldsForValidation = request.FieldUpdates
                .Where(kvp => kvp.Value.HasValue)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value!.Value);
                
            var fieldsValidation = ValidateCustomFields(fieldsForValidation);
            result.Errors.AddRange(fieldsValidation.Errors);
            result.Warnings.AddRange(fieldsValidation.Warnings);
            if (!fieldsValidation.IsValid)
                result.IsValid = false;
        }

        // Validate file paths if provided
        if (request.AddFiles?.Any() == true)
        {
            var filePathsValidation = ValidateFilePaths(request.AddFiles);
            result.Errors.AddRange(filePathsValidation.Errors);
            result.Warnings.AddRange(filePathsValidation.Warnings);
            if (!filePathsValidation.IsValid)
                result.IsValid = false;
        }

        return result;
    }

    public MemoryValidationResult ValidateCustomFields(Dictionary<string, JsonElement>? fields)
    {
        var result = new MemoryValidationResult { IsValid = true };

        if (fields == null || !fields.Any())
            return result;

        if (fields.Count > MaxCustomFieldsCount)
        {
            result.Errors.Add($"Too many custom fields. Maximum allowed: {MaxCustomFieldsCount}");
            result.IsValid = false;
        }

        foreach (var field in fields)
        {
            // Validate field name
            if (string.IsNullOrWhiteSpace(field.Key))
            {
                result.Errors.Add("Custom field names cannot be empty");
                result.IsValid = false;
                continue;
            }

            if (field.Key.Length > MaxCustomFieldNameLength)
            {
                result.Errors.Add($"Custom field name '{field.Key}' exceeds maximum length of {MaxCustomFieldNameLength}");
                result.IsValid = false;
                continue;
            }

            if (ReservedFieldNames.Contains(field.Key))
            {
                var alternatives = GetAlternativesForField(field.Key);
                var errorMessage = $"RESERVED_FIELD: '{field.Key}' is reserved. " +
                                 $"Alternatives: {string.Join(", ", alternatives.Select(a => $"'{a}'"))}. " +
                                 $"(Reserved fields include: {string.Join(", ", ReservedFieldNames.Take(8).Select(f => $"'{f}'"))}...)";
                result.Errors.Add(errorMessage);
                result.IsValid = false;
                continue;
            }

            // Validate field value
            var valueString = field.Value.ValueKind == JsonValueKind.String 
                ? field.Value.GetString() 
                : field.Value.ToString();
                
            if (!string.IsNullOrEmpty(valueString))
            {
                if (valueString.Length > MaxCustomFieldValueLength)
                {
                    result.Errors.Add($"Custom field '{field.Key}' value exceeds maximum length of {MaxCustomFieldValueLength}");
                    result.IsValid = false;
                }

                // Check for potential security issues in field values
                if (ContainsPotentialXSS(valueString))
                {
                    result.Warnings.Add($"Custom field '{field.Key}' contains potentially unsafe content");
                }
            }
        }

        return result;
    }

    public MemoryValidationResult ValidateFilePaths(IEnumerable<string>? filePaths)
    {
        var result = new MemoryValidationResult { IsValid = true };

        if (filePaths == null)
            return result;

        var pathsList = filePaths.ToList();
        if (pathsList.Count > MaxFilePathsCount)
        {
            result.Errors.Add($"Too many file paths. Maximum allowed: {MaxFilePathsCount}");
            result.IsValid = false;
        }

        foreach (var filePath in pathsList)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                result.Warnings.Add("Empty file path found and will be ignored");
                continue;
            }

            if (filePath.Length > MaxFilePathLength)
            {
                result.Errors.Add($"File path exceeds maximum length: {filePath}");
                result.IsValid = false;
                continue;
            }

            // Check for path traversal attempts
            if (ContainsPathTraversal(filePath))
            {
                result.Errors.Add($"File path contains potential path traversal: {filePath}");
                result.IsValid = false;
                continue;
            }

            // Basic pattern validation for Windows paths
            if (!SafeFilePathPattern.IsMatch(filePath))
            {
                result.Warnings.Add($"File path may contain unsafe characters: {filePath}");
            }
        }

        return result;
    }

    private void ValidateContentSecurity(string content, MemoryValidationResult result)
    {
        if (string.IsNullOrEmpty(content))
            return;

        // Check for potential XSS
        if (ContainsPotentialXSS(content))
        {
            result.Warnings.Add("Content contains potentially unsafe markup that could be interpreted as script");
        }

        // Check for potential SQL injection patterns (in case content is used in queries)
        if (ContainsPotentialSQLInjection(content))
        {
            result.Warnings.Add("Content contains patterns that might be unsafe in query contexts");
        }
    }

    private bool ContainsPotentialXSS(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return false;

        var lowerInput = input.ToLowerInvariant();
        return lowerInput.Contains("<script") ||
               lowerInput.Contains("javascript:") ||
               lowerInput.Contains("vbscript:") ||
               lowerInput.Contains("onload=") ||
               lowerInput.Contains("onerror=") ||
               lowerInput.Contains("onclick=");
    }

    private bool ContainsPotentialSQLInjection(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return false;

        var lowerInput = input.ToLowerInvariant();
        return lowerInput.Contains("'; drop") ||
               lowerInput.Contains("' or ") ||
               lowerInput.Contains("union select") ||
               lowerInput.Contains("exec(") ||
               lowerInput.Contains("execute(");
    }

    private bool ContainsPathTraversal(string filePath)
    {
        return filePath.Contains("..") ||
               filePath.Contains("~") ||
               filePath.StartsWith("/") ||
               filePath.Contains("\\\\") ||
               filePath.Contains("%2e%2e") ||
               filePath.Contains("%252e%252e");
    }

    private static List<string> GetAlternativesForField(string fieldName)
    {
        return fieldName.ToLower() switch
        {
            "priority" => new() { "importance", "urgency", "priorityLevel", "rank", "severity" },
            "status" => new() { "state", "phase", "stage", "condition", "progress" },
            "tags" => new() { "labels", "categories", "keywords", "topics", "markers" },
            "author" => new() { "creator", "owner", "assignee", "responsible", "createdBy" },
            "type" => new() { "kind", "category", "classification", "variant" },
            "version" => new() { "revision", "iteration", "release", "build" },
            "workspace" => new() { "project", "context", "environment", "scope" },
            _ => new() { $"custom_{fieldName}", $"{fieldName}Value", $"my{char.ToUpper(fieldName[0])}{fieldName.Substring(1)}" }
        };
    }
}