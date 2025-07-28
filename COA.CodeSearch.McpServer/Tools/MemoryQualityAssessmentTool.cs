using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using Microsoft.Extensions.Logging;
using System.ComponentModel;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Parameters for memory quality assessment
/// </summary>
public class MemoryQualityAssessmentParams
{
    [Description("Memory ID to assess quality for")]
    public string? MemoryId { get; set; }

    [Description("Multiple memory IDs to assess (batch operation)")]
    public List<string>? MemoryIds { get; set; }

    [Description("Memory type to filter by (for bulk assessment)")]
    public string? MemoryType { get; set; }

    [Description("Quality threshold (0.0-1.0, default: 0.7)")]
    public double? QualityThreshold { get; set; }

    [Description("Specific validators to use (if not specified, all validators are used)")]
    public List<string>? EnabledValidators { get; set; }

    [Description("Include improvement suggestions in results")]
    public bool IncludeSuggestions { get; set; } = true;

    [Description("Allow automatic improvements to be applied")]
    public bool AllowAutoImprovements { get; set; } = false;

    [Description("Current workspace context for relevance assessment")]
    public string? ContextWorkspace { get; set; }

    [Description("Recently accessed files for context assessment")]
    public List<string>? RecentFiles { get; set; }

    [Description("Maximum number of memories to assess (for bulk operations)")]
    public int MaxResults { get; set; } = 50;

    [Description("Show detailed validation results")]
    public bool ShowDetails { get; set; } = false;

    [Description("Assessment mode: 'single', 'batch', 'bulk', or 'report'")]
    public string Mode { get; set; } = "single";
}

/// <summary>
/// Tool for assessing and improving memory quality
/// </summary>
public class MemoryQualityAssessmentTool
{
    public string Name => "memory_quality_assessment";
    public string Description => "Assess and improve the quality of stored memories with detailed scoring and suggestions";
    public ToolCategory Category => ToolCategory.Memory;

    private readonly ILogger<MemoryQualityAssessmentTool> _logger;
    private readonly IMemoryQualityValidator _qualityValidator;
    private readonly FlexibleMemoryService _memoryService;

    public MemoryQualityAssessmentTool(
        ILogger<MemoryQualityAssessmentTool> logger,
        IMemoryQualityValidator qualityValidator,
        FlexibleMemoryService memoryService)
    {
        _logger = logger;
        _qualityValidator = qualityValidator;
        _memoryService = memoryService;
    }

    public async Task<object> ExecuteAsync(MemoryQualityAssessmentParams parameters)
    {
        try
        {
            var options = CreateValidationOptions(parameters);
            
            return parameters.Mode switch
            {
                "single" => await AssessSingleMemoryAsync(parameters, options),
                "batch" => await AssessBatchMemoriesAsync(parameters, options),
                "bulk" => await AssessBulkMemoriesAsync(parameters, options),
                "report" => await GenerateQualityReportAsync(parameters, options),
                _ => new { error = "Invalid mode. Use 'single', 'batch', 'bulk', or 'report'" }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during memory quality assessment");
            return new { error = "Quality assessment failed", details = ex.Message };
        }
    }

    private async Task<object> AssessSingleMemoryAsync(
        MemoryQualityAssessmentParams parameters, 
        QualityValidationOptions options)
    {
        if (string.IsNullOrEmpty(parameters.MemoryId))
        {
            return new { error = "MemoryId is required for single memory assessment" };
        }

        var searchResult = await _memoryService.SearchMemoriesAsync(new FlexibleMemorySearchRequest
        {
            Query = $"id:{parameters.MemoryId}",
            MaxResults = 1
        });

        var memory = searchResult.Memories.FirstOrDefault();
        if (memory == null)
        {
            return new { error = $"Memory with ID '{parameters.MemoryId}' not found" };
        }

        var qualityScore = await _qualityValidator.ValidateQualityAsync(memory, options);

        if (parameters.AllowAutoImprovements && qualityScore.Suggestions.Any(s => s.CanAutoImplement))
        {
            var autoImprovements = qualityScore.Suggestions.Where(s => s.CanAutoImplement).ToList();
            var improvedMemory = await _qualityValidator.ApplyImprovementsAsync(memory, autoImprovements);
            
            // Re-assess after improvements
            var improvedScore = await _qualityValidator.ValidateQualityAsync(improvedMemory, options);
            
            return new
            {
                memoryId = parameters.MemoryId,
                originalScore = qualityScore,
                improvedScore = improvedScore,
                improvementsApplied = autoImprovements.Count,
                passesThreshold = improvedScore.PassesThreshold
            };
        }

        var result = new
        {
            memoryId = parameters.MemoryId,
            memoryType = memory.Type,
            overallScore = qualityScore.OverallScore,
            passesThreshold = qualityScore.PassesThreshold,
            explanation = qualityScore.SummaryExplanation,
            componentScores = parameters.ShowDetails ? qualityScore.ComponentScores : null,
            issues = parameters.ShowDetails ? qualityScore.Issues : 
                qualityScore.Issues.Where(i => i.Severity >= QualitySeverity.Major).ToList(),
            suggestions = parameters.IncludeSuggestions ? qualityScore.Suggestions : null,
            availableValidators = _qualityValidator.GetAvailableValidators().ToList()
        };

        return result;
    }

    private async Task<object> AssessBatchMemoriesAsync(
        MemoryQualityAssessmentParams parameters, 
        QualityValidationOptions options)
    {
        if (parameters.MemoryIds == null || !parameters.MemoryIds.Any())
        {
            return new { error = "MemoryIds list is required for batch assessment" };
        }

        var memories = new List<FlexibleMemoryEntry>();
        
        foreach (var memoryId in parameters.MemoryIds.Take(parameters.MaxResults))
        {
            var searchResult = await _memoryService.SearchMemoriesAsync(new FlexibleMemorySearchRequest
            {
                Query = $"id:{memoryId}",
                MaxResults = 1
            });
            
            var memory = searchResult.Memories.FirstOrDefault();
            if (memory != null)
            {
                memories.Add(memory);
            }
        }

        if (!memories.Any())
        {
            return new { error = "No memories found for the provided IDs" };
        }

        var qualityScores = await _qualityValidator.ValidateQualityBatchAsync(memories, options);

        var results = memories.Select(memory => new
        {
            memoryId = memory.Id,
            memoryType = memory.Type,
            overallScore = qualityScores[memory.Id].OverallScore,
            passesThreshold = qualityScores[memory.Id].PassesThreshold,
            explanation = qualityScores[memory.Id].SummaryExplanation,
            majorIssuesCount = qualityScores[memory.Id].Issues.Count(i => i.Severity >= QualitySeverity.Major),
            suggestionsCount = qualityScores[memory.Id].Suggestions.Count,
            autoImprovementsAvailable = qualityScores[memory.Id].Suggestions.Count(s => s.CanAutoImplement)
        }).ToList();

        return new
        {
            assessedCount = results.Count,
            averageScore = results.Average(r => r.overallScore),
            passingCount = results.Count(r => r.passesThreshold),
            failingCount = results.Count(r => !r.passesThreshold),
            results = results,
            summary = GenerateAssessmentSummary(results.Cast<object>().ToList())
        };
    }

    private async Task<object> AssessBulkMemoriesAsync(
        MemoryQualityAssessmentParams parameters, 
        QualityValidationOptions options)
    {
        var searchRequest = new FlexibleMemorySearchRequest
        {
            Query = string.IsNullOrEmpty(parameters.MemoryType) ? "*" : $"type:{parameters.MemoryType}",
            MaxResults = parameters.MaxResults,
            IncludeArchived = false
        };

        var searchResult = await _memoryService.SearchMemoriesAsync(searchRequest);
        
        if (!searchResult.Memories.Any())
        {
            return new { error = "No memories found for bulk assessment" };
        }

        var qualityScores = await _qualityValidator.ValidateQualityBatchAsync(searchResult.Memories, options);

        var results = searchResult.Memories.Select(memory => new
        {
            memoryId = memory.Id,
            memoryType = memory.Type,
            overallScore = qualityScores[memory.Id].OverallScore,
            passesThreshold = qualityScores[memory.Id].PassesThreshold,
            created = memory.Created,
            lastAccessed = memory.LastAccessed,
            accessCount = memory.AccessCount
        }).ToList();

        // Group by memory type for analysis
        var typeAnalysis = results
            .GroupBy(r => r.memoryType)
            .Select(g => new
            {
                memoryType = g.Key,
                count = g.Count(),
                averageScore = g.Average(r => r.overallScore),
                passingRate = (double)g.Count(r => r.passesThreshold) / g.Count(),
                recentCount = g.Count(r => (DateTime.UtcNow - r.created).TotalDays <= 30)
            })
            .OrderByDescending(t => t.count)
            .ToList();

        return new
        {
            totalAssessed = results.Count,
            overallAverageScore = results.Average(r => r.overallScore),
            overallPassingRate = (double)results.Count(r => r.passesThreshold) / results.Count,
            typeAnalysis = typeAnalysis,
            topIssues = await GetTopQualityIssuesAsync(searchResult.Memories, options),
            recommendations = GenerateBulkRecommendations(results.Cast<object>().ToList(), typeAnalysis.Cast<object>().ToList())
        };
    }

    private async Task<object> GenerateQualityReportAsync(
        MemoryQualityAssessmentParams parameters,
        QualityValidationOptions options)
    {
        var searchRequest = new FlexibleMemorySearchRequest
        {
            Query = string.IsNullOrEmpty(parameters.MemoryType) ? "*" : $"type:{parameters.MemoryType}",
            MaxResults = parameters.MaxResults,
            IncludeArchived = false
        };

        var searchResult = await _memoryService.SearchMemoriesAsync(searchRequest);
        var qualityScores = await _qualityValidator.ValidateQualityBatchAsync(searchResult.Memories, options);

        // Detailed analysis for reporting
        var allIssues = qualityScores.Values.SelectMany(s => s.Issues).ToList();
        var allSuggestions = qualityScores.Values.SelectMany(s => s.Suggestions).ToList();

        var issueAnalysis = allIssues
            .GroupBy(i => i.Category)
            .Select(g => new
            {
                category = g.Key,
                count = g.Count(),
                severity = g.GroupBy(i => i.Severity)
                    .ToDictionary(sg => sg.Key.ToString(), sg => sg.Count())
            })
            .OrderByDescending(i => i.count)
            .ToList();

        var suggestionAnalysis = allSuggestions
            .GroupBy(s => s.Type)
            .Select(g => new
            {
                type = g.Key.ToString(),
                count = g.Count(),
                averageImpact = g.Average(s => s.ExpectedImpact),
                autoImplementableCount = g.Count(s => s.CanAutoImplement)
            })
            .OrderByDescending(s => s.count)
            .ToList();

        return new
        {
            reportGenerated = DateTime.UtcNow,
            memoriesAnalyzed = searchResult.Memories.Count,
            qualityThreshold = options.PassingThreshold,
            overallQuality = new
            {
                averageScore = qualityScores.Values.Average(s => s.OverallScore),
                passingRate = (double)qualityScores.Values.Count(s => s.PassesThreshold) / qualityScores.Count,
                distribution = GetScoreDistribution(qualityScores.Values.Select(s => s.OverallScore))
            },
            issueAnalysis = issueAnalysis,
            suggestionAnalysis = suggestionAnalysis,
            validatorsUsed = _qualityValidator.GetAvailableValidators().ToList(),
            actionItems = GenerateActionItems(issueAnalysis.Cast<object>().ToList(), suggestionAnalysis.Cast<object>().ToList())
        };
    }

    private QualityValidationOptions CreateValidationOptions(MemoryQualityAssessmentParams parameters)
    {
        return new QualityValidationOptions
        {
            PassingThreshold = parameters.QualityThreshold ?? 0.7,
            EnabledValidators = parameters.EnabledValidators?.ToHashSet() ?? new HashSet<string>(),
            IncludeImprovementSuggestions = parameters.IncludeSuggestions,
            AllowAutoImprovements = parameters.AllowAutoImprovements,
            ContextWorkspace = parameters.ContextWorkspace,
            RecentFiles = parameters.RecentFiles
        };
    }

    private string GenerateAssessmentSummary(List<object> results)
    {
        var total = results.Count;
        var passing = results.Count(r => ((dynamic)r).passesThreshold);
        var avgScore = results.Average(r => (double)((dynamic)r).overallScore);
        
        return $"{passing}/{total} memories pass quality threshold. Average score: {avgScore:F2}";
    }

    private async Task<List<object>> GetTopQualityIssuesAsync(
        List<FlexibleMemoryEntry> memories, 
        QualityValidationOptions options)
    {
        var qualityScores = await _qualityValidator.ValidateQualityBatchAsync(memories, options);
        
        return qualityScores.Values
            .SelectMany(s => s.Issues)
            .GroupBy(i => $"{i.Category}: {i.Description}")
            .Select(g => new
            {
                issue = g.Key,
                count = g.Count(),
                severity = g.First().Severity.ToString(),
                affectedMemories = g.Count()
            })
            .OrderByDescending(i => i.count)
            .Take(10)
            .ToList<object>();
    }

    private List<string> GenerateBulkRecommendations(List<object> results, List<object> typeAnalysis)
    {
        var recommendations = new List<string>();
        
        var overallPassingRate = (double)results.Count(r => ((dynamic)r).passesThreshold) / results.Count;
        if (overallPassingRate < 0.7)
        {
            recommendations.Add($"Overall quality is below target ({overallPassingRate:P}). Focus on improving memory completeness and relevance.");
        }

        var poorPerformingTypes = typeAnalysis.Where(t => (double)((dynamic)t).passingRate < 0.6).ToList();
        foreach (dynamic type in poorPerformingTypes)
        {
            recommendations.Add($"Memory type '{type.memoryType}' has low quality ({type.passingRate:P} passing). Review guidelines for this type.");
        }

        return recommendations;
    }

    private Dictionary<string, int> GetScoreDistribution(IEnumerable<double> scores)
    {
        var distribution = new Dictionary<string, int>
        {
            ["0.0-0.2"] = 0,
            ["0.2-0.4"] = 0,
            ["0.4-0.6"] = 0,
            ["0.6-0.8"] = 0,
            ["0.8-1.0"] = 0
        };

        foreach (var score in scores)
        {
            var bucket = score switch
            {
                < 0.2 => "0.0-0.2",
                < 0.4 => "0.2-0.4",
                < 0.6 => "0.4-0.6",
                < 0.8 => "0.6-0.8",
                _ => "0.8-1.0"
            };
            
            distribution[bucket]++;
        }

        return distribution;
    }

    private List<string> GenerateActionItems(List<object> issueAnalysis, List<object> suggestionAnalysis)
    {
        var actionItems = new List<string>();
        
        if (issueAnalysis.Any())
        {
            dynamic topIssue = issueAnalysis.First();
            actionItems.Add($"Address most common issue: {topIssue.category} ({topIssue.count} occurrences)");
        }

        if (suggestionAnalysis.Any())
        {
            var autoSuggestion = suggestionAnalysis.FirstOrDefault(s => (int)((dynamic)s).autoImplementableCount > 0);
            if (autoSuggestion != null)
            {
                dynamic suggestion = autoSuggestion;
                actionItems.Add($"Apply {suggestion.autoImplementableCount} automatic improvements for {suggestion.type}");
            }
        }

        return actionItems;
    }
}