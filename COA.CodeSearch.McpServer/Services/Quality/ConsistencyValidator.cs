using COA.CodeSearch.McpServer.Models;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace COA.CodeSearch.McpServer.Services.Quality;

/// <summary>
/// Validates that memories are internally consistent and follow established patterns
/// </summary>
public class ConsistencyValidator : IQualityValidator
{
    public string Name => "consistency";
    public string Description => "Validates that memories are internally consistent and follow established patterns";
    public double Weight => 0.2;

    public async Task<QualityValidatorResult> ValidateAsync(
        FlexibleMemoryEntry memory, 
        QualityValidationOptions options)
    {
        var result = new QualityValidatorResult();
        var scores = new List<double>();

        // Internal consistency assessment
        var internalScore = ValidateInternalConsistency(memory, result);
        scores.Add(internalScore);

        // Field consistency assessment
        var fieldScore = ValidateFieldConsistency(memory, result);
        scores.Add(fieldScore);

        // Format consistency assessment
        var formatScore = ValidateFormatConsistency(memory, result);
        scores.Add(formatScore);

        // Naming consistency assessment
        var namingScore = ValidateNamingConsistency(memory, result);
        scores.Add(namingScore);

        result.Score = scores.Any() ? scores.Average() : 0;
        
        AddConsistencySuggestions(memory, result);
        
        await Task.CompletedTask;
        return result;
    }

    private double ValidateInternalConsistency(FlexibleMemoryEntry memory, QualityValidatorResult result)
    {
        var content = memory.Content ?? string.Empty;
        var memoryType = memory.Type ?? "Default";
        double score = 1.0;

        // Check for contradictory statements
        if (HasContradictoryStatements(content))
        {
            result.Issues.Add(new QualityIssue
            {
                Category = "Internal Consistency",
                Description = "Memory contains potentially contradictory statements",
                Severity = QualitySeverity.Major,
                Field = "content"
            });
            score -= 0.4;
        }

        // Check if content tone matches memory type
        if (!ContentToneMatchesType(content, memoryType))
        {
            result.Issues.Add(new QualityIssue
            {
                Category = "Tone Consistency",
                Description = $"Content tone doesn't match expected tone for {memoryType} memory",
                Severity = QualitySeverity.Minor,
                Field = "content"
            });
            score -= 0.2;
        }

        // Check for consistent terminology
        var inconsistentTerms = FindInconsistentTerminology(content);
        if (inconsistentTerms.Any())
        {
            result.Issues.Add(new QualityIssue
            {
                Category = "Terminology Consistency",
                Description = $"Inconsistent terminology used: {string.Join(", ", inconsistentTerms)}",
                Severity = QualitySeverity.Minor,
                Field = "content"
            });
            score -= 0.1;
        }

        return Math.Max(0, score);
    }

    private double ValidateFieldConsistency(FlexibleMemoryEntry memory, QualityValidatorResult result)
    {
        var fields = memory.Fields ?? new Dictionary<string, JsonElement>();
        double score = 1.0;

        // Check field naming consistency
        var inconsistentNames = FindInconsistentFieldNames(fields.Keys);
        foreach (var name in inconsistentNames)
        {
            result.Issues.Add(new QualityIssue
            {
                Category = "Field Naming",
                Description = $"Field name '{name}' doesn't follow consistent naming convention",
                Severity = QualitySeverity.Minor,
                Field = name
            });
            score -= 0.1;
        }

        // Check field value types consistency
        var typeInconsistencies = FindFieldTypeInconsistencies(fields);
        foreach (var inconsistency in typeInconsistencies)
        {
            result.Issues.Add(new QualityIssue
            {
                Category = "Field Type Consistency",
                Description = $"Field '{inconsistency.Field}' has unexpected value type: {inconsistency.Issue}",
                Severity = QualitySeverity.Minor,
                Field = inconsistency.Field
            });
            score -= 0.05;
        }

        // Check for duplicate or conflicting field information
        var duplicates = FindDuplicateFieldInformation(fields);
        foreach (var duplicate in duplicates)
        {
            result.Issues.Add(new QualityIssue
            {
                Category = "Field Duplication",
                Description = $"Fields '{duplicate.Field1}' and '{duplicate.Field2}' contain similar information",
                Severity = QualitySeverity.Info,
                Field = duplicate.Field1
            });
            score -= 0.05;
        }

        return Math.Max(0, score);
    }

    private double ValidateFormatConsistency(FlexibleMemoryEntry memory, QualityValidatorResult result)
    {
        var content = memory.Content ?? string.Empty;
        double score = 1.0;

        // Check date format consistency
        var dateInconsistencies = FindDateFormatInconsistencies(content);
        if (dateInconsistencies.Any())
        {
            result.Issues.Add(new QualityIssue
            {
                Category = "Date Format Consistency",
                Description = $"Inconsistent date formats found: {string.Join(", ", dateInconsistencies)}",
                Severity = QualitySeverity.Minor,
                Field = "content"
            });
            score -= 0.1;
        }

        // Check URL format consistency
        var urlInconsistencies = FindUrlFormatInconsistencies(content);
        if (urlInconsistencies.Any())
        {
            result.Issues.Add(new QualityIssue
            {
                Category = "URL Format Consistency",
                Description = "Inconsistent URL formats or invalid URLs found",
                Severity = QualitySeverity.Minor,
                Field = "content"
            });
            score -= 0.1;
        }

        // Check code formatting consistency
        if (HasInconsistentCodeFormatting(content))
        {
            result.Issues.Add(new QualityIssue
            {
                Category = "Code Format Consistency",
                Description = "Inconsistent code formatting (mixed inline code and code blocks)",
                Severity = QualitySeverity.Minor,
                Field = "content"
            });
            score -= 0.1;
        }

        return Math.Max(0, score);
    }

    private double ValidateNamingConsistency(FlexibleMemoryEntry memory, QualityValidatorResult result)
    {
        var content = memory.Content ?? string.Empty;
        var files = memory.FilesInvolved ?? Array.Empty<string>();
        double score = 1.0;

        // Check file path consistency
        var pathInconsistencies = FindFilePathInconsistencies(files);
        foreach (var inconsistency in pathInconsistencies)
        {
            result.Issues.Add(new QualityIssue
            {
                Category = "File Path Consistency",
                Description = inconsistency,
                Severity = QualitySeverity.Minor,
                Field = "filesInvolved"
            });
            score -= 0.1;
        }

        // Check identifier naming consistency
        var identifierInconsistencies = FindIdentifierNamingInconsistencies(content);
        if (identifierInconsistencies.Any())
        {
            result.Issues.Add(new QualityIssue
            {
                Category = "Identifier Naming",
                Description = $"Inconsistent identifier naming patterns: {string.Join(", ", identifierInconsistencies)}",
                Severity = QualitySeverity.Info,
                Field = "content"
            });
            score -= 0.05;
        }

        return Math.Max(0, score);
    }

    private void AddConsistencySuggestions(FlexibleMemoryEntry memory, QualityValidatorResult result)
    {
        var content = memory.Content ?? string.Empty;

        if (HasContradictoryStatements(content))
        {
            result.Suggestions.Add(new QualityImprovement
            {
                Category = "Internal Consistency",
                Description = "Resolve contradictory statements in memory content",
                ActionText = "Review content for conflicting information and clarify or remove contradictions",
                Type = QualityImprovementType.ConsistencyFix,
                ExpectedImpact = 0.4,
                CanAutoImplement = false
            });
        }

        var inconsistentTerms = FindInconsistentTerminology(content);
        if (inconsistentTerms.Any())
        {
            result.Suggestions.Add(new QualityImprovement
            {
                Category = "Terminology Standardization",
                Description = "Standardize terminology throughout the memory",
                ActionText = $"Use consistent terms instead of: {string.Join(", ", inconsistentTerms)}",
                Type = QualityImprovementType.ConsistencyFix,
                ExpectedImpact = 0.2,
                CanAutoImplement = false
            });
        }

        var fields = memory.Fields ?? new Dictionary<string, JsonElement>();
        var inconsistentNames = FindInconsistentFieldNames(fields.Keys);
        if (inconsistentNames.Any())
        {
            result.Suggestions.Add(new QualityImprovement
            {
                Category = "Field Naming Standardization",
                Description = "Standardize field naming convention",
                ActionText = "Use consistent field naming (e.g., camelCase or snake_case)",
                Type = QualityImprovementType.ConsistencyFix,
                ExpectedImpact = 0.15,
                CanAutoImplement = true
            });
        }
    }

    private bool HasContradictoryStatements(string content)
    {
        // Simple heuristic: look for opposing statements
        var contradictionPairs = new[]
        {
            new[] { "should", "should not" },
            new[] { "always", "never" },
            new[] { "must", "must not" },
            new[] { "required", "optional" },
            new[] { "enabled", "disabled" },
            new[] { "true", "false" }
        };

        var contentLower = content.ToLowerInvariant();
        
        foreach (var pair in contradictionPairs)
        {
            if (contentLower.Contains(pair[0]) && contentLower.Contains(pair[1]))
            {
                return true;
            }
        }

        return false;
    }

    private bool ContentToneMatchesType(string content, string memoryType)
    {
        var contentLower = content.ToLowerInvariant();
        
        return memoryType switch
        {
            "TechnicalDebt" => HasProblemTone(contentLower) || HasActionTone(contentLower),
            "ArchitecturalDecision" => HasDecisionTone(contentLower) || HasRationaleTone(contentLower),
            "SecurityRule" => HasRuleTone(contentLower) || HasComplianceTone(contentLower),
            "CodePattern" => HasDescriptiveTone(contentLower) || HasInstructionalTone(contentLower),
            _ => true
        };
    }

    private List<string> FindInconsistentTerminology(string content)
    {
        var inconsistencies = new List<string>();
        
        // Common technical term variations
        var termVariations = new Dictionary<string, string[]>
        {
            ["database"] = new[] { "database", "db", "data base" },
            ["function"] = new[] { "function", "method", "func" },
            ["variable"] = new[] { "variable", "var", "field" },
            ["configuration"] = new[] { "configuration", "config", "settings" },
            ["application"] = new[] { "application", "app", "program" }
        };

        var contentLower = content.ToLowerInvariant();
        
        foreach (var (standardTerm, variations) in termVariations)
        {
            var foundVariations = variations.Where(v => contentLower.Contains(v)).ToList();
            if (foundVariations.Count > 1)
            {
                inconsistencies.Add($"{string.Join("/", foundVariations)} (use '{standardTerm}')");
            }
        }

        return inconsistencies;
    }

    private List<string> FindInconsistentFieldNames(IEnumerable<string> fieldNames)
    {
        var inconsistent = new List<string>();
        var names = fieldNames.ToList();
        
        // Check for mixed naming conventions
        var hasCamelCase = names.Any(n => Regex.IsMatch(n, @"^[a-z][a-zA-Z0-9]*$"));
        var hasSnakeCase = names.Any(n => Regex.IsMatch(n, @"^[a-z][a-z0-9_]*$"));
        var hasPascalCase = names.Any(n => Regex.IsMatch(n, @"^[A-Z][a-zA-Z0-9]*$"));
        
        var conventionCount = new[] { hasCamelCase, hasSnakeCase, hasPascalCase }.Count(c => c);
        
        if (conventionCount > 1)
        {
            // Mixed conventions found
            inconsistent.AddRange(names.Where(n => 
                !Regex.IsMatch(n, @"^[a-z][a-zA-Z0-9]*$"))); // Return non-camelCase as inconsistent
        }

        return inconsistent;
    }

    private List<(string Field, string Issue)> FindFieldTypeInconsistencies(Dictionary<string, JsonElement> fields)
    {
        var inconsistencies = new List<(string, string)>();
        
        // Check for expected field types based on field names
        var expectedTypes = new Dictionary<string, JsonValueKind>
        {
            ["priority"] = JsonValueKind.String,
            ["effort"] = JsonValueKind.String,
            ["impact"] = JsonValueKind.String,
            ["deadline"] = JsonValueKind.String,
            ["status"] = JsonValueKind.String,
            ["completed"] = JsonValueKind.True // Boolean
        };

        foreach (var (fieldName, expectedType) in expectedTypes)
        {
            if (fields.TryGetValue(fieldName, out var value))
            {
                if (value.ValueKind != expectedType && 
                    !(expectedType == JsonValueKind.True && value.ValueKind == JsonValueKind.False))
                {
                    inconsistencies.Add((fieldName, 
                        $"Expected {expectedType}, got {value.ValueKind}"));
                }
            }
        }

        return inconsistencies;
    }

    private List<(string Field1, string Field2)> FindDuplicateFieldInformation(Dictionary<string, JsonElement> fields)
    {
        var duplicates = new List<(string, string)>();
        
        // Check for semantically similar fields
        var similarFieldGroups = new[]
        {
            new[] { "priority", "importance", "criticality" },
            new[] { "effort", "work", "hours", "time" },
            new[] { "deadline", "dueDate", "targetDate" },
            new[] { "status", "state", "condition" }
        };

        foreach (var group in similarFieldGroups)
        {
            var foundFields = group.Where(f => fields.ContainsKey(f)).ToList();
            if (foundFields.Count > 1)
            {
                for (int i = 0; i < foundFields.Count - 1; i++)
                {
                    duplicates.Add((foundFields[i], foundFields[i + 1]));
                }
            }
        }

        return duplicates;
    }

    private List<string> FindDateFormatInconsistencies(string content)
    {
        var dateFormats = new List<string>();
        
        // Find different date patterns
        var patterns = new[]
        {
            (@"\d{4}-\d{2}-\d{2}", "YYYY-MM-DD"),
            (@"\d{2}/\d{2}/\d{4}", "MM/DD/YYYY"),
            (@"\d{2}-\d{2}-\d{4}", "MM-DD-YYYY"),
            (@"\d{1,2}/\d{1,2}/\d{2,4}", "M/D/YY or MM/DD/YYYY")
        };

        var foundFormats = new List<string>();
        foreach (var (pattern, formatName) in patterns)
        {
            if (Regex.IsMatch(content, pattern))
            {
                foundFormats.Add(formatName);
            }
        }

        return foundFormats.Count > 1 ? foundFormats : new List<string>();
    }

    private List<string> FindUrlFormatInconsistencies(string content)
    {
        var urls = Regex.Matches(content, @"https?://[^\s]+")
            .Cast<Match>()
            .Select(m => m.Value)
            .ToList();

        var inconsistencies = new List<string>();
        
        foreach (var url in urls)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out _))
            {
                inconsistencies.Add(url);
            }
        }

        return inconsistencies;
    }

    private bool HasInconsistentCodeFormatting(string content)
    {
        var hasInlineCode = Regex.IsMatch(content, @"`[^`\n]+`");
        var hasCodeBlocks = Regex.IsMatch(content, @"```[\s\S]*?```");
        
        // Check if both are used inconsistently
        return hasInlineCode && hasCodeBlocks && 
               !content.Contains("```") || // Mixed usage without clear separation
               Regex.Matches(content, @"`[^`\n]*\n[^`\n]*`").Count > 0; // Multi-line inline code
    }

    private List<string> FindFilePathInconsistencies(string[] files)
    {
        var inconsistencies = new List<string>();
        
        if (files.Length <= 1) return inconsistencies;
        
        // Check for mixed path separators
        var hasForwardSlash = files.Any(f => f.Contains('/'));
        var hasBackSlash = files.Any(f => f.Contains('\\'));
        
        if (hasForwardSlash && hasBackSlash)
        {
            inconsistencies.Add("Mixed path separators (/ and \\) in file paths");
        }

        // Check for inconsistent path styles (relative vs absolute)
        var hasAbsolute = files.Any(f => Path.IsPathRooted(f));
        var hasRelative = files.Any(f => !Path.IsPathRooted(f));
        
        if (hasAbsolute && hasRelative)
        {
            inconsistencies.Add("Mixed absolute and relative file paths");
        }

        return inconsistencies;
    }

    private List<string> FindIdentifierNamingInconsistencies(string content)
    {
        var inconsistencies = new List<string>();
        
        // Find potential identifiers (simple heuristic)
        var identifiers = Regex.Matches(content, @"\b[a-zA-Z_][a-zA-Z0-9_]*\b")
            .Cast<Match>()
            .Select(m => m.Value)
            .Where(s => s.Length > 2 && char.IsLower(s[0]))
            .Distinct()
            .ToList();

        if (identifiers.Count < 3) return inconsistencies;

        // Check for mixed naming conventions
        var camelCaseCount = identifiers.Count(id => Regex.IsMatch(id, @"^[a-z][a-zA-Z0-9]*$"));
        var snakeCaseCount = identifiers.Count(id => Regex.IsMatch(id, @"^[a-z][a-z0-9_]*$"));
        
        if (camelCaseCount > 0 && snakeCaseCount > 0)
        {
            inconsistencies.Add("Mixed camelCase and snake_case naming");
        }

        return inconsistencies;
    }

    private bool HasProblemTone(string content) => 
        content.Contains("issue") || content.Contains("problem") || content.Contains("bug");

    private bool HasActionTone(string content) => 
        content.Contains("fix") || content.Contains("refactor") || content.Contains("improve");

    private bool HasDecisionTone(string content) => 
        content.Contains("decided") || content.Contains("chosen") || content.Contains("selected");

    private bool HasRationaleTone(string content) => 
        content.Contains("because") || content.Contains("rationale") || content.Contains("reason");

    private bool HasRuleTone(string content) => 
        content.Contains("must") || content.Contains("shall") || content.Contains("required");

    private bool HasComplianceTone(string content) => 
        content.Contains("comply") || content.Contains("enforce") || content.Contains("violate");

    private bool HasDescriptiveTone(string content) => 
        content.Contains("pattern") || content.Contains("approach") || content.Contains("example");

    private bool HasInstructionalTone(string content) => 
        content.Contains("use") || content.Contains("implement") || content.Contains("apply");
}