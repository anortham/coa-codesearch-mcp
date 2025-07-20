using Lucene.Net.Search;
using Microsoft.Extensions.Logging;
using COA.CodeSearch.McpServer.Services;
using Lucene.Net.Index;

namespace COA.CodeSearch.McpServer.Tools;

public class DiagnoseMemoryIndexTool
{
    private readonly ILogger<DiagnoseMemoryIndexTool> _logger;
    private readonly ILuceneIndexService _luceneService;
    
    public DiagnoseMemoryIndexTool(
        ILogger<DiagnoseMemoryIndexTool> logger,
        ILuceneIndexService luceneService)
    {
        _logger = logger;
        _luceneService = luceneService;
    }
    
    public async Task<object> DiagnoseMemoryIndex(string workspace = "project-memory")
    {
        try
        {
            var searcher = await _luceneService.GetIndexSearcherAsync(workspace);
            var reader = searcher.IndexReader;
            
            var fieldList = new List<string>();
            var scopeCounts = new Dictionary<string, int>();
            var sampleDocs = new List<object>();
            
            var result = new
            {
                success = true,
                workspace = workspace,
                totalDocuments = reader.NumDocs,
                deletedDocuments = reader.NumDeletedDocs,
                fields = fieldList,
                scopeValues = scopeCounts,
                sampleDocuments = sampleDocs
            };
            
            // Get all field names
            var leaves = reader.Leaves;
            if (leaves.Count > 0)
            {
                var fields = leaves[0].AtomicReader.FieldInfos;
                foreach (var field in fields)
                {
                    fieldList.Add(field.Name);
                }
            }
            
            // Count documents by scope
            var allDocsQuery = new MatchAllDocsQuery();
            var collector = TopScoreDocCollector.Create(1000, true);
            searcher.Search(allDocsQuery, collector);
            var hits = collector.GetTopDocs().ScoreDocs;
            
            foreach (var hit in hits)
            {
                var doc = searcher.Doc(hit.Doc);
                var scope = doc.Get("scope");
                if (scope != null)
                {
                    if (!scopeCounts.ContainsKey(scope))
                        scopeCounts[scope] = 0;
                    scopeCounts[scope]++;
                }
                
                // Add first 5 as samples
                if (sampleDocs.Count < 5)
                {
                    var sample = new Dictionary<string, string>();
                    foreach (var field in doc.Fields)
                    {
                        sample[field.Name] = field.GetStringValue() ?? field.ToString();
                    }
                    sampleDocs.Add(sample);
                }
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error diagnosing memory index");
            return new { success = false, error = ex.Message };
        }
    }
}