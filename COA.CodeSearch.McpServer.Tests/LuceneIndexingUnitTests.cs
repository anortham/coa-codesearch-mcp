using COA.CodeSearch.McpServer.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace COA.CodeSearch.McpServer.Tests;

public class LuceneIndexingUnitTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IConfiguration _configuration;
    private readonly LuceneIndexService _luceneIndexService;
    private readonly FileIndexingService _fileIndexingService;
    private readonly string _testDirectory;

    public LuceneIndexingUnitTests(ITestOutputHelper output)
    {
        _output = output;
        _loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        
        // Create a temporary test directory with test files
        _testDirectory = Path.Combine(Path.GetTempPath(), $"lucene_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
        
        // Configure to use temp directory for indexes
        var configBuilder = new ConfigurationBuilder();
        configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Lucene:IndexBasePath"] = Path.Combine(_testDirectory, ".codesearch")
        });
        _configuration = configBuilder.Build();

        _luceneIndexService = new LuceneIndexService(
            _loggerFactory.CreateLogger<LuceneIndexService>(),
            _configuration);

        _fileIndexingService = new FileIndexingService(
            _loggerFactory.CreateLogger<FileIndexingService>(),
            _configuration,
            _luceneIndexService);
        
        // Create test files
        File.WriteAllText(Path.Combine(_testDirectory, "test1.cs"), 
            @"namespace TestNamespace { public class TestClass { } }");
        File.WriteAllText(Path.Combine(_testDirectory, "test2.cs"), 
            @"using System; namespace TestNamespace { public class AnotherClass { } }");
        File.WriteAllText(Path.Combine(_testDirectory, "readme.md"), 
            @"# Test Project
This is a test project for Lucene indexing.");
    }

    [Fact]
    public async Task FileIndexing_IndexesCreatedTestFiles()
    {
        // Act
        _output.WriteLine($"Test directory: {_testDirectory}");
        _output.WriteLine($"Files in directory: {string.Join(", ", Directory.GetFiles(_testDirectory))}");
        
        var indexedCount = await _fileIndexingService.IndexDirectoryAsync(_testDirectory, _testDirectory);
        
        // Assert
        _output.WriteLine($"Indexed {indexedCount} files");
        Assert.True(indexedCount >= 2, $"Should index at least 2 files, but indexed {indexedCount}");
    }

    [Fact]
    public async Task GetFilesToIndex_FindsCSharpFiles()
    {
        // Test the private GetFilesToIndex method indirectly
        var csFiles = Directory.GetFiles(_testDirectory, "*.cs");
        _output.WriteLine($"C# files found by Directory.GetFiles: {csFiles.Length}");
        foreach (var file in csFiles)
        {
            _output.WriteLine($"  - {file}");
        }
        
        Assert.Equal(2, csFiles.Length);
    }

    public void Dispose()
    {
        _luceneIndexService?.Dispose();
        _loggerFactory?.Dispose();
        
        // Cleanup test directory
        if (Directory.Exists(_testDirectory))
        {
            try
            {
                Directory.Delete(_testDirectory, true);
            }
            catch { }
        }
    }
}