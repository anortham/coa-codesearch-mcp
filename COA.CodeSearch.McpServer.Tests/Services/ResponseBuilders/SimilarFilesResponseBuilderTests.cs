using COA.CodeSearch.Contracts.Responses.SimilarFiles;
using System.Collections.Generic;
using System.Text.Json;
using Xunit;

namespace COA.CodeSearch.McpServer.Tests.Services.ResponseBuilders;

public class SimilarFilesResponseBuilderTests
{
    [Fact]
    public void SimilarityRanges_SerializesWithExactPropertyNames()
    {
        // Arrange
        var ranges = new SimilarityRanges
        {
            veryHigh = 5,
            high = 8,
            moderate = 12,
            low = 3
        };

        // Act
        var json = JsonSerializer.Serialize(ranges);
        var parsed = JsonDocument.Parse(json);

        // Assert - exact property names
        Assert.True(parsed.RootElement.TryGetProperty("veryHigh", out var veryHighProp));
        Assert.Equal(5, veryHighProp.GetInt32());
        Assert.True(parsed.RootElement.TryGetProperty("high", out var highProp));
        Assert.Equal(8, highProp.GetInt32());
        Assert.True(parsed.RootElement.TryGetProperty("moderate", out var moderateProp));
        Assert.Equal(12, moderateProp.GetInt32());
        Assert.True(parsed.RootElement.TryGetProperty("low", out var lowProp));
        Assert.Equal(3, lowProp.GetInt32());
    }

    [Fact]
    public void DirectoryPattern_SerializesWithExactPropertyNames()
    {
        // Arrange
        var pattern = new DirectoryPattern
        {
            directory = "src/Services",
            count = 15,
            avgScore = 0.85
        };

        // Act
        var json = JsonSerializer.Serialize(pattern);
        var parsed = JsonDocument.Parse(json);

        // Assert - exact property names
        Assert.True(parsed.RootElement.TryGetProperty("directory", out var dirProp));
        Assert.Equal("src/Services", dirProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("count", out var countProp));
        Assert.Equal(15, countProp.GetInt32());
        Assert.True(parsed.RootElement.TryGetProperty("avgScore", out var avgProp));
        Assert.Equal(0.85, avgProp.GetDouble());
    }

    [Fact]
    public void SimilarFilesSource_SerializesWithExactPropertyNames()
    {
        // Arrange
        var source = new SimilarFilesSource
        {
            file = "UserService.cs",
            path = "src/Services/UserService.cs",
            size = 5432,
            sizeFormatted = "5.31 KB"
        };

        // Act
        var json = JsonSerializer.Serialize(source);
        var parsed = JsonDocument.Parse(json);

        // Assert - exact property names
        Assert.True(parsed.RootElement.TryGetProperty("file", out var fileProp));
        Assert.Equal("UserService.cs", fileProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("path", out var pathProp));
        Assert.Equal("src/Services/UserService.cs", pathProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("size", out var sizeProp));
        Assert.Equal(5432, sizeProp.GetInt64());
        Assert.True(parsed.RootElement.TryGetProperty("sizeFormatted", out var sizeFormattedProp));
        Assert.Equal("5.31 KB", sizeFormattedProp.GetString());
    }

    [Fact]
    public void SimilarFilesSummary_SerializesWithExactPropertyNames()
    {
        // Arrange
        var summary = new SimilarFilesSummary
        {
            totalFound = 25,
            searchTime = "15.2ms",
            avgSimilarity = 0.72,
            similarityDistribution = new SimilarityRanges
            {
                veryHigh = 3,
                high = 8,
                moderate = 10,
                low = 4
            }
        };

        // Act
        var json = JsonSerializer.Serialize(summary);
        var parsed = JsonDocument.Parse(json);

        // Assert - exact property names
        Assert.True(parsed.RootElement.TryGetProperty("totalFound", out var totalProp));
        Assert.Equal(25, totalProp.GetInt32());
        Assert.True(parsed.RootElement.TryGetProperty("searchTime", out var timeProp));
        Assert.Equal("15.2ms", timeProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("avgSimilarity", out var avgProp));
        Assert.Equal(0.72, avgProp.GetDouble());
        Assert.True(parsed.RootElement.TryGetProperty("similarityDistribution", out var distProp));
        Assert.True(distProp.TryGetProperty("veryHigh", out var veryHighProp));
        Assert.Equal(3, veryHighProp.GetInt32());
    }

    [Fact]
    public void SimilarFilesAnalysis_SerializesWithExactPropertyNames()
    {
        // Arrange
        var analysis = new SimilarFilesAnalysis
        {
            patterns = new List<string> { "Found 25 similar files", "3 files are very similar" },
            topTerms = new List<string> { "user", "service", "method", "class" },
            directoryPatterns = new List<DirectoryPattern>
            {
                new() { directory = "src/Services", count = 15, avgScore = 0.85 }
            },
            extensionDistribution = new Dictionary<string, int> { { ".cs", 20 }, { ".ts", 5 } }
        };

        // Act
        var json = JsonSerializer.Serialize(analysis);
        var parsed = JsonDocument.Parse(json);

        // Assert - exact property names
        Assert.True(parsed.RootElement.TryGetProperty("patterns", out var patternsProp));
        Assert.Equal(2, patternsProp.GetArrayLength());
        Assert.True(parsed.RootElement.TryGetProperty("topTerms", out var termsProp));
        Assert.Equal(4, termsProp.GetArrayLength());
        Assert.True(parsed.RootElement.TryGetProperty("directoryPatterns", out var dirPatternsProp));
        Assert.Equal(1, dirPatternsProp.GetArrayLength());
        Assert.True(parsed.RootElement.TryGetProperty("extensionDistribution", out var extProp));
        Assert.True(extProp.TryGetProperty(".cs", out var csProp));
        Assert.Equal(20, csProp.GetInt32());
    }

    [Fact]
    public void SimilarFilesResultItem_SerializesWithExactPropertyNames()
    {
        // Arrange
        var resultItem = new SimilarFilesResultItem
        {
            file = "AccountService.cs",
            path = "src/Services/AccountService.cs",
            score = 0.873,
            similarity = "very similar",
            size = 4250,
            sizeFormatted = "4.15 KB",
            matchingTerms = 3
        };

        // Act
        var json = JsonSerializer.Serialize(resultItem);
        var parsed = JsonDocument.Parse(json);

        // Assert - exact property names
        Assert.True(parsed.RootElement.TryGetProperty("file", out var fileProp));
        Assert.Equal("AccountService.cs", fileProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("path", out var pathProp));
        Assert.Equal("src/Services/AccountService.cs", pathProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("score", out var scoreProp));
        Assert.Equal(0.873, scoreProp.GetDouble());
        Assert.True(parsed.RootElement.TryGetProperty("similarity", out var simProp));
        Assert.Equal("very similar", simProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("size", out var sizeProp));
        Assert.Equal(4250, sizeProp.GetInt64());
        Assert.True(parsed.RootElement.TryGetProperty("sizeFormatted", out var sizeFormattedProp));
        Assert.Equal("4.15 KB", sizeFormattedProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("matchingTerms", out var termsProp));
        Assert.Equal(3, termsProp.GetInt32());
    }

    [Fact]
    public void SimilarFilesResultsSummary_SerializesWithExactPropertyNames()
    {
        // Arrange
        var summary = new SimilarFilesResultsSummary
        {
            included = 10,
            total = 25,
            hasMore = true
        };

        // Act
        var json = JsonSerializer.Serialize(summary);
        var parsed = JsonDocument.Parse(json);

        // Assert - exact property names
        Assert.True(parsed.RootElement.TryGetProperty("included", out var includedProp));
        Assert.Equal(10, includedProp.GetInt32());
        Assert.True(parsed.RootElement.TryGetProperty("total", out var totalProp));
        Assert.Equal(25, totalProp.GetInt32());
        Assert.True(parsed.RootElement.TryGetProperty("hasMore", out var hasMoreProp));
        Assert.True(hasMoreProp.GetBoolean());
    }

    [Fact]
    public void SimilarFilesAction_SerializesWithExactPropertyNames()
    {
        // Arrange
        var action = new SimilarFilesAction
        {
            id = "compare_files",
            description = "Compare with most similar file",
            command = "diff",
            parameters = new Dictionary<string, object> 
            { 
                { "file1", "src/UserService.cs" }, 
                { "file2", "src/AccountService.cs" } 
            },
            estimatedTokens = 1500,
            priority = "recommended"
        };

        // Act
        var json = JsonSerializer.Serialize(action);
        var parsed = JsonDocument.Parse(json);

        // Assert - exact property names
        Assert.True(parsed.RootElement.TryGetProperty("id", out var idProp));
        Assert.Equal("compare_files", idProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("description", out var descProp));
        Assert.Equal("Compare with most similar file", descProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("command", out var cmdProp));
        Assert.Equal("diff", cmdProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("parameters", out var paramsProp));
        Assert.True(paramsProp.TryGetProperty("file1", out var file1Prop));
        Assert.Equal("src/UserService.cs", file1Prop.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("estimatedTokens", out var tokensProp));
        Assert.Equal(1500, tokensProp.GetInt32());
        Assert.True(parsed.RootElement.TryGetProperty("priority", out var priorityProp));
        Assert.Equal("recommended", priorityProp.GetString());
    }

    [Fact]
    public void SimilarFilesMeta_SerializesWithExactPropertyNames()
    {
        // Arrange
        var meta = new SimilarFilesMeta
        {
            mode = "summary",
            truncated = true,
            tokens = 850,
            format = "ai-optimized",
            algorithm = "more-like-this"
        };

        // Act
        var json = JsonSerializer.Serialize(meta);
        var parsed = JsonDocument.Parse(json);

        // Assert - exact property names
        Assert.True(parsed.RootElement.TryGetProperty("mode", out var modeProp));
        Assert.Equal("summary", modeProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("truncated", out var truncProp));
        Assert.True(truncProp.GetBoolean());
        Assert.True(parsed.RootElement.TryGetProperty("tokens", out var tokensProp));
        Assert.Equal(850, tokensProp.GetInt32());
        Assert.True(parsed.RootElement.TryGetProperty("format", out var formatProp));
        Assert.Equal("ai-optimized", formatProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("algorithm", out var algProp));
        Assert.Equal("more-like-this", algProp.GetString());
    }

    [Fact]
    public void SimilarFilesResponse_IntegrationTest_SerializesWithExactStructure()
    {
        // Arrange
        var response = new SimilarFilesResponse
        {
            success = true,
            operation = "similar_files",
            source = new SimilarFilesSource
            {
                file = "UserService.cs",
                path = "src/Services/UserService.cs",
                size = 5432,
                sizeFormatted = "5.31 KB"
            },
            summary = new SimilarFilesSummary
            {
                totalFound = 25,
                searchTime = "15.2ms",
                avgSimilarity = 0.72,
                similarityDistribution = new SimilarityRanges
                {
                    veryHigh = 3,
                    high = 8,
                    moderate = 10,
                    low = 4
                }
            },
            analysis = new SimilarFilesAnalysis
            {
                patterns = new List<string> { "Found 25 similar files" },
                topTerms = new List<string> { "user", "service" },
                directoryPatterns = new List<DirectoryPattern>
                {
                    new() { directory = "src/Services", count = 15, avgScore = 0.85 }
                },
                extensionDistribution = new Dictionary<string, int> { { ".cs", 20 } }
            },
            results = new List<SimilarFilesResultItem>
            {
                new()
                {
                    file = "AccountService.cs",
                    path = "src/Services/AccountService.cs",
                    score = 0.873,
                    similarity = "very similar",
                    size = 4250,
                    sizeFormatted = "4.15 KB",
                    matchingTerms = 2
                }
            },
            resultsSummary = new SimilarFilesResultsSummary
            {
                included = 1,
                total = 25,
                hasMore = true
            },
            insights = new List<string> { "Found 25 similar files in 15.2ms" },
            actions = new List<object>
            {
                new SimilarFilesAction
                {
                    id = "compare_files",
                    description = "Compare with most similar file",
                    command = "diff",
                    parameters = new Dictionary<string, object> { { "file1", "src/UserService.cs" } },
                    estimatedTokens = 1500,
                    priority = "recommended"
                }
            },
            meta = new SimilarFilesMeta
            {
                mode = "summary",
                truncated = true,
                tokens = 850,
                format = "ai-optimized",
                algorithm = "more-like-this"
            }
        };

        // Act
        var json = JsonSerializer.Serialize(response);
        var parsed = JsonDocument.Parse(json);

        // Assert - verify complete structure with exact property names
        Assert.True(parsed.RootElement.TryGetProperty("success", out var successProp));
        Assert.True(successProp.GetBoolean());
        Assert.True(parsed.RootElement.TryGetProperty("operation", out var opProp));
        Assert.Equal("similar_files", opProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("source", out var sourceProp));
        Assert.True(sourceProp.TryGetProperty("file", out var fileProp));
        Assert.Equal("UserService.cs", fileProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("summary", out var summaryProp));
        Assert.True(summaryProp.TryGetProperty("totalFound", out var totalProp));
        Assert.Equal(25, totalProp.GetInt32());
        Assert.True(parsed.RootElement.TryGetProperty("analysis", out var analysisProp));
        Assert.True(analysisProp.TryGetProperty("patterns", out var patternsProp));
        Assert.Equal(1, patternsProp.GetArrayLength());
        Assert.True(parsed.RootElement.TryGetProperty("results", out var resultsProp));
        Assert.Equal(1, resultsProp.GetArrayLength());
        Assert.True(parsed.RootElement.TryGetProperty("resultsSummary", out var resSummaryProp));
        Assert.True(resSummaryProp.TryGetProperty("included", out var includedProp));
        Assert.Equal(1, includedProp.GetInt32());
        Assert.True(parsed.RootElement.TryGetProperty("insights", out var insightsProp));
        Assert.Equal(1, insightsProp.GetArrayLength());
        Assert.True(parsed.RootElement.TryGetProperty("actions", out var actionsProp));
        Assert.Equal(1, actionsProp.GetArrayLength());
        Assert.True(parsed.RootElement.TryGetProperty("meta", out var metaProp));
        Assert.True(metaProp.TryGetProperty("algorithm", out var algProp));
        Assert.Equal("more-like-this", algProp.GetString());
    }
}