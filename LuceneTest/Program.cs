using System;
using System.Collections.Generic;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;

class TestScopeQuery
{
    static void Main()
    {
        Console.WriteLine("=== Testing Lucene Scope Field Queries ===\n");

        // Test with different analyzers
        TestWithAnalyzer("StandardAnalyzer", new StandardAnalyzer(LuceneVersion.LUCENE_48));
        TestWithAnalyzer("WhitespaceAnalyzer", new WhitespaceAnalyzer(LuceneVersion.LUCENE_48));
        TestWithAnalyzer("KeywordAnalyzer", new KeywordAnalyzer());
    }

    static void TestWithAnalyzer(string analyzerName, Analyzer analyzer)
    {
        Console.WriteLine($"\n--- Testing with {analyzerName} ---");

        using var directory = new RAMDirectory();
        
        // Create index
        var config = new IndexWriterConfig(LuceneVersion.LUCENE_48, analyzer);
        using (var writer = new IndexWriter(directory, config))
        {
            // Add test documents
            AddDocument(writer, "ArchitecturalDecision", "Use Repository pattern", "doc1");
            AddDocument(writer, "SecurityRule", "Validate all inputs", "doc2");
            AddDocument(writer, "CodePattern", "Async/await pattern", "doc3");
            AddDocument(writer, "ArchitecturalDecision", "Use microservices", "doc4");
            AddDocument(writer, "ProjectInsight", "Performance is critical", "doc5");
            
            writer.Commit();
        }

        // Test different query methods
        using var reader = DirectoryReader.Open(directory);
        var searcher = new IndexSearcher(reader);

        // Method 1: TermQuery (exact term match)
        Console.WriteLine("\n1. TermQuery Results:");
        var termQuery = new TermQuery(new Term("scope", "ArchitecturalDecision"));
        var termResults = searcher.Search(termQuery, 10);
        Console.WriteLine($"   Found {termResults.TotalHits} documents with TermQuery");
        PrintResults(searcher, termResults);

        // Method 2: TermQuery with lowercase
        Console.WriteLine("\n2. TermQuery (lowercase) Results:");
        var termQueryLower = new TermQuery(new Term("scope", "architecturaldecision"));
        var termResultsLower = searcher.Search(termQueryLower, 10);
        Console.WriteLine($"   Found {termResultsLower.TotalHits} documents with lowercase TermQuery");
        PrintResults(searcher, termResultsLower);

        // Method 3: QueryParser
        Console.WriteLine("\n3. QueryParser Results:");
        var parser = new QueryParser(LuceneVersion.LUCENE_48, "scope", analyzer);
        var parsedQuery = parser.Parse("ArchitecturalDecision");
        var parserResults = searcher.Search(parsedQuery, 10);
        Console.WriteLine($"   Found {parserResults.TotalHits} documents with QueryParser");
        Console.WriteLine($"   Parsed query: {parsedQuery}");
        PrintResults(searcher, parserResults);

        // Method 4: PhraseQuery
        Console.WriteLine("\n4. PhraseQuery Results:");
        var phraseQuery = new PhraseQuery();
        phraseQuery.Add(new Term("scope", "ArchitecturalDecision"));
        var phraseResults = searcher.Search(phraseQuery, 10);
        Console.WriteLine($"   Found {phraseResults.TotalHits} documents with PhraseQuery");
        PrintResults(searcher, phraseResults);

        // Method 5: WildcardQuery
        Console.WriteLine("\n5. WildcardQuery Results:");
        var wildcardQuery = new WildcardQuery(new Term("scope", "Architectural*"));
        var wildcardResults = searcher.Search(wildcardQuery, 10);
        Console.WriteLine($"   Found {wildcardResults.TotalHits} documents with WildcardQuery");
        PrintResults(searcher, wildcardResults);

        // Show what's actually in the index
        Console.WriteLine("\n6. Index Contents (first document's scope field):");
        var doc = searcher.Doc(0);
        var scopeField = doc.GetField("scope");
        if (scopeField != null)
        {
            Console.WriteLine($"   Field name: {scopeField.Name}");
            Console.WriteLine($"   String value: '{scopeField.GetStringValue()}'");
            Console.WriteLine($"   Is indexed: {scopeField.FieldType.IsIndexed}");
            Console.WriteLine($"   Is stored: {scopeField.FieldType.IsStored}");
            Console.WriteLine($"   Is tokenized: {scopeField.FieldType.IsTokenized}");
        }

        // List all terms in the scope field
        Console.WriteLine("\n7. All terms in 'scope' field:");
        using var termsEnum = MultiFields.GetTerms(reader, "scope")?.GetEnumerator();
        if (termsEnum != null)
        {
            while (termsEnum.MoveNext())
            {
                Console.WriteLine($"   Term: '{termsEnum.Term.Utf8ToString()}'");
            }
        }
    }

    static void AddDocument(IndexWriter writer, string scope, string content, string id)
    {
        var doc = new Document();
        
        // Try different field types
        // StringField: not analyzed, indexed as single token
        doc.Add(new StringField("id", id, Field.Store.YES));
        doc.Add(new StringField("scope", scope, Field.Store.YES));
        
        // TextField: analyzed
        doc.Add(new TextField("content", content, Field.Store.YES));
        
        // Also try storing scope as TextField to see the difference
        doc.Add(new TextField("scope_analyzed", scope, Field.Store.YES));
        
        writer.AddDocument(doc);
    }

    static void PrintResults(IndexSearcher searcher, TopDocs results)
    {
        foreach (var scoreDoc in results.ScoreDocs)
        {
            var doc = searcher.Doc(scoreDoc.Doc);
            Console.WriteLine($"   - ID: {doc.Get("id")}, Scope: {doc.Get("scope")}, Content: {doc.Get("content")}");
        }
    }
}