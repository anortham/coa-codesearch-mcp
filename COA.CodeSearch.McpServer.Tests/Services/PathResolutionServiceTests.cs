using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using COA.CodeSearch.McpServer.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace COA.CodeSearch.McpServer.Tests.Services;

[TestFixture]
public class PathResolutionServiceTests
{
    private PathResolutionService _service = null!;
    private Mock<IConfiguration> _mockConfiguration = null!;
    private Mock<ILogger<PathResolutionService>> _mockLogger = null!;
    private string _testBasePath = null!;

    [SetUp]
    public void SetUp()
    {
        _mockConfiguration = new Mock<IConfiguration>();
        _mockLogger = new Mock<ILogger<PathResolutionService>>();
        _testBasePath = Path.Combine(Path.GetTempPath(), "codesearch-test", Guid.NewGuid().ToString());
        
        // Setup configuration for hybrid model - configure primary workspace
        _mockConfiguration.Setup(c => c["CodeSearch:PrimaryWorkspace"])
            .Returns(_testBasePath);
        
        // Also setup base path config for any legacy paths
        _mockConfiguration.Setup(c => c[PathConstants.BasePathConfigKey])
            .Returns(_testBasePath);
        
        _service = new PathResolutionService(_mockConfiguration.Object, _mockLogger.Object);
    }

    [TearDown]
    public void TearDown()
    {
        // Clean up test directories
        if (Directory.Exists(_testBasePath))
        {
            try
            {
                Directory.Delete(_testBasePath, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    #region Core Path Resolution Tests

    [Test]
    public void GetBasePath_Should_Return_Consistent_Path()
    {
        // Act
        var path1 = _service.GetBasePath();
        var path2 = _service.GetBasePath();

        // Assert
        Assert.That(path1, Is.Not.Null.And.Not.Empty);
        Assert.That(path2, Is.EqualTo(path1), "Path should be consistent across calls");
        // In hybrid model, GetBasePath returns {primaryWorkspace}/.coa/codesearch
        Assert.That(path1, Does.Contain("codesearch-test"), "Path should contain our test directory");
        Assert.That(path1, Does.Contain(".coa"), "Path should contain .coa directory");
        Assert.That(path1, Does.EndWith("codesearch"), "Path should end with codesearch");
    }

    [Test]
    public void GetIndexPath_Should_Return_Hashed_Directory_Name()
    {
        // Arrange - Use platform-appropriate path
        var workspace = Path.Combine(Path.GetTempPath(), "source", "MyProject");

        // Act
        var indexPath = _service.GetIndexPath(workspace);

        // Assert
        Assert.That(indexPath, Is.Not.Null.And.Not.Empty);
        Assert.That(indexPath, Does.Contain("indexes"), "Should be in indexes directory");
        Assert.That(indexPath, Does.Not.Contain("MyProject"), "Should not contain original workspace name");
        Assert.That(Path.GetFileName(indexPath), Does.Match(@"^myproject_[a-f0-9]{8}$"), 
            "Should follow pattern: projectname_hash with 8-char hash");
    }

    [Test]
    public void GetIndexPath_Should_Be_Deterministic()
    {
        // Arrange
        var workspace = @"C:\source\TestProject";

        // Act
        var path1 = _service.GetIndexPath(workspace);
        var path2 = _service.GetIndexPath(workspace);

        // Assert
        Assert.That(path2, Is.EqualTo(path1), "Same workspace should always produce same index path");
    }

    [Test]
    public void GetIndexPath_Should_Handle_Different_Cases_As_Same_Workspace()
    {
        // Arrange - Use platform-appropriate paths with case differences
        var workspace1 = Path.Combine(Path.GetTempPath(), "source", "MyProject");
        var workspace2 = Path.Combine(Path.GetTempPath(), "source", "myproject");

        // Act
        var path1 = _service.GetIndexPath(workspace1);
        var path2 = _service.GetIndexPath(workspace2);

        // Assert
        // In hybrid model, GetSafeWorkspaceName normalizes to lowercase,
        // so case variations should produce the same index path
        Assert.That(path2, Is.EqualTo(path1), "Case variations should produce same index path due to normalization");
        
        // Both should end with the same normalized directory name
        var dir1 = Path.GetFileName(path1);
        var dir2 = Path.GetFileName(path2);
        Assert.That(dir2, Is.EqualTo(dir1), "Directory names should be identical after normalization");
    }

    [Test]
    public void ComputeWorkspaceHash_Should_Generate_8_Character_Hash()
    {
        // Arrange
        var workspace = @"C:\source\SomeProject";

        // Act
        var hash = _service.ComputeWorkspaceHash(workspace);

        // Assert
        Assert.That(hash, Is.Not.Null);
        // The hash is now 8 characters based on PathConstants.WorkspaceHashLength
        Assert.That(hash.Length, Is.EqualTo(PathConstants.WorkspaceHashLength), "Hash should be 8 characters");
        Assert.That(hash, Does.Match("^[a-f0-9]{8}$"), "Hash should be lowercase hexadecimal");
    }

    [Test]
    public void ComputeWorkspaceHash_Should_Be_Deterministic()
    {
        // Arrange
        var workspace = @"C:\source\ConsistentProject";

        // Act
        var hash1 = _service.ComputeWorkspaceHash(workspace);
        var hash2 = _service.ComputeWorkspaceHash(workspace);

        // Assert
        Assert.That(hash2, Is.EqualTo(hash1), "Same input should produce same hash");
    }

    [Test]
    public void GetLogsPath_Should_Return_Logs_Directory()
    {
        // Act
        var logsPath = _service.GetLogsPath();

        // Assert
        Assert.That(logsPath, Is.Not.Null.And.Not.Empty);
        Assert.That(logsPath, Does.EndWith("logs"), "Should end with 'logs' directory");
        Assert.That(logsPath, Does.Contain("codesearch-test"), "Should be under our test directory");
    }

    [Test]
    public void GetIndexRootPath_Should_Return_Root_Index_Directory()
    {
        // Act
        var indexRoot = _service.GetIndexRootPath();

        // Assert
        Assert.That(indexRoot, Is.Not.Null.And.Not.Empty);
        Assert.That(indexRoot, Does.EndWith("indexes"), "Should end with 'indexes' directory");
        Assert.That(indexRoot, Does.Contain("codesearch-test"), "Should be under our test directory");
        Assert.That(indexRoot, Does.Contain(".coa"), "Should contain .coa directory");
    }

    #endregion

    #region Directory Management Tests

    [Test]
    public void EnsureDirectoryExists_Should_Create_Directory_If_Not_Exists()
    {
        // Arrange
        var testDir = Path.Combine(_testBasePath, "new-directory");
        Assert.That(Directory.Exists(testDir), Is.False, "Directory should not exist initially");

        // Act
        _service.EnsureDirectoryExists(testDir);

        // Assert
        Assert.That(Directory.Exists(testDir), Is.True, "Directory should be created");
    }

    [Test]
    public void EnsureDirectoryExists_Should_Handle_Existing_Directory()
    {
        // Arrange
        var testDir = Path.Combine(_testBasePath, "existing-directory");
        Directory.CreateDirectory(testDir);
        Assert.That(Directory.Exists(testDir), Is.True, "Directory should exist");

        // Act & Assert - Should not throw
        Assert.DoesNotThrow(() => _service.EnsureDirectoryExists(testDir));
        Assert.That(Directory.Exists(testDir), Is.True, "Directory should still exist");
    }

    [Test]
    public void EnsureDirectoryExists_Should_Create_Nested_Directories()
    {
        // Arrange
        var nestedDir = Path.Combine(_testBasePath, "level1", "level2", "level3");
        Assert.That(Directory.Exists(nestedDir), Is.False, "Nested directory should not exist");

        // Act
        _service.EnsureDirectoryExists(nestedDir);

        // Assert
        Assert.That(Directory.Exists(nestedDir), Is.True, "Nested directory should be created");
    }

    #endregion

    #region Safe File System Operations Tests

    [Test]
    public void DirectoryExists_Should_Return_True_For_Existing_Directory()
    {
        // Arrange
        Directory.CreateDirectory(_testBasePath);

        // Act
        var exists = _service.DirectoryExists(_testBasePath);

        // Assert
        Assert.That(exists, Is.True);
    }

    [Test]
    public void DirectoryExists_Should_Return_False_For_NonExistent_Directory()
    {
        // Arrange
        var nonExistentDir = Path.Combine(_testBasePath, "does-not-exist");

        // Act
        var exists = _service.DirectoryExists(nonExistentDir);

        // Assert
        Assert.That(exists, Is.False);
    }

    [Test]
    public void DirectoryExists_Should_Handle_Null_Path()
    {
        // Act
        var exists = _service.DirectoryExists(null!);

        // Assert
        Assert.That(exists, Is.False);
    }

    [Test]
    public void FileExists_Should_Return_True_For_Existing_File()
    {
        // Arrange
        Directory.CreateDirectory(_testBasePath);
        var testFile = Path.Combine(_testBasePath, "test.txt");
        File.WriteAllText(testFile, "test content");

        // Act
        var exists = _service.FileExists(testFile);

        // Assert
        Assert.That(exists, Is.True);
    }

    [Test]
    public void FileExists_Should_Return_False_For_NonExistent_File()
    {
        // Arrange
        var nonExistentFile = Path.Combine(_testBasePath, "does-not-exist.txt");

        // Act
        var exists = _service.FileExists(nonExistentFile);

        // Assert
        Assert.That(exists, Is.False);
    }

    [Test]
    public void GetFullPath_Should_Return_Absolute_Path()
    {
        // Arrange
        var relativePath = "..\\some\\path";

        // Act
        var fullPath = _service.GetFullPath(relativePath);

        // Assert
        Assert.That(fullPath, Is.Not.Null);
        Assert.That(Path.IsPathRooted(fullPath), Is.True, "Should return absolute path");
    }

    [Test]
    public void GetFullPath_Should_Handle_Relative_Path()
    {
        // Arrange
        var relativePath = "test.txt";

        // Act
        var result = _service.GetFullPath(relativePath);

        // Assert
        // Path.GetFullPath will convert relative paths to absolute paths
        Assert.That(result, Is.Not.Null);
        Assert.That(Path.IsPathRooted(result), Is.True, "Should convert to absolute path");
        Assert.That(result, Does.EndWith("test.txt"), "Should preserve filename");
    }

    [Test]
    public void GetFileName_Should_Extract_File_Name()
    {
        // Arrange - Use platform-appropriate path
        var filePath = Path.Combine("some", "directory", "file.txt");

        // Act
        var fileName = _service.GetFileName(filePath);

        // Assert
        Assert.That(fileName, Is.EqualTo("file.txt"));
    }

    [Test]
    public void GetFileName_Should_Handle_Invalid_Path()
    {
        // Arrange
        string? invalidPath = null;

        // Act
        var fileName = _service.GetFileName(invalidPath!);

        // Assert
        Assert.That(fileName, Is.Empty);
    }

    [Test]
    public void GetExtension_Should_Extract_File_Extension()
    {
        // Arrange
        var filePath = @"C:\some\directory\file.txt";

        // Act
        var extension = _service.GetExtension(filePath);

        // Assert
        Assert.That(extension, Is.EqualTo(".txt"));
    }

    [Test]
    public void GetExtension_Should_Handle_File_Without_Extension()
    {
        // Arrange
        var filePath = @"C:\some\directory\README";

        // Act
        var extension = _service.GetExtension(filePath);

        // Assert
        Assert.That(extension, Is.Empty);
    }

    [Test]
    public void GetDirectoryName_Should_Extract_Directory_Path()
    {
        // Arrange - Use platform-appropriate path
        var filePath = Path.Combine("some", "directory", "file.txt");
        var expectedDir = Path.Combine("some", "directory");

        // Act
        var dirName = _service.GetDirectoryName(filePath);

        // Assert
        Assert.That(dirName, Is.EqualTo(expectedDir));
    }

    [Test]
    public void GetRelativePath_Should_Calculate_Relative_Path()
    {
        // Arrange - Use platform-appropriate paths
        var basePath = Path.Combine(Path.GetTempPath(), "source", "project");
        var targetPath = Path.Combine(basePath, "src", "file.cs");
        var expectedRelative = Path.Combine("src", "file.cs");

        // Act
        var relativePath = _service.GetRelativePath(basePath, targetPath);

        // Assert
        Assert.That(relativePath, Is.EqualTo(expectedRelative));
    }

    [Test]
    public void EnumerateFiles_Should_Return_Files_In_Directory()
    {
        // Arrange
        Directory.CreateDirectory(_testBasePath);
        File.WriteAllText(Path.Combine(_testBasePath, "file1.txt"), "content1");
        File.WriteAllText(Path.Combine(_testBasePath, "file2.txt"), "content2");
        File.WriteAllText(Path.Combine(_testBasePath, "file3.cs"), "content3");

        // Act
        var files = _service.EnumerateFiles(_testBasePath).ToList();

        // Assert
        Assert.That(files.Count, Is.EqualTo(3));
        Assert.That(files.Any(f => f.EndsWith("file1.txt")), Is.True);
        Assert.That(files.Any(f => f.EndsWith("file2.txt")), Is.True);
        Assert.That(files.Any(f => f.EndsWith("file3.cs")), Is.True);
    }

    [Test]
    public void EnumerateFiles_Should_Filter_By_Pattern()
    {
        // Arrange
        Directory.CreateDirectory(_testBasePath);
        File.WriteAllText(Path.Combine(_testBasePath, "file1.txt"), "content1");
        File.WriteAllText(Path.Combine(_testBasePath, "file2.txt"), "content2");
        File.WriteAllText(Path.Combine(_testBasePath, "file3.cs"), "content3");

        // Act
        var files = _service.EnumerateFiles(_testBasePath, "*.txt").ToList();

        // Assert
        Assert.That(files.Count, Is.EqualTo(2));
        Assert.That(files.All(f => f.EndsWith(".txt")), Is.True);
    }

    [Test]
    public void EnumerateFiles_Should_Handle_Invalid_Directory()
    {
        // Arrange
        var invalidDir = Path.Combine(_testBasePath, "does-not-exist");

        // Act
        var files = _service.EnumerateFiles(invalidDir).ToList();

        // Assert
        Assert.That(files, Is.Empty, "Should return empty collection for invalid directory");
    }

    [Test]
    public void EnumerateDirectories_Should_Return_Subdirectories()
    {
        // Arrange
        Directory.CreateDirectory(_testBasePath);
        Directory.CreateDirectory(Path.Combine(_testBasePath, "dir1"));
        Directory.CreateDirectory(Path.Combine(_testBasePath, "dir2"));
        Directory.CreateDirectory(Path.Combine(_testBasePath, "dir3"));

        // Act
        var dirs = _service.EnumerateDirectories(_testBasePath).ToList();

        // Assert
        Assert.That(dirs.Count, Is.EqualTo(3));
        Assert.That(dirs.Any(d => d.EndsWith("dir1")), Is.True);
        Assert.That(dirs.Any(d => d.EndsWith("dir2")), Is.True);
        Assert.That(dirs.Any(d => d.EndsWith("dir3")), Is.True);
    }

    [Test]
    public void EnumerateDirectories_Should_Handle_Invalid_Directory()
    {
        // Arrange
        var invalidDir = Path.Combine(_testBasePath, "does-not-exist");

        // Act
        var dirs = _service.EnumerateDirectories(invalidDir).ToList();

        // Assert
        Assert.That(dirs, Is.Empty, "Should return empty collection for invalid directory");
    }

    #endregion

    /// <summary>
    /// IMPORTANT NOTE ON EXCEPTION HANDLING TESTS:
    /// 
    /// The PathResolutionService uses extensive try-catch blocks to handle exceptions gracefully.
    /// When exceptions occur, the service:
    /// 1. Logs the exception using ILogger
    /// 2. Returns a safe default value (empty string, false, or empty collection)
    /// 
    /// This is by design to ensure the CodeSearch MCP server remains stable even when
    /// file system operations fail (permissions, network issues, etc.).
    /// 
    /// Since we're using a Mock<ILogger>, we could verify that logging occurs, but
    /// the current tests focus on the return values. Future improvements could add
    /// assertions marked with "TODO" to ensure exceptions are being properly logged.
    /// </summary>
}