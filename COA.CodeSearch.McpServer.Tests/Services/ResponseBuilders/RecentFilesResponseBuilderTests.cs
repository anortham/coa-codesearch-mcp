using COA.CodeSearch.Contracts.Responses.RecentFiles;
using COA.CodeSearch.McpServer.Services.ResponseBuilders;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using Xunit;

namespace COA.CodeSearch.McpServer.Tests.Services.ResponseBuilders;

/// <summary>
/// Tests to verify JSON compatibility during anonymous type replacement
/// Following TECHNICAL_DEBT_REMEDIATION_PLAN.md requirements
/// </summary>
public class RecentFilesResponseBuilderTests
{
    private readonly RecentFilesResponseBuilder _builder;

    public RecentFilesResponseBuilderTests()
    {
        var logger = new Mock<ILogger<RecentFilesResponseBuilder>>();
        _builder = new RecentFilesResponseBuilder(logger.Object);
    }

    [Fact]
    public void RecentFilesQuery_SerializesCorrectly()
    {
        // Arrange - following plan example from lines 177-194
        var query = new RecentFilesQuery
        {
            timeFrame = "24h",
            cutoff = "2025-07-31 12:00:00",
            workspace = "MyProject"
        };
        
        // Act
        var json = JsonSerializer.Serialize(query);
        
        // Assert - verify exact JSON structure
        Assert.Contains("\"timeFrame\":\"24h\"", json);
        Assert.Contains("\"cutoff\":\"2025-07-31 12:00:00\"", json);
        Assert.Contains("\"workspace\":\"MyProject\"", json);
        
        // Verify property casing is exact (lowercase as in anonymous type)
        Assert.DoesNotContain("TimeFrame", json); // Should not be PascalCase
        Assert.DoesNotContain("Cutoff", json);    // Should not be PascalCase
        Assert.DoesNotContain("Workspace", json); // Should not be PascalCase
    }

    [Fact]
    public void RecentFilesQuery_DeserializesCorrectly()
    {
        // Arrange - JSON that would come from original anonymous type
        var json = """{"timeFrame":"24h","cutoff":"2025-07-31 12:00:00","workspace":"MyProject"}""";
        
        // Act
        var query = JsonSerializer.Deserialize<RecentFilesQuery>(json);
        
        // Assert
        Assert.NotNull(query);
        Assert.Equal("24h", query.timeFrame);
        Assert.Equal("2025-07-31 12:00:00", query.cutoff);
        Assert.Equal("MyProject", query.workspace);
    }

    [Fact]
    public void TimeBuckets_SerializesCorrectly()
    {
        // Arrange - following plan requirements for exact property matching
        var timeBuckets = new TimeBuckets
        {
            lastHour = 5,
            last24Hours = 25,
            lastWeek = 45,
            older = 10
        };
        
        // Act
        var json = JsonSerializer.Serialize(timeBuckets);
        
        // Assert - verify exact JSON structure matches anonymous type (line 42-48)
        Assert.Contains("\"lastHour\":5", json);
        Assert.Contains("\"last24Hours\":25", json);
        Assert.Contains("\"lastWeek\":45", json);
        Assert.Contains("\"older\":10", json);
        
        // Verify property casing is exact (camelCase as in anonymous type)
        Assert.DoesNotContain("LastHour", json);     // Should not be PascalCase
        Assert.DoesNotContain("Last24Hours", json);  // Should not be PascalCase
        Assert.DoesNotContain("LastWeek", json);     // Should not be PascalCase
        Assert.DoesNotContain("Older", json);        // Should not be PascalCase
    }

    [Fact]
    public void TimeBuckets_DeserializesCorrectly()
    {
        // Arrange - JSON that would come from original anonymous type
        var json = """{"lastHour":5,"last24Hours":25,"lastWeek":45,"older":10}""";
        
        // Act
        var timeBuckets = JsonSerializer.Deserialize<TimeBuckets>(json);
        
        // Assert
        Assert.NotNull(timeBuckets);
        Assert.Equal(5, timeBuckets.lastHour);
        Assert.Equal(25, timeBuckets.last24Hours);
        Assert.Equal(45, timeBuckets.lastWeek);
        Assert.Equal(10, timeBuckets.older);
    }

    [Fact]
    public void DirectoryGroup_SerializesCorrectly()
    {
        // Arrange - following plan requirements for exact property matching (lines 55-61)
        var directoryGroup = new DirectoryGroup
        {
            directory = "src/components",
            fileCount = 12,
            totalSize = 45678,
            mostRecent = DateTime.Parse("2025-07-31T23:30:00")
        };
        
        // Act
        var json = JsonSerializer.Serialize(directoryGroup);
        
        // Assert - verify exact JSON structure matches anonymous type
        Assert.Contains("\"directory\":\"src/components\"", json);
        Assert.Contains("\"fileCount\":12", json);
        Assert.Contains("\"totalSize\":45678", json);
        Assert.Contains("\"mostRecent\":\"2025-07-31T23:30:00\"", json);
        
        // Verify property casing is exact (camelCase as in anonymous type)
        Assert.DoesNotContain("Directory", json);    // Should not be PascalCase
        Assert.DoesNotContain("FileCount", json);    // Should not be PascalCase
        Assert.DoesNotContain("TotalSize", json);    // Should not be PascalCase
        Assert.DoesNotContain("MostRecent", json);   // Should not be PascalCase
    }

    [Fact]
    public void DirectoryGroup_DeserializesCorrectly()
    {
        // Arrange - JSON that would come from original anonymous type
        var json = """{"directory":"src/components","fileCount":12,"totalSize":45678,"mostRecent":"2025-07-31T23:30:00"}""";
        
        // Act
        var directoryGroup = JsonSerializer.Deserialize<DirectoryGroup>(json);
        
        // Assert
        Assert.NotNull(directoryGroup);
        Assert.Equal("src/components", directoryGroup.directory);
        Assert.Equal(12, directoryGroup.fileCount);
        Assert.Equal(45678, directoryGroup.totalSize);
        Assert.Equal(DateTime.Parse("2025-07-31T23:30:00"), directoryGroup.mostRecent);
    }

    [Fact]
    public void RecentFilesSummary_SerializesCorrectly()
    {
        // Arrange - following plan requirements for exact property matching (lines 107-119)
        var summary = new RecentFilesSummary
        {
            totalFound = 25,
            searchTime = "15.3ms",
            totalSize = 1234567,
            totalSizeFormatted = "1.18 MB",
            avgFileSize = "47.5 KB",
            distribution = new RecentFilesDistribution
            {
                byTime = new TimeBuckets
                {
                    lastHour = 5,
                    last24Hours = 20,
                    lastWeek = 25,
                    older = 0
                },
                byExtension = new Dictionary<string, int> { { ".cs", 20 }, { ".json", 5 } }
            }
        };
        
        // Act
        var json = JsonSerializer.Serialize(summary);
        
        // Assert - verify exact JSON structure matches anonymous type
        Assert.Contains("\"totalFound\":25", json);
        Assert.Contains("\"searchTime\":\"15.3ms\"", json);
        Assert.Contains("\"totalSize\":1234567", json);
        Assert.Contains("\"totalSizeFormatted\":\"1.18 MB\"", json);
        Assert.Contains("\"avgFileSize\":\"47.5 KB\"", json);
        Assert.Contains("\"distribution\":", json);
        Assert.Contains("\"byTime\":", json);
        Assert.Contains("\"byExtension\":", json);
        
        // Verify property casing is exact (camelCase as in anonymous type)
        Assert.DoesNotContain("TotalFound", json);        // Should not be PascalCase
        Assert.DoesNotContain("SearchTime", json);        // Should not be PascalCase
        Assert.DoesNotContain("TotalSize", json);         // Should not be PascalCase
        Assert.DoesNotContain("AvgFileSize", json);       // Should not be PascalCase
        Assert.DoesNotContain("Distribution", json);      // Should not be PascalCase
    }

    [Fact]
    public void RecentFilesSummary_DeserializesCorrectly()
    {
        // Arrange - JSON that would come from original anonymous type
        var json = """{"totalFound":25,"searchTime":"15.3ms","totalSize":1234567,"totalSizeFormatted":"1.18 MB","avgFileSize":"47.5 KB","distribution":{"byTime":{"lastHour":5,"last24Hours":20,"lastWeek":25,"older":0},"byExtension":{".cs":20,".json":5}}}""";
        
        // Act
        var summary = JsonSerializer.Deserialize<RecentFilesSummary>(json);
        
        // Assert
        Assert.NotNull(summary);
        Assert.Equal(25, summary.totalFound);
        Assert.Equal("15.3ms", summary.searchTime);
        Assert.Equal(1234567, summary.totalSize);
        Assert.Equal("1.18 MB", summary.totalSizeFormatted);
        Assert.Equal("47.5 KB", summary.avgFileSize);
        Assert.NotNull(summary.distribution);
        Assert.Equal(5, summary.distribution.byTime.lastHour);
        Assert.Equal(20, summary.distribution.byExtension[".cs"]);
    }

    [Fact]
    public void RecentFilesAnalysis_SerializesCorrectly()
    {
        // Arrange - following plan requirements for exact property matching (lines 120-134)
        var analysis = new RecentFilesAnalysis
        {
            patterns = new List<string> { "High activity detected", "Multiple file types", "Recent burst" },
            hotspots = new RecentFilesHotspots
            {
                directories = new List<HotspotDirectory>
                {
                    new HotspotDirectory
                    {
                        path = "src/components",
                        files = 12,
                        size = "45.2 KB",
                        lastModified = "2 minutes ago"
                    }
                }
            },
            activityPattern = new ModificationPatterns
            {
                burstActivity = true,
                workingHours = true,
                peakHour = 14
            }
        };
        
        // Act
        var json = JsonSerializer.Serialize(analysis);
        
        // Assert - verify exact JSON structure matches anonymous type
        Assert.Contains("\"patterns\":[", json);
        Assert.Contains("\"hotspots\":", json);
        Assert.Contains("\"directories\":[", json);
        Assert.Contains("\"activityPattern\":", json);
        Assert.Contains("\"burstActivity\":true", json);
        Assert.Contains("\"workingHours\":true", json);
        Assert.Contains("\"peakHour\":14", json);
        
        // Verify property casing is exact (camelCase as in anonymous type)
        Assert.DoesNotContain("Patterns", json);         // Should not be PascalCase
        Assert.DoesNotContain("Hotspots", json);         // Should not be PascalCase
        Assert.DoesNotContain("ActivityPattern", json);  // Should not be PascalCase
    }

    [Fact]
    public void RecentFilesAnalysis_DeserializesCorrectly()
    {
        // Arrange - JSON that would come from original anonymous type
        var json = """{"patterns":["High activity","Multiple types"],"hotspots":{"directories":[{"path":"src/components","files":12,"size":"45.2 KB","lastModified":"2 minutes ago"}]},"activityPattern":{"burstActivity":true,"workingHours":true,"peakHour":14}}""";
        
        // Act
        var analysis = JsonSerializer.Deserialize<RecentFilesAnalysis>(json);
        
        // Assert
        Assert.NotNull(analysis);
        Assert.Equal(2, analysis.patterns.Count);
        Assert.Equal("High activity", analysis.patterns[0]);
        Assert.NotNull(analysis.hotspots);
        Assert.Single(analysis.hotspots.directories);
        Assert.Equal("src/components", analysis.hotspots.directories[0].path);
        Assert.Equal(12, analysis.hotspots.directories[0].files);
        Assert.NotNull(analysis.activityPattern);
        Assert.True(analysis.activityPattern.burstActivity);
        Assert.Equal(14, analysis.activityPattern.peakHour);
    }

    [Fact]
    public void RecentFileResultItem_SerializesCorrectly()
    {
        // Arrange - following plan requirements for exact property matching (lines 135-144)
        var resultItem = new RecentFileResultItem
        {
            file = "UserService.cs",
            path = "src/services/UserService.cs",
            modified = "2025-08-01 00:10:30",
            modifiedAgo = "5 minutes ago",
            size = 15234,
            sizeFormatted = "14.9 KB",
            extension = ".cs"
        };
        
        // Act
        var json = JsonSerializer.Serialize(resultItem);
        
        // Assert - verify exact JSON structure matches anonymous type
        Assert.Contains("\"file\":\"UserService.cs\"", json);
        Assert.Contains("\"path\":\"src/services/UserService.cs\"", json);
        Assert.Contains("\"modified\":\"2025-08-01 00:10:30\"", json);
        Assert.Contains("\"modifiedAgo\":\"5 minutes ago\"", json);
        Assert.Contains("\"size\":15234", json);
        Assert.Contains("\"sizeFormatted\":\"14.9 KB\"", json);
        Assert.Contains("\"extension\":\".cs\"", json);
        
        // Verify property casing is exact (camelCase as in anonymous type)
        Assert.DoesNotContain("File", json);           // Should not be PascalCase
        Assert.DoesNotContain("Path", json);           // Should not be PascalCase
        Assert.DoesNotContain("Modified", json);       // Should not be PascalCase
        Assert.DoesNotContain("ModifiedAgo", json);    // Should not be PascalCase
        Assert.DoesNotContain("Size", json);           // Should not be PascalCase
        Assert.DoesNotContain("SizeFormatted", json);  // Should not be PascalCase
        Assert.DoesNotContain("Extension", json);      // Should not be PascalCase
    }

    [Fact]
    public void RecentFileResultItem_DeserializesCorrectly()
    {
        // Arrange - JSON that would come from original anonymous type
        var json = """{"file":"UserService.cs","path":"src/services/UserService.cs","modified":"2025-08-01 00:10:30","modifiedAgo":"5 minutes ago","size":15234,"sizeFormatted":"14.9 KB","extension":".cs"}""";
        
        // Act
        var resultItem = JsonSerializer.Deserialize<RecentFileResultItem>(json);
        
        // Assert
        Assert.NotNull(resultItem);
        Assert.Equal("UserService.cs", resultItem.file);
        Assert.Equal("src/services/UserService.cs", resultItem.path);
        Assert.Equal("2025-08-01 00:10:30", resultItem.modified);
        Assert.Equal("5 minutes ago", resultItem.modifiedAgo);
        Assert.Equal(15234, resultItem.size);
        Assert.Equal("14.9 KB", resultItem.sizeFormatted);
        Assert.Equal(".cs", resultItem.extension);
    }

    [Fact]
    public void ResultsSummary_SerializesCorrectly()
    {
        // Arrange - following plan requirements for exact property matching (lines 146-150)
        var resultsSummary = new ResultsSummary
        {
            included = 15,
            total = 25,
            hasMore = true
        };
        
        // Act
        var json = JsonSerializer.Serialize(resultsSummary);
        
        // Assert - verify exact JSON structure matches anonymous type
        Assert.Contains("\"included\":15", json);
        Assert.Contains("\"total\":25", json);
        Assert.Contains("\"hasMore\":true", json);
        
        // Verify property casing is exact (camelCase as in anonymous type)
        Assert.DoesNotContain("Included", json);    // Should not be PascalCase
        Assert.DoesNotContain("Total", json);       // Should not be PascalCase
        Assert.DoesNotContain("HasMore", json);     // Should not be PascalCase
    }

    [Fact] 
    public void ResultsSummary_DeserializesCorrectly()
    {
        // Arrange - JSON that would come from original anonymous type
        var json = """{"included":15,"total":25,"hasMore":true}""";
        
        // Act
        var resultsSummary = JsonSerializer.Deserialize<ResultsSummary>(json);
        
        // Assert
        Assert.NotNull(resultsSummary);
        Assert.Equal(15, resultsSummary.included);
        Assert.Equal(25, resultsSummary.total);
        Assert.True(resultsSummary.hasMore);
    }

    [Fact]
    public void ResponseMeta_SerializesCorrectly()
    {
        // Arrange - following plan requirements for exact property matching (lines 161-168)
        var responseMeta = new ResponseMeta
        {
            mode = "full",
            truncated = false,
            tokens = 1250,
            format = "ai-optimized",
            indexed = true
        };
        
        // Act
        var json = JsonSerializer.Serialize(responseMeta);
        
        // Assert - verify exact JSON structure matches anonymous type
        Assert.Contains("\"mode\":\"full\"", json);
        Assert.Contains("\"truncated\":false", json);
        Assert.Contains("\"tokens\":1250", json);
        Assert.Contains("\"format\":\"ai-optimized\"", json);
        Assert.Contains("\"indexed\":true", json);
        
        // Verify property casing is exact (camelCase as in anonymous type)
        Assert.DoesNotContain("Mode", json);        // Should not be PascalCase
        Assert.DoesNotContain("Truncated", json);   // Should not be PascalCase
        Assert.DoesNotContain("Tokens", json);      // Should not be PascalCase
        Assert.DoesNotContain("Format", json);      // Should not be PascalCase
        Assert.DoesNotContain("Indexed", json);     // Should not be PascalCase
    }

    [Fact]
    public void ResponseMeta_DeserializesCorrectly()
    {
        // Arrange - JSON that would come from original anonymous type
        var json = """{"mode":"full","truncated":false,"tokens":1250,"format":"ai-optimized","indexed":true}""";
        
        // Act
        var responseMeta = JsonSerializer.Deserialize<ResponseMeta>(json);
        
        // Assert
        Assert.NotNull(responseMeta);
        Assert.Equal("full", responseMeta.mode);
        Assert.False(responseMeta.truncated);
        Assert.Equal(1250, responseMeta.tokens);
        Assert.Equal("ai-optimized", responseMeta.format);
        Assert.True(responseMeta.indexed);
    }

    [Fact]
    public void ActionResult_SerializesCorrectly()
    {
        // Arrange - following plan requirements for exact property matching (lines 152-161)
        var actionResult = new ActionResult
        {
            id = "view_recent",
            description = "View most recently modified file",
            command = "read",
            parameters = new Dictionary<string, object>
            {
                ["file_path"] = "/path/to/file.cs",
                ["line_number"] = 42
            },
            estimatedTokens = 1000,
            priority = "medium"
        };
        
        // Act
        var json = JsonSerializer.Serialize(actionResult);
        
        // Assert - verify exact JSON structure matches anonymous type
        Assert.Contains("\"id\":\"view_recent\"", json);
        Assert.Contains("\"description\":\"View most recently modified file\"", json);
        Assert.Contains("\"command\":\"read\"", json);
        Assert.Contains("\"parameters\":", json);
        Assert.Contains("\"file_path\":\"/path/to/file.cs\"", json);
        Assert.Contains("\"estimatedTokens\":1000", json);
        Assert.Contains("\"priority\":\"medium\"", json);
        
        // Verify property casing is exact (camelCase as in anonymous type)
        Assert.DoesNotContain("Id", json);                // Should not be PascalCase
        Assert.DoesNotContain("Description", json);       // Should not be PascalCase
        Assert.DoesNotContain("Command", json);           // Should not be PascalCase
        Assert.DoesNotContain("Parameters", json);        // Should not be PascalCase
        Assert.DoesNotContain("EstimatedTokens", json);   // Should not be PascalCase
        Assert.DoesNotContain("Priority", json);          // Should not be PascalCase
    }

    [Fact]
    public void ActionResult_DeserializesCorrectly()
    {
        // Arrange - JSON that would come from original anonymous type
        var json = """{"id":"view_recent","description":"View most recently modified file","command":"read","parameters":{"file_path":"/path/to/file.cs","line_number":42},"estimatedTokens":1000,"priority":"medium"}""";
        
        // Act
        var actionResult = JsonSerializer.Deserialize<ActionResult>(json);
        
        // Assert
        Assert.NotNull(actionResult);
        Assert.Equal("view_recent", actionResult.id);
        Assert.Equal("View most recently modified file", actionResult.description);
        Assert.Equal("read", actionResult.command);
        Assert.NotNull(actionResult.parameters);
        Assert.Equal("/path/to/file.cs", actionResult.parameters["file_path"].ToString());
        Assert.Equal(1000, actionResult.estimatedTokens);
        Assert.Equal("medium", actionResult.priority);
    }

    [Fact]
    public void RecentFilesResponse_SerializesCorrectly()
    {
        // Arrange - Complete response structure test 
        var response = new RecentFilesResponse
        {
            success = true,
            operation = "recent_files",
            query = new RecentFilesQuery
            {
                timeFrame = "1h",
                cutoff = "2025-08-01 00:00:00",
                workspace = "TestProject"
            },
            summary = new RecentFilesSummary
            {
                totalFound = 5,
                searchTime = "10.5ms",
                totalSize = 50000,
                totalSizeFormatted = "48.8 KB",
                avgFileSize = "9.76 KB",
                distribution = new RecentFilesDistribution
                {
                    byTime = new TimeBuckets { lastHour = 5, last24Hours = 5, lastWeek = 5, older = 0 },
                    byExtension = new Dictionary<string, int> { { ".cs", 5 } }
                }
            },
            results = new List<RecentFileResultItem>(),
            resultsSummary = new ResultsSummary { included = 5, total = 5, hasMore = false },
            insights = new List<string> { "High activity detected" },
            actions = new List<object>(),
            meta = new ResponseMeta
            {
                mode = "full",
                truncated = false,
                tokens = 800,
                format = "ai-optimized",
                indexed = true
            }
        };
        
        // Act
        var json = JsonSerializer.Serialize(response);
        
        // Assert - verify complete structure
        Assert.Contains("\"success\":true", json);
        Assert.Contains("\"operation\":\"recent_files\"", json);
        Assert.Contains("\"query\":", json);
        Assert.Contains("\"summary\":", json);
        Assert.Contains("\"results\":", json);
        Assert.Contains("\"resultsSummary\":", json);
        Assert.Contains("\"insights\":", json);
        Assert.Contains("\"actions\":", json);
        Assert.Contains("\"meta\":", json);
    }
}