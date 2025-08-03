using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Xunit;

namespace COA.CodeSearch.McpServer.Tests.Services;

public class RegexSearchTests
{
    private readonly LuceneVersion _version = LuceneVersion.LUCENE_48;

    [Fact]
    public void PhraseQueryWithSlop_Should_Find_AsyncTask_Pattern()
    {
        // Arrange
        using var directory = new RAMDirectory();
        using var analyzer = new StandardAnalyzer(_version);
        
        var config = new IndexWriterConfig(_version, analyzer);
        using (var writer = new IndexWriter(directory, config))
        {
            // Add test documents
            var doc1 = new Document();
            doc1.Add(new TextField("content", "public async Task<string> GetDataAsync()", Field.Store.YES));
            writer.AddDocument(doc1);
            
            var doc2 = new Document();
            doc2.Add(new TextField("content", "private async Task DoSomethingAsync()", Field.Store.YES));
            writer.AddDocument(doc2);
            
            var doc3 = new Document();
            doc3.Add(new TextField("content", "public string GetData() // not async", Field.Store.YES));
            writer.AddDocument(doc3);
            
            writer.Commit();
        }
        
        // Act - Test the phrase query approach
        using var reader = DirectoryReader.Open(directory);
        var searcher = new IndexSearcher(reader);
        
        var phraseQuery = new PhraseQuery();
        phraseQuery.Slop = 20; // Allow words between
        phraseQuery.Add(new Term("content", "async"));
        phraseQuery.Add(new Term("content", "task"));
        
        var results = searcher.Search(phraseQuery, 10);
        
        // Assert
        Assert.Equal(2, results.TotalHits); // Should find both async methods
    }
    
    [Fact]
    public void RegexPattern_AsyncTask_Should_Work_With_PhraseQuery()
    {
        // This test simulates what our code does for regex patterns like "async.*Task"
        var regexPattern = "async.*Task";
        var parts = System.Text.RegularExpressions.Regex.Split(regexPattern, @"\.\*|\.\+");
        
        Assert.Equal(2, parts.Length);
        Assert.Equal("async", parts[0]);
        Assert.Equal("Task", parts[1]);
        
        // Verify the phrase query would be constructed correctly
        var phraseQuery = new PhraseQuery();
        phraseQuery.Slop = 20;
        phraseQuery.Add(new Term("content", parts[0].ToLowerInvariant()));
        phraseQuery.Add(new Term("content", parts[1].ToLowerInvariant()));
        
        Assert.Equal(2, phraseQuery.GetTerms().Length);
    }
}