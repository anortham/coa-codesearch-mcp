using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using System.Dynamic;
using Newtonsoft.Json;

namespace COA.CodeSearch.McpServer.Tests;

public class FastTextSearchIntegrationTests : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly IConfiguration _configuration;
    private readonly LuceneIndexService _luceneIndexService;
    private readonly FileIndexingService _fileIndexingService;
    private readonly FastTextSearchTool _fastTextSearchTool;
    private readonly string _testProjectPath;

    public FastTextSearchIntegrationTests()
    {
        _loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        
        var configBuilder = new ConfigurationBuilder();
        configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Lucene:IndexBasePath"] = Path.Combine(Path.GetTempPath(), "test-lucene-indexes")
        });
        _configuration = configBuilder.Build();

        _luceneIndexService = new LuceneIndexService(
            _loggerFactory.CreateLogger<LuceneIndexService>(),
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

        // Use the test project in the Tests folder
        _testProjectPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "TestProjects", "TestProject1"));
    }

    [Fact]
    public async Task FastTextSearch_FindsTextInCSharpFiles()
    {
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
        var json = JsonConvert.SerializeObject(result);
        dynamic searchResult = JsonConvert.DeserializeObject<ExpandoObject>(json);
        Assert.True((bool)searchResult.success);
        Assert.NotNull(searchResult.results);
        Assert.True((long)searchResult.totalResults > 0);
    }

    [Fact]
    public async Task FastTextSearch_FindsTextWithContext()
    {
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
        dynamic searchResult = JsonConvert.DeserializeObject<ExpandoObject>(json);
        Assert.True((bool)searchResult.success);
        Assert.NotNull(searchResult.results);
        
        if (searchResult.totalResults > 0)
        {
            var firstResult = searchResult.results[0];
            Assert.NotNull(firstResult.Context);
            Assert.True(firstResult.Context.Count > 0);
        }
    }

    [Fact]
    public async Task FastTextSearch_SupportsWildcardSearch()
    {
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
        dynamic searchResult = result;
        Assert.True(searchResult.success);
    }

    [Fact]
    public async Task FastTextSearch_FiltersByExtension()
    {
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
        dynamic searchResult = result;
        Assert.True(searchResult.success);
        
        if (searchResult.totalResults > 0)
        {
            foreach (var res in searchResult.results)
            {
                Assert.Equal(".cs", res.Extension);
            }
        }
    }

    [Fact]
    public async Task FastTextSearch_CreatesCodesearchFolder()
    {
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
    }
}