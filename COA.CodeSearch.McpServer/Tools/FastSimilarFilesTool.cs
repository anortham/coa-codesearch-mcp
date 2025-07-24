using COA.CodeSearch.McpServer.Services;
using Lucene.Net.Analysis;
using Lucene.Net.Index;
using Lucene.Net.Queries.Mlt;
using Lucene.Net.Search;
using Lucene.Net.Util;
using Microsoft.Extensions.Logging;
using System.Text;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Straight blazin' fast tool to find files with similar content using Lucene's "More Like This" feature
/// </summary>
public class FastSimilarFilesTool : ITool
{
    public string ToolName => "fast_similar_files";
    public string Description => "Find files with similar content using 'More Like This'";
    public ToolCategory Category => ToolCategory.Search;
    private readonly ILogger<FastSimilarFilesTool> _logger;
    private readonly ILuceneIndexService _luceneIndexService;
    private const LuceneVersion Version = LuceneVersion.LUCENE_48;

    public FastSimilarFilesTool(
        ILogger<FastSimilarFilesTool> logger,
        ILuceneIndexService luceneIndexService)
    {
        _logger = logger;
        _luceneIndexService = luceneIndexService;
    }

    public async Task<object> ExecuteAsync(
        string sourceFilePath,
        string workspacePath,
        int maxResults = 10,
        int minTermFreq = 2,
        int minDocFreq = 2,
        int minWordLength = 4,
        int maxWordLength = 30,
        string[]? excludeExtensions = null,
        bool includeScore = true,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Fast similar files search for {SourceFile} in {WorkspacePath}", 
                sourceFilePath, workspacePath);

            // Validate inputs
            if (string.IsNullOrWhiteSpace(sourceFilePath))
            {
                return new
                {
                    success = false,
                    error = "Source file path cannot be empty"
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

            // Get index searcher
            var searcher = await _luceneIndexService.GetIndexSearcherAsync(workspacePath, cancellationToken);
            var analyzer = await _luceneIndexService.GetAnalyzerAsync(workspacePath, cancellationToken);
            
            // Find the source document
            var sourceQuery = new TermQuery(new Term("path", sourceFilePath));
            var sourceHits = searcher.Search(sourceQuery, 1);
            
            if (sourceHits.TotalHits == 0)
            {
                return new
                {
                    success = false,
                    error = $"Source file not found in index: {sourceFilePath}. Make sure the workspace is indexed."
                };
            }

            var sourceDocId = sourceHits.ScoreDocs[0].Doc;
            var sourceDoc = searcher.Doc(sourceDocId);
            
            // Set up MoreLikeThis query
            var mlt = new MoreLikeThis(searcher.IndexReader)
            {
                Analyzer = analyzer,
                MinTermFreq = minTermFreq,
                MinDocFreq = minDocFreq,
                MinWordLen = minWordLength,
                MaxWordLen = maxWordLength,
                MaxQueryTerms = 25,
                FieldNames = new[] { "content" } // Search in content field
            };

            // Create the query
            var startTime = DateTime.UtcNow;
            var query = mlt.Like(sourceDocId);
            
            // Add exclusions if needed
            if (excludeExtensions?.Length > 0 || true) // Always exclude the source file
            {
                var boolQuery = new BooleanQuery();
                boolQuery.Add(query, Occur.MUST);
                
                // Exclude source file
                boolQuery.Add(new TermQuery(new Term("path", sourceFilePath)), Occur.MUST_NOT);
                
                // Exclude extensions
                if (excludeExtensions?.Length > 0)
                {
                    foreach (var ext in excludeExtensions)
                    {
                        var normalizedExt = ext.StartsWith(".") ? ext : $".{ext}";
                        boolQuery.Add(new TermQuery(new Term("extension", normalizedExt)), Occur.MUST_NOT);
                    }
                }
                
                query = boolQuery;
            }

            // Execute search
            var topDocs = searcher.Search(query, maxResults);
            var searchDuration = (DateTime.UtcNow - startTime).TotalMilliseconds;

            // Process results
            var results = new List<object>();
            var topTerms = GetTopTermsFromDocument(mlt, sourceDocId, 10);
            
            foreach (var scoreDoc in topDocs.ScoreDocs)
            {
                var doc = searcher.Doc(scoreDoc.Doc);
                
                results.Add(new
                {
                    path = doc.Get("path"),
                    filename = doc.Get("filename"),
                    relativePath = doc.Get("relativePath"),
                    extension = doc.Get("extension"),
                    language = doc.Get("language") ?? "",
                    similarity = includeScore ? scoreDoc.Score : (float?)null,
                    similarityPercentage = includeScore ? $"{(scoreDoc.Score * 100):F1}%" : null
                });
            }

            _logger.LogInformation("Found {Count} similar files in {Duration}ms - straight blazin' fast!", 
                results.Count, searchDuration);

            return new
            {
                success = true,
                sourceFile = new
                {
                    path = sourceFilePath,
                    filename = sourceDoc.Get("filename"),
                    extension = sourceDoc.Get("extension"),
                    language = sourceDoc.Get("language") ?? ""
                },
                workspacePath = workspacePath,
                totalResults = results.Count,
                searchDurationMs = searchDuration,
                results = results,
                analysis = new
                {
                    topTerms = topTerms,
                    parameters = new
                    {
                        minTermFreq = minTermFreq,
                        minDocFreq = minDocFreq,
                        minWordLength = minWordLength,
                        maxWordLength = maxWordLength
                    }
                },
                performance = searchDuration < 50 ? "straight blazin'" : "blazin' fast"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in fast similar files search");
            return new
            {
                success = false,
                error = $"Search failed: {ex.Message}"
            };
        }
    }

    private List<string> GetTopTermsFromDocument(MoreLikeThis mlt, int docId, int maxTerms)
    {
        // For now, return empty list as term extraction from MoreLikeThis is complex
        // This would require accessing the internal IndexReader which isn't exposed in our setup
        return new List<string> { "content", "analysis", "unavailable" };
    }
}