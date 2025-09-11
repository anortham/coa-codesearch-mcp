using NUnit.Framework;
using FluentAssertions;
using COA.CodeSearch.McpServer.Tools;
using COA.CodeSearch.McpServer.Tests.Helpers;
using COA.CodeSearch.McpServer.Tests.Base;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Services.Analysis;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;

namespace COA.CodeSearch.McpServer.Tests.Integration;

/// <summary>
/// Working Golden Master testing using real files from serena project.
/// Implements proper copy-edit-diff methodology with DiffPlex validation.
/// </summary>
[TestFixture]
public class WorkingGoldenMasterTests : CodeSearchToolTestBase<DeleteLinesTool>
{
    private TestFileManager _fileManager = null!;
    private DeleteLinesTool _deleteTool = null!;
    private InsertAtLineTool _insertTool = null!;
    private ReplaceLinesTool _replaceTool = null!;
    private SearchAndReplaceTool _searchReplaceTool = null!;

    private string TestResourcesPath => Path.Combine(TestContext.CurrentContext.TestDirectory, @"..\..\..\..\COA.CodeSearch.McpServer.Tests\Resources\GoldenMaster");

    protected override DeleteLinesTool CreateTool()
    {
        var deleteLogger = new Mock<ILogger<DeleteLinesTool>>();
        _deleteTool = new DeleteLinesTool(
            ServiceProvider,
            PathResolutionServiceMock.Object,
            deleteLogger.Object
        );
        return _deleteTool;
    }

    [SetUp]
    public override void SetUp()
    {
        base.SetUp();
        _fileManager = new TestFileManager();
        
        var insertLogger = new Mock<ILogger<InsertAtLineTool>>();
        _insertTool = new InsertAtLineTool(
            ServiceProvider,
            PathResolutionServiceMock.Object,
            insertLogger.Object
        );
        
        var replaceLogger = new Mock<ILogger<ReplaceLinesTool>>();
        _replaceTool = new ReplaceLinesTool(
            ServiceProvider,
            PathResolutionServiceMock.Object,
            replaceLogger.Object
        );
        
        // Create additional services needed for SearchAndReplaceTool
        var lineAwareSearchLogger = new Mock<ILogger<LineAwareSearchService>>();
        var lineAwareSearchService = new LineAwareSearchService(lineAwareSearchLogger.Object);
        
        var smartQueryPreprocessorLogger = new Mock<ILogger<SmartQueryPreprocessor>>();
        var smartQueryPreprocessor = new SmartQueryPreprocessor(smartQueryPreprocessorLogger.Object);
        
        var advancedPatternMatcher = new AdvancedPatternMatcher();
        
        var searchReplaceLogger = new Mock<ILogger<SearchAndReplaceTool>>();
        _searchReplaceTool = new SearchAndReplaceTool(
            ServiceProvider,
            LuceneIndexServiceMock.Object,
            lineAwareSearchService,
            PathResolutionServiceMock.Object,
            smartQueryPreprocessor,
            ResourceStorageServiceMock.Object,
            advancedPatternMatcher,
            CodeAnalyzer,
            searchReplaceLogger.Object
        );
    }

    [Test]
    [TestCaseSource(nameof(GetGoldenMasterTestCases))]
    public async Task GoldenMasterTest_WithRealFiles_ProducesExpectedResults(GoldenMasterTestCase testCase)
    {
        // Arrange - Copy source file to test directory
        var sourceFile = Path.Combine(TestResourcesPath, "Sources", testCase.SourceFile);
        File.Exists(sourceFile).Should().BeTrue($"Source file should exist: {testCase.SourceFile}");
        
        var controlFile = Path.Combine(TestResourcesPath, "Controls", testCase.ControlFile);
        File.Exists(controlFile).Should().BeTrue($"Control file should exist: {testCase.ControlFile}");

        var testFile = await _fileManager.CreateTestCopyAsync(sourceFile, $"golden_{testCase.TestName}");

        // Act - Apply the specified editing operation
        dynamic result = testCase.Operation.Tool switch
        {
            "DeleteLinesTool" => await ExecuteDeleteOperation(testCase, testFile.FilePath),
            "InsertAtLineTool" => await ExecuteInsertOperation(testCase, testFile.FilePath),
            "ReplaceLinesTool" => await ExecuteReplaceOperation(testCase, testFile.FilePath),
            "SearchAndReplaceTool" => await ExecuteSearchReplaceOperation(testCase, testFile.FilePath),
            _ => throw new ArgumentException($"Unknown tool: {testCase.Operation.Tool}")
        };

        // Assert - Operation should succeed
        ((bool)result.Success).Should().BeTrue($"Operation should succeed: {result.Error?.Message}");

        // Special handling for preview mode tests
        if (testCase.Operation.Preview == true)
        {
            // Preview mode should not modify the file - it should be identical to source
            var sourceContent = File.ReadAllText(Path.Combine(TestResourcesPath, "Sources", testCase.SourceFile));
            var actualContent = File.ReadAllText(testFile.FilePath);
            actualContent.Should().Be(sourceContent, "Preview mode should not modify the file");
            Console.WriteLine($"✅ {testCase.TestName}: Preview mode validation passed - file unchanged");
            return;
        }

        // Golden Master Comparison using DiffValidator
        EditExpectation editExpectation;
        
        if (testCase.Operation.Tool == "SearchAndReplaceTool")
        {
            // SearchAndReplaceTool works by pattern matching, not line ranges
            editExpectation = new EditExpectation
            {
                RequireEncodingPreservation = true,
                RequireLineEndingPreservation = true,
                AllowedOperations = GetAllowedOperationsForTool(testCase.Operation.Tool),
                // No line range restriction for search/replace operations
                TargetLineRange = null
            };
        }
        else
        {
            // Line-based tools work within specific ranges
            editExpectation = new EditExpectation
            {
                RequireEncodingPreservation = true,
                RequireLineEndingPreservation = true,
                AllowedOperations = GetAllowedOperationsForTool(testCase.Operation.Tool),
                TargetLineRange = (testCase.Operation.StartLine, testCase.Operation.EndLine ?? testCase.Operation.StartLine)
            };
        }
        
        var diffResult = DiffValidator.ValidateEdit(testFile.FilePath, controlFile, editExpectation);

        // Log detailed diff information if there are differences
        if (!diffResult.IsValid)
        {
            var report = DiffValidator.GenerateDiffReport(diffResult);
            Console.WriteLine("=== GOLDEN MASTER VALIDATION FAILED ===");
            Console.WriteLine(report);
            Console.WriteLine("=== END REPORT ===");
        }

        // Assert exact match
        diffResult.IsValid.Should().BeTrue($"Golden master validation failed for {testCase.TestName}. Check console output for detailed diff report.");
        
        Console.WriteLine($"✅ {testCase.TestName}: Golden master validation passed");
    }

    private async Task<dynamic> ExecuteDeleteOperation(GoldenMasterTestCase testCase, string filePath)
    {
        var parameters = new DeleteLinesParameters
        {
            FilePath = filePath,
            StartLine = testCase.Operation.StartLine,
            EndLine = testCase.Operation.EndLine ?? testCase.Operation.StartLine,
            ContextLines = testCase.Operation.ContextLines ?? 3
        };

        return await _deleteTool.ExecuteAsync(parameters, CancellationToken.None);
    }

    private async Task<dynamic> ExecuteInsertOperation(GoldenMasterTestCase testCase, string filePath)
    {
        var parameters = new InsertAtLineParameters
        {
            FilePath = filePath,
            LineNumber = testCase.Operation.StartLine,
            Content = testCase.Operation.Content ?? "// Inserted by test",
            PreserveIndentation = testCase.Operation.PreserveIndentation ?? true,
            ContextLines = testCase.Operation.ContextLines ?? 3
        };

        return await _insertTool.ExecuteAsync(parameters, CancellationToken.None);
    }

    private async Task<dynamic> ExecuteReplaceOperation(GoldenMasterTestCase testCase, string filePath)
    {
        var parameters = new ReplaceLinesParameters
        {
            FilePath = filePath,
            StartLine = testCase.Operation.StartLine,
            EndLine = testCase.Operation.EndLine ?? testCase.Operation.StartLine,
            Content = testCase.Operation.Content ?? "// Replaced by test",
            PreserveIndentation = testCase.Operation.PreserveIndentation ?? true,
            ContextLines = testCase.Operation.ContextLines ?? 3
        };

        return await _replaceTool.ExecuteAsync(parameters, CancellationToken.None);
    }

    private async Task<dynamic> ExecuteSearchReplaceOperation(GoldenMasterTestCase testCase, string filePath)
    {
        var parameters = new SearchAndReplaceParams
        {
            SearchPattern = testCase.Operation.SearchPattern ?? throw new ArgumentException("SearchPattern is required"),
            ReplacePattern = testCase.Operation.ReplacePattern ?? throw new ArgumentException("ReplacePattern is required"),
            WorkspacePath = Path.GetDirectoryName(filePath),
            SearchType = testCase.Operation.SearchType ?? "literal",
            CaseSensitive = testCase.Operation.CaseSensitive ?? true,
            Preview = testCase.Operation.Preview ?? false,
            ContextLines = testCase.Operation.ContextLines ?? 3,
            MaxMatches = testCase.Operation.MaxMatches ?? 10,
            FilePattern = testCase.Operation.FilePattern,
            NoCache = true
        };

        return await _searchReplaceTool.ExecuteAsync(parameters, CancellationToken.None);
    }

    private HashSet<ChangeType> GetAllowedOperationsForTool(string toolName)
    {
        return toolName switch
        {
            "DeleteLinesTool" => new HashSet<ChangeType> { ChangeType.Deletion },
            "InsertAtLineTool" => new HashSet<ChangeType> { ChangeType.Addition },
            "ReplaceLinesTool" => new HashSet<ChangeType> { ChangeType.Deletion, ChangeType.Addition, ChangeType.Modification },
            "SearchAndReplaceTool" => new HashSet<ChangeType> { ChangeType.Deletion, ChangeType.Addition, ChangeType.Modification },
            _ => new HashSet<ChangeType> { ChangeType.Addition, ChangeType.Deletion, ChangeType.Modification }
        };
    }

    private static IEnumerable<TestCaseData> GetGoldenMasterTestCases()
    {
        var testCases = new List<TestCaseData>();
        var testResourcesPath = Path.Combine(TestContext.CurrentContext.TestDirectory, @"..\..\..\..\COA.CodeSearch.McpServer.Tests\Resources\GoldenMaster");
        var manifestsDir = Path.Combine(testResourcesPath, "Manifests");
        
        if (!Directory.Exists(manifestsDir))
        {
            return testCases;
        }

        var manifestFiles = Directory.GetFiles(manifestsDir, "*.json");
        
        foreach (var manifestFile in manifestFiles)
        {
            var jsonContent = File.ReadAllText(manifestFile);
            
            JsonDocument? document = null;
            try
            {
                document = JsonDocument.Parse(jsonContent);
                var testCasesElement = document.RootElement.GetProperty("TestCases");
                
                foreach (var testCaseElement in testCasesElement.EnumerateArray())
                {
                    var testCase = new GoldenMasterTestCase
                    {
                        TestName = testCaseElement.GetProperty("TestName").GetString()!,
                        Description = testCaseElement.GetProperty("Description").GetString()!,
                        SourceFile = testCaseElement.GetProperty("SourceFile").GetString()!,
                        ControlFile = testCaseElement.GetProperty("ControlFile").GetString()!,
                        Operation = ParseOperation(testCaseElement.GetProperty("Operation"))
                    };
                    
                    testCases.Add(new TestCaseData(testCase).SetName($"GoldenMaster_{testCase.TestName}"));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not parse manifest file {manifestFile}: {ex.Message}");
            }
            finally
            {
                document?.Dispose();
            }
        }
        
        return testCases;
    }

    private static EditOperation ParseOperation(JsonElement operationElement)
    {
        return new EditOperation
        {
            Tool = operationElement.GetProperty("Tool").GetString()!,
            StartLine = operationElement.TryGetProperty("StartLine", out var startLineElement) ? startLineElement.GetInt32() : 0,
            EndLine = operationElement.TryGetProperty("EndLine", out var endLineElement) ? endLineElement.GetInt32() : null,
            Content = operationElement.TryGetProperty("Content", out var contentElement) ? contentElement.GetString() : null,
            PreserveIndentation = operationElement.TryGetProperty("PreserveIndentation", out var preserveElement) ? preserveElement.GetBoolean() : null,
            ContextLines = operationElement.TryGetProperty("ContextLines", out var contextElement) ? contextElement.GetInt32() : null,
            
            // SearchAndReplaceTool properties
            SearchPattern = operationElement.TryGetProperty("SearchPattern", out var searchPatternElement) ? searchPatternElement.GetString() : null,
            ReplacePattern = operationElement.TryGetProperty("ReplacePattern", out var replacePatternElement) ? replacePatternElement.GetString() : null,
            SearchType = operationElement.TryGetProperty("SearchType", out var searchTypeElement) ? searchTypeElement.GetString() : null,
            CaseSensitive = operationElement.TryGetProperty("CaseSensitive", out var caseSensitiveElement) ? caseSensitiveElement.GetBoolean() : null,
            Preview = operationElement.TryGetProperty("Preview", out var previewElement) ? previewElement.GetBoolean() : null,
            MaxMatches = operationElement.TryGetProperty("MaxMatches", out var maxMatchesElement) ? maxMatchesElement.GetInt32() : null,
            FilePattern = operationElement.TryGetProperty("FilePattern", out var filePatternElement) ? filePatternElement.GetString() : null
        };
    }

    [TearDown]
    public override void TearDown()
    {
        _fileManager?.Dispose();
        base.TearDown();
    }
}

public class GoldenMasterTestCase
{
    public string TestName { get; set; } = "";
    public string Description { get; set; } = "";
    public string SourceFile { get; set; } = "";
    public string ControlFile { get; set; } = "";
    public EditOperation Operation { get; set; } = new();
}

public class EditOperation
{
    public string Tool { get; set; } = "";
    public int StartLine { get; set; }
    public int? EndLine { get; set; }
    public string? Content { get; set; }
    public bool? PreserveIndentation { get; set; }
    public int? ContextLines { get; set; }
    
    // SearchAndReplaceTool properties
    public string? SearchPattern { get; set; }
    public string? ReplacePattern { get; set; }
    public string? SearchType { get; set; }
    public bool? CaseSensitive { get; set; }
    public bool? Preview { get; set; }
    public int? MaxMatches { get; set; }
    public string? FilePattern { get; set; }
}