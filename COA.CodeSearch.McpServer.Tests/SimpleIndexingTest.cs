using COA.CodeSearch.McpServer.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace COA.CodeSearch.McpServer.Tests;

public class SimpleIndexingTest
{
    private readonly ITestOutputHelper _output;

    public SimpleIndexingTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task TestDirectIndexing()
    {
        // Create a simple test setup
        var loggerFactory = LoggerFactory.Create(builder => 
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });
        
        // Create test directory
        var testDir = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}");
        Directory.CreateDirectory(testDir);
        
        // Configure to use temp directory for indexes
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lucene:IndexBasePath"] = Path.Combine(testDir, ".codesearch")
            })
            .Build();
            
        var pathResolutionService = new PathResolutionService(config);
        var luceneService = new LuceneIndexService(loggerFactory.CreateLogger<LuceneIndexService>(), config, pathResolutionService);
        var fileService = new FileIndexingService(loggerFactory.CreateLogger<FileIndexingService>(), config, luceneService, pathResolutionService);
        
        // Create test file
        var testFile = Path.Combine(testDir, "test.cs");
        await File.WriteAllTextAsync(testFile, "public class Test { }");
        
        _output.WriteLine($"Created test file: {testFile}");
        _output.WriteLine($"File exists: {File.Exists(testFile)}");
        
        // Test direct file indexing
        var result = await fileService.IndexFileAsync(testDir, testFile);
        _output.WriteLine($"Direct index result: {result}");
        
        Assert.True(result, "Should be able to index a single file");
        
        // Cleanup
        luceneService.Dispose();
        Directory.Delete(testDir, true);
    }
    
    [Fact]
    public void TestGetFilesToIndex()
    {
        // Test the file enumeration logic
        var testDir = Path.Combine(Path.GetTempPath(), $"enum_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(testDir);
        
        // Create files
        File.WriteAllText(Path.Combine(testDir, "file1.cs"), "test");
        File.WriteAllText(Path.Combine(testDir, "file2.cs"), "test");
        File.WriteAllText(Path.Combine(testDir, "file3.txt"), "test");
        
        // Create subdirectory
        var subDir = Path.Combine(testDir, "sub");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "file4.cs"), "test");
        
        // Count .cs files
        var csFiles = Directory.GetFiles(testDir, "*.cs", SearchOption.AllDirectories);
        _output.WriteLine($"Found {csFiles.Length} .cs files:");
        foreach (var file in csFiles)
        {
            _output.WriteLine($"  - {file}");
        }
        
        Assert.Equal(3, csFiles.Length);
        
        // Cleanup
        Directory.Delete(testDir, true);
    }
}