using COA.CodeSearch.Contracts.Responses.FileSizeAnalysis;
using System.Collections.Generic;
using System.Text.Json;
using Xunit;

namespace COA.CodeSearch.McpServer.Tests.Services.ResponseBuilders;

public class FileSizeAnalysisResponseBuilderTests
{
    [Fact]
    public void ExtensionGroupValue_SerializesWithExactPropertyNames()
    {
        // Arrange
        var value = new ExtensionGroupValue
        {
            count = 42,
            totalSize = 1048576
        };

        // Act
        var json = JsonSerializer.Serialize(value);
        var parsed = JsonDocument.Parse(json);

        // Assert - exact property names
        Assert.True(parsed.RootElement.TryGetProperty("count", out var countProp));
        Assert.Equal(42, countProp.GetInt32());
        Assert.True(parsed.RootElement.TryGetProperty("totalSize", out var sizeProp));
        Assert.Equal(1048576, sizeProp.GetInt64());
    }

    [Fact]
    public void FileSizeDirectoryGroup_SerializesWithExactPropertyNames()
    {
        // Arrange
        var group = new FileSizeDirectoryGroup
        {
            directory = "src/Services",
            fileCount = 15,
            totalSize = 2048000,
            avgSize = 136533.33
        };

        // Act
        var json = JsonSerializer.Serialize(group);
        var parsed = JsonDocument.Parse(json);

        // Assert - exact property names
        Assert.True(parsed.RootElement.TryGetProperty("directory", out var dirProp));
        Assert.Equal("src/Services", dirProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("fileCount", out var countProp));
        Assert.Equal(15, countProp.GetInt32());
        Assert.True(parsed.RootElement.TryGetProperty("totalSize", out var sizeProp));
        Assert.Equal(2048000, sizeProp.GetInt64());
        Assert.True(parsed.RootElement.TryGetProperty("avgSize", out var avgProp));
        Assert.Equal(136533.33, avgProp.GetDouble());
    }

    [Fact]
    public void FileSizeQuery_SerializesWithExactPropertyNames()
    {
        // Arrange
        var query = new FileSizeQuery
        {
            mode = "largest",
            workspace = "MyProject"
        };

        // Act
        var json = JsonSerializer.Serialize(query);
        var parsed = JsonDocument.Parse(json);

        // Assert - exact property names
        Assert.True(parsed.RootElement.TryGetProperty("mode", out var modeProp));
        Assert.Equal("largest", modeProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("workspace", out var workspaceProp));
        Assert.Equal("MyProject", workspaceProp.GetString());
    }

    [Fact]
    public void FileSizeSummary_SerializesWithExactPropertyNames()
    {
        // Arrange
        var summary = new FileSizeSummary
        {
            totalFiles = 156,
            searchTime = "25.3ms",
            totalSize = 52428800,
            totalSizeFormatted = "50 MB",
            avgSize = "336.7 KB",
            medianSize = "125.5 KB"
        };

        // Act
        var json = JsonSerializer.Serialize(summary);
        var parsed = JsonDocument.Parse(json);

        // Assert - exact property names
        Assert.True(parsed.RootElement.TryGetProperty("totalFiles", out var totalProp));
        Assert.Equal(156, totalProp.GetInt32());
        Assert.True(parsed.RootElement.TryGetProperty("searchTime", out var timeProp));
        Assert.Equal("25.3ms", timeProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("totalSize", out var sizeProp));
        Assert.Equal(52428800, sizeProp.GetInt64());
        Assert.True(parsed.RootElement.TryGetProperty("totalSizeFormatted", out var formattedProp));
        Assert.Equal("50 MB", formattedProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("avgSize", out var avgProp));
        Assert.Equal("336.7 KB", avgProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("medianSize", out var medianProp));
        Assert.Equal("125.5 KB", medianProp.GetString());
    }

    [Fact]
    public void SizeDistribution_SerializesWithExactPropertyNames()
    {
        // Arrange
        var distribution = new SizeDistribution
        {
            tiny = 45,
            small = 68,
            medium = 32,
            large = 8,
            huge = 3
        };

        // Act
        var json = JsonSerializer.Serialize(distribution);
        var parsed = JsonDocument.Parse(json);

        // Assert - exact property names
        Assert.True(parsed.RootElement.TryGetProperty("tiny", out var tinyProp));
        Assert.Equal(45, tinyProp.GetInt32());
        Assert.True(parsed.RootElement.TryGetProperty("small", out var smallProp));
        Assert.Equal(68, smallProp.GetInt32());
        Assert.True(parsed.RootElement.TryGetProperty("medium", out var mediumProp));
        Assert.Equal(32, mediumProp.GetInt32());
        Assert.True(parsed.RootElement.TryGetProperty("large", out var largeProp));
        Assert.Equal(8, largeProp.GetInt32());
        Assert.True(parsed.RootElement.TryGetProperty("huge", out var hugeProp));
        Assert.Equal(3, hugeProp.GetInt32());
    }

    [Fact]
    public void FileSizeStatisticsInfo_SerializesWithExactPropertyNames()
    {
        // Arrange
        var stats = new FileSizeStatisticsInfo
        {
            min = "1 B",
            max = "15.2 MB",
            mean = "336.7 KB",
            median = "125.5 KB",
            stdDev = "892.4 KB",
            distribution = new SizeDistribution
            {
                tiny = 45,
                small = 68,
                medium = 32,
                large = 8,
                huge = 3
            }
        };

        // Act
        var json = JsonSerializer.Serialize(stats);
        var parsed = JsonDocument.Parse(json);

        // Assert - exact property names
        Assert.True(parsed.RootElement.TryGetProperty("min", out var minProp));
        Assert.Equal("1 B", minProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("max", out var maxProp));
        Assert.Equal("15.2 MB", maxProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("mean", out var meanProp));
        Assert.Equal("336.7 KB", meanProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("median", out var medianProp));
        Assert.Equal("125.5 KB", medianProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("stdDev", out var stdDevProp));
        Assert.Equal("892.4 KB", stdDevProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("distribution", out var distProp));
        Assert.True(distProp.TryGetProperty("tiny", out var tinyProp));
        Assert.Equal(45, tinyProp.GetInt32());
    }

    [Fact]
    public void HotspotDirectory_SerializesWithExactPropertyNames()
    {
        // Arrange
        var hotspot = new HotspotDirectory
        {
            path = "src/Services",
            files = 15,
            totalSize = "2 MB",
            avgSize = "136.5 KB"
        };

        // Act
        var json = JsonSerializer.Serialize(hotspot);
        var parsed = JsonDocument.Parse(json);

        // Assert - exact property names
        Assert.True(parsed.RootElement.TryGetProperty("path", out var pathProp));
        Assert.Equal("src/Services", pathProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("files", out var filesProp));
        Assert.Equal(15, filesProp.GetInt32());
        Assert.True(parsed.RootElement.TryGetProperty("totalSize", out var sizeProp));
        Assert.Equal("2 MB", sizeProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("avgSize", out var avgProp));
        Assert.Equal("136.5 KB", avgProp.GetString());
    }

    [Fact]
    public void HotspotExtension_SerializesWithExactPropertyNames()
    {
        // Arrange
        var hotspot = new HotspotExtension
        {
            extension = ".cs",
            count = 42,
            totalSize = "15.2 MB"
        };

        // Act
        var json = JsonSerializer.Serialize(hotspot);
        var parsed = JsonDocument.Parse(json);

        // Assert - exact property names
        Assert.True(parsed.RootElement.TryGetProperty("extension", out var extProp));
        Assert.Equal(".cs", extProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("count", out var countProp));
        Assert.Equal(42, countProp.GetInt32());
        Assert.True(parsed.RootElement.TryGetProperty("totalSize", out var sizeProp));
        Assert.Equal("15.2 MB", sizeProp.GetString());
    }

    [Fact]
    public void FileSizeResultItem_SerializesWithExactPropertyNames()
    {
        // Arrange
        var item = new FileSizeResultItem
        {
            file = "LargeData.json",
            path = "src/Data/LargeData.json",
            size = 15728640,
            sizeFormatted = "15 MB",
            extension = ".json",
            percentOfTotal = 25.68
        };

        // Act
        var json = JsonSerializer.Serialize(item);
        var parsed = JsonDocument.Parse(json);

        // Assert - exact property names
        Assert.True(parsed.RootElement.TryGetProperty("file", out var fileProp));
        Assert.Equal("LargeData.json", fileProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("path", out var pathProp));
        Assert.Equal("src/Data/LargeData.json", pathProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("size", out var sizeProp));
        Assert.Equal(15728640, sizeProp.GetInt64());
        Assert.True(parsed.RootElement.TryGetProperty("sizeFormatted", out var formattedProp));
        Assert.Equal("15 MB", formattedProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("extension", out var extProp));
        Assert.Equal(".json", extProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("percentOfTotal", out var percentProp));
        Assert.Equal(25.68, percentProp.GetDouble());
    }

    [Fact]
    public void SizeOutlier_SerializesWithExactPropertyNames()
    {
        // Arrange
        var outlier = new SizeOutlier
        {
            file = "HugeFile.data",
            path = "temp/HugeFile.data",
            size = "500 MB",
            zScore = 5.23
        };

        // Act
        var json = JsonSerializer.Serialize(outlier);
        var parsed = JsonDocument.Parse(json);

        // Assert - exact property names
        Assert.True(parsed.RootElement.TryGetProperty("file", out var fileProp));
        Assert.Equal("HugeFile.data", fileProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("path", out var pathProp));
        Assert.Equal("temp/HugeFile.data", pathProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("size", out var sizeProp));
        Assert.Equal("500 MB", sizeProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("zScore", out var zScoreProp));
        Assert.Equal(5.23, zScoreProp.GetDouble());
    }

    [Fact]
    public void FileSizeAnalysisResponse_IntegrationTest_SerializesWithExactStructure()
    {
        // Arrange
        var response = new FileSizeAnalysisResponse
        {
            success = true,
            operation = "file_size_analysis",
            query = new FileSizeQuery
            {
                mode = "largest",
                workspace = "MyProject"
            },
            summary = new FileSizeSummary
            {
                totalFiles = 156,
                searchTime = "25.3ms",
                totalSize = 52428800,
                totalSizeFormatted = "50 MB",
                avgSize = "336.7 KB",
                medianSize = "125.5 KB"
            },
            statistics = new FileSizeStatisticsInfo
            {
                min = "1 B",
                max = "15.2 MB",
                mean = "336.7 KB",
                median = "125.5 KB",
                stdDev = "892.4 KB",
                distribution = new SizeDistribution
                {
                    tiny = 45,
                    small = 68,
                    medium = 32,
                    large = 8,
                    huge = 3
                }
            },
            analysis = new FileSizeAnalysis
            {
                patterns = new List<string> { "Analyzed 156 files", "3 files exceed 50MB" },
                outliers = new List<object>
                {
                    new SizeOutlier
                    {
                        file = "HugeFile.data",
                        path = "temp/HugeFile.data",
                        size = "500 MB",
                        zScore = 5.23
                    }
                },
                hotspots = new FileSizeHotspots
                {
                    byDirectory = new List<HotspotDirectory>
                    {
                        new()
                        {
                            path = "src/Services",
                            files = 15,
                            totalSize = "2 MB",
                            avgSize = "136.5 KB"
                        }
                    },
                    byExtension = new List<HotspotExtension>
                    {
                        new()
                        {
                            extension = ".cs",
                            count = 42,
                            totalSize = "15.2 MB"
                        }
                    }
                }
            },
            results = new List<FileSizeResultItem>
            {
                new()
                {
                    file = "LargeData.json",
                    path = "src/Data/LargeData.json",
                    size = 15728640,
                    sizeFormatted = "15 MB",
                    extension = ".json",
                    percentOfTotal = 25.68
                }
            },
            resultsSummary = new FileSizeResultsSummary
            {
                included = 1,
                total = 156,
                hasMore = true
            },
            insights = new List<string> { "Analyzed 156 files totaling 50 MB" },
            actions = new List<object>
            {
                new FileSizeAction
                {
                    id = "analyze_largest",
                    description = "Analyze largest file content",
                    command = "file_analysis",
                    parameters = new Dictionary<string, object> { { "file_path", "/src/Data/LargeData.json" } },
                    estimatedTokens = 1500,
                    priority = "recommended"
                }
            },
            meta = new FileSizeMeta
            {
                mode = "summary",
                analysisMode = "largest",
                truncated = true,
                tokens = 1250,
                format = "ai-optimized"
            }
        };

        // Act
        var json = JsonSerializer.Serialize(response);
        var parsed = JsonDocument.Parse(json);

        // Assert - verify complete structure with exact property names
        Assert.True(parsed.RootElement.TryGetProperty("success", out var successProp));
        Assert.True(successProp.GetBoolean());
        Assert.True(parsed.RootElement.TryGetProperty("operation", out var opProp));
        Assert.Equal("file_size_analysis", opProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("query", out var queryProp));
        Assert.True(queryProp.TryGetProperty("mode", out var modeProp));
        Assert.Equal("largest", modeProp.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("summary", out var summaryProp));
        Assert.True(summaryProp.TryGetProperty("totalFiles", out var totalProp));
        Assert.Equal(156, totalProp.GetInt32());
        Assert.True(parsed.RootElement.TryGetProperty("statistics", out var statsProp));
        Assert.True(statsProp.TryGetProperty("distribution", out var distProp));
        Assert.True(distProp.TryGetProperty("tiny", out var tinyProp));
        Assert.Equal(45, tinyProp.GetInt32());
        Assert.True(parsed.RootElement.TryGetProperty("analysis", out var analysisProp));
        Assert.True(analysisProp.TryGetProperty("hotspots", out var hotspotsProp));
        Assert.True(hotspotsProp.TryGetProperty("byDirectory", out var dirProp));
        Assert.Equal(1, dirProp.GetArrayLength());
        Assert.True(parsed.RootElement.TryGetProperty("results", out var resultsProp));
        Assert.Equal(1, resultsProp.GetArrayLength());
        Assert.True(parsed.RootElement.TryGetProperty("resultsSummary", out var resSummaryProp));
        Assert.True(resSummaryProp.TryGetProperty("included", out var includedProp));
        Assert.Equal(1, includedProp.GetInt32());
        Assert.True(parsed.RootElement.TryGetProperty("meta", out var metaProp));
        Assert.True(metaProp.TryGetProperty("analysisMode", out var analysisModeoProp));
        Assert.Equal("largest", analysisModeoProp.GetString());
    }
}