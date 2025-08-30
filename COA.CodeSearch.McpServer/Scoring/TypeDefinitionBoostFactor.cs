using Lucene.Net.Documents;
using Lucene.Net.Index;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Scoring;

public class TypeDefinitionBoostFactor : IScoringFactor
{
    private readonly ILogger<TypeDefinitionBoostFactor>? _logger;
    
    public TypeDefinitionBoostFactor(ILogger<TypeDefinitionBoostFactor>? logger = null)
    {
        _logger = logger;
    }
    
    public string Name => "TypeDefinitionBoost";
    
    public float Weight { get; set; } = 1.0f;
    
    public float CalculateScore(IndexReader reader, int docId, ScoringContext searchContext)
    {
        var doc = reader.Document(docId);
        return CalculateBoost(doc, searchContext.QueryText, searchContext);
    }
    
    private float CalculateBoost(Document doc, string queryText, ScoringContext context)
    {
        var typeNames = doc.Get("type_names");
        if (string.IsNullOrEmpty(typeNames))
        {
            return 1.0f;
        }
        
        var queryTerms = queryText.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var typeTerms = typeNames.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        
        var matchCount = 0;
        foreach (var queryTerm in queryTerms)
        {
            if (typeTerms.Any(t => t.Contains(queryTerm) || queryTerm.Contains(t)))
            {
                matchCount++;
            }
        }
        
        if (matchCount > 0)
        {
            // MASSIVELY boost type name matches - Claude needs to see definitions FIRST
            var boost = 1.0f + (2.0f * matchCount); // Increased from 0.5f to 2.0f
            _logger?.LogDebug("Type definition boost for document {Path}: {Boost} (matched {Count} terms)", 
                doc.Get("path"), boost, matchCount);
            return boost;
        }
        
        var typeDefs = doc.GetFields("type_def");
        if (typeDefs != null && typeDefs.Length > 0)
        {
            foreach (var typeDef in typeDefs)
            {
                var defValue = typeDef.GetStringValue()?.ToLower();
                if (!string.IsNullOrEmpty(defValue))
                {
                    foreach (var queryTerm in queryTerms)
                    {
                        if (defValue.Contains(queryTerm))
                        {
                            // HUGE boost for actual type definitions - make them unmissable!
                            _logger?.LogDebug("Type definition field boost for document {Path}: 10.0", doc.Get("path"));
                            return 10.0f; // Increased from 1.3f to 10.0f - type definitions MUST be first
                        }
                    }
                }
            }
        }
        
        return 1.0f;
    }
}