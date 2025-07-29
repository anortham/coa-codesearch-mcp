using Lucene.Net.Index;

namespace COA.CodeSearch.McpServer.Scoring;

/// <summary>
/// Scoring factor that boosts actual implementations over mock/test implementations
/// when searching for interfaces or type names. Designed for codebase searches.
/// </summary>
public class InterfaceImplementationFactor : IScoringFactor
{
    public string Name => "InterfaceImplementation";
    public float Weight { get; set; } = 0.3f; // Moderate weight

    public float CalculateScore(IndexReader reader, int docId, ScoringContext searchContext)
    {
        try
        {
            var doc = reader.Document(docId);
            var filename = doc.Get("filename") ?? "";
            var relativePath = doc.Get("relativePath") ?? "";
            var content = doc.Get("content") ?? "";

            if (string.IsNullOrEmpty(filename) || string.IsNullOrEmpty(searchContext.QueryText))
                return 0.5f; // Neutral score

            var queryLower = searchContext.QueryText.ToLowerInvariant();
            var filenameLower = filename.ToLowerInvariant();
            var pathLower = relativePath.ToLowerInvariant();
            var contentLower = content.ToLowerInvariant();

            // Check if this looks like an interface search (starts with 'I' and uppercase next letter)
            var isInterfaceSearch = IsLikelyInterfaceSearch(searchContext.QueryText);

            if (!isInterfaceSearch)
                return 0.5f; // Neutral - not an interface search

            var score = 0.5f; // Start neutral

            // Strong penalty for obvious mocks and test implementations
            if (IsMockOrTestImplementation(filenameLower, pathLower, contentLower))
            {
                score = 0.2f; // Strong penalty
            }
            // Strong boost for actual implementations
            else if (IsActualImplementation(filenameLower, pathLower, contentLower, queryLower))
            {
                score = 1.0f; // Maximum boost for real implementations
            }
            // Moderate boost for service/component files that might be implementations
            else if (IsLikelyImplementationFile(filenameLower, pathLower))
            {
                score = 0.8f; // Good boost for likely implementations
            }
            // Slight penalty for files that just reference the interface but don't implement it
            else if (IsJustReference(contentLower, queryLower))
            {
                score = 0.4f; // Slight penalty for mere references
            }

            return score;
        }
        catch (Exception)
        {
            return 0.5f; // Neutral score on error
        }
    }

    private bool IsLikelyInterfaceSearch(string query)
    {
        // Check if query starts with 'I' followed by uppercase letter (common C# interface pattern)
        return query.Length > 1 && 
               query[0] == 'I' && 
               char.IsUpper(query[1]) &&
               !query.Contains(' '); // Single term, not a phrase
    }

    private bool IsMockOrTestImplementation(string filename, string path, string content)
    {
        return filename.Contains("mock") ||
               filename.Contains("test") ||
               filename.Contains("fake") ||
               filename.Contains("stub") ||
               path.Contains("test") ||
               path.Contains("mock") ||
               path.Contains("spec") ||
               content.Contains("class Mock") ||
               content.Contains("class Test") ||
               content.Contains("class Fake") ||
               content.Contains("[Test") ||
               content.Contains("[Fact");
    }

    private bool IsActualImplementation(string filename, string path, string content, string queryInterface)
    {
        // Remove 'I' prefix to get the likely implementation name
        if (queryInterface.Length <= 1) return false;
        
        var implementationName = queryInterface.Substring(1); // Remove 'I' prefix
        var implementationNameLower = implementationName.ToLowerInvariant();

        // Check if filename matches the expected implementation name
        var filenameMatches = filename.Contains(implementationNameLower) && 
                             !filename.Contains("mock") && 
                             !filename.Contains("test");

        // Check if it's in a production directory
        var inProductionPath = path.Contains("services") ||
                              path.Contains("domain") ||
                              path.Contains("core") ||
                              path.Contains("infrastructure") ||
                              path.Contains("implementation") ||
                              (!path.Contains("test") && !path.Contains("mock") && !path.Contains("spec"));

        // Check if content suggests it's an actual implementation
        var hasImplementationPattern = content.Contains($"class {implementationName}") ||
                                     content.Contains($": {queryInterface}") ||
                                     (content.Contains("class ") && content.Contains($"{queryInterface}"));

        return filenameMatches && inProductionPath && hasImplementationPattern;
    }

    private bool IsLikelyImplementationFile(string filename, string path)
    {
        var isInProductionPath = path.Contains("services") ||
                               path.Contains("domain") ||
                               path.Contains("core") ||
                               path.Contains("infrastructure") ||
                               (!path.Contains("test") && !path.Contains("mock"));

        var hasImplementationFileName = filename.EndsWith("service.cs") ||
                                      filename.EndsWith("repository.cs") ||
                                      filename.EndsWith("manager.cs") ||
                                      filename.EndsWith("handler.cs") ||
                                      filename.EndsWith("provider.cs");

        return isInProductionPath && hasImplementationFileName;
    }

    private bool IsJustReference(string content, string queryInterface)
    {
        // Count occurrences of the interface name
        var interfaceCount = CountOccurrences(content, queryInterface.ToLowerInvariant());
        
        // If it's mentioned only a few times and doesn't seem to implement it, it's likely just a reference
        return interfaceCount > 0 && interfaceCount <= 3 && 
               !content.Contains($": {queryInterface}") &&
               !content.Contains($"class") && content.Contains($": {queryInterface}");
    }

    private int CountOccurrences(string text, string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return 0;
        
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.OrdinalIgnoreCase)) != -1)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }
}