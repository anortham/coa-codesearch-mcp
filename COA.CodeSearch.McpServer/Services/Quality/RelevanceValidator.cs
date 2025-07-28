using COA.CodeSearch.McpServer.Models;
using System.Text.RegularExpressions;

namespace COA.CodeSearch.McpServer.Services.Quality;

/// <summary>
/// Validates that memories are relevant and focused on the intended topic
/// </summary>
public class RelevanceValidator : IQualityValidator
{
    public string Name => "relevance";
    public string Description => "Validates that memories are relevant and focused on the intended topic";
    public double Weight => 0.3;

    // Common technical terms that indicate relevant content
    private static readonly HashSet<string> TechnicalTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        "function", "method", "class", "interface", "variable", "parameter", "return",
        "database", "query", "table", "index", "schema", "migration",
        "api", "endpoint", "request", "response", "http", "rest", "graphql",
        "authentication", "authorization", "security", "encryption", "hash",
        "performance", "optimization", "cache", "memory", "cpu", "latency",
        "error", "exception", "bug", "issue", "problem", "solution", "fix",
        "test", "testing", "unit", "integration", "mock", "stub",
        "deployment", "docker", "kubernetes", "container", "service",
        "configuration", "environment", "variable", "setting", "option"
    };

    public async Task<QualityValidatorResult> ValidateAsync(
        FlexibleMemoryEntry memory, 
        QualityValidationOptions options)
    {
        var result = new QualityValidatorResult();
        var scores = new List<double>();

        // Topic focus assessment
        var focusScore = ValidateTopicFocus(memory, result);
        scores.Add(focusScore);

        // Technical relevance assessment
        var technicalScore = ValidateTechnicalRelevance(memory, result);
        scores.Add(technicalScore);

        // Context relevance assessment
        var contextScore = ValidateContextRelevance(memory, options, result);
        scores.Add(contextScore);

        // Actionability assessment (for certain memory types)
        var actionabilityScore = ValidateActionability(memory, result);
        scores.Add(actionabilityScore);

        result.Score = scores.Any() ? scores.Average() : 0;
        
        AddRelevanceSuggestions(memory, result);
        
        await Task.CompletedTask;
        return result;
    }

    private double ValidateTopicFocus(FlexibleMemoryEntry memory, QualityValidatorResult result)
    {
        var content = memory.Content ?? string.Empty;
        var memoryType = memory.Type ?? "Default";
        double score = 1.0;

        // Check if content is too generic
        if (IsGenericContent(content))
        {
            result.Issues.Add(new QualityIssue
            {
                Category = "Topic Focus",
                Description = "Memory content appears too generic and lacks specific focus",
                Severity = QualitySeverity.Minor,
                Field = "content"
            });
            score -= 0.3;
        }

        // Check if content matches memory type expectations
        if (!ContentMatchesType(content, memoryType))
        {
            result.Issues.Add(new QualityIssue
            {
                Category = "Type Relevance",
                Description = $"Content doesn't appear to match expected {memoryType} characteristics",
                Severity = QualitySeverity.Major,
                Field = "content"
            });
            score -= 0.4;
        }

        // Check for topic drift (multiple unrelated topics)
        if (HasTopicDrift(content))
        {
            result.Issues.Add(new QualityIssue
            {
                Category = "Topic Focus",
                Description = "Memory appears to cover multiple unrelated topics",
                Severity = QualitySeverity.Minor,
                Field = "content"
            });
            score -= 0.2;
        }

        return Math.Max(0, score);
    }

    private double ValidateTechnicalRelevance(FlexibleMemoryEntry memory, QualityValidatorResult result)
    {
        var content = memory.Content ?? string.Empty;
        var words = GetWords(content);
        
        var technicalTermCount = words.Count(word => TechnicalTerms.Contains(word));
        var technicalDensity = words.Count > 0 ? (double)technicalTermCount / words.Count : 0;
        
        double score = 1.0;

        // Low technical relevance for memories that should be technical
        if (ShouldBeTechnical(memory.Type) && technicalDensity < 0.1)
        {
            result.Issues.Add(new QualityIssue
            {
                Category = "Technical Relevance",
                Description = "Memory lacks technical terminology expected for this type",
                Severity = QualitySeverity.Minor,
                Field = "content"
            });
            score -= 0.3;
        }

        // Check for programming language consistency
        var detectedLanguages = DetectProgrammingLanguages(content);
        if (detectedLanguages.Count > 2)
        {
            result.Issues.Add(new QualityIssue
            {
                Category = "Technical Relevance",
                Description = "Memory references multiple programming languages which may indicate lack of focus",
                Severity = QualitySeverity.Info,
                Field = "content"
            });
        }

        // Bonus for good technical depth
        if (HasTechnicalDepth(content))
        {
            score = Math.Min(1.0, score + 0.1);
        }

        return score;
    }

    private double ValidateContextRelevance(
        FlexibleMemoryEntry memory, 
        QualityValidationOptions options, 
        QualityValidatorResult result)
    {
        double score = 1.0;

        // Check workspace relevance if context is provided
        if (!string.IsNullOrEmpty(options.ContextWorkspace))
        {
            var workspaceRelevance = ValidateWorkspaceRelevance(memory, options.ContextWorkspace);
            if (workspaceRelevance < 0.5)
            {
                result.Issues.Add(new QualityIssue
                {
                    Category = "Context Relevance",
                    Description = "Memory may not be relevant to current workspace context",
                    Severity = QualitySeverity.Info,
                    Field = "content"
                });
                score -= 0.2;
            }
        }

        // Check file reference relevance
        var files = memory.FilesInvolved ?? Array.Empty<string>();
        if (files.Any() && options.RecentFiles != null)
        {
            var relevantFileCount = files.Count(f => 
                options.RecentFiles.Any(rf => 
                    Path.GetFileName(f).Equals(Path.GetFileName(rf), StringComparison.OrdinalIgnoreCase)));
            
            if (files.Length > 0 && relevantFileCount == 0)
            {
                result.Issues.Add(new QualityIssue
                {
                    Category = "Context Relevance",
                    Description = "Referenced files don't appear to be part of current work context",
                    Severity = QualitySeverity.Info,
                    Field = "filesInvolved"
                });
                score -= 0.1;
            }
        }

        // Check temporal relevance
        var age = DateTime.UtcNow - memory.Created;
        if (age.TotalDays > 365 && memory.AccessCount <= 1)
        {
            result.Issues.Add(new QualityIssue
            {
                Category = "Temporal Relevance",
                Description = "Memory is old and hasn't been accessed recently, may be outdated",
                Severity = QualitySeverity.Info,
                Field = "created"
            });
            score -= 0.15;
        }

        return Math.Max(0, score);
    }

    private double ValidateActionability(FlexibleMemoryEntry memory, QualityValidatorResult result)
    {
        var memoryType = memory.Type ?? "Default";
        
        // Only validate actionability for types that should be actionable
        if (!ShouldBeActionable(memoryType))
        {
            return 1.0; // Not applicable
        }

        var content = memory.Content ?? string.Empty;
        double score = 1.0;

        if (!HasActionableContent(content))
        {
            result.Issues.Add(new QualityIssue
            {
                Category = "Actionability",
                Description = $"{memoryType} memories should contain actionable information",
                Severity = QualitySeverity.Minor,
                Field = "content"
            });
            score -= 0.4;
        }

        if (!HasClearSteps(content) && RequiresClearSteps(memoryType))
        {
            result.Issues.Add(new QualityIssue
            {
                Category = "Actionability",
                Description = "Memory lacks clear steps or procedures",
                Severity = QualitySeverity.Minor,
                Field = "content"
            });
            score -= 0.3;
        }

        return Math.Max(0, score);
    }

    private void AddRelevanceSuggestions(FlexibleMemoryEntry memory, QualityValidatorResult result)
    {
        var content = memory.Content ?? string.Empty;
        var memoryType = memory.Type ?? "Default";

        if (IsGenericContent(content))
        {
            result.Suggestions.Add(new QualityImprovement
            {
                Category = "Focus Enhancement",
                Description = "Make content more specific and focused",
                ActionText = "Add specific details, examples, or context to make the memory more focused",
                Type = QualityImprovementType.ContentExpansion,
                ExpectedImpact = 0.3,
                CanAutoImplement = false
            });
        }

        if (ShouldBeTechnical(memoryType) && GetTechnicalDensity(content) < 0.1)
        {
            result.Suggestions.Add(new QualityImprovement
            {
                Category = "Technical Depth",
                Description = "Add more technical details and terminology",
                ActionText = "Include specific technical terms, code examples, or technical explanations",
                Type = QualityImprovementType.ContentExpansion,
                ExpectedImpact = 0.25,
                CanAutoImplement = false
            });
        }

        if (ShouldBeActionable(memoryType) && !HasActionableContent(content))
        {
            result.Suggestions.Add(new QualityImprovement
            {
                Category = "Actionability",
                Description = "Add actionable information and clear steps",
                ActionText = "Include specific actions, steps, or procedures that can be followed",
                Type = QualityImprovementType.Restructuring,
                ExpectedImpact = 0.4,
                CanAutoImplement = false
            });
        }
    }

    private bool IsGenericContent(string content)
    {
        var genericPhrases = new[]
        {
            "need to", "should do", "have to", "might want", "could be",
            "this is", "that is", "there are", "we need", "it would"
        };
        
        var lowSpecificityIndicators = genericPhrases.Count(phrase => 
            content.ToLowerInvariant().Contains(phrase));
            
        return lowSpecificityIndicators > 2 || content.Length < 30;
    }

    private bool ContentMatchesType(string content, string memoryType)
    {
        var contentLower = content.ToLowerInvariant();
        
        return memoryType switch
        {
            "TechnicalDebt" => contentLower.Contains("debt") || contentLower.Contains("refactor") || 
                              contentLower.Contains("improve") || contentLower.Contains("fix"),
            "ArchitecturalDecision" => contentLower.Contains("decision") || contentLower.Contains("architecture") ||
                                     contentLower.Contains("design") || contentLower.Contains("pattern"),
            "CodePattern" => contentLower.Contains("pattern") || contentLower.Contains("code") ||
                           contentLower.Contains("implementation") || contentLower.Contains("approach"),
            "SecurityRule" => contentLower.Contains("security") || contentLower.Contains("rule") ||
                            contentLower.Contains("policy") || contentLower.Contains("compliance"),
            _ => true // Generic content matches any type
        };
    }

    private bool HasTopicDrift(string content)
    {
        // Simple heuristic: check for multiple distinct topics
        var sentences = content.Split('.', '!', '?')
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
            
        if (sentences.Count < 3) return false;
        
        // Check if sentences are talking about very different things
        var topicKeywords = new List<HashSet<string>>();
        foreach (var sentence in sentences)
        {
            var words = GetWords(sentence);
            var keywords = words.Where(w => TechnicalTerms.Contains(w)).ToHashSet();
            topicKeywords.Add(keywords);
        }
        
        // If no common keywords between sentences, might indicate topic drift
        var commonKeywords = topicKeywords.Aggregate((h1, h2) => h1.Intersect(h2).ToHashSet());
        return commonKeywords.Count == 0 && topicKeywords.Count > 2;
    }

    private double ValidateWorkspaceRelevance(FlexibleMemoryEntry memory, string workspace)
    {
        var content = memory.Content ?? string.Empty;
        var workspaceName = Path.GetFileName(workspace);
        
        // Simple relevance check based on workspace name appearing in content
        return content.ToLowerInvariant().Contains(workspaceName.ToLowerInvariant()) ? 1.0 : 0.3;
    }

    private List<string> DetectProgrammingLanguages(string content)
    {
        var languages = new List<string>();
        var contentLower = content.ToLowerInvariant();
        
        var languageIndicators = new Dictionary<string, string[]>
        {
            ["C#"] = new[] { "c#", "csharp", ".cs", "dotnet", ".net" },
            ["JavaScript"] = new[] { "javascript", "js", ".js", "node", "npm" },
            ["Python"] = new[] { "python", ".py", "pip", "django", "flask" },
            ["Java"] = new[] { "java", ".java", "maven", "gradle", "spring" },
            ["TypeScript"] = new[] { "typescript", ".ts", ".tsx", "tsc" },
            ["SQL"] = new[] { "sql", "select", "insert", "update", "delete", "table" }
        };

        foreach (var (language, indicators) in languageIndicators)
        {
            if (indicators.Any(indicator => contentLower.Contains(indicator)))
            {
                languages.Add(language);
            }
        }

        return languages;
    }

    private bool HasTechnicalDepth(string content)
    {
        // Check for code snippets or technical explanations
        return Regex.IsMatch(content, @"`[^`]+`") || // Inline code
               Regex.IsMatch(content, @"```[\s\S]*?```") || // Code blocks
               content.Contains("()") || // Method calls
               content.Contains("{}"); // Object/code structures
    }

    private double GetTechnicalDensity(string content)
    {
        var words = GetWords(content);
        var technicalTermCount = words.Count(word => TechnicalTerms.Contains(word));
        return words.Count > 0 ? (double)technicalTermCount / words.Count : 0;
    }

    private bool ShouldBeTechnical(string? memoryType)
    {
        return memoryType switch
        {
            "TechnicalDebt" => true,
            "CodePattern" => true,
            "SecurityRule" => true,
            _ => false
        };
    }

    private bool ShouldBeActionable(string memoryType)
    {
        return memoryType switch
        {
            "TechnicalDebt" => true,
            "SecurityRule" => true,
            _ => false
        };
    }

    private bool RequiresClearSteps(string memoryType)
    {
        return memoryType switch
        {
            "TechnicalDebt" => true,
            _ => false
        };
    }

    private bool HasActionableContent(string content)
    {
        var actionWords = new[] { "fix", "refactor", "implement", "update", "change", "add", "remove", "replace" };
        return actionWords.Any(word => content.ToLowerInvariant().Contains(word));
    }

    private bool HasClearSteps(string content)
    {
        // Look for numbered lists or step indicators
        return Regex.IsMatch(content, @"\d+\.") || 
               content.Contains("step") ||
               content.Contains("first") ||
               content.Contains("then") ||
               content.Contains("next");
    }

    private List<string> GetWords(string text)
    {
        return Regex.Split(text, @"\W+")
            .Where(w => !string.IsNullOrWhiteSpace(w) && w.Length > 2)
            .ToList();
    }
}