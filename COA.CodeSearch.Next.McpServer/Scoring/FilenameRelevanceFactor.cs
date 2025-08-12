using Lucene.Net.Index;

namespace COA.CodeSearch.Next.McpServer.Scoring;

/// <summary>
/// Boosts documents where the search query appears in the filename.
/// This helps ensure that files named after the concept being searched for
/// appear higher in results.
/// </summary>
public class FilenameRelevanceFactor : IScoringFactor
{
    public string Name => "FilenameRelevance";
    public float Weight { get; set; } = 0.8f;

    public float CalculateScore(IndexReader reader, int docId, ScoringContext searchContext)
    {
        try
        {
            var doc = reader.Document(docId);
            var filename = doc.Get("filename")?.ToLowerInvariant() ?? "";
            var queryText = searchContext.QueryText.ToLowerInvariant();
            
            if (string.IsNullOrEmpty(filename) || string.IsNullOrEmpty(queryText))
                return 0f;

            // Clean query text
            queryText = CleanQueryText(queryText);
            if (string.IsNullOrEmpty(queryText))
                return 0f;

            // Extract query terms
            var queryTerms = ExtractTerms(queryText);
            if (!queryTerms.Any())
                return 0f;

            var score = 0f;
            
            // Exact filename match (without extension)
            var filenameWithoutExt = Path.GetFileNameWithoutExtension(filename).ToLowerInvariant();
            if (filenameWithoutExt.Equals(queryText, StringComparison.OrdinalIgnoreCase))
            {
                return 1.0f; // Perfect match
            }

            // Check if all query terms appear in filename
            var allTermsMatch = queryTerms.All(term => filenameWithoutExt.Contains(term));
            if (allTermsMatch)
            {
                score = 0.8f;
            }
            else
            {
                // Partial term matching
                var matchingTerms = queryTerms.Count(term => filenameWithoutExt.Contains(term));
                score = (float)matchingTerms / queryTerms.Count * 0.6f;
            }

            // Boost for term position in filename
            if (queryTerms.Any(term => filenameWithoutExt.StartsWith(term)))
            {
                score = Math.Min(1.0f, score + 0.2f);
            }

            // Special patterns
            if (IsTestFile(filename) && queryTerms.Contains("test"))
            {
                score = Math.Min(1.0f, score + 0.3f);
            }
            
            if (IsInterfaceFile(filename) && queryTerms.Contains("interface"))
            {
                score = Math.Min(1.0f, score + 0.3f);
            }

            return score;
        }
        catch (Exception)
        {
            return 0f;
        }
    }

    private string CleanQueryText(string query)
    {
        // Remove operators
        query = query.Replace(" AND ", " ")
                     .Replace(" OR ", " ")
                     .Replace(" NOT ", " ");
        
        // Remove special characters
        query = query.Replace("*", "")
                     .Replace("?", "")
                     .Replace("~", "")
                     .Replace("\"", "")
                     .Replace("'", "");
        
        return query.Trim();
    }

    private List<string> ExtractTerms(string query)
    {
        // Split on spaces and filter out empty terms
        var terms = query.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(t => t.ToLowerInvariant())
                        .Where(t => t.Length > 1) // Ignore single character terms
                        .Distinct()
                        .ToList();
        
        return terms;
    }

    private bool IsTestFile(string filename)
    {
        return filename.Contains("test", StringComparison.OrdinalIgnoreCase) ||
               filename.Contains("spec", StringComparison.OrdinalIgnoreCase) ||
               filename.EndsWith(".test.cs", StringComparison.OrdinalIgnoreCase) ||
               filename.EndsWith(".tests.cs", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsInterfaceFile(string filename)
    {
        return filename.StartsWith("I", StringComparison.Ordinal) &&
               filename.Length > 1 &&
               char.IsUpper(filename[1]);
    }
}