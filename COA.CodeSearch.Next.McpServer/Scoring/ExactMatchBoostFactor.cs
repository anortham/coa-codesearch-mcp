using Lucene.Net.Index;
using System.Text.RegularExpressions;

namespace COA.CodeSearch.Next.McpServer.Scoring;

/// <summary>
/// Boosts documents that contain exact matches of the search query.
/// For example, searching for "TODO" will boost documents with the exact word "TODO"
/// higher than documents containing "TODO:" or "TodoList".
/// </summary>
public class ExactMatchBoostFactor : IScoringFactor
{
    private readonly bool _caseSensitive;

    public string Name => "ExactMatchBoost";
    public float Weight { get; set; } = 1.0f;

    public ExactMatchBoostFactor(bool caseSensitive = false)
    {
        _caseSensitive = caseSensitive;
    }

    public float CalculateScore(IndexReader reader, int docId, ScoringContext searchContext)
    {
        try
        {
            // Get the document
            var doc = reader.Document(docId);
            var content = doc.Get("content");
            
            if (string.IsNullOrEmpty(content) || string.IsNullOrEmpty(searchContext.QueryText))
                return 0f;

            // Create regex for exact word matching if not already created
            var exactMatchRegex = GetOrCreateRegex(searchContext.QueryText);
            if (exactMatchRegex == null)
                return 0f;

            // Count exact matches
            var matches = exactMatchRegex.Matches(content);
            if (matches.Count == 0)
                return 0f;

            // Score based on number of exact matches
            // Use logarithmic scale to prevent overwhelming scores
            var score = Math.Min(1.0f, (float)(Math.Log(matches.Count + 1) / Math.Log(10)));
            
            // Additional boost for matches in filename
            var filename = doc.Get("filename");
            if (!string.IsNullOrEmpty(filename))
            {
                var filenameMatches = exactMatchRegex.Matches(filename);
                if (filenameMatches.Count > 0)
                {
                    score = Math.Min(1.0f, score + 0.3f); // Extra 30% boost for filename matches
                }
            }

            return score;
        }
        catch (Exception)
        {
            // If any error occurs, don't affect scoring
            return 0f;
        }
    }

    private Regex? GetOrCreateRegex(string queryText)
    {
        // Clean the query text to get the core search term
        var cleanQuery = queryText.Trim();
        
        // Remove common search operators
        if (cleanQuery.Contains("AND") || cleanQuery.Contains("OR") || cleanQuery.Contains("NOT"))
        {
            // For complex queries, extract the first meaningful term
            var terms = cleanQuery.Split(new[] { "AND", "OR", "NOT" }, StringSplitOptions.RemoveEmptyEntries);
            if (terms.Length > 0)
            {
                cleanQuery = terms[0].Trim();
            }
        }

        // Remove quotes if present
        cleanQuery = cleanQuery.Trim('"', '\'');
        
        // Remove wildcards
        cleanQuery = cleanQuery.Replace("*", "").Replace("?", "");
        
        // Remove fuzzy search indicator
        cleanQuery = cleanQuery.TrimEnd('~');

        if (string.IsNullOrWhiteSpace(cleanQuery))
            return null;

        // Escape regex special characters
        cleanQuery = Regex.Escape(cleanQuery);

        // Create word boundary regex pattern
        var pattern = $@"\b{cleanQuery}\b";
        var options = _caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
        
        return new Regex(pattern, options | RegexOptions.Compiled);
    }
}