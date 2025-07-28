using COA.CodeSearch.McpServer.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Main service for validating memory quality with type-specific criteria
/// </summary>
public class MemoryQualityValidationService : IMemoryQualityValidator
{
    private readonly ILogger<MemoryQualityValidationService> _logger;
    private readonly Dictionary<string, IQualityValidator> _validators;
    private readonly Dictionary<string, QualityCriteria> _typeCriteria;

    public MemoryQualityValidationService(
        ILogger<MemoryQualityValidationService> logger,
        IEnumerable<IQualityValidator> validators)
    {
        _logger = logger;
        _validators = validators.ToDictionary(v => v.Name, v => v);
        _typeCriteria = InitializeTypeCriteria();
    }

    public async Task<MemoryQualityScore> ValidateQualityAsync(
        FlexibleMemoryEntry memory, 
        QualityValidationOptions? options = null)
    {
        options ??= new QualityValidationOptions();
        
        try
        {
            var score = new MemoryQualityScore();
            var validatorsToUse = GetValidatorsForMemory(memory, options);
            
            var validationTasks = validatorsToUse.Select(async validator =>
            {
                try
                {
                    var result = await validator.ValidateAsync(memory, options);
                    return new { validator.Name, validator.Weight, Result = result };
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Quality validator {ValidatorName} failed for memory {MemoryId}", 
                        validator.Name, memory.Id);
                    return new { validator.Name, validator.Weight, Result = (QualityValidatorResult?)null };
                }
            });

            var validationResults = await Task.WhenAll(validationTasks);
            
            // Calculate weighted overall score
            double totalWeight = 0;
            double weightedScore = 0;
            
            foreach (var result in validationResults.Where(r => r.Result != null))
            {
                var componentScore = Math.Max(0, Math.Min(1, result.Result!.Score));
                score.ComponentScores[result.Name] = componentScore;
                
                weightedScore += componentScore * result.Weight;
                totalWeight += result.Weight;
                
                score.Issues.AddRange(result.Result.Issues);
                
                if (options.IncludeImprovementSuggestions)
                {
                    score.Suggestions.AddRange(result.Result.Suggestions);
                }
            }

            score.OverallScore = totalWeight > 0 ? weightedScore / totalWeight : 0;
            score.PassesThreshold = score.OverallScore >= options.PassingThreshold;
            score.SummaryExplanation = GenerateSummaryExplanation(score, memory);
            
            _logger.LogDebug("Memory {MemoryId} quality score: {Score:F2} (threshold: {Threshold:F2})", 
                memory.Id, score.OverallScore, options.PassingThreshold);
                
            return score;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate quality for memory {MemoryId}", memory.Id);
            return new MemoryQualityScore 
            { 
                OverallScore = 0,
                SummaryExplanation = "Quality validation failed due to an error"
            };
        }
    }

    public async Task<Dictionary<string, MemoryQualityScore>> ValidateQualityBatchAsync(
        IEnumerable<FlexibleMemoryEntry> memories, 
        QualityValidationOptions? options = null)
    {
        var tasks = memories.Select(async memory => new 
        { 
            Memory = memory, 
            Score = await ValidateQualityAsync(memory, options) 
        });
        
        var results = await Task.WhenAll(tasks);
        return results.ToDictionary(r => r.Memory.Id, r => r.Score);
    }

    public QualityCriteria GetCriteriaForType(string memoryType)
    {
        return _typeCriteria.GetValueOrDefault(memoryType, _typeCriteria["Default"]);
    }

    public async Task<FlexibleMemoryEntry> ApplyImprovementsAsync(
        FlexibleMemoryEntry memory, 
        List<QualityImprovement> improvements)
    {
        var improvedMemory = new FlexibleMemoryEntry
        {
            Id = memory.Id,
            Type = memory.Type,
            Content = memory.Content,
            Created = memory.Created,
            Modified = DateTime.UtcNow,
            LastAccessed = memory.LastAccessed,
            AccessCount = memory.AccessCount,
            SessionId = memory.SessionId,
            IsShared = memory.IsShared,
            FilesInvolved = memory.FilesInvolved,
            Fields = memory.Fields?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
            // Archived = memory.Archived,  // Property doesn't exist
            // Expires = memory.Expires     // Property doesn't exist
        };

        var applicableImprovements = improvements.Where(i => i.CanAutoImplement).ToList();
        
        foreach (var improvement in applicableImprovements)
        {
            try
            {
                improvedMemory = await ApplySingleImprovementAsync(improvedMemory, improvement);
                _logger.LogDebug("Applied improvement {ImprovementType} to memory {MemoryId}", 
                    improvement.Type, memory.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to apply improvement {ImprovementType} to memory {MemoryId}", 
                    improvement.Type, memory.Id);
            }
        }

        return improvedMemory;
    }

    public IEnumerable<string> GetAvailableValidators()
    {
        return _validators.Keys;
    }

    private List<IQualityValidator> GetValidatorsForMemory(
        FlexibleMemoryEntry memory, 
        QualityValidationOptions options)
    {
        var validatorsToUse = new List<IQualityValidator>();
        
        // If specific validators are specified, use only those
        if (options.EnabledValidators.Any())
        {
            validatorsToUse.AddRange(
                options.EnabledValidators
                    .Where(name => _validators.ContainsKey(name))
                    .Select(name => _validators[name])
            );
        }
        else
        {
            // Use all available validators
            validatorsToUse.AddRange(_validators.Values);
        }

        return validatorsToUse;
    }

    private string GenerateSummaryExplanation(MemoryQualityScore score, FlexibleMemoryEntry memory)
    {
        var explanation = new List<string>();
        
        if (score.PassesThreshold)
        {
            explanation.Add($"Memory meets quality threshold ({score.OverallScore:F2}/1.0)");
        }
        else
        {
            explanation.Add($"Memory below quality threshold ({score.OverallScore:F2}/1.0)");
        }

        var majorIssues = score.Issues.Where(i => i.Severity >= QualitySeverity.Major).ToList();
        if (majorIssues.Any())
        {
            explanation.Add($"{majorIssues.Count} major issues found");
        }

        var improvementCount = score.Suggestions.Count;
        if (improvementCount > 0)
        {
            var autoImprovements = score.Suggestions.Count(s => s.CanAutoImplement);
            explanation.Add($"{improvementCount} improvements suggested ({autoImprovements} automatic)");
        }

        return string.Join(". ", explanation);
    }

    private async Task<FlexibleMemoryEntry> ApplySingleImprovementAsync(
        FlexibleMemoryEntry memory,
        QualityImprovement improvement)
    {
        // Implementation would depend on the improvement type
        // For now, return the memory unchanged
        await Task.CompletedTask;
        return memory;
    }

    private Dictionary<string, QualityCriteria> InitializeTypeCriteria()
    {
        return new Dictionary<string, QualityCriteria>
        {
            ["Default"] = new QualityCriteria
            {
                MemoryType = "Default",
                RequiredComponentWeights = new Dictionary<string, double>
                {
                    ["completeness"] = 0.3,
                    ["relevance"] = 0.3,
                    ["consistency"] = 0.2,
                    ["clarity"] = 0.2
                },
                MinContentLength = 10,
                RecommendedContentLength = 50
            },
            ["TechnicalDebt"] = new QualityCriteria
            {
                MemoryType = "TechnicalDebt",
                RequiredComponentWeights = new Dictionary<string, double>
                {
                    ["completeness"] = 0.4,
                    ["relevance"] = 0.3,
                    ["consistency"] = 0.2,
                    ["actionability"] = 0.1
                },
                RequiredFields = new List<string> { "priority", "effort" },
                RecommendedFields = new List<string> { "impact", "deadline", "owner" },
                MinContentLength = 20,
                RecommendedContentLength = 100
            },
            ["ArchitecturalDecision"] = new QualityCriteria
            {
                MemoryType = "ArchitecturalDecision",
                RequiredComponentWeights = new Dictionary<string, double>
                {
                    ["completeness"] = 0.4,
                    ["consistency"] = 0.3,
                    ["relevance"] = 0.2,
                    ["rationale"] = 0.1
                },
                RequiredFields = new List<string> { "decision", "rationale" },
                RecommendedFields = new List<string> { "alternatives", "consequences", "status" },
                MinContentLength = 50,
                RecommendedContentLength = 200
            },
            ["CodePattern"] = new QualityCriteria
            {
                MemoryType = "CodePattern",
                RequiredComponentWeights = new Dictionary<string, double>
                {
                    ["completeness"] = 0.3,
                    ["relevance"] = 0.3,
                    ["reusability"] = 0.2,
                    ["clarity"] = 0.2
                },
                RequiredFields = new List<string> { "pattern", "usage" },
                RecommendedFields = new List<string> { "examples", "when-to-use", "alternatives" },
                MinContentLength = 30,
                RecommendedContentLength = 150
            },
            ["SecurityRule"] = new QualityCriteria
            {
                MemoryType = "SecurityRule",
                RequiredComponentWeights = new Dictionary<string, double>
                {
                    ["completeness"] = 0.4,
                    ["relevance"] = 0.3,
                    ["enforceability"] = 0.2,
                    ["clarity"] = 0.1
                },
                RequiredFields = new List<string> { "rule", "rationale", "enforcement" },
                RecommendedFields = new List<string> { "violations", "exceptions", "references" },
                MinContentLength = 25,
                RecommendedContentLength = 120
            }
        };
    }
}