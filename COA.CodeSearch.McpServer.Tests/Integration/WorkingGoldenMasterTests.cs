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
public class WorkingGoldenMasterTests : CodeSearchToolTestBase<EditLinesTool>
{
    private TestFileManager _fileManager = null!;
    private EditLinesTool _deleteTool = null!;
    private EditLinesTool _insertTool = null!;
    private EditLinesTool _replaceTool = null!;
    private SearchAndReplaceTool _searchReplaceTool = null!;

    private string TestResourcesPath => Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "COA.CodeSearch.McpServer.Tests", "Resources", "GoldenMaster");

    protected override EditLinesTool CreateTool()
    {
        var unifiedFileEditService = new UnifiedFileEditService(
            new Mock<ILogger<UnifiedFileEditService>>().Object);
        var deleteLogger = new Mock<ILogger<EditLinesTool>>();
        _deleteTool = new EditLinesTool(
            ServiceProvider,
            PathResolutionServiceMock.Object,
            unifiedFileEditService,
            deleteLogger.Object
        );
        return _deleteTool;
    }

    [SetUp]
    public override void SetUp()
    {
        base.SetUp();
        _fileManager = new TestFileManager();
        
        // Setup index exists for SearchAndReplaceTool tests
        SetupExistingIndex();
        
        // Setup index exists for any workspace path used by the tests
        LuceneIndexServiceMock
            .Setup(x => x.IndexExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
            
        // Setup search results that return the test file being operated on
        // This will be dynamically updated for each test case
        LuceneIndexServiceMock
            .Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<Lucene.Net.Search.Query>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string workspace, Lucene.Net.Search.Query query, int maxResults, CancellationToken ct) =>
            {
                // Return a result that indicates the file was found
                return CreateTestSearchResult(1);
            });
        
        var unifiedFileEditServiceForInsert = new UnifiedFileEditService(
            new Mock<ILogger<UnifiedFileEditService>>().Object);
        var insertLogger = new Mock<ILogger<EditLinesTool>>();
        _insertTool = new EditLinesTool(
            ServiceProvider,
            PathResolutionServiceMock.Object,
            unifiedFileEditServiceForInsert,
            insertLogger.Object
        );
        
        var unifiedFileEditServiceForReplace = new UnifiedFileEditService(
            new Mock<ILogger<UnifiedFileEditService>>().Object);
        var replaceLogger = new Mock<ILogger<EditLinesTool>>();
        _replaceTool = new EditLinesTool(
            ServiceProvider,
            PathResolutionServiceMock.Object,
            unifiedFileEditServiceForReplace,
            replaceLogger.Object
        );
        
        // Create additional services needed for enhanced SearchAndReplaceTool
        var smartQueryPreprocessorLogger = new Mock<ILogger<SmartQueryPreprocessor>>();
        var smartQueryPreprocessor = new SmartQueryPreprocessor(smartQueryPreprocessorLogger.Object);
        
        var unifiedFileEditService = new UnifiedFileEditService(
            new Mock<ILogger<UnifiedFileEditService>>().Object);
        var workspacePermissionServiceMock = new Mock<IWorkspacePermissionService>();
        workspacePermissionServiceMock.Setup(x => x.IsEditAllowedAsync(It.IsAny<EditPermissionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EditPermissionResult { Allowed = true });
        
        var searchReplaceLogger = new Mock<ILogger<SearchAndReplaceTool>>();
        _searchReplaceTool = new SearchAndReplaceTool(
            ServiceProvider,
            LuceneIndexServiceMock.Object,
            PathResolutionServiceMock.Object,
            smartQueryPreprocessor,
            ResourceStorageServiceMock.Object,
            CodeAnalyzer,
            unifiedFileEditService,
            workspacePermissionServiceMock.Object,
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
        var parameters = new EditLinesParameters
        {
            FilePath = filePath,
            Operation = "delete",
            StartLine = testCase.Operation.StartLine,
            EndLine = testCase.Operation.EndLine ?? testCase.Operation.StartLine,
            ContextLines = testCase.Operation.ContextLines ?? 3
        };

        return await _deleteTool.ExecuteAsync(parameters, CancellationToken.None);
    }

    private async Task<dynamic> ExecuteInsertOperation(GoldenMasterTestCase testCase, string filePath)
    {
        var parameters = new EditLinesParameters
        {
            FilePath = filePath,
            Operation = "insert",
            StartLine = testCase.Operation.StartLine,
            Content = testCase.Operation.Content ?? "// Inserted by test",
            PreserveIndentation = testCase.Operation.PreserveIndentation ?? true,
            ContextLines = testCase.Operation.ContextLines ?? 3
        };

        return await _insertTool.ExecuteAsync(parameters, CancellationToken.None);
    }

    private async Task<dynamic> ExecuteReplaceOperation(GoldenMasterTestCase testCase, string filePath)
    {
        var parameters = new EditLinesParameters
        {
            FilePath = filePath,
            Operation = "replace",
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
        // Setup search results to return the specific test file
        var searchResult = new COA.CodeSearch.McpServer.Services.Lucene.SearchResult
        {
            Query = testCase.Operation.SearchPattern ?? "test",
            TotalHits = 1,
            Hits = new List<COA.CodeSearch.McpServer.Services.Lucene.SearchHit>
            {
                new COA.CodeSearch.McpServer.Services.Lucene.SearchHit
                {
                    FilePath = filePath,
                    Score = 1.0f,
                    Fields = new Dictionary<string, string>
                    {
                        { "content", File.ReadAllText(filePath) },
                        { "filename", Path.GetFileName(filePath) },
                        { "relativePath", Path.GetRelativePath(Path.GetDirectoryName(filePath) ?? "", filePath) },
                        { "extension", Path.GetExtension(filePath) }
                    }
                }
            },
            SearchTime = TimeSpan.FromMilliseconds(10)
        };

        LuceneIndexServiceMock
            .Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<Lucene.Net.Search.Query>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchResult);

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
        var testResourcesPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "COA.CodeSearch.McpServer.Tests", "Resources", "GoldenMaster");
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
