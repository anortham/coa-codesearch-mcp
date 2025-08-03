using Lucene.Net.Analysis;
using Lucene.Net.Analysis.TokenAttributes;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Text.RegularExpressions;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Service that handles regex searches across tokenized content.
/// Since Lucene's RegexpQuery only works on individual terms, this service
/// provides a way to search for regex patterns that span multiple tokens.
/// </summary>
public class RegexSearchService
{
    private readonly ILogger<RegexSearchService> _logger;

    public RegexSearchService(ILogger<RegexSearchService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Performs a regex search across documents, handling patterns that may span multiple tokens.
    /// </summary>
    public Task<List<Document>> SearchWithRegexAsync(
        IndexSearcher searcher,
        string regexPattern,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        var results = new List<Document>();
        var regex = new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        
        // First, get all documents (we'll need to implement a more efficient approach later)
        var allDocsQuery = new MatchAllDocsQuery();
        var topDocs = searcher.Search(allDocsQuery, maxResults * 10); // Get more candidates
        
        _logger.LogDebug("Regex search: Evaluating {Count} documents against pattern '{Pattern}'", 
            topDocs.ScoreDocs.Length, regexPattern);
        
        foreach (var scoreDoc in topDocs.ScoreDocs)
        {
            if (cancellationToken.IsCancellationRequested)
                break;
                
            var doc = searcher.Doc(scoreDoc.Doc);
            var content = doc.Get("content");
            
            if (!string.IsNullOrEmpty(content) && regex.IsMatch(content))
            {
                results.Add(doc);
                if (results.Count >= maxResults)
                    break;
            }
        }
        
        _logger.LogInformation("Regex search found {Count} matches for pattern '{Pattern}'", 
            results.Count, regexPattern);
        
        return Task.FromResult(results);
    }

    /// <summary>
    /// Creates a more efficient query for simple regex patterns that can be converted to other query types.
    /// </summary>
    public Query? TryOptimizeRegexQuery(string regexPattern, Analyzer analyzer)
    {
        // Handle simple patterns that can be optimized
        
        // Pattern: word1.*word2 -> Convert to a phrase query with slop
        var multiWordMatch = Regex.Match(regexPattern, @"^(\w+)\.\*(\w+)$");
        if (multiWordMatch.Success)
        {
            var word1 = multiWordMatch.Groups[1].Value;
            var word2 = multiWordMatch.Groups[2].Value;
            
            _logger.LogDebug("Optimizing regex '{Pattern}' to phrase query with slop", regexPattern);
            
            var phraseQuery = new PhraseQuery();
            phraseQuery.Slop = 10; // Allow up to 10 words between
            phraseQuery.Add(new Term("content", word1.ToLowerInvariant()));
            phraseQuery.Add(new Term("content", word2.ToLowerInvariant()));
            
            return phraseQuery;
        }
        
        // Pattern: ^word or word$ -> Convert to prefix/suffix queries
        if (regexPattern.StartsWith("^") && !regexPattern.Contains(".*") && !regexPattern.Contains(".+"))
        {
            var word = regexPattern.TrimStart('^').ToLowerInvariant();
            return new PrefixQuery(new Term("content", word));
        }
        
        // Pattern: simple word without regex metacharacters
        if (!Regex.IsMatch(regexPattern, @"[.*+?^${}()|[\]\\]"))
        {
            return new TermQuery(new Term("content", regexPattern.ToLowerInvariant()));
        }
        
        return null; // Can't optimize, will need full regex search
    }
}