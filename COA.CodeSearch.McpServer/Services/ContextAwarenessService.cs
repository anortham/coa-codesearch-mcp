using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Service for tracking and managing context awareness for intelligent memory search
/// </summary>
public class ContextAwarenessService : IContextAwarenessService
{
    private readonly ILogger<ContextAwarenessService> _logger;
    private readonly ContextBoostOptions _options;
    
    // Thread-safe collections for tracking context
    private readonly ConcurrentQueue<string> _recentFiles = new();
    private readonly ConcurrentQueue<SearchHistoryItem> _recentQueries = new();
    private volatile string? _currentFile;
    private volatile ProjectContext? _projectContext;
    
    // Technology detection patterns
    private static readonly Dictionary<string, string[]> TechnologyPatterns = new()
    {
        ["aspnet"] = ["Controllers", "Models", "Views", "Startup.cs", "Program.cs", ".csproj"],
        ["blazor"] = [".razor", "BlazorServer", "BlazorWebAssembly"],
        ["ef"] = ["Entity", "DbContext", "Migrations", "Repository"],
        ["api"] = ["Controllers", "Swagger", "OpenAPI", "WebApi"],
        ["web"] = ["wwwroot", "css", "js", "Views", "Controllers"],
        ["console"] = ["Program.cs", "Main(", "Console."],
        ["test"] = ["Tests", "Test.cs", "Xunit", "NUnit", "MSTest"],
        ["desktop"] = ["WPF", "WinForms", "MAUI", "Avalonia"],
        ["mobile"] = ["MAUI", "Xamarin", "Android", "iOS"]
    };
    
    // File type patterns for context extraction
    private static readonly Regex FileContextRegex = new(
        @"(?i)(?:(?<type>service|controller|model|entity|repository|manager|provider|factory|helper|util|config|setting)|(?<domain>auth|user|admin|payment|order|product|customer|inventory|billing|security|login|profile))",
        RegexOptions.Compiled);
    
    public ContextAwarenessService(ILogger<ContextAwarenessService> logger, IOptions<ContextBoostOptions>? options = null)
    {
        _logger = logger;
        _options = options?.Value ?? new ContextBoostOptions();
    }
    
    public async Task<SearchContext> GetCurrentContextAsync()
    {
        var context = new SearchContext
        {
            CurrentFile = _currentFile,
            RecentFiles = GetRecentFiles(),
            RecentQueries = GetRecentQueries(),
            ProjectInfo = _projectContext ?? await DetectProjectContextAsync(),
            Timestamp = DateTime.UtcNow
        };
        
        // Extract context keywords from current context
        context.ContextKeywords = ExtractContextKeywords(context);
        
        // Get active working memory topics (this would integrate with working memory)
        context.ActiveWorkingMemoryTopics = await GetActiveWorkingMemoryTopicsAsync();
        
        _logger.LogDebug("Generated search context with {FileCount} recent files, {QueryCount} recent queries, {KeywordCount} keywords",
            context.RecentFiles.Length, context.RecentQueries.Length, context.ContextKeywords.Length);
        
        return context;
    }
    
    public Task UpdateCurrentFileAsync(string? filePath)
    {
        _currentFile = filePath;
        
        if (!string.IsNullOrEmpty(filePath))
        {
            // Also track it as a recent file access
            return TrackFileAccessAsync(filePath);
        }
        
        return Task.CompletedTask;
    }
    
    public Task TrackFileAccessAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return Task.CompletedTask;
        
        // Add to recent files (thread-safe)
        _recentFiles.Enqueue(filePath);
        
        // Trim to max size
        while (_recentFiles.Count > _options.MaxRecentFiles)
        {
            _recentFiles.TryDequeue(out _);
        }
        
        _logger.LogTrace("Tracked file access: {FilePath}", filePath);
        return Task.CompletedTask;
    }
    
    public Task TrackSearchQueryAsync(string query, int resultsFound)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Task.CompletedTask;
        
        var historyItem = new SearchHistoryItem
        {
            Query = query,
            ResultsFound = resultsFound,
            Timestamp = DateTime.UtcNow
        };
        
        _recentQueries.Enqueue(historyItem);
        
        // Trim to max size
        while (_recentQueries.Count > _options.MaxRecentQueries)
        {
            _recentQueries.TryDequeue(out _);
        }
        
        _logger.LogTrace("Tracked search query: '{Query}' ({Results} results)", query, resultsFound);
        return Task.CompletedTask;
    }
    
    public string[] ExtractFileContextKeywords(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return Array.Empty<string>();
        
        var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        // Extract from file name and path
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var pathParts = filePath.Split('/', '\\', StringSplitOptions.RemoveEmptyEntries);
        
        // Add relevant path segments
        foreach (var part in pathParts)
        {
            if (part.Length >= _options.MinKeywordLength && !IsCommonPathSegment(part))
            {
                // Remove file extension before processing
                var cleanPart = Path.GetFileNameWithoutExtension(part);
                if (string.IsNullOrEmpty(cleanPart)) cleanPart = part; // Fallback if not a file
                
                if (cleanPart.Length >= _options.MinKeywordLength)
                {
                    keywords.Add(cleanPart.ToLowerInvariant());
                    
                    // Extract camelCase/PascalCase parts
                    var subParts = SplitCamelCase(cleanPart);
                    foreach (var subPart in subParts.Where(p => p.Length >= _options.MinKeywordLength))
                    {
                        keywords.Add(subPart.ToLowerInvariant());
                    }
                }
            }
        }
        
        // Use regex to extract semantic keywords
        var matches = FileContextRegex.Matches(filePath);
        foreach (Match match in matches)
        {
            if (match.Groups["type"].Success)
            {
                keywords.Add(match.Groups["type"].Value.ToLowerInvariant());
            }
            if (match.Groups["domain"].Success)
            {
                keywords.Add(match.Groups["domain"].Value.ToLowerInvariant());
            }
        }
        
        return keywords.ToArray();
    }
    
    public Dictionary<string, float> GetContextBoosts(SearchContext context, string[] searchTerms)
    {
        var boosts = new Dictionary<string, float>();
        
        foreach (var term in searchTerms)
        {
            var boost = 1.0f; // Base weight
            var normalizedTerm = term.ToLowerInvariant();
            
            // Current file context boost
            if (!string.IsNullOrEmpty(context.CurrentFile))
            {
                var fileKeywords = ExtractFileContextKeywords(context.CurrentFile);
                if (fileKeywords.Contains(normalizedTerm, StringComparer.OrdinalIgnoreCase))
                {
                    boost *= _options.CurrentFileBoost;
                }
            }
            
            // Recent files context boost
            foreach (var recentFile in context.RecentFiles.Take(5)) // Top 5 recent files
            {
                var fileKeywords = ExtractFileContextKeywords(recentFile);
                if (fileKeywords.Contains(normalizedTerm, StringComparer.OrdinalIgnoreCase))
                {
                    boost *= _options.RecentFilesBoost;
                    break; // Only apply once
                }
            }
            
            // Recent query patterns boost
            var recentQueryTerms = context.RecentQueries
                .SelectMany(q => q.Query.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .Select(q => q.ToLowerInvariant())
                .ToHashSet();
            
            if (recentQueryTerms.Contains(normalizedTerm))
            {
                boost *= _options.RecentQueryBoost;
            }
            
            // Technology/project context boost
            if (context.ProjectInfo.Technologies.Contains(normalizedTerm, StringComparer.OrdinalIgnoreCase) ||
                context.ProjectInfo.CommonPatterns.Contains(normalizedTerm, StringComparer.OrdinalIgnoreCase))
            {
                boost *= _options.TechnologyBoost;
            }
            
            // Context keywords boost
            if (context.ContextKeywords.Contains(normalizedTerm, StringComparer.OrdinalIgnoreCase))
            {
                boost *= _options.CurrentFileBoost; // Reuse current file boost
            }
            
            boosts[term] = boost;
        }
        
        _logger.LogDebug("Applied context boosts to {TermCount} terms, max boost: {MaxBoost:F2}",
            boosts.Count, boosts.Values.DefaultIfEmpty(1.0f).Max());
        
        return boosts;
    }
    
    private string[] GetRecentFiles()
    {
        return _recentFiles.ToArray();
    }
    
    private SearchHistoryItem[] GetRecentQueries()
    {
        return _recentQueries.ToArray();
    }
    
    private string[] ExtractContextKeywords(SearchContext context)
    {
        var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        // From current file
        if (!string.IsNullOrEmpty(context.CurrentFile))
        {
            keywords.UnionWith(ExtractFileContextKeywords(context.CurrentFile));
        }
        
        // From recent files (weighted by recency)
        foreach (var recentFile in context.RecentFiles.Take(3)) // Top 3 recent files only
        {
            keywords.UnionWith(ExtractFileContextKeywords(recentFile));
        }
        
        // From project technologies
        keywords.UnionWith(context.ProjectInfo.Technologies.Select(t => t.ToLowerInvariant()));
        keywords.UnionWith(context.ProjectInfo.CommonPatterns.Select(p => p.ToLowerInvariant()));
        
        return keywords.Where(k => k.Length >= _options.MinKeywordLength).ToArray();
    }
    
    private Task<ProjectContext> DetectProjectContextAsync()
    {
        if (_projectContext != null)
            return Task.FromResult(_projectContext);
        
        var context = new ProjectContext();
        
        // Simple project type detection based on current file patterns
        var allFiles = _recentFiles.ToArray();
        
        // Detect technologies
        var technologies = new HashSet<string>();
        foreach (var (tech, patterns) in TechnologyPatterns)
        {
            if (patterns.Any(pattern => allFiles.Any(file => file.Contains(pattern, StringComparison.OrdinalIgnoreCase))))
            {
                technologies.Add(tech);
            }
        }
        
        context.Technologies = technologies.ToArray();
        
        // Detect languages
        var languages = new HashSet<string>();
        foreach (var file in allFiles)
        {
            var extension = Path.GetExtension(file).ToLowerInvariant();
            switch (extension)
            {
                case ".cs": languages.Add("csharp"); break;
                case ".ts": case ".tsx": languages.Add("typescript"); break;
                case ".js": case ".jsx": languages.Add("javascript"); break;
                case ".py": languages.Add("python"); break;
                case ".java": languages.Add("java"); break;
                case ".cpp": case ".cc": case ".cxx": languages.Add("cpp"); break;
            }
        }
        
        context.Languages = languages.ToArray();
        
        // Determine project type
        if (technologies.Contains("aspnet") || technologies.Contains("api"))
            context.ProjectType = "web-api";
        else if (technologies.Contains("blazor") || technologies.Contains("web"))
            context.ProjectType = "web-app";
        else if (technologies.Contains("console"))
            context.ProjectType = "console";
        else if (technologies.Contains("test"))
            context.ProjectType = "test";
        else if (technologies.Contains("desktop"))
            context.ProjectType = "desktop";
        else if (technologies.Contains("mobile"))
            context.ProjectType = "mobile";
        
        _projectContext = context;
        
        _logger.LogDebug("Detected project context: Type={ProjectType}, Technologies=[{Technologies}], Languages=[{Languages}]",
            context.ProjectType, string.Join(", ", context.Technologies), string.Join(", ", context.Languages));
        
        return Task.FromResult(context);
    }
    
    private Task<string[]> GetActiveWorkingMemoryTopicsAsync()
    {
        // This would integrate with the working memory system to get current active topics
        // For now, return empty array - this can be enhanced later
        return Task.FromResult(Array.Empty<string>());
    }
    
    private static bool IsCommonPathSegment(string segment)
    {
        var commonSegments = new[] { "src", "bin", "obj", "debug", "release", "net9.0", "netcoreapp", "framework" };
        return commonSegments.Contains(segment, StringComparer.OrdinalIgnoreCase);
    }
    
    private static string[] SplitCamelCase(string input)
    {
        if (string.IsNullOrEmpty(input))
            return Array.Empty<string>();
        
        // Split on capital letters
        var regex = new Regex(@"(?<!^)(?=[A-Z])", RegexOptions.Compiled);
        return regex.Split(input)
            .Where(s => !string.IsNullOrEmpty(s))
            .ToArray();
    }
}