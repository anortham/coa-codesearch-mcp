using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Tool to load AI working context with relevant memories for the current work environment
/// </summary>
public class LoadContextTool : ITool
{
    public string ToolName => "load_context";
    public string Description => "Load AI working context with relevant memories for directory or session";
    public ToolCategory Category => ToolCategory.Memory;
    
    private readonly ILogger<LoadContextTool> _logger;
    private readonly AIContextService _contextService;
    private readonly IMemoryCache _cache;
    
    public LoadContextTool(
        ILogger<LoadContextTool> logger,
        AIContextService contextService,
        IMemoryCache cache)
    {
        _logger = logger;
        _contextService = contextService;
        _cache = cache;
    }
    
    public async Task<object> ExecuteAsync(
        string? workingDirectory = null,
        string? sessionId = null,
        bool refreshCache = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Use current directory if not specified
            workingDirectory ??= Directory.GetCurrentDirectory();
            
            _logger.LogInformation("Loading context for directory: {Directory}, Session: {SessionId}", 
                workingDirectory, sessionId ?? "new");
            
            // Check cache first (unless refresh requested)
            var cacheKey = $"context_{workingDirectory}_{sessionId ?? "default"}";
            if (!refreshCache && _cache.TryGetValue<AIWorkingContext>(cacheKey, out var cached) && cached != null)
            {
                _logger.LogDebug("Returning cached context for {CacheKey}", cacheKey);
                return new
                {
                    success = true,
                    context = new
                    {
                        sessionId = cached.SessionId ?? "default",
                        workingDirectory = cached.WorkingDirectory,
                        loadedAt = cached.LoadedAt,
                        fromCache = true,
                        primaryMemories = cached.PrimaryMemories.Select(FormatMemoryForResponse).ToArray(),
                        secondaryMemories = cached.SecondaryMemories.Select(FormatMemoryForResponse).ToArray(),
                        availableMemories = cached.AvailableMemories.Select(FormatMemoryForResponse).ToArray(),
                        suggestedActions = cached.SuggestedActions.ToArray(),
                        summary = new
                        {
                            totalMemories = cached.PrimaryMemories.Count + cached.SecondaryMemories.Count + cached.AvailableMemories.Count,
                            primaryCount = cached.PrimaryMemories.Count,
                            secondaryCount = cached.SecondaryMemories.Count,
                            availableCount = cached.AvailableMemories.Count
                        }
                    }
                };
            }
            
            // Load fresh context
            var context = await _contextService.LoadContextAsync(workingDirectory, sessionId);
            
            // Cache for 5 minutes
            var cacheOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
                SlidingExpiration = TimeSpan.FromMinutes(2),
                Priority = CacheItemPriority.Normal
            };
            _cache.Set(cacheKey, context, cacheOptions);
            
            _logger.LogInformation("Context loaded successfully: {Primary} primary, {Secondary} secondary, {Available} available memories",
                context.PrimaryMemories.Count, context.SecondaryMemories.Count, context.AvailableMemories.Count);
            
            return new
            {
                success = true,
                context = new
                {
                    sessionId = context.SessionId ?? "default",
                    workingDirectory = context.WorkingDirectory,
                    loadedAt = context.LoadedAt,
                    fromCache = false,
                    primaryMemories = context.PrimaryMemories.Select(FormatMemoryForResponse).ToArray(),
                    secondaryMemories = context.SecondaryMemories.Select(FormatMemoryForResponse).ToArray(),
                    availableMemories = context.AvailableMemories.Select(FormatMemoryForResponse).ToArray(),
                    suggestedActions = context.SuggestedActions.ToArray(),
                    summary = new
                    {
                        totalMemories = context.PrimaryMemories.Count + context.SecondaryMemories.Count + context.AvailableMemories.Count,
                        primaryCount = context.PrimaryMemories.Count,
                        secondaryCount = context.SecondaryMemories.Count,
                        availableCount = context.AvailableMemories.Count
                    }
                },
                insights = GenerateInsights(context),
                actions = GenerateActions(context)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load context for directory: {Directory}", workingDirectory);
            return new
            {
                success = false,
                error = ex.Message,
                context = new
                {
                    sessionId = sessionId ?? Guid.NewGuid().ToString(),
                    workingDirectory = workingDirectory ?? Directory.GetCurrentDirectory(),
                    loadedAt = DateTime.UtcNow,
                    fromCache = false,
                    primaryMemories = Array.Empty<object>(),
                    secondaryMemories = Array.Empty<object>(),
                    availableMemories = Array.Empty<object>(),
                    suggestedActions = new[] { "Context loading failed - try manual memory search" },
                    summary = new
                    {
                        totalMemories = 0,
                        primaryCount = 0,
                        secondaryCount = 0,
                        availableCount = 0
                    }
                }
            };
        }
    }
    
    private static object FormatMemoryForResponse(FlexibleMemoryEntry memory)
    {
        return new
        {
            id = memory.Id,
            type = memory.Type,
            content = TruncateContent(memory.Content, 200),
            created = memory.Created.ToString("yyyy-MM-dd"),
            files = memory.FilesInvolved?.Take(3).ToArray() ?? Array.Empty<string>(),
            status = memory.GetField<string>("status"),
            priority = memory.GetField<string>("priority")
        };
    }
    
    private static string TruncateContent(string content, int maxLength)
    {
        if (string.IsNullOrEmpty(content) || content.Length <= maxLength)
            return content;
        
        return content.Substring(0, maxLength - 3) + "...";
    }
    
    private static string[] GenerateInsights(AIWorkingContext context)
    {
        var insights = new List<string>();
        
        if (context.PrimaryMemories.Any())
        {
            var typeGroups = context.PrimaryMemories.GroupBy(m => m.Type);
            var topType = typeGroups.OrderByDescending(g => g.Count()).First();
            insights.Add($"Primary focus: {topType.Count()} {topType.Key} memories");
        }
        
        var recentMemories = context.PrimaryMemories
            .Concat(context.SecondaryMemories)
            .Where(m => m.Created > DateTime.UtcNow.AddDays(-7))
            .Count();
        
        if (recentMemories > 0)
        {
            insights.Add($"{recentMemories} recent memories - active topic area");
        }
        
        var filesWithMostMemories = context.PrimaryMemories
            .Concat(context.SecondaryMemories)
            .SelectMany(m => m.FilesInvolved ?? Array.Empty<string>())
            .GroupBy(f => f)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();
        
        if (filesWithMostMemories != null)
        {
            var fileName = Path.GetFileName(filesWithMostMemories.Key);
            insights.Add($"Most referenced: {fileName} ({filesWithMostMemories.Count()} memories)");
        }
        
        return insights.ToArray();
    }
    
    private static object[] GenerateActions(AIWorkingContext context)
    {
        var actions = new List<object>();
        
        if (context.PrimaryMemories.Any())
        {
            actions.Add(new
            {
                id = "review_primary",
                description = $"Review {context.PrimaryMemories.Count} primary memories for current work",
                command = "mcp__codesearch__search_memories",
                parameters = new { query = "*", types = context.PrimaryMemories.Select(m => m.Type).Distinct().ToArray() },
                priority = "high",
                estimatedTokens = 200
            });
        }
        
        var technicalDebt = context.PrimaryMemories
            .Concat(context.SecondaryMemories)
            .Where(m => m.Type == "TechnicalDebt" && m.GetField<string>("status") != "resolved")
            .ToArray();
        
        if (technicalDebt.Any())
        {
            actions.Add(new
            {
                id = "address_debt",
                description = $"Address {technicalDebt.Length} unresolved technical debt items",
                command = "mcp__codesearch__search_memories",
                parameters = new { query = "*", types = new[] { "TechnicalDebt" }, facets = new { status = "pending" } },
                priority = "medium",
                estimatedTokens = 150
            });
        }
        
        if (context.AvailableMemories.Any())
        {
            actions.Add(new
            {
                id = "explore_available",
                description = $"Explore {context.AvailableMemories.Count} additional available memories",
                command = "mcp__codesearch__memory_graph_navigator",
                parameters = new { startPoint = context.WorkingDirectory },
                priority = "low",
                estimatedTokens = 300
            });
        }
        
        return actions.ToArray();
    }
}