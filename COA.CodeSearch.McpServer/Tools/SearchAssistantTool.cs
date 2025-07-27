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
/// AI-optimized tool that orchestrates multi-step search operations while maintaining context.
/// Designed to help AI agents perform complex discovery tasks efficiently.
/// </summary>
public class SearchAssistantTool : ClaudeOptimizedToolBase
{
    public override string ToolName => ToolNames.SearchAssistant;
    public override string Description => "Orchestrates multi-step search operations with context preservation";
    public override ToolCategory Category => ToolCategory.Search;

    private readonly FlexibleMemoryTools _memoryTools;
    private readonly FastTextSearchToolV2 _textSearchTool;
    private readonly FastFileSearchToolV2 _fileSearchTool;
    private readonly IErrorRecoveryService _errorRecoveryService;

    public SearchAssistantTool(
        ILogger<SearchAssistantTool> logger,
        FlexibleMemoryTools memoryTools,
        FastTextSearchToolV2 textSearchTool,
        FastFileSearchToolV2 fileSearchTool,
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
        _errorRecoveryService = errorRecoveryService;
    }

    public async Task<object> ExecuteAsync(
        string goal,
        string workspacePath,
        SearchConstraints? constraints = null,
        string? previousContext = null,
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

            Logger.LogInformation("Search assistant starting for goal: {Goal} in {WorkspacePath}", goal, workspacePath);

            // Validate input
            if (string.IsNullOrWhiteSpace(goal))
            {
                return UnifiedToolResponse<object>.CreateError(
                    ErrorCodes.VALIDATION_ERROR,
                    "Search goal cannot be empty",
                    _errorRecoveryService.GetValidationErrorRecovery("goal", "clear description of what you're looking for"));
            }

            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                return UnifiedToolResponse<object>.CreateError(
                    ErrorCodes.VALIDATION_ERROR,
                    "Workspace path cannot be empty",
                    _errorRecoveryService.GetValidationErrorRecovery("workspacePath", "absolute directory path"));
            }

            var strategy = new List<string>();
            var findings = new SearchAssistantFindings();
            var resourceUri = $"codesearch-search-assistant://{Guid.NewGuid():N}";

            // Step 1: Analyze the goal and determine search strategy
            var searchPlan = AnalyzeGoal(goal, constraints);
            strategy.AddRange(searchPlan.Steps);

            // Step 2: Execute the search plan using existing tools
            var searchResults = await ExecuteSearchPlanAsync(searchPlan, workspacePath, constraints, cancellationToken);
            findings.Primary.AddRange(searchResults.Results);
            strategy.AddRange(searchResults.ExecutedSteps);

            // Step 3: Analyze findings for patterns and insights
            var insights = AnalyzeFindings(findings.Primary, goal);
            findings.Insights.AddRange(insights);

            // Step 4: Generate suggested next actions
            var suggestedNext = GenerateSuggestedActions(findings, goal);

            // Step 5: Store context as a temporary memory for future use
            await StoreSearchContextAsync(resourceUri, findings, strategy, goal);

            var result = new SearchAssistantResult
            {
                Strategy = strategy,
                Findings = findings,
                ResourceUri = resourceUri,
                SuggestedNext = suggestedNext,
                Success = true,
                Operation = ToolNames.SearchAssistant,
                Query = new { goal, workspacePath }
            };

            Logger.LogInformation("Search assistant completed. Found {PrimaryCount} primary results",
                findings.Primary.Count);

            // Return Claude-optimized response
            return await CreateClaudeResponseAsync(
                result,
                mode,
                GenerateSearchAssistantSummary,
                cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Search assistant failed for goal: {Goal}", goal);
            return UnifiedToolResponse<object>.CreateError(
                ErrorCodes.INTERNAL_ERROR,
                ex.Message,
                _errorRecoveryService.GetValidationErrorRecovery(ToolNames.SearchAssistant, "Try refining your search goal or check workspace path"));
        }
    }

    private SearchPlan AnalyzeGoal(string goal, SearchConstraints? constraints)
    {
        var plan = new SearchPlan();
        var goalLower = goal.ToLowerInvariant();

        // Analyze the goal to determine search strategy
        if (goalLower.Contains("error") || goalLower.Contains("exception") || goalLower.Contains("bug"))
        {
            plan.Steps.Add("Search for error handling patterns");
            plan.Operations.Add(new SearchOperation("text_search", "try|catch|throw|exception", "regex"));
            plan.Operations.Add(new SearchOperation("text_search", "error|Error", "standard"));
        }
        else if (goalLower.Contains("test") || goalLower.Contains("unit test") || goalLower.Contains("testing"))
        {
            plan.Steps.Add("Locate test files and patterns");
            plan.Operations.Add(new SearchOperation("file_search", "*Test.cs", "wildcard"));
            plan.Operations.Add(new SearchOperation("text_search", "[Test]|[Fact]|[Theory]", "regex"));
        }
        else if (goalLower.Contains("config") || goalLower.Contains("setting") || goalLower.Contains("option"))
        {
            plan.Steps.Add("Find configuration files and settings");
            plan.Operations.Add(new SearchOperation("file_search", "*.config|*.json|*.yaml|*.yml", "wildcard"));
            plan.Operations.Add(new SearchOperation("text_search", "appsettings|configuration", "standard"));
        }
        else if (goalLower.Contains("api") || goalLower.Contains("endpoint") || goalLower.Contains("controller"))
        {
            plan.Steps.Add("Discover API endpoints and controllers");
            plan.Operations.Add(new SearchOperation("file_search", "*Controller.cs", "wildcard"));
            plan.Operations.Add(new SearchOperation("text_search", "[HttpGet]|[HttpPost]|[Route]", "regex"));
        }
        else
        {
            // General search
            plan.Steps.Add("Perform general text search for goal terms");
            plan.Operations.Add(new SearchOperation("text_search", goal, "standard"));
        }

        return plan;
    }

    private async Task<SearchExecutionResult> ExecuteSearchPlanAsync(
        SearchPlan plan, 
        string workspacePath,
        SearchConstraints? constraints, 
        CancellationToken cancellationToken)
    {
        var result = new SearchExecutionResult();

        foreach (var operation in plan.Operations)
        {
            try
            {
                switch (operation.Type)
                {
                    case "text_search":
                        var textResults = await _textSearchTool.ExecuteAsync(
                            operation.Query,
                            workspacePath,
                            filePattern: BuildFilePattern(constraints),
                            extensions: constraints?.FileTypes?.ToArray(),
                            maxResults: constraints?.MaxResults ?? 50,
                            searchType: operation.SearchType ?? "standard",
                            mode: ResponseMode.Full,
                            cancellationToken: cancellationToken);

                        if (textResults != null)
                        {
                            result.Results.Add(textResults);
                            result.ExecutedSteps.Add($"Text search for '{operation.Query}' completed successfully");
                        }
                        break;

                    case "file_search":
                        var fileResults = await _fileSearchTool.ExecuteAsync(
                            operation.Query,
                            workspacePath,
                            searchType: operation.SearchType ?? "standard",
                            maxResults: constraints?.MaxResults ?? 50,
                            mode: ResponseMode.Full,
                            cancellationToken: cancellationToken);

                        if (fileResults != null)
                        {
                            result.Results.Add(fileResults);
                            result.ExecutedSteps.Add($"File search for '{operation.Query}' completed successfully");
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Search operation failed: {Type} {Query}", operation.Type, operation.Query);
                result.ExecutedSteps.Add($"Search operation failed: {operation.Type} '{operation.Query}' - {ex.Message}");
            }
        }

        return result;
    }

    private List<string> AnalyzeFindings(List<object> findings, string goal)
    {
        var insights = new List<string>();

        if (findings.Count > 50)
        {
            insights.Add($"Large result set with {findings.Count} findings - consider refining the goal");
        }

        if (goal.ToLowerInvariant().Contains("error") && findings.Count > 10)
        {
            insights.Add("Multiple error handling locations found - consider centralizing error handling");
        }

        if (goal.ToLowerInvariant().Contains("test") && findings.Count < 5)
        {
            insights.Add("Limited test coverage detected - consider adding more tests");
        }

        return insights;
    }

    private List<string> GenerateSuggestedActions(SearchAssistantFindings findings, string goal)
    {
        var actions = new List<string>();

        if (findings.Primary.Count > 50)
        {
            actions.Add("Consider refining search criteria - many results found");
        }

        if (goal.ToLowerInvariant().Contains("refactor"))
        {
            actions.Add("Create architectural decision memory before refactoring");
        }

        if (actions.Count == 0)
        {
            actions.Add("Consider expanding search with broader terms");
            actions.Add("Store important findings as memories for future reference");
        }

        return actions;
    }

    private async Task StoreSearchContextAsync(string resourceUri, SearchAssistantFindings findings, List<string> strategy, string goal)
    {
        try
        {
            var searchId = resourceUri.Split("://")[1];
            var contextContent = JsonSerializer.Serialize(new
            {
                goal,
                strategy,
                findings = new
                {
                    primaryCount = findings.Primary.Count,
                    insights = findings.Insights
                },
                timestamp = DateTime.UtcNow,
                resourceUri
            }, new JsonSerializerOptions { WriteIndented = true });

            await _memoryTools.StoreMemoryAsync(
                "SearchContext",
                $"Search assistant context for: {goal}",
                isShared: false,
                files: new string[0],
                fields: new Dictionary<string, JsonElement>
                {
                    ["searchId"] = JsonSerializer.SerializeToElement(searchId),
                    ["goal"] = JsonSerializer.SerializeToElement(goal),
                    ["resultCount"] = JsonSerializer.SerializeToElement(findings.Primary.Count)
                });
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to store search context for {ResourceUri}", resourceUri);
        }
    }

    private string? BuildFilePattern(SearchConstraints? constraints)
    {
        if (constraints?.FileTypes == null || !constraints.FileTypes.Any())
            return null;

        if (constraints.FileTypes.Count == 1)
            return $"*.{constraints.FileTypes.First()}";

        return $"*.{{{string.Join(",", constraints.FileTypes)}}}";
    }

    // Override required base class methods
    protected override int GetTotalResults<T>(T data)
    {
        if (data is SearchAssistantResult result)
        {
            return result.Findings.Primary.Count;
        }
        return 0;
    }

    protected override List<string> GenerateKeyInsights<T>(T data)
    {
        var insights = base.GenerateKeyInsights(data);

        if (data is SearchAssistantResult result)
        {
            insights.AddRange(result.Findings.Insights.Take(3));
        }

        return insights;
    }

    private ClaudeSummaryData GenerateSearchAssistantSummary(SearchAssistantResult result)
    {
        return new ClaudeSummaryData
        {
            Overview = new Overview
            {
                TotalItems = result.Findings.Primary.Count,
                AffectedFiles = 0, // Would need to parse this from actual search results
                EstimatedFullResponseTokens = result.Findings.Primary.Count * 100, // Rough estimate
                KeyInsights = result.Findings.Insights.Take(3).ToList()
            },
            ByCategory = new Dictionary<string, CategorySummary>
            {
                ["search_operations"] = new CategorySummary
                {
                    Files = result.Findings.Primary.Count,
                    Occurrences = result.Strategy.Count,
                    PrimaryPattern = "Multi-step search orchestration"
                }
            },
            Hotspots = new List<Hotspot>
            {
                new Hotspot
                {
                    File = "Search Results",
                    Occurrences = result.Findings.Primary.Count,
                    Complexity = result.Findings.Primary.Count > 50 ? "high" : "medium",
                    Reason = $"Found {result.Findings.Primary.Count} results for goal: {result.Query}"
                }
            }
        };
    }
}

/// <summary>
/// Search constraints to limit scope and improve performance
/// </summary>
public class SearchConstraints
{
    /// <summary>
    /// File types to include (e.g., ["cs", "ts"])
    /// </summary>
    public List<string>? FileTypes { get; set; }

    /// <summary>
    /// Paths to exclude from search
    /// </summary>
    public List<string>? ExcludePaths { get; set; }

    /// <summary>
    /// Maximum number of results to return
    /// </summary>
    public int? MaxResults { get; set; }
}

/// <summary>
/// Result of the search assistant operation
/// </summary>
public class SearchAssistantResult
{
    /// <summary>
    /// Success indicator
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if operation failed
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Operation name for consistency with other tools
    /// </summary>
    public string Operation { get; set; } = ToolNames.SearchAssistant;

    /// <summary>
    /// Query information for context
    /// </summary>
    public object? Query { get; set; }

    /// <summary>
    /// Steps taken during the search operation
    /// </summary>
    public List<string> Strategy { get; set; } = new();

    /// <summary>
    /// The search findings organized by category
    /// </summary>
    public SearchAssistantFindings Findings { get; set; } = new();

    /// <summary>
    /// Persistent resource URI for this search context
    /// </summary>
    public string ResourceUri { get; set; } = string.Empty;

    /// <summary>
    /// Recommended follow-up actions
    /// </summary>
    public List<string> SuggestedNext { get; set; } = new();
}

/// <summary>
/// Organized search findings
/// </summary>
public class SearchAssistantFindings
{
    /// <summary>
    /// Main search results
    /// </summary>
    public List<object> Primary { get; set; } = new();

    /// <summary>
    /// Related content discovered during search
    /// </summary>
    public List<object> Related { get; set; } = new();

    /// <summary>
    /// Patterns and insights discovered
    /// </summary>
    public List<string> Insights { get; set; } = new();
}

// Internal helper classes
internal class SearchPlan
{
    public List<string> Steps { get; set; } = new();
    public List<SearchOperation> Operations { get; set; } = new();
}

internal class SearchOperation
{
    public string Type { get; set; }
    public string Query { get; set; }
    public string? SearchType { get; set; }

    public SearchOperation(string type, string query, string? searchType = null)
    {
        Type = type;
        Query = query;
        SearchType = searchType;
    }
}

internal class SearchExecutionResult
{
    public List<object> Results { get; set; } = new();
    public List<string> ExecutedSteps { get; set; } = new();
}