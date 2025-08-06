# Migration Example: TextSearchTool

This document provides a detailed, step-by-step example of migrating the `text_search` tool to use the COA MCP Framework. This serves as a template for migrating other tools.

## Current Implementation Analysis

### Original TextSearchTool Structure
```csharp
[McpServerToolType]
public class TextSearchTool
{
    private readonly ILuceneSearchService _searchService;
    private readonly IQueryExecutionService _queryService;
    private readonly ILogger<TextSearchTool> _logger;

    [McpServerTool(Name = "text_search")]
    [Description("Searches for text patterns within indexed files using Lucene.NET")]
    public async Task<object> SearchTextAsync(
        [Description("The search query")] string query,
        [Description("Search type: literal, regex, or code")] string? searchType = "code",
        [Description("File path to search in")] string? path = null,
        [Description("Maximum results to return")] int? maxResults = null)
    {
        // Validation
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Query is required");

        // Execute search
        var results = await _queryService.ExecuteQueryAsync(new QueryRequest
        {
            Query = query,
            SearchType = searchType,
            Path = path,
            MaxResults = maxResults ?? 100
        });

        // Return results
        return new
        {
            results = results.Select(r => new
            {
                file = r.FilePath,
                matches = r.Matches,
                context = r.Context,
                score = r.Score
            }),
            totalMatches = results.TotalMatches,
            searchType = searchType
        };
    }
}
```

## Migrated Implementation

### 1. Create Parameter Class
```csharp
public class TextSearchParams
{
    [Description("The search query")]
    [Required]
    public string Query { get; set; } = string.Empty;

    [Description("Search type: literal, regex, or code")]
    [AllowedValues("literal", "regex", "code")]
    public string SearchType { get; set; } = "code";

    [Description("File path to search in")]
    public string? Path { get; set; }

    [Description("Maximum results to return")]
    [Range(1, 1000)]
    public int MaxResults { get; set; } = 100;

    [Description("Response mode: full or summary")]
    [AllowedValues("full", "summary")]
    public string ResponseMode { get; set; } = "auto";

    [Description("Include insights in response")]
    public bool IncludeInsights { get; set; } = true;

    [Description("Include suggested actions")]
    public bool IncludeActions { get; set; } = true;
}
```

### 2. Create Result Model
```csharp
public class TextSearchResult : AIOptimizedResponse
{
    public TextSearchData Data { get; set; } = new();
}

public class TextSearchData : AIResponseData
{
    public List<SearchResultItem> SearchResults { get; set; } = new();
    public int TotalMatches { get; set; }
    public string SearchType { get; set; } = string.Empty;
    public SearchDistribution? Distribution { get; set; }
}

public class SearchResultItem
{
    public string FilePath { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public List<SearchMatch> Matches { get; set; } = new();
    public double Score { get; set; }
    public SearchContext? Context { get; set; }
}

public class SearchDistribution
{
    public Dictionary<string, int> ByFileType { get; set; } = new();
    public Dictionary<string, int> ByDirectory { get; set; } = new();
    public List<HotspotInfo> Hotspots { get; set; } = new();
}
```

### 3. Implement Migrated Tool
```csharp
[McpServerToolType]
public class TextSearchTool : McpToolBase<TextSearchParams, TextSearchResult>
{
    private readonly ILuceneSearchService _searchService;
    private readonly IQueryExecutionService _queryService;
    private readonly ITextSearchResponseBuilder _responseBuilder;
    private readonly ITokenEstimator _tokenEstimator;
    private readonly IResponseCacheService _cacheService;
    private readonly ICacheKeyGenerator _cacheKeyGenerator;

    public TextSearchTool(
        ILuceneSearchService searchService,
        IQueryExecutionService queryService,
        ITextSearchResponseBuilder responseBuilder,
        ITokenEstimator tokenEstimator,
        IResponseCacheService cacheService,
        ICacheKeyGenerator cacheKeyGenerator,
        ILogger<TextSearchTool> logger)
        : base(logger)
    {
        _searchService = searchService;
        _queryService = queryService;
        _responseBuilder = responseBuilder;
        _tokenEstimator = tokenEstimator;
        _cacheService = cacheService;
        _cacheKeyGenerator = cacheKeyGenerator;
    }

    protected override string ToolName => "text_search";
    
    protected override string ToolDescription => 
        "Searches for text patterns within indexed files using Lucene.NET with AI-optimized responses";

    protected override async Task<TextSearchResult> ExecuteCoreAsync(
        TextSearchParams parameters,
        ToolExecutionContext context)
    {
        // Check cache first
        var cacheKey = _cacheKeyGenerator.GenerateKey(ToolName, parameters, context.UserId);
        var cachedResult = await _cacheService.GetAsync<TextSearchResult>(cacheKey);
        if (cachedResult != null)
        {
            Logger.LogDebug("Cache hit for query: {Query}", parameters.Query);
            return cachedResult;
        }

        // Execute search
        var searchResults = await ExecuteSearchAsync(parameters);

        // Determine response mode
        var responseMode = DetermineResponseMode(parameters, searchResults, context);

        // Build AI-optimized response
        var result = await _responseBuilder.BuildResponseAsync(
            searchResults,
            new ResponseContext
            {
                OperationName = ToolName,
                Parameters = parameters,
                TokenBudget = context.TokenBudget ?? GetTokenBudget(responseMode),
                ResponseMode = responseMode,
                IncludeInsights = parameters.IncludeInsights,
                IncludeActions = parameters.IncludeActions,
                UserContext = context
            });

        // Cache the result
        await _cacheService.SetAsync(cacheKey, result, new CacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(15),
            Priority = CachePriority.Normal,
            Tags = new[] { "search", parameters.SearchType }
        });

        return result;
    }

    protected override async Task<ToolValidationResult> ValidateParametersAsync(
        TextSearchParams parameters)
    {
        var result = await base.ValidateParametersAsync(parameters);
        
        // Additional custom validation
        if (parameters.SearchType == "regex")
        {
            try
            {
                _ = new Regex(parameters.Query);
            }
            catch (ArgumentException ex)
            {
                result.AddError($"Invalid regex pattern: {ex.Message}");
            }
        }

        // Validate path if provided
        if (!string.IsNullOrEmpty(parameters.Path))
        {
            var resolvedPath = _pathResolver.ResolvePath(parameters.Path);
            if (!Directory.Exists(resolvedPath) && !File.Exists(resolvedPath))
            {
                result.AddWarning($"Path does not exist: {parameters.Path}");
            }
        }

        return result;
    }

    private async Task<List<SearchResult>> ExecuteSearchAsync(TextSearchParams parameters)
    {
        var request = new QueryRequest
        {
            Query = parameters.Query,
            SearchType = parameters.SearchType,
            Path = parameters.Path,
            MaxResults = parameters.MaxResults * 2 // Get extra for reduction
        };

        return await _queryService.ExecuteQueryAsync(request);
    }

    private ResponseMode DetermineResponseMode(
        TextSearchParams parameters,
        List<SearchResult> results,
        ToolExecutionContext context)
    {
        if (parameters.ResponseMode != "auto")
        {
            return Enum.Parse<ResponseMode>(parameters.ResponseMode, true);
        }

        // Auto-determine based on result size
        var estimatedTokens = _tokenEstimator.EstimateCollection(
            results,
            r => EstimateSearchResultTokens(r));

        return estimatedTokens > 5000 ? ResponseMode.Summary : ResponseMode.Full;
    }

    private int EstimateSearchResultTokens(SearchResult result)
    {
        var tokens = 50; // Base structure
        tokens += _tokenEstimator.EstimateString(result.FilePath) / 2; // Paths repeat
        tokens += result.Matches.Count * 30; // Each match with context
        tokens += result.Context?.Lines.Count * 20 ?? 0; // Context lines
        return tokens;
    }

    private int GetTokenBudget(ResponseMode mode)
    {
        return mode == ResponseMode.Summary ? 5000 : 50000;
    }
}
```

### 4. Implement Response Builder
```csharp
public class TextSearchResponseBuilder : BaseResponseBuilder<List<SearchResult>, TextSearchResult>
{
    private readonly IInsightGenerator _insightGenerator;
    private readonly INextActionProvider _actionProvider;
    private readonly ICodeAnalysisService _codeAnalysis;
    private readonly IPathResolutionService _pathResolver;

    public TextSearchResponseBuilder(
        IInsightGenerator insightGenerator,
        INextActionProvider actionProvider,
        ICodeAnalysisService codeAnalysis,
        IPathResolutionService pathResolver,
        ITokenEstimator tokenEstimator,
        ILogger<TextSearchResponseBuilder> logger)
        : base(tokenEstimator, logger)
    {
        _insightGenerator = insightGenerator;
        _actionProvider = actionProvider;
        _codeAnalysis = codeAnalysis;
        _pathResolver = pathResolver;
    }

    protected override async Task<TextSearchResult> BuildCoreAsync(
        List<SearchResult> searchResults,
        ResponseContext context)
    {
        var startTime = DateTime.UtcNow;

        // Apply token management and reduction
        var tokenAware = await ApplyTokenManagementAsync(searchResults, context);

        // Convert to response items
        var resultItems = ConvertToResultItems(tokenAware.Items);

        // Generate distribution analysis
        var distribution = GenerateDistribution(tokenAware.Items);

        // Generate insights
        var insights = await GenerateInsightsAsync(tokenAware, distribution, context);

        // Generate actions
        var actions = await GenerateActionsAsync(tokenAware, context);

        // Build final response
        return new TextSearchResult
        {
            Format = "ai-optimized",
            Data = new TextSearchData
            {
                SearchResults = resultItems,
                TotalMatches = tokenAware.OriginalCount,
                SearchType = context.Parameters.SearchType,
                Distribution = distribution,
                Summary = GenerateSummary(tokenAware, distribution),
                Count = resultItems.Count
            },
            Insights = insights,
            Actions = actions,
            Meta = BuildMetadata(tokenAware, context, startTime)
        };
    }

    protected override async Task<TokenAwareResult<List<SearchResult>>> ApplyTokenManagementAsync(
        List<SearchResult> data,
        ResponseContext context)
    {
        // Use custom reduction strategy for search results
        var reductionEngine = new ProgressiveReductionEngine(Logger);
        reductionEngine.RegisterStrategy(new CodeSearchReductionStrategy());

        var result = reductionEngine.Reduce(
            data,
            r => EstimateSearchResultTokens(r),
            context.TokenBudget,
            "code-search",
            new ReductionContext
            {
                PreservationRules = new[]
                {
                    "Preserve exact matches",
                    "Preserve high-score results",
                    "Maintain file diversity"
                }
            });

        return new TokenAwareResult<List<SearchResult>>
        {
            Items = result.Items,
            OriginalCount = result.OriginalCount,
            EstimatedTokens = result.EstimatedTokens,
            WasTruncated = result.WasReduced,
            ReductionStrategy = result.Metadata["strategy"]?.ToString()
        };
    }

    private List<SearchResultItem> ConvertToResultItems(List<SearchResult> results)
    {
        return results.Select(r => new SearchResultItem
        {
            FilePath = r.FilePath,
            RelativePath = _pathResolver.GetRelativePath(r.FilePath),
            Matches = r.Matches.Select(m => new SearchMatch
            {
                Line = m.Line,
                Column = m.Column,
                Text = m.Text,
                Type = m.IsExact ? "exact" : "partial"
            }).ToList(),
            Score = r.Score,
            Context = r.Context != null ? new SearchContext
            {
                BeforeLines = r.Context.BeforeLines,
                AfterLines = r.Context.AfterLines
            } : null
        }).ToList();
    }

    private SearchDistribution GenerateDistribution(List<SearchResult> results)
    {
        var byFileType = results
            .GroupBy(r => Path.GetExtension(r.FilePath))
            .ToDictionary(g => g.Key, g => g.Count());

        var byDirectory = results
            .GroupBy(r => Path.GetDirectoryName(r.FilePath) ?? "")
            .OrderByDescending(g => g.Count())
            .Take(10)
            .ToDictionary(g => _pathResolver.GetRelativePath(g.Key), g => g.Count());

        var hotspots = results
            .GroupBy(r => r.FilePath)
            .Where(g => g.Count() > 5)
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => new HotspotInfo
            {
                FilePath = _pathResolver.GetRelativePath(g.Key),
                MatchCount = g.Count(),
                Significance = CalculateSignificance(g)
            })
            .ToList();

        return new SearchDistribution
        {
            ByFileType = byFileType,
            ByDirectory = byDirectory,
            Hotspots = hotspots
        };
    }

    private async Task<List<string>> GenerateInsightsAsync(
        TokenAwareResult<List<SearchResult>> tokenAware,
        SearchDistribution distribution,
        ResponseContext context)
    {
        var insightData = new TextSearchInsightData
        {
            Results = tokenAware.Items,
            Distribution = distribution,
            Query = context.Parameters.Query,
            SearchType = context.Parameters.SearchType,
            WasTruncated = tokenAware.WasTruncated
        };

        var insights = await _insightGenerator.GenerateInsightsAsync(
            insightData,
            new InsightContext
            {
                OperationName = "text_search",
                MinInsights = 2,
                MaxInsights = 5,
                UserGoal = DetermineUserGoal(context)
            });

        return insights.Select(i => i.Text).ToList();
    }

    private async Task<List<AIAction>> GenerateActionsAsync(
        TokenAwareResult<List<SearchResult>> tokenAware,
        ResponseContext context)
    {
        var actionContext = new ActionContext
        {
            OperationName = "text_search",
            HasManyResults = tokenAware.Items.Count > 20,
            WasTruncated = tokenAware.WasTruncated,
            Parameters = context.Parameters
        };

        var actions = await _actionProvider.GetNextActionsAsync(
            tokenAware.Items,
            actionContext);

        // Add specific search actions
        if (tokenAware.WasTruncated)
        {
            actions.Insert(0, new AIAction
            {
                Tool = "text_search",
                Description = "Get all search results without truncation",
                Parameters = new Dictionary<string, object>
                {
                    ["query"] = context.Parameters.Query,
                    ["searchType"] = context.Parameters.SearchType,
                    ["path"] = context.Parameters.Path,
                    ["maxResults"] = tokenAware.OriginalCount,
                    ["responseMode"] = "full"
                },
                Priority = 100,
                Category = "expand"
            });
        }

        return actions.Take(5).ToList();
    }

    private string GenerateSummary(
        TokenAwareResult<List<SearchResult>> tokenAware,
        SearchDistribution distribution)
    {
        var parts = new List<string>();

        parts.Add($"Found {tokenAware.OriginalCount} matches");

        if (distribution.Hotspots.Any())
        {
            var topHotspot = distribution.Hotspots.First();
            parts.Add($"with hotspot in {topHotspot.FilePath} ({topHotspot.MatchCount} matches)");
        }

        if (tokenAware.WasTruncated)
        {
            parts.Add($"(showing {tokenAware.Items.Count} most relevant)");
        }

        return string.Join(" ", parts);
    }
}
```

### 5. Implement Custom Reduction Strategy
```csharp
public class CodeSearchReductionStrategy : IReductionStrategy
{
    public string Name => "code-search";

    public ReductionResult<T> Reduce<T>(
        IList<T> items,
        Func<T, int> itemEstimator,
        int tokenLimit,
        ReductionContext? context = null)
    {
        if (items is not IList<SearchResult> searchResults)
        {
            throw new ArgumentException("This strategy only works with SearchResult items");
        }

        var startTime = DateTime.UtcNow;
        var originalCount = searchResults.Count;

        // Group by file to maintain context
        var fileGroups = searchResults
            .GroupBy(r => r.FilePath)
            .OrderByDescending(g => g.Max(r => r.Score))
            .ToList();

        var selectedResults = new List<SearchResult>();
        var currentTokens = 500; // Base overhead

        // First pass: Take at least one result from each file
        foreach (var group in fileGroups)
        {
            var bestResult = group.OrderByDescending(r => r.Score).First();
            var tokens = itemEstimator(bestResult);

            if (currentTokens + tokens <= tokenLimit)
            {
                selectedResults.Add(bestResult);
                currentTokens += tokens;
            }
            else
            {
                break;
            }
        }

        // Second pass: Add more results from high-value files
        foreach (var group in fileGroups)
        {
            var remaining = group.Except(selectedResults).OrderByDescending(r => r.Score);
            
            foreach (var result in remaining)
            {
                var tokens = itemEstimator(result);
                if (currentTokens + tokens <= tokenLimit)
                {
                    selectedResults.Add(result);
                    currentTokens += tokens;
                }
            }
        }

        // Sort final results by score
        selectedResults = selectedResults.OrderByDescending(r => r.Score).ToList();

        return new ReductionResult<T>
        {
            Items = (IList<T>)selectedResults,
            OriginalCount = originalCount,
            EstimatedTokens = currentTokens,
            ReductionPercentage = ((double)(originalCount - selectedResults.Count) / originalCount) * 100,
            Metadata = new Dictionary<string, object>
            {
                ["strategy"] = Name,
                ["duration"] = (DateTime.UtcNow - startTime).TotalMilliseconds,
                ["filesPreserved"] = selectedResults.Select(r => r.FilePath).Distinct().Count(),
                ["exactMatchesPreserved"] = selectedResults.Count(r => r.IsExactMatch)
            }
        };
    }
}
```

### 6. Create Tests for Migrated Tool
```csharp
[TestFixture]
public class TextSearchToolTests : ToolTestBase<TextSearchTool>
{
    private Mock<ILuceneSearchService> _searchServiceMock;
    private Mock<IQueryExecutionService> _queryServiceMock;
    private Mock<ITextSearchResponseBuilder> _responseBuilderMock;

    [SetUp]
    public void SetUp()
    {
        _searchServiceMock = new Mock<ILuceneSearchService>();
        _queryServiceMock = new Mock<IQueryExecutionService>();
        _responseBuilderMock = new Mock<ITextSearchResponseBuilder>();
    }

    [Test]
    public async Task ExecuteAsync_WithValidQuery_ReturnsAIOptimizedResponse()
    {
        // Arrange
        var tool = CreateTool();
        var parameters = new TextSearchParams
        {
            Query = "TODO",
            SearchType = "literal",
            MaxResults = 50
        };

        var searchResults = GenerateSearchResults(100);
        _queryServiceMock
            .Setup(q => q.ExecuteQueryAsync(It.IsAny<QueryRequest>()))
            .ReturnsAsync(searchResults);

        var expectedResponse = new TextSearchResult
        {
            Format = "ai-optimized",
            Data = new TextSearchData { TotalMatches = 100 },
            Insights = new List<string> { "Found TODO comments in 15 files" },
            Actions = new List<AIAction>()
        };

        _responseBuilderMock
            .Setup(b => b.BuildResponseAsync(It.IsAny<List<SearchResult>>(), It.IsAny<ResponseContext>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await tool.ExecuteAsync(parameters);

        // Assert
        result.Should().BeOfType<TextSearchResult>();
        var typedResult = (TextSearchResult)result;
        typedResult.Format.Should().Be("ai-optimized");
        typedResult.Insights.Should().NotBeEmpty();
    }

    [Test]
    public async Task ExecuteAsync_WithTokenLimit_AppliesReduction()
    {
        // Arrange
        var tool = CreateTool();
        var parameters = new TextSearchParams { Query = "test" };
        var context = CreateContext(tokenBudget: 1000);

        var searchResults = GenerateSearchResults(1000); // Large result set
        _queryServiceMock
            .Setup(q => q.ExecuteQueryAsync(It.IsAny<QueryRequest>()))
            .ReturnsAsync(searchResults);

        // Act
        var result = await tool.ExecuteAsync(parameters, context);

        // Assert
        _responseBuilderMock.Verify(
            b => b.BuildResponseAsync(
                It.IsAny<List<SearchResult>>(),
                It.Is<ResponseContext>(c => c.TokenBudget == 1000)),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_WithCachedResult_ReturnsCachedResponse()
    {
        // Arrange
        var tool = CreateTool();
        var parameters = new TextSearchParams { Query = "cached" };
        var cachedResult = new TextSearchResult { Data = new TextSearchData() };

        _cacheServiceMock
            .Setup(c => c.GetAsync<TextSearchResult>(It.IsAny<string>()))
            .ReturnsAsync(cachedResult);

        // Act
        var result = await tool.ExecuteAsync(parameters);

        // Assert
        result.Should().BeSameAs(cachedResult);
        _queryServiceMock.Verify(q => q.ExecuteQueryAsync(It.IsAny<QueryRequest>()), Times.Never);
    }

    [Test]
    public async Task ValidateParametersAsync_WithInvalidRegex_ReturnsError()
    {
        // Arrange
        var tool = CreateTool();
        var parameters = new TextSearchParams
        {
            Query = "[invalid regex",
            SearchType = "regex"
        };

        // Act
        var result = await tool.ValidateParametersAsync(parameters);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Invalid regex pattern"));
    }

    private List<SearchResult> GenerateSearchResults(int count)
    {
        return Enumerable.Range(1, count)
            .Select(i => new SearchResult
            {
                FilePath = $"/path/to/file{i % 20}.cs",
                Score = 1.0 - (i * 0.001),
                Matches = new List<Match>
                {
                    new Match { Line = i, Column = 10, Text = "TODO: test" }
                },
                IsExactMatch = i % 3 == 0
            })
            .ToList();
    }
}
```

## Migration Validation Checklist

### Functional Validation
- [ ] Tool executes successfully with all parameter combinations
- [ ] Response format matches AIOptimizedResponse structure
- [ ] Insights are generated and relevant
- [ ] Actions are contextually appropriate
- [ ] Token limits are respected
- [ ] Cache is working correctly
- [ ] Error handling maintains compatibility

### Performance Validation
- [ ] Response time is equal or better than original
- [ ] Token usage reduced for large result sets
- [ ] Memory usage is acceptable
- [ ] Cache hit rate is measurable

### Integration Validation
- [ ] Tool works with Claude Code
- [ ] Backward compatibility maintained
- [ ] All tests pass
- [ ] No breaking changes for consumers

## Key Learnings

1. **Parameter Classes**: Using dedicated parameter classes with validation attributes provides better type safety and documentation.

2. **Response Builders**: Separating response building logic makes it reusable and testable.

3. **Token Management**: Pre-estimating tokens and applying reduction early prevents overruns.

4. **Caching**: Strategic caching significantly improves performance for repeated queries.

5. **Insights and Actions**: These add significant value for AI agents by providing context and next steps.

6. **Custom Strategies**: Domain-specific reduction strategies preserve the most valuable information.

## Next Steps

1. Apply this pattern to remaining tools
2. Create shared response builders for similar tools
3. Implement cross-tool action suggestions
4. Add telemetry for token usage monitoring
5. Create performance benchmarks

This migration example demonstrates the complete transformation of a tool to leverage the COA MCP Framework's capabilities while maintaining backward compatibility and improving the AI agent experience.