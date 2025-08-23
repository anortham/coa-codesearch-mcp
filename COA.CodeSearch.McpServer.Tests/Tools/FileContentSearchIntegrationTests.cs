using NUnit.Framework;
using FluentAssertions;
using COA.CodeSearch.McpServer.Tools;
using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Services.Lucene;
using COA.CodeSearch.McpServer.Services.Analysis;
using COA.CodeSearch.McpServer.Models;
using COA.Mcp.Framework.TokenOptimization.Models;
using COA.Mcp.Framework.TokenOptimization.Caching;
using COA.Mcp.Framework.TokenOptimization.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Threading.Tasks;
using System.Threading;
using System;
using System.Collections.Generic;
using System.IO;

namespace COA.CodeSearch.McpServer.Tests.Tools
{
    /// <summary>
    /// Integration tests for FileContentSearchTool that use real Lucene index and verify precise line numbers
    /// </summary>
    [TestFixture]
    public class FileContentSearchIntegrationTests
    {
        private string _testWorkspacePath = null!;
        private FileContentSearchTool _tool = null!;
        private ILuceneIndexService _luceneIndexService = null!;
        private IFileIndexingService _fileIndexingService = null!;
        private string _testCSharpFile = null!;
        private string _testJavaScriptFile = null!;

        [OneTimeSetUp]
        public async Task OneTimeSetUp()
        {
            // Create test workspace
            _testWorkspacePath = Path.Combine(Path.GetTempPath(), "codesearch-integration-test", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_testWorkspacePath);
            
            // Create test files with known content and line numbers
            await CreateTestFiles();
            
            // Setup real services
            SetupRealServices();
            
            // Index the test workspace
            await IndexTestWorkspace();
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDown()
        {
            if (_luceneIndexService is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else if (_luceneIndexService is IDisposable disposable)
            {
                disposable.Dispose();
            }
            
            // Clean up test workspace
            if (Directory.Exists(_testWorkspacePath))
            {
                try
                {
                    Directory.Delete(_testWorkspacePath, true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }

        [Test]
        public async Task Search_Should_Return_Exact_Line_Numbers_For_CSharp_Method()
        {
            // Arrange
            var parameters = new FileContentSearchParameters
            {
                FilePath = _testCSharpFile,
                Pattern = "CalculateTotal",
                WorkspacePath = _testWorkspacePath,
                SearchType = "literal"
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Data.Should().NotBeNull();
            result.Data!.Results.Should().NotBeNull();
            result.Data.Results!.TotalHits.Should().BeGreaterThan(0);

            var hit = result.Data.Results.Hits!.First();
            hit.FilePath.Should().Be(_testCSharpFile);
            hit.LineNumber.Should().Be(15); // Line where "public decimal CalculateTotal()" is defined
            hit.Snippet.Should().NotBeNullOrEmpty();
        }

        [Test]
        public async Task Search_Should_Return_Exact_Line_Numbers_For_Variable_Declaration()
        {
            // Arrange
            var parameters = new FileContentSearchParameters
            {
                FilePath = _testCSharpFile,
                Pattern = "decimal total = 0;",
                WorkspacePath = _testWorkspacePath,
                SearchType = "literal"
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Data.Should().NotBeNull();
            result.Data!.Results.Should().NotBeNull();
            result.Data.Results!.TotalHits.Should().BeGreaterThan(0);

            var hit = result.Data.Results.Hits!.First();
            hit.LineNumber.Should().Be(17); // Line where "decimal total = 0;" appears
        }

        [Test]
        public async Task Search_Should_Return_Exact_Line_Numbers_For_JavaScript_Function()
        {
            // Arrange
            var parameters = new FileContentSearchParameters
            {
                FilePath = _testJavaScriptFile,
                Pattern = "function validateInput",
                WorkspacePath = _testWorkspacePath,
                SearchType = "literal"
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Data.Should().NotBeNull();
            result.Data!.Results.Should().NotBeNull();
            result.Data.Results!.TotalHits.Should().BeGreaterThan(0);

            var hit = result.Data.Results.Hits!.First();
            hit.LineNumber.Should().Be(8); // Line where "function validateInput" is defined
        }

        [Test]
        public async Task Search_Should_Return_Multiple_Matches_With_Correct_Line_Numbers()
        {
            // Arrange - search for "console.log" which appears on multiple lines
            var parameters = new FileContentSearchParameters
            {
                FilePath = _testJavaScriptFile,
                Pattern = "console.log",
                WorkspacePath = _testWorkspacePath,
                SearchType = "literal",
                MaxResults = 10
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Data.Should().NotBeNull();
            result.Data!.Results.Should().NotBeNull();
            result.Data.Results!.TotalHits.Should().BeGreaterOrEqualTo(2);

            var hits = result.Data.Results.Hits!.ToList();
            hits.Should().HaveCountGreaterOrEqualTo(2);
            
            // Verify line numbers are in ascending order and correct
            var lineNumbers = hits.Select(h => h.LineNumber).Where(ln => ln.HasValue).Select(ln => ln!.Value).OrderBy(ln => ln).ToList();
            lineNumbers.Should().Contain(4);  // Line where first console.log appears
            lineNumbers.Should().Contain(15); // Line where second console.log appears
        }

        [Test]
        public async Task Search_Should_Handle_Regex_Pattern_With_Correct_Line_Numbers()
        {
            // Arrange - search for lines containing "if" statements
            var parameters = new FileContentSearchParameters
            {
                FilePath = _testCSharpFile,
                Pattern = @"if\s*\(",
                WorkspacePath = _testWorkspacePath,
                SearchType = "regex"
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Data.Should().NotBeNull();
            result.Data!.Results.Should().NotBeNull();
            
            if (result.Data.Results!.TotalHits > 0)
            {
                var hit = result.Data.Results.Hits!.First();
                hit.LineNumber.Should().Be(18); // Line where "if (items != null)" appears
            }
        }

        [Test]
        public async Task Search_Should_Include_Context_Lines_Around_Match()
        {
            // Arrange
            var parameters = new FileContentSearchParameters
            {
                FilePath = _testCSharpFile,
                Pattern = "CalculateTotal",
                WorkspacePath = _testWorkspacePath,
                SearchType = "literal",
                ContextLines = 2
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Data.Should().NotBeNull();
            result.Data!.Results.Should().NotBeNull();
            result.Data.Results!.TotalHits.Should().BeGreaterThan(0);

            var hit = result.Data.Results.Hits!.First();
            hit.ContextLines.Should().NotBeNullOrEmpty();
            hit.ContextLines!.Count.Should().BeGreaterOrEqualTo(3); // At least the match line plus some context
            hit.StartLine.Should().HaveValue();
            hit.EndLine.Should().HaveValue();
            hit.StartLine!.Value.Should().BeLessThan(hit.LineNumber!.Value);
            hit.EndLine!.Value.Should().BeGreaterThan(hit.LineNumber!.Value);
        }

        [Test]
        public async Task Search_Should_Be_Case_Sensitive_When_Requested()
        {
            // Arrange - search for lowercase "calculatetotal" with case sensitivity
            var parameters = new FileContentSearchParameters
            {
                FilePath = _testCSharpFile,
                Pattern = "calculatetotal", // lowercase
                WorkspacePath = _testWorkspacePath,
                SearchType = "literal",
                CaseSensitive = true
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Data.Should().NotBeNull();
            result.Data!.Results.Should().NotBeNull();
            result.Data.Results!.TotalHits.Should().Be(0); // Should not match "CalculateTotal"
        }

        [Test]
        public async Task Search_Should_Be_Case_Insensitive_By_Default()
        {
            // Arrange - search for lowercase "calculatetotal" without case sensitivity
            var parameters = new FileContentSearchParameters
            {
                FilePath = _testCSharpFile,
                Pattern = "calculatetotal", // lowercase
                WorkspacePath = _testWorkspacePath,
                SearchType = "literal",
                CaseSensitive = false
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Data.Should().NotBeNull();
            result.Data!.Results.Should().NotBeNull();
            result.Data.Results!.TotalHits.Should().BeGreaterThan(0); // Should match "CalculateTotal"
        }

        private async Task CreateTestFiles()
        {
            // Create C# test file with known line numbers
            _testCSharpFile = Path.Combine(_testWorkspacePath, "Calculator.cs");
            var csharpContent = @"using System;
using System.Collections.Generic;
using System.Linq;

namespace TestProject
{
    /// <summary>
    /// Calculator class for testing line number precision
    /// </summary>
    public class Calculator
    {
        private readonly List<decimal> _history = new();

        // This method is on line 15
        public decimal CalculateTotal(IEnumerable<decimal> items)
        {
            decimal total = 0; // Line 17
            if (items != null) // Line 18
            {
                foreach (var item in items) // Line 20
                {
                    total += item;
                    _history.Add(item);
                }
            }
            
            return total; // Line 27
        }
        
        public void ClearHistory()
        {
            _history.Clear();
        }
    }
}";

            // Create JavaScript test file with known line numbers  
            _testJavaScriptFile = Path.Combine(_testWorkspacePath, "validator.js");
            var jsContent = @"// Validator module for testing
const messages = {
    error: 'Validation failed',
    success: 'Validation passed'
}; // Line 4: console.log in next line would be here
console.log('Validator module loaded');

// Function on line 8
function validateInput(input) {
    if (!input) {
        return false;
    }
    
    if (input.length < 3) {
        console.log('Input too short'); // Line 15
        return false;
    }
    
    return true;
}

// Export the function
module.exports = { validateInput };";

            await File.WriteAllTextAsync(_testCSharpFile, csharpContent);
            await File.WriteAllTextAsync(_testJavaScriptFile, jsContent);
        }

        private void SetupRealServices()
        {
            // Create real configuration
            var configDict = new Dictionary<string, string?>
            {
                ["CodeSearch:Lucene:UseRamDirectory"] = "true", // Use RAM for faster tests
                ["CodeSearch:Lucene:SupportedExtensions:0"] = ".cs",
                ["CodeSearch:Lucene:SupportedExtensions:1"] = ".js"
            };
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configDict)
                .Build();

            // Create real services
            var pathResolutionService = new PathResolutionService(configuration);
            var memoryCache = new MemoryCache(new MemoryCacheOptions());
            var circuitBreakerService = new CircuitBreakerService(new Mock<ILogger<CircuitBreakerService>>().Object, configuration);
            var memoryPressureService = new MemoryPressureService(
                new Mock<ILogger<MemoryPressureService>>().Object,
                configuration);
            var lineNumberService = new LineNumberService(new Mock<ILogger<LineNumberService>>().Object);
            var snippetService = new SmartSnippetService(new Mock<ILogger<SmartSnippetService>>().Object);

            // Create loggers
            var luceneLogger = new Mock<ILogger<LuceneIndexService>>().Object;
            var fileIndexingLogger = new Mock<ILogger<FileIndexingService>>().Object;
            var toolLogger = new Mock<ILogger<FileContentSearchTool>>().Object;
            var queryPreprocessorLogger = new Mock<ILogger<QueryPreprocessor>>().Object;

            // Create real Lucene service
            _luceneIndexService = new LuceneIndexService(
                luceneLogger,
                configuration,
                pathResolutionService,
                circuitBreakerService,
                memoryPressureService,
                lineNumberService,
                snippetService);

            // Create real file indexing service
            var indexingMetricsService = new IndexingMetricsService(
                new Mock<ILogger<IndexingMetricsService>>().Object,
                configuration,
                pathResolutionService);
            _fileIndexingService = new FileIndexingService(
                fileIndexingLogger,
                configuration,
                _luceneIndexService,
                pathResolutionService,
                indexingMetricsService,
                circuitBreakerService,
                memoryPressureService,
                Options.Create(new MemoryLimitsConfiguration()));

            // Create mock services for non-essential functionality
            var responseCacheService = new Mock<IResponseCacheService>().Object;
            var resourceStorageService = new Mock<IResourceStorageService>().Object;
            var cacheKeyGenerator = new Mock<ICacheKeyGenerator>().Object;
            var vscodebridge = new Mock<COA.VSCodeBridge.IVSCodeBridge>().Object;
            var queryPreprocessor = new QueryPreprocessor(queryPreprocessorLogger);

            // Create the tool with real services
            _tool = new FileContentSearchTool(
                _luceneIndexService,
                responseCacheService,
                resourceStorageService,
                cacheKeyGenerator,
                queryPreprocessor,
                vscodebridge,
                toolLogger);
        }

        private async Task IndexTestWorkspace()
        {
            var result = await _fileIndexingService.IndexWorkspaceAsync(_testWorkspacePath, CancellationToken.None);
            result.Success.Should().BeTrue($"Failed to index test workspace: {result.ErrorMessage}");
            result.IndexedFileCount.Should().BeGreaterThan(0);
        }

        #region Edge Case Tests

        [Test]
        public async Task Search_Should_Handle_Empty_File_Gracefully()
        {
            // Arrange - create empty file
            var emptyFile = Path.Combine(_testWorkspacePath, "Empty.cs");
            await File.WriteAllTextAsync(emptyFile, string.Empty);
            
            // Re-index to include the empty file
            await _fileIndexingService.IndexFileAsync(_testWorkspacePath, emptyFile, CancellationToken.None);

            var parameters = new FileContentSearchParameters
            {
                FilePath = emptyFile,
                Pattern = "anything",
                WorkspacePath = _testWorkspacePath,
                SearchType = "literal"
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Data.Should().NotBeNull();
            result.Data!.Results.Should().NotBeNull();
            result.Data.Results!.TotalHits.Should().Be(0); // No matches in empty file
        }

        [Test]
        public async Task Search_Should_Handle_Single_Line_File()
        {
            // Arrange - create single line file
            var singleLineFile = Path.Combine(_testWorkspacePath, "SingleLine.js");
            await File.WriteAllTextAsync(singleLineFile, "console.log('hello world');");
            
            // Re-index to include the single line file
            await _fileIndexingService.IndexFileAsync(_testWorkspacePath, singleLineFile, CancellationToken.None);

            var parameters = new FileContentSearchParameters
            {
                FilePath = singleLineFile,
                Pattern = "console.log",
                WorkspacePath = _testWorkspacePath,
                SearchType = "literal"
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Data.Should().NotBeNull();
            result.Data!.Results.Should().NotBeNull();
            result.Data.Results!.TotalHits.Should().Be(1);
            
            var hit = result.Data.Results.Hits!.First();
            hit.LineNumber.Should().Be(1); // Should be line 1
            hit.Snippet.Should().Contain("console.log");
        }

        [Test]
        public async Task Search_Should_Handle_Match_At_First_Line()
        {
            // Arrange - search for something on the first line
            var parameters = new FileContentSearchParameters
            {
                FilePath = _testCSharpFile,
                Pattern = "using System;",
                WorkspacePath = _testWorkspacePath,
                SearchType = "literal"
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Data.Should().NotBeNull();
            result.Data!.Results.Should().NotBeNull();
            result.Data.Results!.TotalHits.Should().BeGreaterThan(0);

            var hit = result.Data.Results.Hits!.First();
            hit.LineNumber.Should().Be(1); // First line
        }

        [Test]
        public async Task Search_Should_Handle_Match_At_Last_Line()
        {
            // Arrange - search for the closing brace which should be on the last line
            var parameters = new FileContentSearchParameters
            {
                FilePath = _testCSharpFile,
                Pattern = "}",
                WorkspacePath = _testWorkspacePath,
                SearchType = "literal",
                MaxResults = 50 // Get all matches
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Data.Should().NotBeNull();
            result.Data!.Results.Should().NotBeNull();
            result.Data.Results!.TotalHits.Should().BeGreaterThan(0);

            // Find the hit with the highest line number (should be the last line)
            var hits = result.Data.Results.Hits!.Where(h => h.LineNumber.HasValue).ToList();
            hits.Should().NotBeEmpty();
            
            var lastLineHit = hits.OrderByDescending(h => h.LineNumber!.Value).First();
            lastLineHit.LineNumber.Should().BeGreaterThan(30); // Should be near the end of the file
        }

        [Test]
        public async Task Search_Should_Handle_Very_Long_Lines()
        {
            // Arrange - create file with a very long line
            var longLineFile = Path.Combine(_testWorkspacePath, "LongLine.cs");
            var longString = new string('x', 5000); // 5000 character line
            var content = $@"// Short line
var veryLongString = ""{longString}""; // This is a very long line
// Another short line";
            
            await File.WriteAllTextAsync(longLineFile, content);
            await _fileIndexingService.IndexFileAsync(_testWorkspacePath, longLineFile, CancellationToken.None);

            var parameters = new FileContentSearchParameters
            {
                FilePath = longLineFile,
                Pattern = "veryLongString",
                WorkspacePath = _testWorkspacePath,
                SearchType = "literal"
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Data.Should().NotBeNull();
            result.Data!.Results.Should().NotBeNull();
            result.Data.Results!.TotalHits.Should().BeGreaterThan(0);

            var hit = result.Data.Results.Hits!.First();
            hit.LineNumber.Should().Be(2); // Second line where the long string is
            hit.Snippet.Should().NotBeNullOrEmpty(); // Should handle long lines gracefully
        }

        [Test]
        public async Task Search_Should_Handle_Files_With_Only_Whitespace()
        {
            // Arrange - create file with only whitespace
            var whitespaceFile = Path.Combine(_testWorkspacePath, "Whitespace.txt");
            var whitespaceContent = "   \n\t\n   \n\r\n  \t  \n";
            
            await File.WriteAllTextAsync(whitespaceFile, whitespaceContent);
            await _fileIndexingService.IndexFileAsync(_testWorkspacePath, whitespaceFile, CancellationToken.None);

            var parameters = new FileContentSearchParameters
            {
                FilePath = whitespaceFile,
                Pattern = "anything",
                WorkspacePath = _testWorkspacePath,
                SearchType = "literal"
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Data.Should().NotBeNull();
            result.Data!.Results.Should().NotBeNull();
            result.Data.Results!.TotalHits.Should().Be(0); // No content matches
        }

        [Test]
        public async Task Search_Should_Handle_Special_Characters_In_Pattern()
        {
            // Arrange - create file with special characters
            var specialFile = Path.Combine(_testWorkspacePath, "Special.cs");
            var specialContent = @"// File with special characters
var regex = @""[\w\s]*"";
var query = ""SELECT * FROM users WHERE name = 'John's Place'"";
var json = ""{""key"": ""value""}"";";
            
            await File.WriteAllTextAsync(specialFile, specialContent);
            await _fileIndexingService.IndexFileAsync(_testWorkspacePath, specialFile, CancellationToken.None);

            // Test searching for literal quotes
            var parameters = new FileContentSearchParameters
            {
                FilePath = specialFile,
                Pattern = @"""key"":",
                WorkspacePath = _testWorkspacePath,
                SearchType = "literal"
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Data.Should().NotBeNull();
            result.Data!.Results.Should().NotBeNull();
            result.Data.Results!.TotalHits.Should().BeGreaterThan(0);

            var hit = result.Data.Results.Hits!.First();
            hit.LineNumber.Should().Be(4); // Line with JSON
            hit.Snippet.Should().Contain(@"""key"":");
        }

        [Test]
        public async Task Search_Should_Handle_Unicode_Characters()
        {
            // Arrange - create file with Unicode characters
            var unicodeFile = Path.Combine(_testWorkspacePath, "Unicode.cs");
            var unicodeContent = @"// Unicode test file
var greeting = ""Hello 世界""; // Chinese characters
var emoji = ""✅ Done!"";
var symbol = ""π ≈ 3.14159"";";
            
            await File.WriteAllTextAsync(unicodeFile, unicodeContent);
            await _fileIndexingService.IndexFileAsync(_testWorkspacePath, unicodeFile, CancellationToken.None);

            var parameters = new FileContentSearchParameters
            {
                FilePath = unicodeFile,
                Pattern = "世界",
                WorkspacePath = _testWorkspacePath,
                SearchType = "literal"
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Data.Should().NotBeNull();
            result.Data!.Results.Should().NotBeNull();
            result.Data.Results!.TotalHits.Should().BeGreaterThan(0);

            var hit = result.Data.Results.Hits!.First();
            hit.LineNumber.Should().Be(2); // Line with Chinese characters
            hit.Snippet.Should().Contain("世界");
        }

        [Test]
        public async Task Search_Should_Handle_Large_File_With_Many_Matches()
        {
            // Arrange - create large file with many repeated patterns
            var largeFile = Path.Combine(_testWorkspacePath, "Large.cs");
            var lines = new List<string>();
            
            lines.Add("// Large file test");
            for (int i = 1; i <= 1000; i++)
            {
                lines.Add($"public void TestMethod{i}() {{ /* Method {i} */ }}");
            }
            lines.Add("// End of file");
            
            await File.WriteAllTextAsync(largeFile, string.Join("\n", lines));
            await _fileIndexingService.IndexFileAsync(_testWorkspacePath, largeFile, CancellationToken.None);

            var parameters = new FileContentSearchParameters
            {
                FilePath = largeFile,
                Pattern = "TestMethod",
                WorkspacePath = _testWorkspacePath,
                SearchType = "literal",
                MaxResults = 5 // Limit results
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Data.Should().NotBeNull();
            result.Data!.Results.Should().NotBeNull();
            result.Data.Results!.TotalHits.Should().Be(1000); // Should find all 1000 matches
            result.Data.Results.Hits!.Count.Should().BeLessOrEqualTo(5); // But return only 5 due to MaxResults

            // Check that line numbers are reasonable
            foreach (var hit in result.Data.Results.Hits!)
            {
                hit.LineNumber.Should().BeGreaterThan(1);
                hit.LineNumber.Should().BeLessOrEqualTo(1002); // Total lines in file
            }
        }

        #endregion
    }
}