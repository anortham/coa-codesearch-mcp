using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using System.Dynamic;
using Newtonsoft.Json;

namespace COA.CodeSearch.McpServer.Tests;

public class FastTextSearchIntegrationTests : IDisposable
{
    private ILoggerFactory _loggerFactory = null!;
    private IConfiguration _configuration = null!;
    private ImprovedLuceneIndexService _luceneIndexService = null!;
    private FileIndexingService _fileIndexingService = null!;
    private FastTextSearchTool _fastTextSearchTool = null!;
    private string _testProjectPath = null!;
    private string _tempBasePath = null!;

    public FastTextSearchIntegrationTests()
    {
        // Use the test project in the Tests folder
        // Test projects are copied to output directory via csproj configuration
        _testProjectPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "TestProjects", "TestProject1"));
        
        InitializeServices();
    }
    
    private void InitializeServices()
    {
        // Dispose existing services if any
        _luceneIndexService?.Dispose();
        _loggerFactory?.Dispose();
        
        _loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        
        // Use a unique temp directory for each test to avoid conflicts
        var testId = Guid.NewGuid().ToString("N");
        _tempBasePath = Path.Combine(Path.GetTempPath(), "codesearch-tests", testId);
        Directory.CreateDirectory(_tempBasePath);
        
        var configBuilder = new ConfigurationBuilder();
        configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Lucene:IndexBasePath"] = _tempBasePath
        });
        _configuration = configBuilder.Build();

        _luceneIndexService = new ImprovedLuceneIndexService(
            _loggerFactory.CreateLogger<ImprovedLuceneIndexService>(),
            _configuration);

        _fileIndexingService = new FileIndexingService(
            _loggerFactory.CreateLogger<FileIndexingService>(),
            _configuration,
            _luceneIndexService);

        _fastTextSearchTool = new FastTextSearchTool(
            _loggerFactory.CreateLogger<FastTextSearchTool>(),
            _configuration,
            _luceneIndexService,
            _fileIndexingService);
    }

    private async Task SetupTestAsync()
    {
        // Clean any existing index in the test project
        var testIndexPath = Path.Combine(_testProjectPath, ".codesearch");
        if (Directory.Exists(testIndexPath))
        {
            try
            {
                Directory.Delete(testIndexPath, true);
                await Task.Delay(100); // Wait for deletion to complete
            }
            catch { }
        }
        
        // Reinitialize services for a clean state
        InitializeServices();
    }
    
    [Fact(Skip = "Skipping due to Lucene file locking issues in parallel test execution")]
    public async Task FastTextSearch_FindsTextInCSharpFiles()
    {
        await SetupTestAsync();
        // Verify test project exists
        Assert.True(Directory.Exists(_testProjectPath), $"Test project path should exist: {_testProjectPath}");
        
        // List files to debug
        var csFiles = Directory.GetFiles(_testProjectPath, "*.cs", SearchOption.AllDirectories);
        Assert.True(csFiles.Length > 0, $"Should find .cs files in {_testProjectPath}");

        // Arrange - Index the test project
        var indexed = await _fileIndexingService.IndexDirectoryAsync(_testProjectPath, _testProjectPath);
        Assert.True(indexed > 0, $"Should have indexed at least one file. Found {csFiles.Length} .cs files but indexed {indexed}");

        // Act - Search for a common C# keyword
        var result = await _fastTextSearchTool.ExecuteAsync(
            query: "class",
            workspacePath: _testProjectPath,
            filePattern: "*.cs",
            maxResults: 10);

        // Assert
        Assert.NotNull(result);
        // The result is an anonymous type, serialize and check the JSON
        var json = JsonConvert.SerializeObject(result);
        Assert.Contains("\"success\":true", json);
        Assert.Contains("\"results\":", json);
        Assert.DoesNotContain("\"totalResults\":0", json);
    }

    [Fact(Skip = "Skipping due to Lucene file locking issues in parallel test execution")]
    public async Task FastTextSearch_FindsTextWithContext()
    {
        await SetupTestAsync();
        // Arrange - Index the test project
        await _fileIndexingService.IndexDirectoryAsync(_testProjectPath, _testProjectPath);

        // Act - Search with context lines
        var result = await _fastTextSearchTool.ExecuteAsync(
            query: "namespace",
            workspacePath: _testProjectPath,
            contextLines: 2,
            maxResults: 5);

        // Assert
        Assert.NotNull(result);
        var json = JsonConvert.SerializeObject(result);
        Assert.Contains("\"success\":true", json);
        Assert.Contains("\"results\":", json);
        
        // Check if we have results with context
        var resultObj = JsonConvert.DeserializeObject<dynamic>(json);
        if (resultObj?.totalResults > 0 && resultObj?.results?.Count > 0)
        {
            var firstResult = resultObj.results[0];
            Assert.NotNull(firstResult.Context);
        }
    }

    [Fact(Skip = "Skipping due to Lucene file locking issues in parallel test execution")]
    public async Task FastTextSearch_SupportsWildcardSearch()
    {
        await SetupTestAsync();
        // Arrange - Index the test project
        await _fileIndexingService.IndexDirectoryAsync(_testProjectPath, _testProjectPath);

        // Act - Wildcard search
        var result = await _fastTextSearchTool.ExecuteAsync(
            query: "Test*",
            workspacePath: _testProjectPath,
            searchType: "wildcard",
            maxResults: 10);

        // Assert
        Assert.NotNull(result);
        var json = JsonConvert.SerializeObject(result);
        Assert.Contains("\"success\":true", json);
    }

    [Fact(Skip = "Skipping due to Lucene file locking issues in parallel test execution")]
    public async Task FastTextSearch_FiltersByExtension()
    {
        await SetupTestAsync();
        // Arrange - Index the test project
        await _fileIndexingService.IndexDirectoryAsync(_testProjectPath, _testProjectPath);

        // Act - Search only in .cs files
        var result = await _fastTextSearchTool.ExecuteAsync(
            query: "using",
            workspacePath: _testProjectPath,
            extensions: new[] { ".cs" },
            maxResults: 10);

        // Assert
        Assert.NotNull(result);
        var json = JsonConvert.SerializeObject(result);
        Assert.Contains("\"success\":true", json);
        
        // Check if all results are .cs files
        var resultObj = JsonConvert.DeserializeObject<dynamic>(json);
        if (resultObj?.totalResults > 0 && resultObj?.results != null)
        {
            foreach (var res in resultObj.results)
            {
                Assert.Equal(".cs", (string)res.Extension);
            }
        }
    }

    [Fact(Skip = "Skipping due to Lucene file locking issues in parallel test execution")]
    public async Task FastTextSearch_CreatesCodesearchFolder()
    {
        await SetupTestAsync();
        // Act - Index a project
        await _fileIndexingService.IndexDirectoryAsync(_testProjectPath, _testProjectPath);

        // Assert - Check if .codesearch folder was created
        var codesearchPath = Path.Combine(_testProjectPath, ".codesearch");
        Assert.True(Directory.Exists(codesearchPath), ".codesearch folder should be created");

        // Check if .gitignore was updated
        var gitignorePath = Path.Combine(_testProjectPath, ".gitignore");
        if (File.Exists(gitignorePath))
        {
            var gitignoreContent = await File.ReadAllTextAsync(gitignorePath);
            Assert.Contains(".codesearch/", gitignoreContent);
        }
    }

    public void Dispose()
    {
        _luceneIndexService?.Dispose();
        _loggerFactory?.Dispose();
        
        // Cleanup test indexes
        var testIndexPath = Path.Combine(_testProjectPath, ".codesearch");
        if (Directory.Exists(testIndexPath))
        {
            try
            {
                Directory.Delete(testIndexPath, true);
            }
            catch { }
        }
        
        // Also cleanup the temp index directory
        if (!string.IsNullOrEmpty(_tempBasePath) && Directory.Exists(_tempBasePath))
        {
            try
            {
                Directory.Delete(_tempBasePath, true);
            }
            catch { }
        }
    }
}