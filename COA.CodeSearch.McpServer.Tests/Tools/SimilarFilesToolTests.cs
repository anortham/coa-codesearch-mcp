using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Services.Lucene;
using COA.CodeSearch.McpServer.Tools;
using COA.CodeSearch.McpServer.Tests.Base;
using COA.Mcp.Framework.TokenOptimization.Caching;
using COA.Mcp.Framework.TokenOptimization.Models;
using COA.Mcp.Framework.TokenOptimization.Storage;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Lucene.Net.Documents;
using Lucene.Net.Store;
using Lucene.Net.Index;
using COA.CodeSearch.McpServer.Services.Analysis;
using Lucene.Net.Util;

namespace COA.CodeSearch.McpServer.Tests.Tools;

[TestFixture]
public class SimilarFilesToolTests : CodeSearchToolTestBase<SimilarFilesTool>
{
    private SimilarFilesTool _tool = null!;
    private RAMDirectory _testDirectory = null!;

    protected override SimilarFilesTool CreateTool()
    {
        _testDirectory = new RAMDirectory();
        
        _tool = new SimilarFilesTool(
            ServiceProvider,
            LuceneIndexServiceMock.Object,
            ResponseCacheServiceMock.Object,
            ResourceStorageServiceMock.Object,
            CacheKeyGeneratorMock.Object,
            PathResolutionServiceMock.Object,
            VSCodeBridgeMock.Object,
            CodeAnalyzer,
            Mock.Of<ILogger<SimilarFilesTool>>()
        );
        
        return _tool;
    }

    [Test]
    public async Task ExecuteAsync_ValidFilePath_ReturnsSimilarFiles()
    {
        // Arrange
        var testFile = Path.Combine(TestWorkspacePath, "test.cs");
        File.WriteAllText(testFile, "public class TestClass { }");
        
        var parameters = new SimilarFilesParameters
        {
            FilePath = testFile,
            WorkspacePath = TestWorkspacePath,
            MaxResults = 10
        };

        LuceneIndexServiceMock.Setup(x => x.IndexExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        SetupTestIndex();

        // Act
        var result = await _tool.ExecuteAsync(parameters);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True);
        Assert.That(result.Data, Is.Not.Null);
        Assert.That(result.Data.Results, Is.Not.Null);
    }

    [Test]
    public async Task ExecuteAsync_FileNotInWorkspace_ReturnsError()
    {
        // Arrange
        var testFile = Path.Combine(Path.GetTempPath(), "outside-file.cs");
        File.WriteAllText(testFile, "public class TestClass { }");
        
        var parameters = new SimilarFilesParameters
        {
            FilePath = testFile,
            WorkspacePath = TestWorkspacePath,
            MaxResults = 10
        };

        // Act
        var result = await _tool.ExecuteAsync(parameters);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.Not.Null);
        Assert.That(result.Error.Code, Is.EqualTo("FILE_NOT_IN_WORKSPACE"));
    }

    [Test]
    public async Task ExecuteAsync_NoIndexExists_ReturnsError()
    {
        // Arrange
        var testFile = Path.Combine(TestWorkspacePath, "test.cs");
        File.WriteAllText(testFile, "public class TestClass { }");
        
        var parameters = new SimilarFilesParameters
        {
            FilePath = testFile,
            WorkspacePath = TestWorkspacePath,
            MaxResults = 10
        };

        LuceneIndexServiceMock.Setup(x => x.IndexExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _tool.ExecuteAsync(parameters);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.Not.Null);
        Assert.That(result.Error.Code, Is.EqualTo("NO_INDEX"));
    }

    [Test]
    public async Task ExecuteAsync_WithCaching_ReturnsCachedResult()
    {
        // Arrange
        var testFile = Path.Combine(TestWorkspacePath, "test.cs");
        File.WriteAllText(testFile, "public class TestClass { }");
        
        var parameters = new SimilarFilesParameters
        {
            FilePath = testFile,
            WorkspacePath = TestWorkspacePath,
            MaxResults = 10,
            NoCache = false
        };

        var cachedResponse = new AIOptimizedResponse<SimilarFilesResult>
        {
            Success = true,
            Data = new AIResponseData<SimilarFilesResult>
            {
                Results = new SimilarFilesResult
                {
                    Success = true,
                    QueryFile = testFile,
                    TotalMatches = 5
                }
            },
            Meta = new AIResponseMeta()
        };

        ResponseCacheServiceMock.Setup(x => x.GetAsync<AIOptimizedResponse<SimilarFilesResult>>(It.IsAny<string>()))
            .ReturnsAsync(cachedResponse);

        // Act
        var result = await _tool.ExecuteAsync(parameters);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True);
        Assert.That((bool)result.Meta?.ExtensionData?["cacheHit"]!, Is.True);
        LuceneIndexServiceMock.Verify(x => x.IndexExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_WithMinScore_FiltersResults()
    {
        // Arrange
        var testFile = Path.Combine(TestWorkspacePath, "test.cs");
        File.WriteAllText(testFile, "public class TestClass { }");
        
        var parameters = new SimilarFilesParameters
        {
            FilePath = testFile,
            WorkspacePath = TestWorkspacePath,
            MaxResults = 10,
            MinScore = 0.5f
        };

        LuceneIndexServiceMock.Setup(x => x.IndexExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        SetupTestIndex();

        // Act
        var result = await _tool.ExecuteAsync(parameters);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True);
        if (result.Data?.Results?.Files != null && result.Data.Results.Files.Any())
        {
            Assert.That(result.Data.Results.Files.All(file => file.Score >= 0.5f), Is.True);
        }
    }

    [Test]
    public async Task ExecuteAsync_NoSimilarFilesFound_ReturnsEmptyResult()
    {
        // Arrange
        var testFile = Path.Combine(TestWorkspacePath, "unique.cs");
        File.WriteAllText(testFile, "// Unique content that won't match anything");
        
        var parameters = new SimilarFilesParameters
        {
            FilePath = testFile,
            WorkspacePath = TestWorkspacePath,
            MaxResults = 10
        };

        LuceneIndexServiceMock.Setup(x => x.IndexExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        SetupEmptyIndex();

        // Act
        var result = await _tool.ExecuteAsync(parameters);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True);
        Assert.That(result.Data?.Results, Is.Not.Null);
        Assert.That(result.Data.Results.TotalMatches, Is.EqualTo(0));
        Assert.That(result.Insights, Is.Not.Null);
        Assert.That(result.Insights[0], Does.Contain("No similar files"));
    }

    [Test]
    public async Task ExecuteAsync_ResponseModes_AffectOutput()
    {
        // Arrange
        var testFile = Path.Combine(TestWorkspacePath, "test.cs");
        File.WriteAllText(testFile, "public class TestClass { }");
        
        var summaryParams = new SimilarFilesParameters
        {
            FilePath = testFile,
            WorkspacePath = TestWorkspacePath,
            ResponseMode = "summary",
            MaxResults = 100
        };

        var fullParams = new SimilarFilesParameters
        {
            FilePath = testFile,
            WorkspacePath = TestWorkspacePath,
            ResponseMode = "full",
            MaxResults = 100
        };

        LuceneIndexServiceMock.Setup(x => x.IndexExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        SetupTestIndexWithManyFiles();

        // Act
        var summaryResult = await _tool.ExecuteAsync(summaryParams);
        var fullResult = await _tool.ExecuteAsync(fullParams);

        // Assert
        Assert.That(summaryResult?.Data?.Results, Is.Not.Null);
        Assert.That(fullResult?.Data?.Results, Is.Not.Null);
        
        // Summary mode should have fewer files (max 5)
        Assert.That(summaryResult.Data.Results.Files.Count, Is.LessThanOrEqualTo(5));
        // Full mode should have more files (max 20)
        Assert.That(fullResult.Data.Results.Files.Count, Is.LessThanOrEqualTo(20));
    }

    [Test]
    public void Name_ReturnsCorrectToolName()
    {
        Assert.That(_tool.Name, Is.EqualTo("similar_files"));
    }

    [Test]
    public void Description_ReturnsCorrectDescription()
    {
        Assert.That(_tool.Description.ToLower(), Does.Contain("similar"));
        Assert.That(_tool.Description.ToLower(), Does.Contain("existing"));
    }

    [Test]
    public void Category_ReturnsQueryCategory()
    {
        Assert.That(_tool.Category, Is.EqualTo(COA.Mcp.Framework.ToolCategory.Query));
    }

    private void SetupTestIndex()
    {
        using (var writer = new IndexWriter(_testDirectory, new IndexWriterConfig(LuceneVersion.LUCENE_48, CodeAnalyzer)))
        {
            // Add some test documents
            writer.AddDocument(CreateDocument("test.cs", "public class TestClass { public void Method() { } }"));
            writer.AddDocument(CreateDocument("similar1.cs", "public class SimilarClass { public void Method() { } }"));
            writer.AddDocument(CreateDocument("similar2.cs", "public class AnotherClass { public void Method() { } }"));
            writer.AddDocument(CreateDocument("different.cs", "import React from 'react'; const Component = () => {};"));
            writer.Commit();
        }
    }

    private void SetupEmptyIndex()
    {
        using (var writer = new IndexWriter(_testDirectory, new IndexWriterConfig(LuceneVersion.LUCENE_48, CodeAnalyzer)))
        {
            // Add only the query file
            writer.AddDocument(CreateDocument("unique.cs", "// Unique content that won't match anything"));
            writer.Commit();
        }
    }

    private void SetupTestIndexWithManyFiles()
    {
        using (var writer = new IndexWriter(_testDirectory, new IndexWriterConfig(LuceneVersion.LUCENE_48, CodeAnalyzer)))
        {
            // Add the main test file
            writer.AddDocument(CreateDocument("test.cs", "public class TestClass { public void Method() { } }"));
            
            // Add many similar files
            for (int i = 1; i <= 30; i++)
            {
                writer.AddDocument(CreateDocument($"similar{i}.cs", $"public class SimilarClass{i} {{ public void Method() {{ }} }}"));
            }
            writer.Commit();
        }
    }

    private Document CreateDocument(string path, string content)
    {
        var doc = new Document();
        doc.Add(new StringField("path", path, Field.Store.YES));
        doc.Add(new TextField("fileName", Path.GetFileName(path), Field.Store.YES));
        doc.Add(new TextField("content", content, Field.Store.YES));
        return doc;
    }

    [TearDown]
    public override void TearDown()
    {
        _testDirectory?.Dispose();
    }

    protected override void OnTearDown()
    {
        base.OnTearDown();
    }
}