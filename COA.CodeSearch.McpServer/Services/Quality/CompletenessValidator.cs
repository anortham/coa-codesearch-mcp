using COA.CodeSearch.McpServer.Models;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace COA.CodeSearch.McpServer.Services.Quality;

/// <summary>
/// Validates that memories contain sufficient and complete information
/// </summary>
public class CompletenessValidator : IQualityValidator
{
    public string Name => "completeness";
    public string Description => "Validates that memories contain sufficient and complete information";
    public double Weight => 0.3;

    public async Task<QualityValidatorResult> ValidateAsync(
        FlexibleMemoryEntry memory, 
        QualityValidationOptions options)
    {
        var result = new QualityValidatorResult();
        var scores = new List<double>();

        // Content length assessment
        var contentScore = ValidateContentLength(memory, result);
        scores.Add(contentScore);

        // Field completeness assessment
        var fieldScore = ValidateFieldCompleteness(memory, result);
        scores.Add(fieldScore);

        // File references assessment
        var fileScore = ValidateFileReferences(memory, result);
        scores.Add(fileScore);

        // Context completeness assessment
        var contextScore = ValidateContextCompleteness(memory, result);
        scores.Add(contextScore);

        result.Score = scores.Any() ? scores.Average() : 0;
        
        AddCompletionSuggestions(memory, result);
        
        await Task.CompletedTask;
        return result;
    }

    private double ValidateContentLength(FlexibleMemoryEntry memory, QualityValidatorResult result)
    {
        var content = memory.Content ?? string.Empty;
        var wordCount = content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var charCount = content.Length;
        
        // Score based on content length
        double score = 1.0;
        
        if (charCount < 10)
        {
            result.Issues.Add(new QualityIssue
            {
                Category = "Content Length",
                Description = "Memory content is too short (less than 10 characters)",
                Severity = QualitySeverity.Major,
                Field = "content"
            });
            score = 0.2;
        }
        else if (charCount < 30)
        {
            result.Issues.Add(new QualityIssue
            {
                Category = "Content Length",
                Description = "Memory content is very brief and may lack detail",
                Severity = QualitySeverity.Minor,
                Field = "content"
            });
            score = 0.6;
        }
        else if (wordCount < 5)
        {
            result.Issues.Add(new QualityIssue
            {
                Category = "Content Length",
                Description = "Memory content has very few words and may lack context",
                Severity = QualitySeverity.Minor,
                Field = "content"
            });
            score = 0.7;
        }

        // Bonus for well-structured content
        if (HasStructuredContent(content))
        {
            score = Math.Min(1.0, score + 0.1);
        }

        return score;
    }

    private double ValidateFieldCompleteness(FlexibleMemoryEntry memory, QualityValidatorResult result)
    {
        var fields = memory.Fields ?? new Dictionary<string, JsonElement>();
        var memoryType = memory.Type ?? "Default";
        
        // Define expected fields by memory type
        var expectedFields = GetExpectedFieldsForType(memoryType);
        var recommendedFields = GetRecommendedFieldsForType(memoryType);
        
        var missingRequired = expectedFields.Where(f => !fields.ContainsKey(f)).ToList();
        var missingRecommended = recommendedFields.Where(f => !fields.ContainsKey(f)).ToList();
        
        double score = 1.0;
        
        foreach (var missing in missingRequired)
        {
            result.Issues.Add(new QualityIssue
            {
                Category = "Required Fields",
                Description = $"Missing required field '{missing}' for {memoryType} memory",
                Severity = QualitySeverity.Major,
                Field = missing
            });
            score -= 0.3;
        }
        
        foreach (var missing in missingRecommended)
        {
            result.Issues.Add(new QualityIssue
            {
                Category = "Recommended Fields",
                Description = $"Missing recommended field '{missing}' for {memoryType} memory",
                Severity = QualitySeverity.Minor,
                Field = missing
            });
            score -= 0.1;
        }

        // Check for empty field values
        var emptyFields = fields.Where(f => IsEmptyJsonElement(f.Value)).ToList();
        foreach (var empty in emptyFields)
        {
            result.Issues.Add(new QualityIssue
            {
                Category = "Field Values",
                Description = $"Field '{empty.Key}' is present but empty",
                Severity = QualitySeverity.Minor,
                Field = empty.Key
            });
            score -= 0.05;
        }

        return Math.Max(0, score);
    }

    private double ValidateFileReferences(FlexibleMemoryEntry memory, QualityValidatorResult result)
    {
        var files = memory.FilesInvolved ?? Array.Empty<string>();
        var memoryType = memory.Type ?? "Default";
        
        double score = 1.0;
        
        // Check if file references are expected for this memory type
        var expectsFiles = ShouldHaveFileReferences(memoryType);
        
        if (expectsFiles && !files.Any())
        {
            result.Issues.Add(new QualityIssue
            {
                Category = "File References",
                Description = $"{memoryType} memories typically should reference specific files",
                Severity = QualitySeverity.Minor,
                Field = "filesInvolved"
            });
            score = 0.7;
        }

        // Validate file path formats
        var invalidPaths = files.Where(f => !IsValidFilePath(f)).ToList();
        foreach (var invalid in invalidPaths)
        {
            result.Issues.Add(new QualityIssue
            {
                Category = "File References",
                Description = $"Invalid file path format: '{invalid}'",
                Severity = QualitySeverity.Minor,
                Field = "filesInvolved",
                Location = invalid
            });
            score -= 0.1;
        }

        return Math.Max(0, score);
    }

    private double ValidateContextCompleteness(FlexibleMemoryEntry memory, QualityValidatorResult result)
    {
        var content = memory.Content ?? string.Empty;
        double score = 1.0;
        
        // Check for essential context elements
        var hasWhatElement = HasWhatDescription(content);
        var hasWhyElement = HasWhyDescription(content);
        var hasWhenElement = HasWhenDescription(content);
        var hasWhereElement = HasWhereDescription(content);
        
        if (!hasWhatElement)
        {
            result.Issues.Add(new QualityIssue
            {
                Category = "Context Completeness",
                Description = "Memory lacks clear description of 'what' (the main topic/issue)",
                Severity = QualitySeverity.Minor,
                Field = "content"
            });
            score -= 0.2;
        }

        if (!hasWhyElement && RequiresRationale(memory.Type))
        {
            result.Issues.Add(new QualityIssue
            {
                Category = "Context Completeness",
                Description = "Memory lacks explanation of 'why' (rationale/reasoning)",
                Severity = QualitySeverity.Minor,
                Field = "content"
            });
            score -= 0.15;
        }

        return Math.Max(0, score);
    }

    private void AddCompletionSuggestions(FlexibleMemoryEntry memory, QualityValidatorResult result)
    {
        var content = memory.Content ?? string.Empty;
        var memoryType = memory.Type ?? "Default";

        if (content.Length < 50)
        {
            result.Suggestions.Add(new QualityImprovement
            {
                Category = "Content Enhancement",
                Description = "Expand memory content with more details and context",
                ActionText = "Add more descriptive information about the topic",
                Type = QualityImprovementType.ContentExpansion,
                ExpectedImpact = 0.3,
                CanAutoImplement = false
            });
        }

        var missingFields = GetExpectedFieldsForType(memoryType)
            .Where(f => !(memory.Fields?.ContainsKey(f) ?? false))
            .ToList();

        foreach (var field in missingFields)
        {
            result.Suggestions.Add(new QualityImprovement
            {
                Category = "Field Completion",
                Description = $"Add required field '{field}' for {memoryType} memory",
                ActionText = $"Add {field} field with appropriate value",
                Type = QualityImprovementType.TaggingEnhancement,
                ExpectedImpact = 0.2,
                CanAutoImplement = false
            });
        }

        if (!(memory.FilesInvolved?.Any() ?? false) && ShouldHaveFileReferences(memoryType))
        {
            result.Suggestions.Add(new QualityImprovement
            {
                Category = "Context Enhancement",
                Description = "Add file references to provide better context",
                ActionText = "Include paths to relevant files",
                Type = QualityImprovementType.ContextEnrichment,
                ExpectedImpact = 0.15,
                CanAutoImplement = false
            });
        }
    }

    private bool HasStructuredContent(string content)
    {
        // Check for structured elements like bullet points, numbers, sections
        return Regex.IsMatch(content, @"(\d+\.|•|\*|-)\s+") || 
               content.Contains("\n\n") || 
               Regex.IsMatch(content, @"^(##?|\*\*).+", RegexOptions.Multiline);
    }

    private List<string> GetExpectedFieldsForType(string memoryType)
    {
        return memoryType switch
        {
            "TechnicalDebt" => new List<string> { "priority", "effort" },
            "ArchitecturalDecision" => new List<string> { "decision", "rationale" },
            "CodePattern" => new List<string> { "pattern", "usage" },
            "SecurityRule" => new List<string> { "rule", "rationale", "enforcement" },
            _ => new List<string>()
        };
    }

    private List<string> GetRecommendedFieldsForType(string memoryType)
    {
        return memoryType switch
        {
            "TechnicalDebt" => new List<string> { "impact", "deadline", "owner" },
            "ArchitecturalDecision" => new List<string> { "alternatives", "consequences", "status" },
            "CodePattern" => new List<string> { "examples", "when-to-use", "alternatives" },
            "SecurityRule" => new List<string> { "violations", "exceptions", "references" },
            _ => new List<string>()
        };
    }

    private bool ShouldHaveFileReferences(string memoryType)
    {
        return memoryType switch
        {
            "TechnicalDebt" => true,
            "CodePattern" => true,
            "SecurityRule" => true,
            _ => false
        };
    }

    private bool RequiresRationale(string? memoryType)
    {
        return memoryType switch
        {
            "ArchitecturalDecision" => true,
            "SecurityRule" => true,
            _ => false
        };
    }

    private bool IsValidFilePath(string path)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(path) && 
                   path.Length <= 260 && 
                   !path.Any(c => Path.GetInvalidPathChars().Contains(c));
        }
        catch
        {
            return false;
        }
    }

    private bool IsEmptyJsonElement(JsonElement element)
    {
        return element.ValueKind == JsonValueKind.Null ||
               (element.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(element.GetString()));
    }

    private bool HasWhatDescription(string content)
    {
        // Simple heuristic: content should describe what something is/does
        return content.Length > 20 && !string.IsNullOrWhiteSpace(content);
    }

    private bool HasWhyDescription(string content)
    {
        // Look for rationale indicators
        var indicators = new[] { "because", "due to", "reason", "rationale", "why", "purpose", "goal" };
        return indicators.Any(indicator => content.ToLowerInvariant().Contains(indicator));
    }

    private bool HasWhenDescription(string content)
    {
        // Look for temporal context
        var indicators = new[] { "when", "during", "after", "before", "while", "timeline" };
        return indicators.Any(indicator => content.ToLowerInvariant().Contains(indicator));
    }

    private bool HasWhereDescription(string content)
    {
        // Look for location/context indicators
        var indicators = new[] { "where", "in", "at", "location", "context", "scope", "area" };
        return indicators.Any(indicator => content.ToLowerInvariant().Contains(indicator));
    }
}