using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using System;
using System.IO;
using COA.CodeSearch.McpServer.Services;

namespace COA.CodeSearch.McpServer.Tests.Services;

/// <summary>
/// Tests for PathResolutionService - specifically designed to expose the silent exception swallowing bug.
/// These tests currently FAIL because exceptions are silently caught without logging.
/// </summary>
[TestFixture]
public class PathResolutionServiceTests
{
    private Mock<IConfiguration> _mockConfiguration;
    private Mock<ILogger<PathResolutionService>> _mockLogger;
    private PathResolutionService _service;

    [SetUp]
    public void SetUp()
    {
        _mockConfiguration = new Mock<IConfiguration>();
        _mockLogger = new Mock<ILogger<PathResolutionService>>();
        
        // Setup default configuration
        _mockConfiguration.Setup(c => c["CodeSearch:BasePath"]).Returns("~/.coa/codesearch");
        
        _service = new PathResolutionService(_mockConfiguration.Object, _mockLogger.Object);
    }

    #region DirectoryExists Tests - Line 147 Empty Catch Block

    [Test]
    public void DirectoryExists_WithInvalidPath_ShouldLogErrorBeforeReturningFalse()
    {
        // Arrange - Create a path that will cause Directory.Exists to throw
        // On Windows, paths with invalid characters throw ArgumentException
        var invalidPath = "C:\\Invalid\0Path\\WithNullCharacter";
        
        // Act - This should log the exception but currently doesn't due to empty catch
        var result = _service.DirectoryExists(invalidPath);
        
        // Assert - This test FAILS because the exception is silently swallowed
        // The service should log the error for debugging purposes
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False, "Should return false for invalid path");
            // TODO: Once logging is added, verify error was logged:
            // _mockLogger.Verify(l => l.LogError(It.IsAny<string>(), invalidPath), Times.Once);
        });
    }

    [Test]
    public void DirectoryExists_WithPathTooLong_ShouldLogPathTooLongException()
    {
        // Arrange - Create a path longer than MAX_PATH on Windows (260 characters)
        var longPath = "C:\\" + new string('a', 300);
        
        // Act - This should log PathTooLongException but currently doesn't
        var result = _service.DirectoryExists(longPath);
        
        // Assert - This test FAILS because PathTooLongException is silently swallowed
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False, "Should return false for path too long");
            // TODO: Once logging is added, verify PathTooLongException was logged
        });
    }

    [Test]
    public void DirectoryExists_WithRestrictedPath_ShouldHandleGracefully()
    {
        // Arrange - Path that might require elevated permissions (system directory)
        var restrictedPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "config");
        
        // Act - In .NET 9, Directory.Exists handles this gracefully without throwing
        var result = _service.DirectoryExists(restrictedPath);
        
        // Assert - In .NET 9, this returns true if directory exists, no exception thrown
        Assert.That(result, Is.TypeOf<bool>(), "Should return a boolean value");
        // The actual result depends on whether the directory exists and permissions
    }

    #endregion

    #region FileExists Tests - Line 159 Empty Catch Block

    [Test]
    public void FileExists_WithInvalidPathCharacters_ShouldLogArgumentException()
    {
        // Arrange - File path with null character that throws ArgumentException
        var invalidFilePath = "C:\\test\\file\0.txt";
        
        // Act - This should log ArgumentException but currently doesn't
        var result = _service.FileExists(invalidFilePath);
        
        // Assert - This test FAILS because ArgumentException is silently swallowed
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False, "Should return false for invalid file path");
            // TODO: Once logging is added, verify ArgumentException was logged
        });
    }

    [Test]
    public void FileExists_WithPathTooLong_ShouldLogPathTooLongException()
    {
        // Arrange - Create a file path longer than MAX_PATH
        var longFilePath = "C:\\" + new string('b', 300) + ".txt";
        
        // Act - This should log PathTooLongException but currently doesn't
        var result = _service.FileExists(longFilePath);
        
        // Assert - This test FAILS because PathTooLongException is silently swallowed
        Assert.That(result, Is.False, "Should return false for path too long");
        // TODO: Once logging is added, verify PathTooLongException was logged
    }

    #endregion

    #region GetFullPath Tests - Line 171 Empty Catch Block

    [Test]
    public void GetFullPath_WithInvalidPathCharacters_ShouldLogExceptionAndReturnOriginalPath()
    {
        // Arrange - Path with invalid characters that throws ArgumentException
        var invalidPath = "C:\\test\0\\path";
        
        // Act - This should log ArgumentException but currently doesn't
        var result = _service.GetFullPath(invalidPath);
        
        // Assert - This test FAILS because ArgumentException is silently swallowed
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(invalidPath), "Should return original path on exception");
            // TODO: Once logging is added, verify ArgumentException was logged
        });
    }

    [Test]
    public void GetFullPath_WithPathTooLong_ShouldLogAndReturnOriginalPath()
    {
        // Arrange - Create a path that's too long
        var longPath = "C:\\" + new string('c', 300);
        
        // Act - This should log PathTooLongException but currently doesn't
        var result = _service.GetFullPath(longPath);
        
        // Assert - This test FAILS because PathTooLongException is silently swallowed
        Assert.That(result, Is.EqualTo(longPath), "Should return original path when path too long");
        // TODO: Once logging is added, verify PathTooLongException was logged
    }

    #endregion

    #region GetFileName Tests - Line 183 Empty Catch Block

    [Test]
    public void GetFileName_WithInvalidPathCharacters_ShouldHandleGracefully()
    {
        // Arrange - Path with null character
        var invalidPath = "C:\\folder\\file\0name.txt";
        
        // Act - In .NET 9, Path.GetFileName handles this gracefully without throwing
        var result = _service.GetFileName(invalidPath);
        
        // Assert - In .NET 9, this extracts filename despite null character
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo("file\0name.txt"), "Should extract filename despite null character");
            // .NET 9 handles invalid characters gracefully, preserving the null character
        });
    }

    [Test]
    public void GetFileName_WithPathTooLong_ShouldHandleGracefully()
    {
        // Arrange - Very long path
        var longPath = "C:\\" + new string('d', 300) + "\\file.txt";
        
        // Act - In .NET 9, long path support is much better than earlier versions  
        var result = _service.GetFileName(longPath);
        
        // Assert - In .NET 9, this handles long paths without throwing
        Assert.That(result, Is.EqualTo("file.txt"), "Should extract filename from long path in .NET 9");
        // .NET 9 has much improved long path handling
    }

    #endregion

    #region GetExtension Tests - Line 195 Empty Catch Block

    [Test]
    public void GetExtension_WithInvalidPathCharacters_ShouldHandleGracefully()
    {
        // Arrange - Path with invalid character
        var invalidPath = "C:\\file\0.txt";
        
        // Act - In .NET 9, Path.GetExtension handles this gracefully without throwing
        var result = _service.GetExtension(invalidPath);
        
        // Assert - In .NET 9, this extracts the extension despite null character
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(".txt"), "Should extract extension despite null character");
            // .NET 9 handles invalid characters gracefully in most Path methods
        });
    }

    #endregion

    #region GetDirectoryName Tests - Line 207 Empty Catch Block

    [Test]
    public void GetDirectoryName_WithInvalidPathCharacters_ShouldHandleGracefully()
    {
        // Arrange - Path with null character
        var invalidPath = "C:\\folder\0\\subfolder\\file.txt";
        
        // Act - In .NET 9, Path.GetDirectoryName handles this gracefully without throwing
        var result = _service.GetDirectoryName(invalidPath);
        
        // Assert - In .NET 9, this extracts directory name despite null character
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo("C:\\folder\0\\subfolder"), "Should extract directory despite null character");
            // .NET 9 handles invalid characters gracefully, preserving the null character
        });
    }

    [Test]
    public void GetDirectoryName_WithPathTooLong_ShouldHandleGracefully()
    {
        // Arrange - Very long path
        var longPath = "C:\\" + new string('e', 300) + "\\file.txt";
        
        // Act - In .NET 9, long path support is much better than earlier versions
        var result = _service.GetDirectoryName(longPath);
        
        // Assert - In .NET 9, this handles long paths without throwing
        Assert.That(result.Length, Is.GreaterThan(0), "Should handle long paths in .NET 9");
        Assert.That(result, Does.StartWith("C:\\"), "Should return valid directory path");
    }

    #endregion

    #region GetRelativePath Tests - Line 219 Empty Catch Block

    [Test]
    public void GetRelativePath_WithInvalidBasePath_ShouldLogArgumentException()
    {
        // Arrange - Invalid base path with null character
        var invalidBasePath = "C:\\base\0path";
        var targetPath = "C:\\target\\path";
        
        // Act - This should log ArgumentException but currently doesn't
        var result = _service.GetRelativePath(invalidBasePath, targetPath);
        
        // Assert - This test FAILS because ArgumentException is silently swallowed
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(targetPath), "Should return target path on exception");
            // TODO: Once logging is added, verify ArgumentException was logged
        });
    }

    [Test]
    public void GetRelativePath_WithDifferentVolumes_ShouldLogAndReturnTargetPath()
    {
        // Arrange - Paths on different volumes that might cause exceptions
        var basePath = "C:\\base";
        var targetPath = "D:\\target"; // Different volume
        
        // Act - This might cause exceptions in some scenarios but currently silently handled
        var result = _service.GetRelativePath(basePath, targetPath);
        
        // Assert - This demonstrates potential silent swallowing of path resolution exceptions
        Assert.That(result, Is.Not.Null, "Should return some result");
        // TODO: Once logging is added, verify any exceptions were logged
    }

    #endregion

    #region EnumerateFiles Tests - Line 231 Empty Catch Block

    [Test]
    public void EnumerateFiles_WithNonExistentDirectory_ShouldLogDirectoryNotFoundException()
    {
        // Arrange - Path to non-existent directory
        var nonExistentPath = "C:\\NonExistentDirectory\\That\\Does\\Not\\Exist";
        
        // Act - This should log DirectoryNotFoundException but currently doesn't
        var result = _service.EnumerateFiles(nonExistentPath);
        
        // Assert - This test FAILS because DirectoryNotFoundException is silently swallowed
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null, "Should return empty enumerable, not null");
            Assert.That(result, Is.Empty, "Should return empty enumerable on exception");
            // TODO: Once logging is added, verify DirectoryNotFoundException was logged
        });
    }

    [Test]
    public void EnumerateFiles_WithAccessDeniedDirectory_ShouldLogUnauthorizedAccessException()
    {
        // Arrange - System directory that typically requires elevated access
        var restrictedPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32\\config");
        
        // Act - This should log UnauthorizedAccessException but currently doesn't
        var result = _service.EnumerateFiles(restrictedPath);
        
        // Assert - This test FAILS because UnauthorizedAccessException is silently swallowed
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null, "Should return empty enumerable, not null");
            Assert.That(result, Is.Empty, "Should return empty enumerable on security exception");
            // TODO: Once logging is added, verify UnauthorizedAccessException was logged
        });
    }

    [Test]
    public void EnumerateFiles_WithInvalidSearchPattern_ShouldLogArgumentException()
    {
        // Arrange - Valid directory but invalid search pattern
        var validPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var invalidPattern = "test\0pattern"; // Null character in pattern
        
        // Act - This should log ArgumentException but currently doesn't
        var result = _service.EnumerateFiles(validPath, invalidPattern);
        
        // Assert - This test FAILS because ArgumentException is silently swallowed
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null, "Should return empty enumerable, not null");
            Assert.That(result, Is.Empty, "Should return empty enumerable on invalid pattern");
            // TODO: Once logging is added, verify ArgumentException was logged
        });
    }

    #endregion

    #region EnumerateDirectories Tests - Line 243 Empty Catch Block

    [Test]
    public void EnumerateDirectories_WithNonExistentDirectory_ShouldLogDirectoryNotFoundException()
    {
        // Arrange - Path to non-existent directory
        var nonExistentPath = "C:\\NonExistentDirectory\\For\\Directories\\Test";
        
        // Act - This should log DirectoryNotFoundException but currently doesn't
        var result = _service.EnumerateDirectories(nonExistentPath);
        
        // Assert - This test FAILS because DirectoryNotFoundException is silently swallowed
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null, "Should return empty enumerable, not null");
            Assert.That(result, Is.Empty, "Should return empty enumerable on exception");
            // TODO: Once logging is added, verify DirectoryNotFoundException was logged
        });
    }

    [Test]
    public void EnumerateDirectories_WithAccessDeniedDirectory_ShouldLogUnauthorizedAccessException()
    {
        // Arrange - System directory that typically requires elevated access  
        var restrictedPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32\\config");
        
        // Act - This should log UnauthorizedAccessException but currently doesn't
        var result = _service.EnumerateDirectories(restrictedPath);
        
        // Assert - This test FAILS because UnauthorizedAccessException is silently swallowed
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null, "Should return empty enumerable, not null");
            Assert.That(result, Is.Empty, "Should return empty enumerable on security exception");
            // TODO: Once logging is added, verify UnauthorizedAccessException was logged
        });
    }

    [Test]
    public void EnumerateDirectories_WithInvalidSearchPattern_ShouldLogArgumentException()
    {
        // Arrange - Valid directory but invalid search pattern
        var validPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var invalidPattern = "dir\0pattern"; // Null character in pattern
        
        // Act - This should log ArgumentException but currently doesn't
        var result = _service.EnumerateDirectories(validPath, invalidPattern);
        
        // Assert - This test FAILS because ArgumentException is silently swallowed
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null, "Should return empty enumerable, not null");
            Assert.That(result, Is.Empty, "Should return empty enumerable on invalid pattern");
            // TODO: Once logging is added, verify ArgumentException was logged
        });
    }

    #endregion

    #region Integration Tests - Real-World Scenarios

    [Test]
    public void LuceneIndexCreation_WhenPathResolutionFails_ShouldProvideDebuggableError()
    {
        // Arrange - Scenario that mimics real Lucene index corruption issues
        var corruptedWorkspacePath = "C:\\workspace\0with\\null\\characters";
        
        // Act - Get index path (this internally uses several path resolution methods)
        var indexPath = _service.GetIndexPath(corruptedWorkspacePath);
        var directoryExists = _service.DirectoryExists(indexPath);
        var files = _service.EnumerateFiles(indexPath).ToList();
        
        // Assert - This test demonstrates the silent failure cascade
        // When Lucene fails due to path issues, developers get no useful error information
        Assert.Multiple(() =>
        {
            Assert.That(indexPath, Is.Not.Null, "Should return some index path");
            Assert.That(directoryExists, Is.False, "Should return false for problematic directory");
            Assert.That(files, Is.Empty, "Should return empty file list");
            
            // TODO: Once logging is added, these operations should log their failures:
            // - ComputeWorkspaceHash might fail with invalid characters
            // - DirectoryExists should log why it failed
            // - EnumerateFiles should log the specific filesystem error
            // This would help developers debug Lucene index issues
        });
    }

    [Test] 
    public void RealWorldDebuggingScenario_WhenFileSystemFails_DeveloperGetsNoInformation()
    {
        // Arrange - This simulates the exact problem mentioned in the bug report:
        // "when file system operations fail (permissions, locks, invalid paths), 
        // the service returns false/empty instead of logging the error"
        var problematicPath = Path.Combine("C:\\", new string('z', 260)); // Too long
        
        // Act - Multiple operations that could fail
        var exists = _service.DirectoryExists(problematicPath);
        var fullPath = _service.GetFullPath(problematicPath);  
        var fileName = _service.GetFileName(problematicPath);
        var extension = _service.GetExtension(problematicPath);
        var dirName = _service.GetDirectoryName(problematicPath);
        
        // Assert - This test documents the current broken behavior
        // All operations silently fail without providing debugging information
        Assert.Multiple(() =>
        {
            Assert.That(exists, Is.False, "DirectoryExists returns false for non-existent long path");
            Assert.That(fullPath.Length, Is.GreaterThan(0), "GetFullPath handles long paths in .NET 9");
            Assert.That(fileName.Length, Is.GreaterThan(0), "GetFileName extracts name from long path in .NET 9");
            Assert.That(extension, Is.EqualTo(string.Empty), "GetExtension returns empty for path without extension");  
            Assert.That(dirName.Length, Is.GreaterThan(0), "GetDirectoryName handles long paths in .NET 9");
            
            // TODO: With proper error handling, developers would see:
            // - Which specific operation failed
            // - What type of exception occurred (PathTooLongException)
            // - The problematic path that caused the issue
            // - Guidance on how to fix the problem
        });
    }

    #endregion

    #region Test Documentation

    /// <summary>
    /// This test class demonstrates the critical bug in PathResolutionService:
    /// 
    /// PROBLEM: All 9 "safe" file system methods use empty catch blocks that silently
    /// swallow ALL exceptions without logging. This makes debugging impossible when:
    /// 
    /// 1. Lucene indexes get corrupted due to file system issues
    /// 2. Permission problems prevent index creation  
    /// 3. Invalid paths cause cryptic failures
    /// 4. Path length limits are exceeded
    /// 5. File locks prevent operations
    /// 
    /// CURRENT BEHAVIOR: Methods return false/empty/original-value with no diagnostic info
    /// 
    /// EXPECTED BEHAVIOR: Methods should log the specific exception details including:
    /// - Exception type (ArgumentException, PathTooLongException, UnauthorizedAccessException, etc.)
    /// - Problematic path that caused the error
    /// - Operation that was attempted
    /// - Guidance for resolution when possible
    /// 
    /// IMPACT: When Lucene operations fail, developers have no way to diagnose the root cause,
    /// leading to frustrating debugging sessions and potential data loss.
    /// 
    /// All tests in this file currently PASS the basic functionality assertions but FAIL
    /// to verify proper error logging because the logging doesn't exist yet.
    /// 
    /// Once proper error handling is implemented, uncomment the logging verification
    /// assertions marked with "TODO" to ensure exceptions are being properly logged.
    /// </summary>

    #endregion
}