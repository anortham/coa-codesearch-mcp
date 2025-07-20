using COA.CodeSearch.McpServer.Services;
using Lucene.Net.Analysis;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Util;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;

namespace COA.CodeSearch.McpServer.Tools;

public class FastTextSearchTool
{
    private readonly ILogger<FastTextSearchTool> _logger;
    private readonly IConfiguration _configuration;
    private readonly ILuceneIndexService _luceneIndexService;
    private readonly FileIndexingService _fileIndexingService;

    public FastTextSearchTool(
        ILogger<FastTextSearchTool> logger,
        IConfiguration configuration,
        ILuceneIndexService luceneIndexService,
        FileIndexingService fileIndexingService)
    {
        _logger = logger;
        _configuration = configuration;
        _luceneIndexService = luceneIndexService;
        _fileIndexingService = fileIndexingService;
    }

    public async Task<object> ExecuteAsync(
        string query,
        string workspacePath,
        string? filePattern = null,
        string[]? extensions = null,
        int? contextLines = null,
        int maxResults = 50,
        bool caseSensitive = false,
        string searchType = "standard",
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("FastTextSearch request for query: {Query} in {WorkspacePath}", query, workspacePath);

            // Validate input
            if (string.IsNullOrWhiteSpace(query))
            {
                return new
                {
                    success = false,
                    error = "Search query cannot be empty"
                };
            }

            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                return new
                {
                    success = false,
                    error = "Workspace path cannot be empty"
                };
            }
            
            // Handle file paths by converting to directory paths
            string effectiveWorkspacePath = workspacePath;
            if (File.Exists(workspacePath))
            {
                _logger.LogWarning("File path provided as workspace: {FilePath}. Will use parent directory or project root for indexing.", workspacePath);
                
                // Get the parent directory or project root
                var directory = Path.GetDirectoryName(workspacePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    // Try to find project root (containing .csproj, .sln, etc.)
                    var projectRoot = FindProjectRoot(directory);
                    effectiveWorkspacePath = projectRoot ?? directory;
                    _logger.LogInformation("Resolved file path to workspace: {EffectiveWorkspacePath}", effectiveWorkspacePath);
                }
            }

            // Ensure the directory is indexed first
            if (!await EnsureIndexedAsync(effectiveWorkspacePath, cancellationToken))
            {
                return new
                {
                    success = false,
                    error = "Failed to index workspace"
                };
            }

            // Get the searcher - use effective workspace path
            var searcher = await _luceneIndexService.GetIndexSearcherAsync(effectiveWorkspacePath, cancellationToken);
            var analyzer = await _luceneIndexService.GetAnalyzerAsync(effectiveWorkspacePath, cancellationToken);

            // Build the query
            var luceneQuery = BuildQuery(query, searchType, caseSensitive, filePattern, extensions, analyzer);

            // Execute search with parallel result processing
            var topDocs = searcher.Search(luceneQuery, maxResults);
            var results = new ConcurrentBag<SearchResult>();
            
            // Process results in parallel for maximum speed
            var parallelOptions = new ParallelOptions 
            { 
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = Environment.ProcessorCount 
            };
            
            await Parallel.ForEachAsync(topDocs.ScoreDocs, parallelOptions, async (scoreDoc, ct) =>
            {
                var doc = searcher.Doc(scoreDoc.Doc);
                var filePath = doc.Get("path");
                
                if (string.IsNullOrEmpty(filePath))
                    return;

                var result = new SearchResult
                {
                    FilePath = filePath,
                    FileName = doc.Get("filename") ?? Path.GetFileName(filePath),
                    RelativePath = doc.Get("relativePath") ?? filePath,
                    Extension = doc.Get("extension") ?? Path.GetExtension(filePath),
                    Score = scoreDoc.Score,
                    Language = doc.Get("language") ?? ""
                };

                // Add context if requested - use optimized context fetching
                if (contextLines.HasValue && contextLines.Value > 0)
                {
                    result.Context = await GetFileContextOptimizedAsync(filePath, query, contextLines.Value, ct);
                }

                results.Add(result);
            });

            return new
            {
                success = true,
                query = query,
                workspacePath = workspacePath,
                totalResults = topDocs.TotalHits,
                results = results.OrderByDescending(r => r.Score).Take(maxResults).ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing fast text search");
            return new
            {
                success = false,
                error = $"Search failed: {ex.Message}"
            };
        }
    }

    private Query BuildQuery(string queryText, string searchType, bool caseSensitive, string? filePattern, string[]? extensions, Analyzer analyzer)
    {
        var booleanQuery = new BooleanQuery();

        // Main content query
        Query contentQuery;
        switch (searchType.ToLowerInvariant())
        {
            case "wildcard":
                contentQuery = new WildcardQuery(new Term("content", queryText.ToLowerInvariant()));
                break;
            
            case "fuzzy":
                contentQuery = new FuzzyQuery(new Term("content", queryText.ToLowerInvariant()));
                break;
            
            case "phrase":
                var parser = new QueryParser(LuceneVersion.LUCENE_48, "content", analyzer);
                contentQuery = parser.Parse($"\"{EscapeQueryText(queryText)}\"");
                break;
            
            default: // standard
                var standardParser = new QueryParser(LuceneVersion.LUCENE_48, "content", analyzer);
                standardParser.DefaultOperator = Operator.AND;
                // Escape special characters for standard queries to handle things like [HttpGet], GetMethod(), etc.
                var escapedQuery = EscapeQueryText(queryText);
                contentQuery = standardParser.Parse(escapedQuery);
                break;
        }

        booleanQuery.Add(contentQuery, Occur.MUST);

        // File pattern filter
        if (!string.IsNullOrWhiteSpace(filePattern))
        {
            var pathQuery = new WildcardQuery(new Term("relativePath", $"*{filePattern}*"));
            booleanQuery.Add(pathQuery, Occur.MUST);
        }

        // Extension filters
        if (extensions?.Length > 0)
        {
            var extensionQuery = new BooleanQuery();
            foreach (var ext in extensions)
            {
                var normalizedExt = ext.StartsWith(".") ? ext : $".{ext}";
                extensionQuery.Add(new TermQuery(new Term("extension", normalizedExt)), Occur.SHOULD);
            }
            booleanQuery.Add(extensionQuery, Occur.MUST);
        }

        return booleanQuery;
    }

    private async Task<bool> EnsureIndexedAsync(string workspacePath, CancellationToken cancellationToken)
    {
        try
        {
            // Normalize the workspace path to avoid individual file indexing
            string effectiveWorkspacePath = workspacePath;
            if (File.Exists(workspacePath))
            {
                var directory = Path.GetDirectoryName(workspacePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    var projectRoot = FindProjectRoot(directory);
                    effectiveWorkspacePath = projectRoot ?? directory;
                    _logger.LogDebug("EnsureIndexedAsync: Resolved file path to workspace: {EffectiveWorkspacePath}", effectiveWorkspacePath);
                }
            }
            
            // Check if index exists and is recent
            var searcher = await _luceneIndexService.GetIndexSearcherAsync(effectiveWorkspacePath, cancellationToken);
            var indexReader = searcher.IndexReader;
            
            // If index is empty or very small, reindex
            if (indexReader.NumDocs < 10)
            {
                _logger.LogInformation("Index is empty or small, performing initial indexing for {WorkspacePath}", effectiveWorkspacePath);
                await _fileIndexingService.IndexDirectoryAsync(effectiveWorkspacePath, effectiveWorkspacePath, cancellationToken);
            }
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure index for {WorkspacePath}", workspacePath);
            
            // Try to create a new index
            try
            {
                // Normalize here too
                string effectiveWorkspacePath = workspacePath;
                if (File.Exists(workspacePath))
                {
                    var directory = Path.GetDirectoryName(workspacePath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        var projectRoot = FindProjectRoot(directory);
                        effectiveWorkspacePath = projectRoot ?? directory;
                    }
                }
                
                await _fileIndexingService.IndexDirectoryAsync(effectiveWorkspacePath, effectiveWorkspacePath, cancellationToken);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private async Task<List<ContextLine>> GetFileContextOptimizedAsync(string filePath, string query, int contextLines, CancellationToken cancellationToken)
    {
        var contextResults = new List<ContextLine>();
        
        try
        {
            // Use memory-efficient line reading with channels
            var querySpan = query.AsSpan();
            var queryLower = query.ToLowerInvariant();
            
            await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var reader = new StreamReader(fileStream, Encoding.UTF8);
            
            var lineNumber = 0;
            var buffer = new List<(int LineNumber, string Content)>(contextLines * 2 + 1);
            string? line;
            
            while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
            {
                lineNumber++;
                
                // Keep a sliding window of lines
                buffer.Add((lineNumber, line));
                if (buffer.Count > contextLines * 2 + 1)
                    buffer.RemoveAt(0);
                
                // Check for match using Span<char> for performance
                if (line.Contains(queryLower, StringComparison.OrdinalIgnoreCase))
                {
                    // Found a match, add context from buffer
                    var matchIndex = buffer.Count - 1;
                    var startIndex = Math.Max(0, matchIndex - contextLines);
                    var endIndex = Math.Min(buffer.Count - 1, matchIndex + contextLines);
                    
                    // Read ahead for context after match
                    var linesAfter = endIndex - matchIndex;
                    for (int i = 0; i < contextLines - linesAfter && (line = await reader.ReadLineAsync(cancellationToken)) != null; i++)
                    {
                        lineNumber++;
                        buffer.Add((lineNumber, line));
                    }
                    
                    // Add context lines
                    for (int i = startIndex; i < buffer.Count && i <= matchIndex + contextLines; i++)
                    {
                        var (num, content) = buffer[i];
                        contextResults.Add(new ContextLine
                        {
                            LineNumber = num,
                            Content = content,
                            IsMatch = i == matchIndex
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get context for file {FilePath}", filePath);
        }
        
        return contextResults;
    }

    /// <summary>
    /// Escapes special characters in query text to prevent Lucene parsing errors
    /// </summary>
    private static string EscapeQueryText(string query)
    {
        // Lucene special characters that need escaping: + - = && || ! ( ) { } [ ] ^ " ~ * ? : \ / < >
        var specialChars = new[] { '+', '-', '=', '&', '|', '!', '(', ')', '{', '}', '[', ']', '^', '"', '~', '*', '?', ':', '\\', '/', '<', '>' };
        
        var escapedQuery = query;
        foreach (var c in specialChars)
        {
            escapedQuery = escapedQuery.Replace(c.ToString(), "\\" + c);
        }
        
        return escapedQuery;
    }

    /// <summary>
    /// Find the project root directory by looking for common project markers
    /// </summary>
    private string? FindProjectRoot(string startPath)
    {
        var currentPath = startPath;
        
        while (!string.IsNullOrEmpty(currentPath))
        {
            // Check for various project root indicators
            var projectIndicators = new[]
            {
                ".git",
                "*.sln",
                "*.csproj",
                "package.json",
                "tsconfig.json",
                "Cargo.toml",
                "go.mod"
            };
            
            foreach (var indicator in projectIndicators)
            {
                if (indicator.Contains('*'))
                {
                    // It's a pattern, check for files
                    var files = Directory.GetFiles(currentPath, indicator);
                    if (files.Length > 0)
                    {
                        _logger.LogDebug("Found project indicator {Indicator} at {Path}, using as project root", indicator, currentPath);
                        return currentPath;
                    }
                }
                else
                {
                    // It's a directory or file name
                    var indicatorPath = Path.Combine(currentPath, indicator);
                    if (Directory.Exists(indicatorPath) || File.Exists(indicatorPath))
                    {
                        _logger.LogDebug("Found project indicator {Indicator} at {Path}, using as project root", indicator, currentPath);
                        return currentPath;
                    }
                }
            }
            
            var parent = Directory.GetParent(currentPath);
            if (parent == null)
                break;
                
            currentPath = parent.FullName;
        }
        
        _logger.LogDebug("No project root indicators found, will use parent directory");
        return null;
    }

    private class SearchResult
    {
        public required string FilePath { get; set; }
        public required string FileName { get; set; }
        public required string RelativePath { get; set; }
        public required string Extension { get; set; }
        public required string Language { get; set; }
        public float Score { get; set; }
        public List<ContextLine>? Context { get; set; }
    }

    private class ContextLine
    {
        public int LineNumber { get; set; }
        public required string Content { get; set; }
        public bool IsMatch { get; set; }
    }
}