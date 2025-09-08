using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace COA.CodeSearch.McpServer.Tests.Helpers;

/// <summary>
/// Simple validation test to verify our test file collection works
/// </summary>
[TestFixture]
public class SimpleTestFileValidationTest
{
    [Test]
    public async Task TestFileCollection_ShouldFindFiles()
    {
        // Act
        var result = await TestFileCollection.ValidateTestFiles();

        // Report
        TestContext.Out.WriteLine($"=== Test File Collection Validation ===");
        TestContext.Out.WriteLine($"Total files checked: {result.TotalFiles}");
        TestContext.Out.WriteLine($"Valid files: {result.ValidFiles.Count}");
        TestContext.Out.WriteLine($"Missing files: {result.MissingFiles.Count}");
        TestContext.Out.WriteLine($"Error files: {result.ErrorFiles.Count}");
        TestContext.Out.WriteLine("");

        if (result.ValidFiles.Any())
        {
            TestContext.Out.WriteLine("=== Valid Files Found ===");
            foreach (var file in result.ValidFiles.Take(10)) // Show first 10
            {
                TestContext.Out.WriteLine($"✅ {file.RelativePath} ({file.LineCount} lines, {file.SizeBytes} bytes)");
            }
            
            if (result.ValidFiles.Count > 10)
            {
                TestContext.Out.WriteLine($"... and {result.ValidFiles.Count - 10} more files");
            }
        }

        if (result.MissingFiles.Any())
        {
            TestContext.Out.WriteLine("=== Missing Files ===");
            foreach (var file in result.MissingFiles.Take(5)) // Show first 5
            {
                TestContext.Out.WriteLine($"❌ {file}");
            }
        }

        // Basic assertions
        Assert.That(result.ValidFiles.Count, Is.GreaterThan(0), "Should find at least some valid test files");
        Assert.That(result.TotalFiles, Is.GreaterThan(0), "Should have files to check");
        
        TestContext.Out.WriteLine($"\n✅ Found {result.ValidFiles.Count} valid files for testing");
    }

    [Test]
    public async Task CreateEdgeCaseFiles_ShouldWork()
    {
        // Arrange
        var testDir = Path.Combine(Path.GetTempPath(), $"codesearch_test_{Guid.NewGuid():N}");

        try
        {
            // Act
            var createdFiles = await TestFileCollection.CreateEdgeCaseTestFiles(testDir);

            // Report
            TestContext.Out.WriteLine($"=== Edge Case Files Created ===");
            TestContext.Out.WriteLine($"Test directory: {testDir}");
            
            foreach (var file in createdFiles)
            {
                var fileName = Path.GetFileName(file);
                var size = new FileInfo(file).Length;
                TestContext.Out.WriteLine($"✅ {fileName} ({size} bytes)");
            }

            // Basic assertions
            Assert.That(createdFiles.Length, Is.EqualTo(4), "Should create 4 edge case files");
            
            foreach (var file in createdFiles)
            {
                Assert.That(File.Exists(file), Is.True, $"File should exist: {file}");
            }
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(testDir))
            {
                Directory.Delete(testDir, true);
                TestContext.Out.WriteLine($"🧹 Cleaned up test directory");
            }
        }
    }
}