using Microsoft.Extensions.Logging;
using COA.CodeSearch.McpServer.Models;

namespace COA.CodeSearch.McpServer.Services;

public class AIContextService
{
    private readonly FlexibleMemoryService _memoryService;
    private readonly ILogger<AIContextService> _logger;
    
    public AIContextService(
        FlexibleMemoryService memoryService,
        ILogger<AIContextService> logger)
    {
        _memoryService = memoryService;
        _logger = logger;
    }
    
    public async Task<AIWorkingContext> LoadContextAsync(
        string workingDirectory,
        string? sessionId = null)
    {
        _logger.LogInformation("Loading AI context for directory: {Directory}", workingDirectory);
        
        var context = new AIWorkingContext
        {
            SessionId = sessionId ?? Guid.NewGuid().ToString(),
            WorkingDirectory = workingDirectory,
            LoadedAt = DateTime.UtcNow
        };
        
        try
        {
            // 1. Load memories for current directory
            var directoryMemories = await LoadDirectoryMemoriesAsync(workingDirectory);
            
            // 2. Load recent session memories
            var sessionMemories = sessionId != null 
                ? await LoadSessionMemoriesAsync(sessionId)
                : new List<FlexibleMemoryEntry>();
            
            // 3. Load related project memories
            var projectMemories = await LoadProjectMemoriesAsync(workingDirectory);
            
            // 4. Score and rank all memories
            var allMemories = directoryMemories
                .Concat(sessionMemories)
                .Concat(projectMemories)
                .Distinct(new MemoryIdComparer())
                .ToList();
            
            var rankedMemories = RankMemoriesByRelevance(allMemories, workingDirectory);
            
            // 5. Build working context with tiered relevance
            context.PrimaryMemories = rankedMemories.Take(5).ToList();
            context.SecondaryMemories = rankedMemories.Skip(5).Take(10).ToList();
            context.AvailableMemories = rankedMemories.Skip(15).Take(20).ToList();
            
            // 6. Generate contextual suggestions
            context.SuggestedActions = GenerateContextActions(context);
            
            _logger.LogInformation(
                "Loaded context: {Primary} primary, {Secondary} secondary, {Available} available memories",
                context.PrimaryMemories.Count,
                context.SecondaryMemories.Count,
                context.AvailableMemories.Count);
            
            return context;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load AI context for directory: {Directory}", workingDirectory);
            
            // Return minimal context on error
            return new AIWorkingContext
            {
                SessionId = context.SessionId,
                WorkingDirectory = workingDirectory,
                LoadedAt = DateTime.UtcNow,
                PrimaryMemories = new List<FlexibleMemoryEntry>(),
                SecondaryMemories = new List<FlexibleMemoryEntry>(),
                AvailableMemories = new List<FlexibleMemoryEntry>(),
                SuggestedActions = new List<string> { "Context loading failed - try manual memory search" }
            };
        }
    }
    
    private async Task<List<FlexibleMemoryEntry>> LoadDirectoryMemoriesAsync(string directory)
    {
        var memories = new List<FlexibleMemoryEntry>();
        
        try
        {
            if (!Directory.Exists(directory))
            {
                _logger.LogWarning("Directory does not exist: {Directory}", directory);
                return memories;
            }
            
            // Get files in directory (limit for performance)
            var files = Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories)
                .Where(f => !f.Contains("\\.codesearch\\") && !f.Contains("\\node_modules\\"))
                .Take(50)
                .ToHashSet(); // Use HashSet for faster lookups
            
            // Search ALL memories ONCE instead of per-file
            var searchRequest = new FlexibleMemorySearchRequest
            {
                Query = "*",
                MaxResults = 200 // Get more results to find file matches
            };
            
            var result = await _memoryService.SearchMemoriesAsync(searchRequest);
            
            // Filter memories that reference any of our directory files
            var fileMemories = result.Memories
                .Where(m => m.FilesInvolved?.Any(f => files.Contains(f)) == true)
                .ToList();
            
            memories.AddRange(fileMemories);
            
            _logger.LogDebug("Found {Count} directory memories for {FileCount} files (1 search instead of {FileCount})", 
                memories.Count, files.Count, files.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading directory memories from: {Directory}", directory);
        }
        
        return memories;
    }
    
    private async Task<List<FlexibleMemoryEntry>> LoadSessionMemoriesAsync(string sessionId)
    {
        var memories = new List<FlexibleMemoryEntry>();
        
        try
        {
            var searchRequest = new FlexibleMemorySearchRequest
            {
                Query = "*",
                Facets = new Dictionary<string, string> { ["sessionId"] = sessionId },
                MaxResults = 20
            };
            
            var searchResult = await _memoryService.SearchMemoriesAsync(searchRequest);
            
            memories.AddRange(searchResult.Memories);
            
            _logger.LogDebug("Found {Count} session memories for session: {SessionId}", 
                memories.Count, sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading session memories for: {SessionId}", sessionId);
        }
        
        return memories;
    }
    
    private async Task<List<FlexibleMemoryEntry>> LoadProjectMemoriesAsync(string workingDirectory)
    {
        var memories = new List<FlexibleMemoryEntry>();
        
        try
        {
            // Load architectural decisions and project insights
            var searchRequest = new FlexibleMemorySearchRequest
            {
                Query = "*",
                Types = new[] { "ArchitecturalDecision", "ProjectInsight", "CodePattern" },
                MaxResults = 30
            };
            
            var projectSearchResult = await _memoryService.SearchMemoriesAsync(searchRequest);
            
            memories.AddRange(projectSearchResult.Memories);
            
            _logger.LogDebug("Found {Count} project memories", memories.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading project memories for: {Directory}", workingDirectory);
        }
        
        return memories;
    }
    
    private List<FlexibleMemoryEntry> RankMemoriesByRelevance(
        List<FlexibleMemoryEntry> memories,
        string workingDirectory)
    {
        return memories
            .Select(m => new
            {
                Memory = m,
                Score = CalculateRelevanceScore(m, workingDirectory)
            })
            .OrderByDescending(x => x.Score)
            .Select(x => x.Memory)
            .ToList();
    }
    
    private float CalculateRelevanceScore(FlexibleMemoryEntry memory, string workingDirectory)
    {
        float score = 0;
        
        // Recency boost (decays over time)
        var age = DateTime.UtcNow - memory.Created;
        score += (float)(1.0 / (1.0 + age.TotalDays / 7.0));
        
        // File relevance boost
        if (memory.FilesInvolved?.Any(f => f.StartsWith(workingDirectory, StringComparison.OrdinalIgnoreCase)) == true)
        {
            score += 2.0f;
        }
        
        // Type priority
        score += memory.Type switch
        {
            "ArchitecturalDecision" => 1.5f,
            "TechnicalDebt" => 1.2f,
            "CodePattern" => 1.3f,
            "ProjectInsight" => 1.4f,
            "WorkSession" => 0.8f,
            "WorkingMemory" => 0.6f,
            _ => 1.0f
        };
        
        // Access frequency boost
        var accessCount = memory.GetField<int>("accessCount");
        if (accessCount > 0)
        {
            score += Math.Min(accessCount * 0.1f, 1.0f); // Cap at 1.0 bonus
        }
        
        // Status priority
        var status = memory.GetField<string>("status");
        score += status switch
        {
            "urgent" => 0.5f,
            "pending" => 0.3f,
            "in-progress" => 0.4f,
            "resolved" => -0.2f, // Slightly lower priority for resolved items
            _ => 0f
        };
        
        return score;
    }
    
    private List<string> GenerateContextActions(AIWorkingContext context)
    {
        var actions = new List<string>();
        
        if (context.PrimaryMemories.Any())
        {
            actions.Add($"Review {context.PrimaryMemories.Count} primary memories for current work");
        }
        
        var technicalDebt = context.PrimaryMemories
            .Concat(context.SecondaryMemories)
            .Where(m => m.Type == "TechnicalDebt" && m.GetField<string>("status") != "resolved")
            .ToList();
        
        if (technicalDebt.Any())
        {
            actions.Add($"Address {technicalDebt.Count} unresolved technical debt items");
        }
        
        var decisions = context.PrimaryMemories
            .Where(m => m.Type == "ArchitecturalDecision")
            .ToList();
        
        if (decisions.Any())
        {
            actions.Add($"Review {decisions.Count} architectural decisions for context");
        }
        
        if (context.PrimaryMemories.Count == 0)
        {
            actions.Add("No relevant memories found - consider storing current work context");
        }
        
        return actions;
    }
}

public class MemoryIdComparer : IEqualityComparer<FlexibleMemoryEntry>
{
    public bool Equals(FlexibleMemoryEntry? x, FlexibleMemoryEntry? y)
    {
        return x?.Id == y?.Id;
    }
    
    public int GetHashCode(FlexibleMemoryEntry obj)
    {
        return obj.Id.GetHashCode();
    }
}