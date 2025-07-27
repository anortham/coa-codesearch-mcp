using System.Text.RegularExpressions;
using COA.CodeSearch.McpServer.Models;

namespace COA.CodeSearch.McpServer.Infrastructure;

/// <summary>
/// Helper methods for smart analysis of code results, optimized for Claude
/// </summary>
public static class SmartAnalysisHelpers
{
    /// <summary>
    /// Analyzes a collection of file paths to generate insights
    /// </summary>
    public static List<string> AnalyzeFilePatterns(IEnumerable<string> filePaths)
    {
        var insights = new List<string>();
        var files = filePaths.ToList();
        
        if (!files.Any()) return insights;
        
        // Analyze concentration by directory
        var directoryGroups = files
            .GroupBy(f => Path.GetDirectoryName(f) ?? "")
            .OrderByDescending(g => g.Count())
            .ToList();
        
        if (directoryGroups.First().Count() > files.Count * 0.5)
        {
            var topDir = Path.GetFileName(directoryGroups.First().Key) ?? "root";
            insights.Add($"Over 50% of changes are in the '{topDir}' directory");
        }
        
        // Check for test files
        var testFiles = files.Count(f => IsTestFile(f));
        if (testFiles > 0)
        {
            insights.Add($"{testFiles} test files affected - ensure tests are updated");
        }
        
        // Check for configuration files
        var configFiles = files.Count(f => IsConfigFile(f));
        if (configFiles > 0)
        {
            insights.Add($"{configFiles} configuration files affected - review for breaking changes");
        }
        
        // Check for UI files
        var uiFiles = files.Count(f => IsUIFile(f));
        if (uiFiles > 0)
        {
            insights.Add($"{uiFiles} UI components affected - visual testing recommended");
        }
        
        return insights;
    }
    
    /// <summary>
    /// Determines the risk level based on file types and patterns
    /// </summary>
    public static (string Impact, List<string> RiskFactors) AssessImpact(
        IEnumerable<string> filePaths,
        int totalChanges)
    {
        var files = filePaths.ToList();
        var riskFactors = new List<string>();
        
        // High risk indicators
        if (files.Any(f => IsConfigFile(f)))
        {
            riskFactors.Add("Configuration files modified");
        }
        
        if (files.Any(f => IsInterfaceFile(f)))
        {
            riskFactors.Add("Interface changes may break implementations");
        }
        
        if (files.Any(f => IsPublicApiFile(f)))
        {
            riskFactors.Add("Public API changes detected");
        }
        
        if (totalChanges > 100)
        {
            riskFactors.Add("Large number of changes increases risk");
        }
        
        // Determine overall impact
        var impact = riskFactors.Count switch
        {
            0 => "low",
            1 => "medium",
            _ => "high"
        };
        
        return (impact, riskFactors);
    }
    
    /// <summary>
    /// Generates smart suggestions based on the analysis
    /// </summary>
    public static List<string> GenerateSuggestions(
        IEnumerable<string> filePaths,
        Dictionary<string, CategorySummary> categories,
        List<Hotspot> hotspots)
    {
        var suggestions = new List<string>();
        
        // Hotspot suggestions
        if (hotspots.Any(h => h.Complexity == "high"))
        {
            suggestions.Add($"Review {hotspots.First(h => h.Complexity == "high").File} first - it has the highest concentration of changes");
        }
        
        // Category-based suggestions
        if (categories.ContainsKey("tests") && categories["tests"].Files > 0)
        {
            suggestions.Add("Run test suite after changes to ensure nothing is broken");
        }
        
        if (categories.ContainsKey("controllers") && categories["controllers"].Files > 5)
        {
            suggestions.Add("Multiple controllers affected - consider API versioning if needed");
        }
        
        if (categories.ContainsKey("services") && categories.ContainsKey("controllers"))
        {
            suggestions.Add("Both services and controllers affected - full integration testing recommended");
        }
        
        // File pattern suggestions
        var files = filePaths.ToList();
        if (files.Any(f => f.EndsWith(".csproj")))
        {
            suggestions.Add("Project file changes detected - rebuild solution and check for dependency issues");
        }
        
        return suggestions;
    }
    
    /// <summary>
    /// Creates preview items from a collection of changes
    /// </summary>
    public static List<PreviewItem> CreatePreviewItems<T>(
        IEnumerable<T> items,
        Func<T, string> fileSelector,
        Func<T, int> lineSelector,
        Func<T, string> previewGenerator,
        int maxItems = 5)
    {
        return items
            .Take(maxItems)
            .Select(item => new PreviewItem
            {
                File = fileSelector(item),
                Line = lineSelector(item),
                Preview = previewGenerator(item),
                Context = ExtractContext(fileSelector(item))
            })
            .ToList();
    }
    
    private static bool IsTestFile(string filePath)
    {
        var lower = filePath.ToLowerInvariant();
        return lower.Contains("test") || 
               lower.Contains("spec") || 
               lower.Contains(".test.") ||
               lower.Contains(".spec.");
    }
    
    private static bool IsConfigFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath).ToLowerInvariant();
        return fileName.EndsWith(".config") ||
               fileName.EndsWith(".json") ||
               fileName.EndsWith(".xml") ||
               fileName.EndsWith(".yaml") ||
               fileName.EndsWith(".yml") ||
               fileName == "appsettings.json" ||
               fileName.StartsWith("appsettings.");
    }
    
    private static bool IsUIFile(string filePath)
    {
        var lower = filePath.ToLowerInvariant();
        return lower.EndsWith(".razor") ||
               lower.EndsWith(".cshtml") ||
               lower.EndsWith(".jsx") ||
               lower.EndsWith(".tsx") ||
               lower.Contains("/pages/") ||
               lower.Contains("/components/") ||
               lower.Contains("/views/");
    }
    
    private static bool IsInterfaceFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        return fileName.StartsWith("I") && 
               fileName.EndsWith(".cs") && 
               char.IsUpper(fileName[1]);
    }
    
    private static bool IsPublicApiFile(string filePath)
    {
        var lower = filePath.ToLowerInvariant();
        return lower.Contains("/api/") ||
               lower.Contains("controller") ||
               lower.Contains("/public/") ||
               lower.Contains("/contracts/");
    }
    
    private static string ExtractContext(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var directory = Path.GetFileName(Path.GetDirectoryName(filePath)) ?? "";
        
        // Provide context based on file location
        if (directory.Equals("Controllers", StringComparison.OrdinalIgnoreCase))
        {
            return "API Controller";
        }
        if (directory.Equals("Services", StringComparison.OrdinalIgnoreCase))
        {
            return "Business Service";
        }
        if (directory.Equals("Pages", StringComparison.OrdinalIgnoreCase))
        {
            return "Web Page";
        }
        if (fileName.StartsWith("I") && char.IsUpper(fileName[1]))
        {
            return "Interface Definition";
        }
        
        return "";
    }
}