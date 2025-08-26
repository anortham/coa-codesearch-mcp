using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using COA.CodeSearch.McpServer.Controllers;
using COA.CodeSearch.McpServer.Models.Api;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Services.Lucene;

namespace COA.CodeSearch.McpServer.Tests.Controllers;

/// <summary>
/// Tests for WorkspaceController - focusing on the NEW workspace path resolution functionality
/// These tests verify that the API returns actual workspace paths instead of hashed directory names
/// </summary>
[TestFixture]
public class WorkspaceControllerTests
{
    private Mock<ILuceneIndexService> _mockLuceneService;
    private Mock<IFileIndexingService> _mockFileIndexingService;
    private Mock<IPathResolutionService> _mockPathResolver;
    private Mock<ILogger<WorkspaceController>> _mockLogger;
    private WorkspaceController _controller;

    [SetUp]
    public void SetUp()
    {
        _mockLuceneService = new Mock<ILuceneIndexService>();
        _mockFileIndexingService = new Mock<IFileIndexingService>();
        _mockPathResolver = new Mock<IPathResolutionService>();
        _mockLogger = new Mock<ILogger<WorkspaceController>>();

        _controller = new WorkspaceController(
            _mockLuceneService.Object,
            _mockFileIndexingService.Object,
            _mockPathResolver.Object,
            _mockLogger.Object);
    }

    #region NEW: Path Resolution Tests for ListWorkspaces API

    [Test]
    public async Task ListWorkspaces_WithValidMetadata_ShouldReturnResolvedWorkspacePaths()
    {
        // Arrange - Setup mock behavior for path resolution
        var indexRootPath = Path.Combine(Path.GetTempPath(), "test_indexes");
        var hashedDirName1 = "my_project_abcd1234";
        var hashedDirName2 = "another_workspace_5678efgh";
        var indexDir1 = Path.Combine(indexRootPath, hashedDirName1);
        var indexDir2 = Path.Combine(indexRootPath, hashedDirName2);
        
        var originalPath1 = "C:\\source\\MyProject";
        var originalPath2 = "C:\\source\\AnotherWorkspace";

        // Create temporary directories to simulate existing indexes
        try
        {
            Directory.CreateDirectory(indexRootPath);
            Directory.CreateDirectory(indexDir1);
            Directory.CreateDirectory(indexDir2);

            // Create metadata files
            var metadata1 = new WorkspaceIndexInfo
            {
                OriginalPath = originalPath1,
                HashPath = "abcd1234",
                CreatedAt = DateTime.UtcNow,
                LastAccessed = DateTime.UtcNow,
                LastModified = DateTime.UtcNow,
                DocumentCount = 150,
                IndexSizeBytes = 2048000
            };

            var metadata2 = new WorkspaceIndexInfo
            {
                OriginalPath = originalPath2,
                HashPath = "5678efgh",
                CreatedAt = DateTime.UtcNow,
                LastAccessed = DateTime.UtcNow,
                LastModified = DateTime.UtcNow,
                DocumentCount = 200,
                IndexSizeBytes = 1024000
            };

            var metadataFile1 = Path.Combine(indexDir1, "workspace_metadata.json");
            var metadataFile2 = Path.Combine(indexDir2, "workspace_metadata.json");
            
            var json1 = System.Text.Json.JsonSerializer.Serialize(metadata1, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            var json2 = System.Text.Json.JsonSerializer.Serialize(metadata2, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            
            File.WriteAllText(metadataFile1, json1);
            File.WriteAllText(metadataFile2, json2);

            // Setup mocks
            _mockPathResolver.Setup(p => p.GetIndexRootPath()).Returns(indexRootPath);
            _mockPathResolver.Setup(p => p.TryResolveWorkspacePath(indexDir1)).Returns(originalPath1);
            _mockPathResolver.Setup(p => p.TryResolveWorkspacePath(indexDir2)).Returns(originalPath2);

            // Act - Call ListWorkspaces API
            var result = await _controller.ListWorkspaces();

            // Assert - Should return resolved workspace paths, NOT hashed directory names
            Assert.Multiple(() =>
            {
                Assert.That(result, Is.Not.Null, "Result should not be null");
                Assert.That(result.Result, Is.InstanceOf<OkObjectResult>(), "Should return OK result");
                
                var okResult = result.Result as OkObjectResult;
                Assert.That(okResult?.Value, Is.InstanceOf<WorkspacesResponse>(), "Should return WorkspacesResponse");
                
                var response = okResult?.Value as WorkspacesResponse;
                Assert.That(response, Is.Not.Null, "Response should not be null");
                Assert.That(response.Workspaces.Count, Is.EqualTo(2), "Should return 2 workspaces");
                
                // CRITICAL: Verify that API returns actual workspace paths, not hashed directory names
                var workspace1 = response.Workspaces.FirstOrDefault(w => w.Path == originalPath1);
                var workspace2 = response.Workspaces.FirstOrDefault(w => w.Path == originalPath2);
                
                Assert.That(workspace1, Is.Not.Null, $"Should contain workspace with path {originalPath1}");
                Assert.That(workspace2, Is.Not.Null, $"Should contain workspace with path {originalPath2}");
                
                // Verify no hashed directory names are returned
                Assert.That(response.Workspaces.Any(w => w.Path == hashedDirName1), Is.False, 
                    "Should not return hashed directory name as workspace path");
                Assert.That(response.Workspaces.Any(w => w.Path == hashedDirName2), Is.False, 
                    "Should not return hashed directory name as workspace path");
                
                // Verify metadata was used correctly
                Assert.That(workspace1?.FileCount, Is.EqualTo(150), "Should use file count from metadata");
                Assert.That(workspace2?.FileCount, Is.EqualTo(200), "Should use file count from metadata");
            });

            // Verify path resolution was called for each directory
            _mockPathResolver.Verify(p => p.TryResolveWorkspacePath(indexDir1), Times.Once, 
                "Should attempt to resolve path for first index directory");
            _mockPathResolver.Verify(p => p.TryResolveWorkspacePath(indexDir2), Times.Once, 
                "Should attempt to resolve path for second index directory");
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(indexRootPath))
                Directory.Delete(indexRootPath, true);
        }
    }

    [Test]
    public async Task ListWorkspaces_WhenPathResolutionFails_ShouldFallbackToDirectoryName()
    {
        // Arrange - Setup scenario where path resolution fails
        var indexRootPath = Path.Combine(Path.GetTempPath(), "test_indexes_fallback");
        var hashedDirName = "unresolvable_workspace_abcd1234";
        var indexDir = Path.Combine(indexRootPath, hashedDirName);

        try
        {
            Directory.CreateDirectory(indexRootPath);
            Directory.CreateDirectory(indexDir);

            // Setup mocks - path resolution returns null (failed to resolve)
            _mockPathResolver.Setup(p => p.GetIndexRootPath()).Returns(indexRootPath);
            _mockPathResolver.Setup(p => p.TryResolveWorkspacePath(indexDir)).Returns((string?)null);

            // Act - Call ListWorkspaces API
            var result = await _controller.ListWorkspaces();

            // Assert - Should fallback to directory name when resolution fails
            Assert.Multiple(() =>
            {
                var okResult = result.Result as OkObjectResult;
                var response = okResult?.Value as WorkspacesResponse;
                
                Assert.That(response, Is.Not.Null, "Should return valid response");
                Assert.That(response.Workspaces.Count, Is.EqualTo(1), "Should return 1 workspace");
                
                var workspace = response.Workspaces.First();
                Assert.That(workspace.Path, Is.EqualTo($"[Unresolved: {hashedDirName}]"), 
                    "Should fallback to clearly marked unresolved path when path resolution fails");
            });

            // Verify path resolution was attempted
            _mockPathResolver.Verify(p => p.TryResolveWorkspacePath(indexDir), Times.Once, 
                "Should attempt to resolve workspace path");
        }
        finally
        {
            if (Directory.Exists(indexRootPath))
                Directory.Delete(indexRootPath, true);
        }
    }

    [Test]
    public async Task ListWorkspaces_WithMissingMetadataButValidDirectoryStructure_ShouldUseResolvedPath()
    {
        // Arrange - Directory exists but no metadata file
        var indexRootPath = Path.Combine(Path.GetTempPath(), "test_indexes_no_metadata");
        var hashedDirName = "project_without_metadata_1234abcd";
        var indexDir = Path.Combine(indexRootPath, hashedDirName);
        var resolvedPath = "C:\\source\\ProjectWithoutMetadata";

        try
        {
            Directory.CreateDirectory(indexRootPath);
            Directory.CreateDirectory(indexDir);
            // No metadata file created

            // Setup mocks - path resolution succeeds despite missing metadata
            _mockPathResolver.Setup(p => p.GetIndexRootPath()).Returns(indexRootPath);
            _mockPathResolver.Setup(p => p.TryResolveWorkspacePath(indexDir)).Returns(resolvedPath);

            // Act - Call ListWorkspaces API
            var result = await _controller.ListWorkspaces();

            // Assert - Should use resolved path even without metadata
            Assert.Multiple(() =>
            {
                var okResult = result.Result as OkObjectResult;
                var response = okResult?.Value as WorkspacesResponse;
                
                Assert.That(response, Is.Not.Null, "Should return valid response");
                Assert.That(response.Workspaces.Count, Is.EqualTo(1), "Should return 1 workspace");
                
                var workspace = response.Workspaces.First();
                Assert.That(workspace.Path, Is.EqualTo(resolvedPath), 
                    "Should use resolved path even without metadata file");
                Assert.That(workspace.IsIndexed, Is.True, "Should mark as indexed");
            });
        }
        finally
        {
            if (Directory.Exists(indexRootPath))
                Directory.Delete(indexRootPath, true);
        }
    }

    [Test]
    public async Task ListWorkspaces_WithCorruptedMetadata_ShouldHandleGracefully()
    {
        // Arrange - Directory with corrupted metadata file
        var indexRootPath = Path.Combine(Path.GetTempPath(), "test_indexes_corrupted");
        var hashedDirName = "corrupted_metadata_workspace_abcd1234";
        var indexDir = Path.Combine(indexRootPath, hashedDirName);
        var metadataFile = Path.Combine(indexDir, "workspace_metadata.json");
        var resolvedPath = "C:\\source\\CorruptedMetadataWorkspace";

        try
        {
            Directory.CreateDirectory(indexRootPath);
            Directory.CreateDirectory(indexDir);
            
            // Create corrupted metadata file
            File.WriteAllText(metadataFile, "{invalid json content}");

            // Setup mocks
            _mockPathResolver.Setup(p => p.GetIndexRootPath()).Returns(indexRootPath);
            _mockPathResolver.Setup(p => p.TryResolveWorkspacePath(indexDir)).Returns(resolvedPath);

            // Act - Call ListWorkspaces API
            var result = await _controller.ListWorkspaces();

            // Assert - Should handle corrupted metadata gracefully
            Assert.Multiple(() =>
            {
                var okResult = result.Result as OkObjectResult;
                var response = okResult?.Value as WorkspacesResponse;
                
                Assert.That(response, Is.Not.Null, "Should return valid response despite corrupted metadata");
                Assert.That(response.Workspaces.Count, Is.EqualTo(1), "Should return 1 workspace");
                
                var workspace = response.Workspaces.First();
                Assert.That(workspace.Path, Is.EqualTo(resolvedPath), 
                    "Should use resolved path when metadata is corrupted");
                
                // Should fallback to directory-based file count calculation
                Assert.That(workspace.FileCount, Is.GreaterThanOrEqualTo(0), 
                    "Should calculate file count from directory when metadata is corrupted");
            });
        }
        finally
        {
            if (Directory.Exists(indexRootPath))
                Directory.Delete(indexRootPath, true);
        }
    }

    [Test]
    public async Task ListWorkspaces_WithEmptyIndexDirectory_ShouldReturnEmptyList()
    {
        // Arrange - Empty index root directory
        var indexRootPath = Path.Combine(Path.GetTempPath(), "empty_index_root");

        try
        {
            Directory.CreateDirectory(indexRootPath);
            // No subdirectories created

            // Setup mocks
            _mockPathResolver.Setup(p => p.GetIndexRootPath()).Returns(indexRootPath);

            // Act - Call ListWorkspaces API
            var result = await _controller.ListWorkspaces();

            // Assert - Should return empty workspace list
            Assert.Multiple(() =>
            {
                var okResult = result.Result as OkObjectResult;
                var response = okResult?.Value as WorkspacesResponse;
                
                Assert.That(response, Is.Not.Null, "Should return valid response");
                Assert.That(response.Workspaces, Is.Empty, "Should return empty workspace list");
                Assert.That(response.TotalCount, Is.EqualTo(0), "Should have total count of 0");
            });
        }
        finally
        {
            if (Directory.Exists(indexRootPath))
                Directory.Delete(indexRootPath, true);
        }
    }

    [Test]
    public async Task ListWorkspaces_WithNonExistentIndexDirectory_ShouldReturnEmptyList()
    {
        // Arrange - Non-existent index root directory
        var nonExistentPath = Path.Combine(Path.GetTempPath(), "non_existent_index_root");

        // Setup mocks
        _mockPathResolver.Setup(p => p.GetIndexRootPath()).Returns(nonExistentPath);

        // Act - Call ListWorkspaces API
        var result = await _controller.ListWorkspaces();

        // Assert - Should return empty workspace list gracefully
        Assert.Multiple(() =>
        {
            var okResult = result.Result as OkObjectResult;
            var response = okResult?.Value as WorkspacesResponse;
            
            Assert.That(response, Is.Not.Null, "Should return valid response");
            Assert.That(response.Workspaces, Is.Empty, "Should return empty workspace list");
            Assert.That(response.TotalCount, Is.EqualTo(0), "Should have total count of 0");
        });
    }

    #endregion

    #region Integration Test - Complete Path Resolution Workflow

    [Test]
    public async Task ListWorkspaces_EndToEndWorkflow_ShouldReturnActualPathsNotHashedNames()
    {
        // Arrange - This test simulates the complete workflow:
        // 1. Workspace is indexed (creates hashed directory)
        // 2. Metadata is stored during indexing  
        // 3. API is called to list workspaces
        // 4. Path resolution resolves hashed names back to original paths
        // 5. API returns actual workspace paths, not hashed directory names
        
        var indexRootPath = Path.Combine(Path.GetTempPath(), "integration_test_indexes");
        var originalWorkspace1 = "C:\\source\\MyMainProject";
        var originalWorkspace2 = "C:\\Users\\Developer\\Documents\\SecondProject";
        
        // These would be the actual hashed directory names created by GetIndexPath()
        var hashedDir1 = "mymainproject_ab12cd34";
        var hashedDir2 = "secondproject_ef56gh78";
        
        var indexDir1 = Path.Combine(indexRootPath, hashedDir1);
        var indexDir2 = Path.Combine(indexRootPath, hashedDir2);

        try
        {
            // Step 1: Simulate workspace indexing by creating directory structure
            Directory.CreateDirectory(indexRootPath);
            Directory.CreateDirectory(indexDir1);
            Directory.CreateDirectory(indexDir2);

            // Step 2: Simulate metadata storage during indexing
            var metadata1 = new WorkspaceIndexInfo
            {
                OriginalPath = originalWorkspace1,
                HashPath = "ab12cd34",
                CreatedAt = DateTime.UtcNow.AddDays(-5),
                LastAccessed = DateTime.UtcNow.AddHours(-2),
                LastModified = DateTime.UtcNow.AddHours(-1),
                DocumentCount = 1250,
                IndexSizeBytes = 15728640
            };

            var metadata2 = new WorkspaceIndexInfo
            {
                OriginalPath = originalWorkspace2,
                HashPath = "ef56gh78",
                CreatedAt = DateTime.UtcNow.AddDays(-3),
                LastAccessed = DateTime.UtcNow.AddMinutes(-30),
                LastModified = DateTime.UtcNow.AddMinutes(-15),
                DocumentCount = 890,
                IndexSizeBytes = 9437184
            };

            File.WriteAllText(
                Path.Combine(indexDir1, "workspace_metadata.json"), 
                System.Text.Json.JsonSerializer.Serialize(metadata1, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            
            File.WriteAllText(
                Path.Combine(indexDir2, "workspace_metadata.json"), 
                System.Text.Json.JsonSerializer.Serialize(metadata2, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            // Step 3: Setup mocks to simulate PathResolutionService behavior
            _mockPathResolver.Setup(p => p.GetIndexRootPath()).Returns(indexRootPath);
            _mockPathResolver.Setup(p => p.TryResolveWorkspacePath(indexDir1)).Returns(originalWorkspace1);
            _mockPathResolver.Setup(p => p.TryResolveWorkspacePath(indexDir2)).Returns(originalWorkspace2);

            // Step 4: Call API to list workspaces
            var result = await _controller.ListWorkspaces();

            // Step 5: Assert that API returns original workspace paths, NOT hashed directory names
            Assert.Multiple(() =>
            {
                var okResult = result.Result as OkObjectResult;
                var response = okResult?.Value as WorkspacesResponse;
                
                Assert.That(response, Is.Not.Null, "API should return valid response");
                Assert.That(response.Workspaces.Count, Is.EqualTo(2), "Should return 2 indexed workspaces");
                Assert.That(response.TotalCount, Is.EqualTo(2), "Total count should match workspace count");

                // CRITICAL ASSERTION: API must return actual workspace paths
                var paths = response.Workspaces.Select(w => w.Path).ToList();
                Assert.That(paths, Contains.Item(originalWorkspace1), 
                    "API must return the original workspace path, not hashed directory name");
                Assert.That(paths, Contains.Item(originalWorkspace2), 
                    "API must return the original workspace path, not hashed directory name");

                // CRITICAL ASSERTION: API must NOT return hashed directory names  
                Assert.That(paths, Does.Not.Contain(hashedDir1), 
                    "API must NOT return hashed directory names to users");
                Assert.That(paths, Does.Not.Contain(hashedDir2), 
                    "API must NOT return hashed directory names to users");

                // Verify metadata was correctly used
                var workspace1 = response.Workspaces.First(w => w.Path == originalWorkspace1);
                var workspace2 = response.Workspaces.First(w => w.Path == originalWorkspace2);
                
                Assert.That(workspace1.FileCount, Is.EqualTo(1250), "Should use document count from metadata");
                Assert.That(workspace2.FileCount, Is.EqualTo(890), "Should use document count from metadata");
                Assert.That(workspace1.IndexSizeBytes, Is.EqualTo(15728640), "Should use index size from metadata");
                Assert.That(workspace2.IndexSizeBytes, Is.EqualTo(9437184), "Should use index size from metadata");
            });

            // Verify the complete path resolution workflow was executed
            _mockPathResolver.Verify(p => p.GetIndexRootPath(), Times.Once, 
                "Should get index root path to find workspace directories");
            _mockPathResolver.Verify(p => p.TryResolveWorkspacePath(It.IsAny<string>()), Times.Exactly(2), 
                "Should attempt path resolution for each indexed workspace");
        }
        finally
        {
            if (Directory.Exists(indexRootPath))
                Directory.Delete(indexRootPath, true);
        }
    }

    #endregion

    #region Test Documentation

    /// <summary>
    /// This test class verifies the CRITICAL fix for workspace path resolution in the API.
    /// 
    /// PROBLEM SOLVED: Before this fix, the WorkspaceController.ListWorkspaces() API returned 
    /// hashed directory names like "coa_codesearch_mcp_4785ab0f" instead of actual workspace 
    /// paths like "C:\source\COA CodeSearch MCP".
    /// 
    /// SOLUTION IMPLEMENTED:
    /// 1. PathResolutionService.TryResolveWorkspacePath() - Resolves hashed names to original paths
    /// 2. PathResolutionService.StoreWorkspaceMetadata() - Stores metadata during indexing
    /// 3. WorkspaceController.ListWorkspaces() - Uses path resolution for API responses
    /// 
    /// WORKFLOW TESTED:
    /// 1. Workspace indexing creates hashed directory name for uniqueness
    /// 2. Metadata file stores original workspace path for resolution
    /// 3. API calls TryResolveWorkspacePath() to get original path from hashed directory
    /// 4. API returns human-readable workspace paths to users
    /// 
    /// CRITICAL REQUIREMENTS VERIFIED:
    /// - API returns actual workspace paths (C:\source\MyProject)
    /// - API does NOT return hashed directory names (myproject_ab12cd34)
    /// - Metadata-based resolution works correctly
    /// - Fallback behavior works when metadata is missing or corrupted
    /// - Error handling works for edge cases
    /// 
    /// This ensures users see meaningful workspace paths in all tools and interfaces
    /// that consume the CodeSearch API.
    /// </summary>

    #endregion
}