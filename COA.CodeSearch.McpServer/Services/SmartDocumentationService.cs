using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Service that intelligently detects when search results should be documented
/// and determines the appropriate knowledge type and metadata
/// </summary>
public class SmartDocumentationService
{
    private readonly ILogger<SmartDocumentationService> _logger;

    // Pattern-to-knowledge-type mapping
    private readonly Dictionary<string, DocumentationRule> _rules = new()
    {
        // Technical Debt Patterns
        ["Thread\\.Sleep"] = new("TechnicalDebt", "blocking-operations", "Found blocking Thread.Sleep calls", "high"),
        ["Thread\\.Yield"] = new("TechnicalDebt", "blocking-operations", "Found Thread.Yield calls", "medium"),
        ["async void"] = new("TechnicalDebt", "async-antipattern", "Found async void methods (should be async Task)", "high"),
        ["catch.*Exception.*//.*ignore|catch.*Exception.*\\{\\s*\\}"] = new("TechnicalDebt", "error-handling", "Found ignored exceptions", "high"),
        ["String\\.Format.*\\+|\\+.*String\\.Format"] = new("TechnicalDebt", "performance", "String concatenation with String.Format", "medium"),
        ["lock.*Thread\\.Sleep"] = new("TechnicalDebt", "concurrency", "Thread.Sleep inside lock blocks", "critical"),
        ["TODO|FIXME|HACK|XXX"] = new("TechnicalDebt", "code-quality", "Found TODO/FIXME comments requiring attention", "low"),
        ["using.*System\\.Data\\.SqlClient(?!.*Microsoft)"] = new("TechnicalDebt", "deprecated", "Using deprecated SqlClient (use Microsoft.Data.SqlClient)", "medium"),
        ["Console\\.WriteLine|Console\\.Write(?!Line)"] = new("TechnicalDebt", "logging", "Using Console.WriteLine instead of proper logging", "low"),
        
        // Positive Patterns - Project Insights
        ["async Task.*ConfigureAwait\\(false\\)"] = new("ProjectInsight", "async-best-practice", "Proper async/await with ConfigureAwait(false)", "normal"),
        ["readonly struct"] = new("ProjectInsight", "performance", "Good use of readonly structs", "normal"),
        ["IDisposable.*using"] = new("ProjectInsight", "resource-management", "Proper resource disposal patterns", "normal"),
        [": I[A-Z]\\w+"] = new("ProjectInsight", "architecture", "Interface-based design patterns", "normal"),
        ["\\[Fact\\]|\\[Test\\]"] = new("ProjectInsight", "testing", "Test coverage analysis", "normal"),
        ["sealed class"] = new("ProjectInsight", "design", "Sealed classes for performance", "normal"),
        
        // Security Patterns
        ["password|secret|key.*=.*[\"\']\\w+[\"\']"] = new("TechnicalDebt", "security", "Potential hardcoded secrets", "critical"),
        ["http://localhost|http://127\\.0\\.0\\.1"] = new("TechnicalDebt", "configuration", "Hardcoded local URLs", "medium"),
        ["ConnectionString.*=.*[\"\'].*[\"\']"] = new("TechnicalDebt", "security", "Potential hardcoded connection strings", "high"),
        
        // Performance Patterns  
        ["new List<.*>\\(\\)|new Dictionary<.*>\\(\\)"] = new("WorkNote", "performance", "Collections without capacity hints", "low"),
        ["string.*\\+.*string|string.*\\+="] = new("TechnicalDebt", "performance", "String concatenation (consider StringBuilder)", "low"),
        ["LINQ.*ToList\\(\\).*Count|LINQ.*ToArray\\(\\).*Length"] = new("TechnicalDebt", "performance", "Unnecessary LINQ materialization", "medium")
    };

    public SmartDocumentationService(ILogger<SmartDocumentationService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Analyze a search query and results to determine if documentation is warranted
    /// </summary>
    public DocumentationRecommendation AnalyzeSearchResults(string query, int resultCount, string[]? filePaths = null)
    {
        if (resultCount == 0)
        {
            return DocumentationRecommendation.None();
        }

        // Check if query matches known patterns
        var matchedRule = FindMatchingRule(query);
        if (matchedRule != null)
        {
            var content = GenerateContent(matchedRule, query, resultCount, filePaths);
            var tags = GenerateTags(matchedRule, query, filePaths);
            var metadata = GenerateMetadata(query, resultCount, filePaths);

            return new DocumentationRecommendation(
                ShouldDocument: true,
                KnowledgeType: matchedRule.KnowledgeType,
                Content: content,
                Tags: tags,
                Priority: matchedRule.Priority,
                Category: matchedRule.Category,
                Metadata: metadata
            );
        }

        // Heuristic analysis for unknown patterns
        if (resultCount >= 5 && IsLikelyTechnicalDebtQuery(query))
        {
            return new DocumentationRecommendation(
                ShouldDocument: true,
                KnowledgeType: "TechnicalDebt",
                Content: $"Found {resultCount} instances of '{query}' - may require investigation",
                Tags: new[] { "code-quality", "investigation" },
                Priority: "medium",
                Category: "heuristic-detection",
                Metadata: new Dictionary<string, object>
                {
                    ["query"] = query,
                    ["resultCount"] = resultCount,
                    ["detectionMethod"] = "heuristic"
                }
            );
        }

        return DocumentationRecommendation.None();
    }

    private DocumentationRule? FindMatchingRule(string query)
    {
        foreach (var (pattern, rule) in _rules)
        {
            try
            {
                if (Regex.IsMatch(query, pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled))
                {
                    _logger.LogDebug("Query '{Query}' matched pattern '{Pattern}'", query, pattern);
                    return rule;
                }
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Invalid regex pattern: {Pattern}", pattern);
            }
        }
        return null;
    }

    private string GenerateContent(DocumentationRule rule, string query, int resultCount, string[]? filePaths)
    {
        var content = $"{rule.Description}: Found {resultCount} instances of '{query}'";
        
        if (filePaths?.Length > 0)
        {
            var topFiles = filePaths.Take(5);
            content += $"\n\nAffected files:\n{string.Join("\n", topFiles.Select(f => $"- {f}"))}";
            
            if (filePaths.Length > 5)
            {
                content += $"\n... and {filePaths.Length - 5} more files";
            }
        }

        return content;
    }

    private string[] GenerateTags(DocumentationRule rule, string query, string[]? filePaths)
    {
        var tags = new List<string> { rule.Category, "codesearch-auto" };
        
        // Add file-type tags
        if (filePaths?.Length > 0)
        {
            var extensions = filePaths
                .Select(Path.GetExtension)
                .Where(ext => !string.IsNullOrEmpty(ext))
                .Select(ext => ext!.TrimStart('.'))
                .Distinct()
                .Take(3);
                
            tags.AddRange(extensions);
        }
        
        // Add query-based tags
        if (query.Contains("test", StringComparison.OrdinalIgnoreCase))
            tags.Add("testing");
        if (query.Contains("async", StringComparison.OrdinalIgnoreCase))
            tags.Add("async");
        if (query.Contains("performance", StringComparison.OrdinalIgnoreCase))
            tags.Add("performance");
            
        return tags.Distinct().ToArray();
    }

    private Dictionary<string, object> GenerateMetadata(string query, int resultCount, string[]? filePaths)
    {
        return new Dictionary<string, object>
        {
            ["originalQuery"] = query,
            ["resultCount"] = resultCount.ToString(),  // Convert to string
            ["autoDetected"] = "true",  // Convert to string
            ["detectedAt"] = DateTime.UtcNow.ToString("O"),  // ISO 8601 string format
            ["fileTypes"] = string.Join(",", filePaths?.Select(Path.GetExtension).Where(ext => !string.IsNullOrEmpty(ext)).Distinct() ?? Array.Empty<string>()),  // Comma-separated string
            ["topDirectories"] = string.Join(",", filePaths?.Select(Path.GetDirectoryName).Where(dir => !string.IsNullOrEmpty(dir)).GroupBy(dir => dir).OrderByDescending(g => g.Count()).Take(3).Select(g => g.Key) ?? Array.Empty<string?>())  // Comma-separated string
        };
    }

    private bool IsLikelyTechnicalDebtQuery(string query)
    {
        var debtIndicators = new[]
        {
            "exception", "error", "fail", "bug", "issue", "problem",
            "deprecated", "obsolete", "legacy", "old", "remove",
            "todo", "fixme", "hack", "temporary", "workaround",
            "slow", "performance", "memory", "leak"
        };

        return debtIndicators.Any(indicator => 
            query.Contains(indicator, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Rule for determining documentation behavior for search patterns
/// </summary>
public record DocumentationRule(
    string KnowledgeType,
    string Category,
    string Description,
    string Priority
);

/// <summary>
/// Recommendation for documenting search results
/// </summary>
public record DocumentationRecommendation(
    bool ShouldDocument,
    string? KnowledgeType = null,
    string? Content = null,
    string[]? Tags = null,
    string? Priority = null,
    string? Category = null,
    Dictionary<string, object>? Metadata = null
)
{
    public static DocumentationRecommendation None() => new(false);
}