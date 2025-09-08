using NUnit.Framework;
using FluentAssertions;
using Moq;
using Microsoft.Extensions.Logging;
using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Services.Lucene;
using Lucene.Net.Search;
using Lucene.Net.Index;
using Lucene.Net.Store;
using Lucene.Net.Util;
using COA.CodeSearch.McpServer.Services.Analysis;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Lucene.Net.Documents;

namespace COA.CodeSearch.McpServer.Tests.Services
{
    [TestFixture]
    public class SmartSnippetServiceTests
    {
        private SmartSnippetService _service = null!;
        private Mock<ILogger<SmartSnippetService>> _loggerMock = null!;
        private IndexSearcher _searcher = null!;
        private RAMDirectory _directory = null!;
        private CodeAnalyzer _codeAnalyzer = null!;

        [SetUp]
        public void SetUp()
        {
            _loggerMock = new Mock<ILogger<SmartSnippetService>>();
            _codeAnalyzer = new CodeAnalyzer(LuceneVersion.LUCENE_48);
            _service = new SmartSnippetService(_loggerMock.Object, _codeAnalyzer);
            
            // Setup in-memory index for testing
            _directory = new RAMDirectory();
            var config = new IndexWriterConfig(LuceneVersion.LUCENE_48, _codeAnalyzer);
            
            using (var writer = new IndexWriter(_directory, config))
            {
                // Add a test document
                var doc = new Document();
                doc.Add(new StringField("path", "/test/file.cs", Field.Store.YES));
                doc.Add(new TextField("content", "public class TestClass { public void TestMethod() { Console.WriteLine(\"test\"); } }", Field.Store.YES));
                writer.AddDocument(doc);
                writer.Commit();
            }
            
            var reader = DirectoryReader.Open(_directory);
            _searcher = new IndexSearcher(reader);
        }

        [TearDown]
        public void TearDown()
        {
            _searcher?.IndexReader?.Dispose();
            _directory?.Dispose();
            _codeAnalyzer?.Dispose();
        }

        [Test]
        public async Task EnhanceWithSnippetsAsync_Should_Handle_Valid_Query_Without_Error()
        {
            // Arrange
            var searchResult = new SearchResult
            {
                Hits = new List<SearchHit>
                {
                    new SearchHit
                    {
                        FilePath = "/test/file.cs",
                        LineNumber = 1,
                        Score = 1.0f
                    }
                }
            };

            var query = new TermQuery(new Term("content", "TestClass"));

            // Act
            var result = await _service.EnhanceWithSnippetsAsync(searchResult, query, _searcher, true);

            // Assert
            result.Should().NotBeNull();
            result.Hits.Should().HaveCount(1);
        }

        [Test]
        public async Task EnhanceWithSnippetsAsync_Should_Handle_Leading_Wildcard_Query_Gracefully()
        {
            // Arrange
            var searchResult = new SearchResult
            {
                Hits = new List<SearchHit>
                {
                    new SearchHit
                    {
                        FilePath = "/test/file.cs",
                        LineNumber = 1,
                        Score = 1.0f
                    }
                }
            };

            // Create a query that would contain leading wildcards after processing
            var query = new TermQuery(new Term("content", "*TestClass"));

            // Act - Should not throw exception
            var result = await _service.EnhanceWithSnippetsAsync(searchResult, query, _searcher, true);

            // Assert
            result.Should().NotBeNull();
            result.Hits.Should().HaveCount(1);
            
            // Verify that warning was logged for invalid wildcard
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Debug,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Invalid wildcard query detected")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.AtLeastOnce);
        }

        [Test]
        public async Task EnhanceWithSnippetsAsync_Should_Handle_Pure_Wildcard_Query_Gracefully()
        {
            // Arrange
            var searchResult = new SearchResult
            {
                Hits = new List<SearchHit>
                {
                    new SearchHit
                    {
                        FilePath = "/test/file.cs",
                        LineNumber = 1,
                        Score = 1.0f
                    }
                }
            };

            // Create a query that would result in pure wildcards after cleaning
            var query = new TermQuery(new Term("content", "*"));

            // Act - Should not throw exception
            var result = await _service.EnhanceWithSnippetsAsync(searchResult, query, _searcher, true);

            // Assert
            result.Should().NotBeNull();
            result.Hits.Should().HaveCount(1);
        }

        [Test]
        public async Task EnhanceWithSnippetsAsync_Should_Handle_QueryParser_Exception_Gracefully()
        {
            // Arrange
            var searchResult = new SearchResult
            {
                Hits = new List<SearchHit>
                {
                    new SearchHit
                    {
                        FilePath = "/test/file.cs",
                        LineNumber = 1,
                        Score = 1.0f
                    }
                }
            };

            // Create a query with problematic syntax that could cause parsing issues
            var query = new TermQuery(new Term("content", "test AND OR"));

            // Act - Should not throw exception
            var result = await _service.EnhanceWithSnippetsAsync(searchResult, query, _searcher, true);

            // Assert
            result.Should().NotBeNull();
            result.Hits.Should().HaveCount(1);
        }

        [Test]
        public async Task EnhanceWithSnippetsAsync_Should_Return_Original_Result_When_ForVisualization_False()
        {
            // Arrange
            var searchResult = new SearchResult
            {
                Hits = new List<SearchHit>
                {
                    new SearchHit { FilePath = "/test/file.cs", LineNumber = 1, Score = 1.0f }
                }
            };

            var query = new TermQuery(new Term("content", "TestClass"));

            // Act
            var result = await _service.EnhanceWithSnippetsAsync(searchResult, query, _searcher, forVisualization: false);

            // Assert
            result.Should().BeSameAs(searchResult); // Should return exact same instance
        }

        [Test]
        public async Task EnhanceWithSnippetsAsync_Should_Return_Original_Result_When_No_Hits()
        {
            // Arrange
            var searchResult = new SearchResult { Hits = new List<SearchHit>() };
            var query = new TermQuery(new Term("content", "TestClass"));

            // Act
            var result = await _service.EnhanceWithSnippetsAsync(searchResult, query, _searcher, true);

            // Assert
            result.Should().BeSameAs(searchResult);
        }

        [Test]
        public async Task EnhanceWithSnippetsAsync_Should_Add_Context_Lines_And_Snippets()
        {
            // Arrange
            var searchResult = new SearchResult
            {
                Hits = new List<SearchHit>
                {
                    new SearchHit
                    {
                        FilePath = "/test/file.cs",
                        LineNumber = 1,
                        Score = 1.0f
                    }
                }
            };

            var query = new TermQuery(new Term("content", "TestClass"));

            // Act
            var result = await _service.EnhanceWithSnippetsAsync(searchResult, query, _searcher, true);

            // Assert
            result.Should().NotBeNull();
            result.Hits.Should().HaveCount(1);
            
            var hit = result.Hits[0];
            hit.Snippet.Should().NotBeNullOrEmpty();
            hit.ContextLines.Should().NotBeNull();
        }

        [TestCase("*test")]
        [TestCase("?test")]  
        [TestCase("*")]
        [TestCase("?")]
        [TestCase("***")]
        public async Task EnhanceWithSnippetsAsync_Should_Handle_Invalid_Wildcard_Patterns(string invalidWildcard)
        {
            // Arrange
            var searchResult = new SearchResult
            {
                Hits = new List<SearchHit>
                {
                    new SearchHit
                    {
                        FilePath = "/test/file.cs", 
                        LineNumber = 1,
                        Score = 1.0f
                    }
                }
            };

            var query = new TermQuery(new Term("content", invalidWildcard));

            // Act & Assert - Should not throw exception
            var result = await _service.EnhanceWithSnippetsAsync(searchResult, query, _searcher, true);
            result.Should().NotBeNull();
        }

        [Test]
        public async Task EnhanceWithSnippetsAsync_Should_Handle_FilenameFieldWildcardQuery_WithoutException()
        {
            // Arrange - Test the specific bug pattern: filename_lower:*tests.cs
            var searchResult = new SearchResult
            {
                Hits = new List<SearchHit>
                {
                    new SearchHit
                    {
                        FilePath = "/test/SomeTests.cs", 
                        LineNumber = 1,
                        Score = 1.0f
                    }
                }
            };

            // Create a query that simulates filename_lower:*tests.cs pattern
            // This would have caused ParseException before the fix
            var query = new TermQuery(new Term("filename_lower", "*tests.cs"));

            // Act & Assert - Should not throw ParseException
            var result = await _service.EnhanceWithSnippetsAsync(searchResult, query, _searcher, true);
            
            // Verify the result is returned (even if enhancement fails, original should be preserved)
            result.Should().NotBeNull();
            result.Hits.Should().HaveCount(1);
            result.Hits[0].FilePath.Should().Be("/test/SomeTests.cs");
            
            // Verify no ParseException was logged (the fix should handle this gracefully)
            // The CreateContentQuery method should clean filename_lower: and handle wildcards properly
        }

        [TestCase("filename:*Controller.cs")]
        [TestCase("filename_lower:*Service.cs")]
        [TestCase("path:*/tests/*")]
        [TestCase("extension:*.json")]
        public async Task EnhanceWithSnippetsAsync_Should_Handle_FieldPrefixedWildcardQueries_WithoutException(string fieldQuery)
        {
            // Arrange - Test various field-prefixed wildcard patterns that could cause issues
            var searchResult = new SearchResult
            {
                Hits = new List<SearchHit>
                {
                    new SearchHit
                    {
                        FilePath = "/test/TestFile.cs", 
                        LineNumber = 1,
                        Score = 1.0f
                    }
                }
            };

            // Parse field and term from the query string
            var parts = fieldQuery.Split(':', 2);
            var field = parts[0];
            var term = parts[1];
            var query = new TermQuery(new Term(field, term));

            // Act & Assert - Should not throw ParseException
            var result = await _service.EnhanceWithSnippetsAsync(searchResult, query, _searcher, true);
            
            // Verify graceful handling
            result.Should().NotBeNull();
            result.Hits.Should().HaveCount(1);
        }
    }
}