using COA.CodeSearch.McpServer.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace COA.CodeSearch.McpServer.Tests.Services;

public class ContextAwarenessServiceTests
{
    private readonly ContextAwarenessService _service;
    private readonly Mock<ILogger<ContextAwarenessService>> _mockLogger;
    private readonly ContextBoostOptions _options;
    
    public ContextAwarenessServiceTests()
    {
        _mockLogger = new Mock<ILogger<ContextAwarenessService>>();
        _options = new ContextBoostOptions();
        var optionsWrapper = Options.Create(_options);
        _service = new ContextAwarenessService(_mockLogger.Object, optionsWrapper);
    }
    
    [Theory]
    [InlineData("Controllers/AuthController.cs", new[] { "controllers", "auth", "controller" })]
    [InlineData("Services/UserService.cs", new[] { "services", "user", "service" })]
    [InlineData("Models/PaymentModel.cs", new[] { "models", "payment", "model" })]
    [InlineData("src/Authentication/LoginService.cs", new[] { "authentication", "login", "service" })]
    [InlineData("Tests/UserRepositoryTests.cs", new[] { "tests", "user", "repository" })]
    public void ExtractFileContextKeywords_VariousFilePaths_ReturnsExpectedKeywords(string filePath, string[] expectedKeywords)
    {
        // Act
        var result = _service.ExtractFileContextKeywords(filePath);
        
        // Assert
        foreach (var expectedKeyword in expectedKeywords)
        {
            Assert.Contains(expectedKeyword, result, StringComparer.OrdinalIgnoreCase);
        }
    }
    
    [Fact]
    public async Task TrackFileAccessAsync_MultipleFiles_MaintainsRecentFilesList()
    {
        // Arrange
        var files = new[]
        {
            "Controllers/AuthController.cs",
            "Services/UserService.cs",
            "Models/User.cs",
            "Controllers/UserController.cs"
        };
        
        // Act
        foreach (var file in files)
        {
            await _service.TrackFileAccessAsync(file);
        }
        
        var context = await _service.GetCurrentContextAsync();
        
        // Assert
        Assert.Equal(files.Length, context.RecentFiles.Length);
        Assert.Contains("Controllers/AuthController.cs", context.RecentFiles);
        Assert.Contains("Controllers/UserController.cs", context.RecentFiles);
    }
    
    [Fact]
    public async Task TrackSearchQueryAsync_MultipleQueries_MaintainsQueryHistory()
    {
        // Arrange
        var queries = new[]
        {
            ("auth", 5),
            ("user login", 3),
            ("payment", 7),
            ("security", 2)
        };
        
        // Act
        foreach (var (query, results) in queries)
        {
            await _service.TrackSearchQueryAsync(query, results);
        }
        
        var context = await _service.GetCurrentContextAsync();
        
        // Assert
        Assert.Equal(queries.Length, context.RecentQueries.Length);
        Assert.Contains(context.RecentQueries, q => q.Query == "auth" && q.ResultsFound == 5);
        Assert.Contains(context.RecentQueries, q => q.Query == "user login" && q.ResultsFound == 3);
    }
    
    [Fact]
    public async Task UpdateCurrentFileAsync_SetsCurrentFile_AndTracksAsRecentFile()
    {
        // Arrange
        var filePath = "Services/AuthenticationService.cs";
        
        // Act
        await _service.UpdateCurrentFileAsync(filePath);
        var context = await _service.GetCurrentContextAsync();
        
        // Assert
        Assert.Equal(filePath, context.CurrentFile);
        Assert.Contains(filePath, context.RecentFiles);
    }
    
    [Fact]
    public async Task GetCurrentContextAsync_ReturnsCompleteContext()
    {
        // Arrange
        await _service.UpdateCurrentFileAsync("Controllers/AuthController.cs");
        await _service.TrackFileAccessAsync("Services/UserService.cs");
        await _service.TrackSearchQueryAsync("authentication", 3);
        
        // Act
        var context = await _service.GetCurrentContextAsync();
        
        // Assert
        Assert.NotNull(context);
        Assert.Equal("Controllers/AuthController.cs", context.CurrentFile);
        Assert.True(context.RecentFiles.Length > 0);
        Assert.True(context.RecentQueries.Length > 0);
        Assert.True(context.ContextKeywords.Length > 0);
        Assert.NotNull(context.ProjectInfo);
        Assert.True(context.Timestamp > DateTime.UtcNow.AddMinutes(-1));
    }
    
    [Fact]
    public void GetContextBoosts_WithCurrentFileContext_AppliesBoosts()
    {
        // Arrange
        var context = new SearchContext
        {
            CurrentFile = "Controllers/AuthController.cs",
            RecentFiles = new[] { "Services/AuthService.cs" },
            RecentQueries = new[]
            {
                new SearchHistoryItem { Query = "auth login", ResultsFound = 5 }
            }
        };
        
        var searchTerms = new[] { "auth", "controller", "unrelated" };
        
        // Act
        var boosts = _service.GetContextBoosts(context, searchTerms);
        
        // Assert
        Assert.True(boosts["auth"] > 1.0f); // Should be boosted from file context
        Assert.True(boosts["controller"] > 1.0f); // Should be boosted from current file
        Assert.Equal(1.0f, boosts["unrelated"]); // Should have base weight
    }
    
    [Fact]
    public void ExtractFileContextKeywords_EmptyPath_ReturnsEmpty()
    {
        // Act & Assert
        Assert.Empty(_service.ExtractFileContextKeywords(""));
        Assert.Empty(_service.ExtractFileContextKeywords("   "));
        Assert.Empty(_service.ExtractFileContextKeywords(null!));
    }
    
    [Fact]
    public void ExtractFileContextKeywords_CommonPathSegments_FiltersOutCommonSegments()
    {
        // Arrange
        var filePath = "src/bin/debug/net9.0/MyService.cs";
        
        // Act
        var result = _service.ExtractFileContextKeywords(filePath);
        
        // Assert
        // Should extract "MyService" and "service" but filter out "src", "bin", "debug", "net9.0"
        Assert.Contains("service", result, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("src", result);
        Assert.DoesNotContain("bin", result);
        Assert.DoesNotContain("debug", result);
        Assert.DoesNotContain("net9.0", result);
    }
    
    [Fact]
    public async Task TrackFileAccessAsync_ExceedsMaxFiles_TrimsOldestFiles()
    {
        // Arrange
        var maxFiles = 3;
        _options.MaxRecentFiles = maxFiles;
        var service = new ContextAwarenessService(_mockLogger.Object, Options.Create(_options));
        
        // Act - Add more files than the limit
        for (int i = 0; i < maxFiles + 2; i++)
        {
            await service.TrackFileAccessAsync($"File{i}.cs");
        }
        
        var context = await service.GetCurrentContextAsync();
        
        // Assert
        Assert.True(context.RecentFiles.Length <= maxFiles);
        // Should keep the most recent files
        Assert.Contains("File4.cs", context.RecentFiles); // Most recent
        Assert.Contains("File3.cs", context.RecentFiles);
    }
    
    [Fact]
    public async Task TrackSearchQueryAsync_ExceedsMaxQueries_TrimsOldestQueries()
    {
        // Arrange
        var maxQueries = 3;
        _options.MaxRecentQueries = maxQueries;
        var service = new ContextAwarenessService(_mockLogger.Object, Options.Create(_options));
        
        // Act - Add more queries than the limit
        for (int i = 0; i < maxQueries + 2; i++)
        {
            await service.TrackSearchQueryAsync($"query{i}", i);
        }
        
        var context = await service.GetCurrentContextAsync();
        
        // Assert
        Assert.True(context.RecentQueries.Length <= maxQueries);
        // Should keep the most recent queries
        Assert.Contains(context.RecentQueries, q => q.Query == "query4");
        Assert.Contains(context.RecentQueries, q => q.Query == "query3");
    }
    
    [Theory]
    [InlineData("AuthController", new[] { "Auth", "Controller" })]
    [InlineData("UserService", new[] { "User", "Service" })]
    [InlineData("PaymentRepository", new[] { "Payment", "Repository" })]
    [InlineData("simpleword", new[] { "simpleword" })]
    public void ExtractFileContextKeywords_CamelCaseHandling_SplitsCorrectly(string fileName, string[] expectedParts)
    {
        // Arrange
        var filePath = $"Services/{fileName}.cs";
        
        // Act
        var result = _service.ExtractFileContextKeywords(filePath);
        
        // Assert
        foreach (var expectedPart in expectedParts)
        {
            Assert.Contains(expectedPart.ToLowerInvariant(), result, StringComparer.OrdinalIgnoreCase);
        }
    }
}