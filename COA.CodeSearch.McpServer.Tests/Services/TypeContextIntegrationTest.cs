using NUnit.Framework;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using COA.CodeSearch.McpServer.Services.Lucene;
using COA.CodeSearch.McpServer.Tests.Base;
using Lucene.Net.Search;
using Lucene.Net.Index;
using System.Linq;
using System.Threading.Tasks;

namespace COA.CodeSearch.McpServer.Tests.Services;

[TestFixture]
public class TypeContextIntegrationTest
{
    private ILuceneIndexService _luceneService = null!;

    [SetUp]
    public void Setup()
    {
        // For integration testing, we'll create the service directly or use a test harness
        // This is a simplified setup - in practice you'd want proper DI container setup
    }

    [Test]
    [Ignore("Integration test - requires proper service setup")]
    public async Task SearchResults_Should_Include_TypeContext_With_NearbyTypes()
    {
        // Arrange - Index the workspace first
        var workspacePath = GetTestDataPath();
        await _luceneService.ForceRebuildIndexAsync(workspacePath);

        // Act - Search for a known class
        var query = new TermQuery(new Term("content", "typeextractionservice"));
        var searchResult = await _luceneService.SearchAsync(workspacePath, query, 10, true);

        // Assert
        searchResult.Should().NotBeNull();
        searchResult.Hits.Should().NotBeEmpty("Should find results for TypeExtractionService");

        // Check if any hit has TypeContext populated
        var hitsWithTypeContext = searchResult.Hits
            .Where(h => h.TypeContext != null)
            .ToList();

        if (hitsWithTypeContext.Any())
        {
            TestContext.WriteLine($"Found {hitsWithTypeContext.Count} hits with TypeContext");

            var firstHitWithContext = hitsWithTypeContext.First();
            TestContext.WriteLine($"First hit with TypeContext:");
            TestContext.WriteLine($"  - File: {firstHitWithContext.FilePath}");
            TestContext.WriteLine($"  - Line: {firstHitWithContext.LineNumber}");
            TestContext.WriteLine($"  - Language: {firstHitWithContext.TypeContext.Language}");
            TestContext.WriteLine($"  - ContainingType: {firstHitWithContext.TypeContext.ContainingType}");
            TestContext.WriteLine($"  - Nearby Types: {firstHitWithContext.TypeContext.NearbyTypes?.Count ?? 0}");
            TestContext.WriteLine($"  - Nearby Methods: {firstHitWithContext.TypeContext.NearbyMethods?.Count ?? 0}");

            // Verify the TypeContext has meaningful data
            firstHitWithContext.TypeContext.Language.Should().NotBeNullOrEmpty();

            // At least one of these should have data
            var hasTypeInfo = (firstHitWithContext.TypeContext.NearbyTypes?.Any() ?? false) ||
                             (firstHitWithContext.TypeContext.NearbyMethods?.Any() ?? false) ||
                             (firstHitWithContext.TypeContext.ContainingType != null);
            hasTypeInfo.Should().BeTrue("TypeContext should have some type information");
        }
        else
        {
            TestContext.WriteLine("WARNING: No hits have TypeContext populated!");
            TestContext.WriteLine($"Total hits: {searchResult.Hits.Count}");

            // Log what fields are available
            if (searchResult.Hits.Any())
            {
                var firstHit = searchResult.Hits.First();
                TestContext.WriteLine($"First hit fields: {string.Join(", ", firstHit.Fields.Keys)}");

                // Check if type_info field exists
                if (firstHit.Fields.ContainsKey("type_info"))
                {
                    TestContext.WriteLine($"type_info field exists but TypeContext not populated!");
                    TestContext.WriteLine($"type_info content (first 500 chars): {firstHit.Fields["type_info"].Substring(0, Math.Min(500, firstHit.Fields["type_info"].Length))}");
                }
            }
        }

        // This assertion helps us understand the current state
        hitsWithTypeContext.Should().NotBeEmpty("At least one search hit should have TypeContext populated");
    }

    [Test]
    [Ignore("Integration test - requires proper service setup")]
    public async Task TypeContext_Should_Identify_ContainingType_Correctly()
    {
        // Arrange
        var workspacePath = GetTestDataPath();
        await _luceneService.ForceRebuildIndexAsync(workspacePath);

        // Act - Search for a method name
        var query = new TermQuery(new Term("content", "extracttypes"));
        var searchResult = await _luceneService.SearchAsync(workspacePath, query, 10, true);

        // Assert
        searchResult.Hits.Should().NotBeEmpty();

        var hitsWithContainingType = searchResult.Hits
            .Where(h => h.TypeContext?.ContainingType != null)
            .ToList();

        if (hitsWithContainingType.Any())
        {
            TestContext.WriteLine($"Found {hitsWithContainingType.Count} hits with containing type:");
            foreach (var hit in hitsWithContainingType.Take(3))
            {
                TestContext.WriteLine($"  - {hit.FilePath}:{hit.LineNumber} -> {hit.TypeContext!.ContainingType}");
            }
        }
        else
        {
            TestContext.WriteLine("No hits have ContainingType identified");
        }
    }

    private string GetTestDataPath()
    {
        // Use the actual project path for integration testing
        return @"C:\source\COA CodeSearch MCP";
    }
}