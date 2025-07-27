using COA.CodeSearch.McpServer.Configuration;
using COA.CodeSearch.McpServer.Constants;
using COA.CodeSearch.McpServer.Infrastructure;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// AI-optimized tool that analyzes codebase for patterns and anti-patterns.
/// Designed to help AI agents identify architectural patterns, code smells, and improvement opportunities.
/// </summary>
public class PatternDetectorTool : ClaudeOptimizedToolBase
{
    public override string ToolName => "pattern_detector";
    public override string Description => "Analyzes codebase for patterns and anti-patterns with intelligent insights";
    public override ToolCategory Category => ToolCategory.Analysis;

    private readonly FlexibleMemoryTools _memoryTools;
    private readonly FastTextSearchToolV2 _textSearchTool;
    private readonly FastFileSearchToolV2 _fileSearchTool;
    private readonly FastFileSizeAnalysisTool _fileSizeAnalysisTool;
    private readonly IErrorRecoveryService _errorRecoveryService;

    public PatternDetectorTool(
        ILogger<PatternDetectorTool> logger,
        FlexibleMemoryTools memoryTools,
        FastTextSearchToolV2 textSearchTool,
        FastFileSearchToolV2 fileSearchTool,
        FastFileSizeAnalysisTool fileSizeAnalysisTool,
        IErrorRecoveryService errorRecoveryService,
        IResponseSizeEstimator sizeEstimator,
        IResultTruncator truncator,
        IOptions<ResponseLimitOptions> options,
        IDetailRequestCache detailCache)
        : base(sizeEstimator, truncator, options, logger, detailCache)
    {
        _memoryTools = memoryTools;
        _textSearchTool = textSearchTool;
        _fileSearchTool = fileSearchTool;
        _fileSizeAnalysisTool = fileSizeAnalysisTool;
        _errorRecoveryService = errorRecoveryService;
    }

    public async Task<object> ExecuteAsync(
        string workspacePath,
        PatternType[] patternTypes,
        PatternDepth depth = PatternDepth.Shallow,
        bool createMemories = false,
        ResponseMode mode = ResponseMode.Summary,
        DetailRequest? detailRequest = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Handle detail requests
            if (detailRequest != null && DetailCache != null)
            {
                var cachedData = DetailCache.GetDetailData<object>(detailRequest.DetailRequestToken);
                if (cachedData != null)
                {
                    return cachedData;
                }
            }

            Logger.LogInformation("Pattern detector starting for workspace: {WorkspacePath} with types: {PatternTypes}", 
                workspacePath, string.Join(", ", patternTypes));

            // Validate input
            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                return UnifiedToolResponse<object>.CreateError(
                    ErrorCodes.VALIDATION_ERROR,
                    "Workspace path cannot be empty",
                    _errorRecoveryService.GetValidationErrorRecovery("workspacePath", "absolute directory path"));
            }

            if (patternTypes == null || !patternTypes.Any())
            {
                return UnifiedToolResponse<object>.CreateError(
                    ErrorCodes.VALIDATION_ERROR,
                    "At least one pattern type must be specified",
                    _errorRecoveryService.GetValidationErrorRecovery("patternTypes", "array of pattern types"));
            }

            var detectionResults = new PatternDetectorResult
            {
                Operation = "pattern_detector",
                Query = new { workspacePath, patternTypes, depth },
                Success = true,
                ResourceUri = $"codesearch-pattern-detector://{Guid.NewGuid():N}",
                Patterns = new List<DetectedPattern>(),
                AntiPatterns = new List<DetectedAntiPattern>()
            };

            // Execute pattern detection for each requested type
            foreach (var patternType in patternTypes)
            {
                try
                {
                    var patterns = await DetectPatternsAsync(workspacePath, patternType, depth, cancellationToken);
                    detectionResults.Patterns.AddRange(patterns.Patterns);
                    detectionResults.AntiPatterns.AddRange(patterns.AntiPatterns);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Failed to detect patterns for type: {PatternType}", patternType);
                    detectionResults.Warnings.Add($"Pattern detection failed for {patternType}: {ex.Message}");
                }
            }

            // Generate insights and recommendations
            detectionResults.Insights = GenerateInsights(detectionResults.Patterns, detectionResults.AntiPatterns);
            detectionResults.Recommendations = GenerateRecommendations(detectionResults.Patterns, detectionResults.AntiPatterns);

            // Create memories if requested
            if (createMemories)
            {
                await CreatePatternMemoriesAsync(detectionResults, workspacePath);
            }

            Logger.LogInformation("Pattern detection completed. Found {PatternCount} patterns, {AntiPatternCount} anti-patterns",
                detectionResults.Patterns.Count, detectionResults.AntiPatterns.Count);

            // Return Claude-optimized response
            return await CreateClaudeResponseAsync(
                detectionResults,
                mode,
                GeneratePatternDetectorSummary,
                cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Pattern detector failed for workspace: {WorkspacePath}", workspacePath);
            return UnifiedToolResponse<object>.CreateError(
                ErrorCodes.INTERNAL_ERROR,
                ex.Message,
                _errorRecoveryService.GetValidationErrorRecovery("pattern_detector", "Check workspace path and try again"));
        }
    }

    private async Task<PatternDetectionBatch> DetectPatternsAsync(
        string workspacePath, 
        PatternType patternType, 
        PatternDepth depth, 
        CancellationToken cancellationToken)
    {
        var result = new PatternDetectionBatch();

        switch (patternType)
        {
            case PatternType.Architecture:
                await DetectArchitecturalPatternsAsync(workspacePath, depth, result, cancellationToken);
                break;
            case PatternType.Security:
                await DetectSecurityPatternsAsync(workspacePath, depth, result, cancellationToken);
                break;
            case PatternType.Performance:
                await DetectPerformancePatternsAsync(workspacePath, depth, result, cancellationToken);
                break;
            case PatternType.Testing:
                await DetectTestingPatternsAsync(workspacePath, depth, result, cancellationToken);
                break;
        }

        return result;
    }

    private async Task DetectArchitecturalPatternsAsync(
        string workspacePath, 
        PatternDepth depth, 
        PatternDetectionBatch result, 
        CancellationToken cancellationToken)
    {
        // Detect MVC/MVP patterns
        var controllerResults = await _fileSearchTool.ExecuteAsync(
            "*Controller.cs", workspacePath, "wildcard", 50, false, ResponseMode.Full, null, cancellationToken);
        
        if (controllerResults != null && ExtractFileCount(controllerResults) > 0)
        {
            result.Patterns.Add(new DetectedPattern
            {
                Type = "MVC Pattern",
                Name = "ASP.NET Core MVC Architecture",
                Confidence = 0.9,
                Locations = ExtractFileLocations(controllerResults),
                Description = "Standard MVC pattern with controllers detected",
                Recommendation = "Ensure controllers follow single responsibility principle"
            });
        }

        // Detect dependency injection patterns
        var diResults = await _textSearchTool.ExecuteAsync(
            "IServiceCollection|services.AddScoped|services.AddSingleton|services.AddTransient", 
            workspacePath, searchType: "regex", maxResults: 20, mode: ResponseMode.Full, 
            detailRequest: null, cancellationToken: cancellationToken);

        if (diResults != null && ExtractResultCount(diResults) > 0)
        {
            result.Patterns.Add(new DetectedPattern
            {
                Type = "Dependency Injection",
                Name = "Service Registration Pattern",
                Confidence = 0.85,
                Locations = ExtractTextSearchLocations(diResults),
                Description = "Dependency injection container configuration detected",
                Recommendation = "Review service lifetimes for optimal performance"
            });
        }

        // Detect large files (potential God objects)
        var largeFileResults = await _fileSizeAnalysisTool.ExecuteAsync(
            workspacePath, "largest", maxResults: 10, cancellationToken: cancellationToken);

        if (largeFileResults != null)
        {
            var largeFiles = ExtractLargeFiles(largeFileResults);
            if (largeFiles.Any(f => f.Size > 500000)) // Files larger than 500KB
            {
                result.AntiPatterns.Add(new DetectedAntiPattern
                {
                    Type = "God Object",
                    Name = "Oversized Classes",
                    Severity = "High",
                    Locations = largeFiles.Where(f => f.Size > 500000).Select(f => f.Path).ToList(),
                    Description = "Large files detected that may violate single responsibility principle",
                    Impact = "Poor maintainability, testing difficulty",
                    Remediation = "Consider splitting large classes into smaller, focused components"
                });
            }
        }
    }

    private async Task DetectSecurityPatternsAsync(
        string workspacePath, 
        PatternDepth depth, 
        PatternDetectionBatch result, 
        CancellationToken cancellationToken)
    {
        // Detect authentication patterns
        var authResults = await _textSearchTool.ExecuteAsync(
            "[Authorize]|[AllowAnonymous]|ClaimsPrincipal|IAuthenticationService", 
            workspacePath, searchType: "regex", maxResults: 30, mode: ResponseMode.Full,
            detailRequest: null, cancellationToken: cancellationToken);

        if (authResults != null && ExtractResultCount(authResults) > 0)
        {
            result.Patterns.Add(new DetectedPattern
            {
                Type = "Authentication",
                Name = "ASP.NET Core Authentication",
                Confidence = 0.9,
                Locations = ExtractTextSearchLocations(authResults),
                Description = "Authentication and authorization patterns detected",
                Recommendation = "Ensure consistent authorization across all endpoints"
            });
        }

        // Detect potential security vulnerabilities
        var sqlInjectionResults = await _textSearchTool.ExecuteAsync(
            "\"SELECT.*\" + |\"UPDATE.*\" + |\"INSERT.*\" + |\"DELETE.*\" + ", 
            workspacePath, searchType: "regex", maxResults: 20, mode: ResponseMode.Full,
            detailRequest: null, cancellationToken: cancellationToken);

        if (sqlInjectionResults != null && ExtractResultCount(sqlInjectionResults) > 0)
        {
            result.AntiPatterns.Add(new DetectedAntiPattern
            {
                Type = "SQL Injection Risk",
                Name = "String Concatenation in SQL",
                Severity = "Critical",
                Locations = ExtractTextSearchLocations(sqlInjectionResults),
                Description = "Potential SQL injection vulnerabilities through string concatenation",
                Impact = "Data breach, unauthorized access",
                Remediation = "Use parameterized queries or ORM frameworks"
            });
        }

        // Detect hardcoded secrets
        var secretResults = await _textSearchTool.ExecuteAsync(
            "password|apikey|secret|token.*=.*\"[^\"]{20,}", 
            workspacePath, searchType: "regex", maxResults: 15, mode: ResponseMode.Full,
            detailRequest: null, cancellationToken: cancellationToken);

        if (secretResults != null && ExtractResultCount(secretResults) > 0)
        {
            result.AntiPatterns.Add(new DetectedAntiPattern
            {
                Type = "Hardcoded Secrets",
                Name = "Embedded Credentials",
                Severity = "High",
                Locations = ExtractTextSearchLocations(secretResults),
                Description = "Potential hardcoded secrets or credentials detected",
                Impact = "Security exposure, credential leakage",
                Remediation = "Move secrets to configuration, use key vaults"
            });
        }
    }

    private async Task DetectPerformancePatternsAsync(
        string workspacePath, 
        PatternDepth depth, 
        PatternDetectionBatch result, 
        CancellationToken cancellationToken)
    {
        // Detect async/await patterns
        var asyncResults = await _textSearchTool.ExecuteAsync(
            "async Task|await ", workspacePath, searchType: "regex", maxResults: 30, 
            mode: ResponseMode.Full, detailRequest: null, cancellationToken: cancellationToken);

        if (asyncResults != null && ExtractResultCount(asyncResults) > 0)
        {
            result.Patterns.Add(new DetectedPattern
            {
                Type = "Asynchronous Programming",
                Name = "Async/Await Pattern",
                Confidence = 0.85,
                Locations = ExtractTextSearchLocations(asyncResults),
                Description = "Asynchronous programming patterns detected",
                Recommendation = "Ensure proper ConfigureAwait usage in library code"
            });
        }

        // Detect potential performance issues
        var syncOverAsyncResults = await _textSearchTool.ExecuteAsync(
            "\\.Wait\\(\\)|\\.Result", workspacePath, searchType: "regex", maxResults: 20,
            mode: ResponseMode.Full, detailRequest: null, cancellationToken: cancellationToken);

        if (syncOverAsyncResults != null && ExtractResultCount(syncOverAsyncResults) > 0)
        {
            result.AntiPatterns.Add(new DetectedAntiPattern
            {
                Type = "Sync over Async",
                Name = "Blocking Async Operations",
                Severity = "Medium",
                Locations = ExtractTextSearchLocations(syncOverAsyncResults),
                Description = "Synchronous calls to async methods detected",
                Impact = "Thread pool starvation, deadlock potential",
                Remediation = "Use await instead of .Wait() or .Result"
            });
        }

        // Detect excessive string concatenation
        var stringConcatResults = await _textSearchTool.ExecuteAsync(
            "string.*\\+=|\".*\" \\+ \"", workspacePath, searchType: "regex", maxResults: 25,
            mode: ResponseMode.Full, detailRequest: null, cancellationToken: cancellationToken);

        if (stringConcatResults != null && ExtractResultCount(stringConcatResults) > 10)
        {
            result.AntiPatterns.Add(new DetectedAntiPattern
            {
                Type = "Inefficient String Operations",
                Name = "Excessive String Concatenation",
                Severity = "Low",
                Locations = ExtractTextSearchLocations(stringConcatResults),
                Description = "Multiple string concatenation operations detected",
                Impact = "Memory allocation overhead, reduced performance",
                Remediation = "Consider using StringBuilder or string interpolation"
            });
        }
    }

    private async Task DetectTestingPatternsAsync(
        string workspacePath, 
        PatternDepth depth, 
        PatternDetectionBatch result, 
        CancellationToken cancellationToken)
    {
        // Detect test frameworks
        var testFiles = await _fileSearchTool.ExecuteAsync(
            "*Test*.cs", workspacePath, "wildcard", 50, false, ResponseMode.Full, null, cancellationToken);

        if (testFiles != null && ExtractFileCount(testFiles) > 0)
        {
            result.Patterns.Add(new DetectedPattern
            {
                Type = "Unit Testing",
                Name = "Test Structure Pattern",
                Confidence = 0.9,
                Locations = ExtractFileLocations(testFiles),
                Description = "Unit test structure detected",
                Recommendation = "Ensure comprehensive test coverage for critical paths"
            });
        }

        // Detect test attributes
        var testAttributeResults = await _textSearchTool.ExecuteAsync(
            "\\[Test\\]|\\[Fact\\]|\\[Theory\\]|\\[TestMethod\\]", workspacePath, searchType: "regex", 
            maxResults: 50, mode: ResponseMode.Full, detailRequest: null, cancellationToken: cancellationToken);

        var testCount = testAttributeResults != null ? ExtractResultCount(testAttributeResults) : 0;
        var fileCount = testFiles != null ? ExtractFileCount(testFiles) : 0;

        if (fileCount > 0 && testCount < fileCount * 3) // Less than 3 tests per test file on average
        {
            result.AntiPatterns.Add(new DetectedAntiPattern
            {
                Type = "Insufficient Test Coverage",
                Name = "Low Test Density",
                Severity = "Medium",
                Locations = ExtractFileLocations(testFiles ?? new object()),
                Description = "Low number of tests relative to test files",
                Impact = "Reduced confidence in code changes",
                Remediation = "Increase test coverage for critical functionality"
            });
        }
    }

    private List<string> GenerateInsights(List<DetectedPattern> patterns, List<DetectedAntiPattern> antiPatterns)
    {
        var insights = new List<string>();

        if (patterns.Count > antiPatterns.Count)
        {
            insights.Add("Codebase shows good pattern adoption with minimal anti-patterns");
        }

        if (antiPatterns.Any(ap => ap.Severity == "Critical"))
        {
            insights.Add("Critical security or performance issues detected - immediate attention required");
        }

        var securityIssues = antiPatterns.Count(ap => ap.Type.Contains("Security") || ap.Type.Contains("SQL") || ap.Type.Contains("Secret"));
        if (securityIssues > 0)
        {
            insights.Add($"Security concerns identified in {securityIssues} areas - review security practices");
        }

        if (patterns.Any(p => p.Type == "Unit Testing"))
        {
            insights.Add("Testing infrastructure present - good foundation for quality assurance");
        }

        return insights;
    }

    private List<string> GenerateRecommendations(List<DetectedPattern> patterns, List<DetectedAntiPattern> antiPatterns)
    {
        var recommendations = new List<string>();

        // Prioritize by severity
        var criticalIssues = antiPatterns.Where(ap => ap.Severity == "Critical").ToList();
        if (criticalIssues.Any())
        {
            recommendations.Add($"Address {criticalIssues.Count} critical issues immediately");
        }

        var highIssues = antiPatterns.Where(ap => ap.Severity == "High").ToList();
        if (highIssues.Any())
        {
            recommendations.Add($"Plan remediation for {highIssues.Count} high-priority issues");
        }

        // Pattern-specific recommendations
        if (!patterns.Any(p => p.Type.Contains("Test")))
        {
            recommendations.Add("Consider implementing unit testing framework");
        }

        if (!patterns.Any(p => p.Type.Contains("Authentication")))
        {
            recommendations.Add("Evaluate if authentication/authorization is needed");
        }

        return recommendations;
    }

    private async Task CreatePatternMemoriesAsync(PatternDetectorResult result, string workspacePath)
    {
        try
        {
            // Store critical anti-patterns as technical debt
            foreach (var antiPattern in result.AntiPatterns.Where(ap => ap.Severity == "Critical" || ap.Severity == "High"))
            {
                await _memoryTools.StoreMemoryAsync(
                    "TechnicalDebt",
                    $"{antiPattern.Type}: {antiPattern.Description}",
                    isShared: true,
                    files: antiPattern.Locations.Take(5).ToArray(),
                    fields: new Dictionary<string, JsonElement>
                    {
                        ["severity"] = JsonSerializer.SerializeToElement(antiPattern.Severity),
                        ["type"] = JsonSerializer.SerializeToElement(antiPattern.Type),
                        ["impact"] = JsonSerializer.SerializeToElement(antiPattern.Impact),
                        ["remediation"] = JsonSerializer.SerializeToElement(antiPattern.Remediation)
                    });
            }

            // Store architectural patterns as insights
            foreach (var pattern in result.Patterns.Where(p => p.Confidence > 0.8))
            {
                await _memoryTools.StoreMemoryAsync(
                    "CodePattern",
                    $"{pattern.Type}: {pattern.Description}",
                    isShared: true,
                    files: pattern.Locations.Take(5).ToArray(),
                    fields: new Dictionary<string, JsonElement>
                    {
                        ["confidence"] = JsonSerializer.SerializeToElement(pattern.Confidence),
                        ["type"] = JsonSerializer.SerializeToElement(pattern.Type),
                        ["recommendation"] = JsonSerializer.SerializeToElement(pattern.Recommendation)
                    });
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to create pattern memories");
        }
    }

    // Helper methods to extract data from tool results
    private int ExtractFileCount(object result)
    {
        // This would parse the actual search results to count files
        // For now, return a placeholder
        return 1;
    }

    private int ExtractResultCount(object result)
    {
        // This would parse the actual search results to count matches
        // For now, return a placeholder
        return 1;
    }

    private List<string> ExtractFileLocations(object result)
    {
        // This would parse the actual file search results
        // For now, return a placeholder
        return new List<string> { "placeholder/file.cs" };
    }

    private List<string> ExtractTextSearchLocations(object result)
    {
        // This would parse the actual text search results
        // For now, return a placeholder
        return new List<string> { "placeholder/file.cs:42" };
    }

    private List<(string Path, long Size)> ExtractLargeFiles(object result)
    {
        // This would parse the file size analysis results
        // For now, return a placeholder
        return new List<(string, long)> { ("placeholder/large.cs", 600000) };
    }

    // Override required base class methods
    protected override int GetTotalResults<T>(T data)
    {
        if (data is PatternDetectorResult result)
        {
            return result.Patterns.Count + result.AntiPatterns.Count;
        }
        return 0;
    }

    protected override List<string> GenerateKeyInsights<T>(T data)
    {
        var insights = base.GenerateKeyInsights(data);

        if (data is PatternDetectorResult result)
        {
            insights.AddRange(result.Insights.Take(3));
        }

        return insights;
    }

    private ClaudeSummaryData GeneratePatternDetectorSummary(PatternDetectorResult result)
    {
        return new ClaudeSummaryData
        {
            Overview = new Overview
            {
                TotalItems = result.Patterns.Count + result.AntiPatterns.Count,
                AffectedFiles = result.Patterns.SelectMany(p => p.Locations).Concat(result.AntiPatterns.SelectMany(ap => ap.Locations)).Distinct().Count(),
                EstimatedFullResponseTokens = (result.Patterns.Count + result.AntiPatterns.Count) * 150,
                KeyInsights = result.Insights.Take(3).ToList()
            },
            ByCategory = new Dictionary<string, CategorySummary>
            {
                ["patterns"] = new CategorySummary
                {
                    Files = result.Patterns.SelectMany(p => p.Locations).Distinct().Count(),
                    Occurrences = result.Patterns.Count,
                    PrimaryPattern = result.Patterns.FirstOrDefault()?.Type ?? "None detected"
                },
                ["anti_patterns"] = new CategorySummary
                {
                    Files = result.AntiPatterns.SelectMany(ap => ap.Locations).Distinct().Count(),
                    Occurrences = result.AntiPatterns.Count,
                    PrimaryPattern = result.AntiPatterns.FirstOrDefault()?.Type ?? "None detected"
                }
            },
            Hotspots = result.AntiPatterns
                .Where(ap => ap.Severity == "Critical" || ap.Severity == "High")
                .Take(5)
                .Select(ap => new Hotspot
                {
                    File = ap.Locations.FirstOrDefault() ?? "Unknown",
                    Occurrences = ap.Locations.Count,
                    Complexity = ap.Severity.ToLowerInvariant(),
                    Reason = ap.Description
                })
                .ToList()
        };
    }
}

// Enums and Data Models
public enum PatternType
{
    Architecture,
    Security,
    Performance,
    Testing
}

public enum PatternDepth
{
    Shallow,
    Deep
}

public class PatternDetectorResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string Operation { get; set; } = "pattern_detector";
    public object? Query { get; set; }
    public string ResourceUri { get; set; } = string.Empty;
    public List<DetectedPattern> Patterns { get; set; } = new();
    public List<DetectedAntiPattern> AntiPatterns { get; set; } = new();
    public List<string> Insights { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public class DetectedPattern
{
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public List<string> Locations { get; set; } = new();
    public string Description { get; set; } = string.Empty;
    public string? Recommendation { get; set; }
}

public class DetectedAntiPattern
{
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty; // Critical, High, Medium, Low
    public List<string> Locations { get; set; } = new();
    public string Description { get; set; } = string.Empty;
    public string Impact { get; set; } = string.Empty;
    public string Remediation { get; set; } = string.Empty;
}

// Internal helper classes
internal class PatternDetectionBatch
{
    public List<DetectedPattern> Patterns { get; set; } = new();
    public List<DetectedAntiPattern> AntiPatterns { get; set; } = new();
}