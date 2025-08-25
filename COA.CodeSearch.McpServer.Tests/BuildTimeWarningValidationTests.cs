using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace COA.CodeSearch.McpServer.Tests;

/// <summary>
/// Build-time validation tests that ensure the project compiles without warnings.
/// These tests will fail if compiler warnings are reintroduced after fixes.
/// </summary>
[TestFixture]
[Category("BuildValidation")]
public class BuildTimeWarningValidationTests
{
    private static readonly string ProjectRoot = GetProjectRoot();
    private static readonly string[] CriticalFiles = 
    {
        "COA.CodeSearch.McpServer.Tests\\Tools\\SimilarFilesToolTests.cs",
        "COA.CodeSearch.McpServer\\Controllers\\SearchController.cs",
        "COA.CodeSearch.McpServer\\Controllers\\WorkspaceController.cs", 
        "COA.CodeSearch.McpServer\\Tools\\LineSearchTool.cs"
    };

    [Test]
    [Category("CompilerWarnings")]
    public void BuildValidation_ProjectCompiles_WithoutWarnings()
    {
        // Arrange
        var solutionPath = Path.Combine(ProjectRoot, "COA.CodeSearch.McpServer.sln");
        
        // Skip if not in development environment
        if (!File.Exists(solutionPath))
        {
            Assert.Ignore("Solution file not found - skipping build validation");
            return;
        }

        // Act - Build the project and capture output
        var (exitCode, output, errors) = RunDotNetBuild(ProjectRoot);

        // Assert - No warnings should be present
        Assert.That(exitCode, Is.EqualTo(0), $"Build failed with exit code {exitCode}.\nOutput: {output}\nErrors: {errors}");

        // Parse build output for warnings
        var warnings = ExtractWarningsFromBuildOutput(output + "\n" + errors);
        
        if (warnings.Any())
        {
            var warningDetails = string.Join("\n", warnings.Select((w, i) => $"{i+1}. {w}"));
            Assert.Fail($"Build completed but contains {warnings.Count} warning(s):\n{warningDetails}");
        }

        Assert.Pass($"Build successful with 0 warnings. Output contained {CountLines(output)} lines.");
    }

    [Test]
    [Category("CompilerWarnings")]
    public void BuildValidation_CriticalFiles_ExistAndAccessible()
    {
        // Verify all files that previously had warnings are still present and readable
        
        var missingFiles = new List<string>();
        var inaccessibleFiles = new List<string>();

        foreach (var relativeFilePath in CriticalFiles)
        {
            var fullPath = Path.Combine(ProjectRoot, relativeFilePath);
            
            if (!File.Exists(fullPath))
            {
                missingFiles.Add(relativeFilePath);
                continue;
            }

            try
            {
                var content = File.ReadAllText(fullPath);
                if (string.IsNullOrEmpty(content))
                {
                    inaccessibleFiles.Add($"{relativeFilePath} (empty file)");
                }
            }
            catch (Exception ex)
            {
                inaccessibleFiles.Add($"{relativeFilePath} ({ex.Message})");
            }
        }

        // Assert all files are accessible
        if (missingFiles.Any())
        {
            Assert.Fail($"Missing critical files:\n- {string.Join("\n- ", missingFiles)}");
        }

        if (inaccessibleFiles.Any())
        {
            Assert.Fail($"Inaccessible critical files:\n- {string.Join("\n- ", inaccessibleFiles)}");
        }

        Assert.Pass($"All {CriticalFiles.Length} critical files are present and accessible.");
    }

    [Test]
    [Category("CompilerWarnings")]
    public void BuildValidation_SpecificWarningTypes_NotPresent()
    {
        // Test for specific warning types that were problematic
        
        if (!File.Exists(Path.Combine(ProjectRoot, "COA.CodeSearch.McpServer.sln")))
        {
            Assert.Ignore("Solution file not found - skipping specific warning validation");
            return;
        }

        // Act - Build and capture detailed output
        var (exitCode, output, errors) = RunDotNetBuild(ProjectRoot, verbosity: "normal");
        var allOutput = output + "\n" + errors;

        // Assert - Check for specific warning patterns that were fixed
        var problematicWarnings = new[]
        {
            "CS8602", // Possible dereference of a null reference
            "CS8604", // Possible null reference argument
            "CS8618", // Non-nullable field must contain a non-null value when exiting constructor
            "CS4014", // Because this call is not awaited, execution continues before the call is completed
            "CS1998", // Async method lacks 'await' operators and will run synchronously
            "CS0168", // Variable is declared but never used
            "CS0219"  // Variable is assigned but its value is never used
        };

        var foundWarnings = problematicWarnings
            .Where(warningCode => allOutput.Contains(warningCode))
            .ToList();

        if (foundWarnings.Any())
        {
            // Extract the actual warning messages
            var warningMessages = ExtractSpecificWarnings(allOutput, foundWarnings);
            Assert.Fail($"Found {foundWarnings.Count} problematic warning type(s):\n{string.Join("\n", warningMessages)}");
        }

        Assert.Pass($"No problematic warning types detected in build output.");
    }

    [Test]
    [Category("CompilerWarnings")]
    public void BuildValidation_TestProject_CompilesCleanly()
    {
        // Specifically test that the test project (which had many warnings) compiles cleanly
        
        var testProjectPath = Path.Combine(ProjectRoot, "COA.CodeSearch.McpServer.Tests", "COA.CodeSearch.McpServer.Tests.csproj");
        
        if (!File.Exists(testProjectPath))
        {
            Assert.Ignore("Test project file not found - skipping test project validation");
            return;
        }

        // Act - Build only the test project
        var (exitCode, output, errors) = RunDotNetBuild(Path.GetDirectoryName(testProjectPath)!);

        // Assert - Test project builds without warnings
        Assert.That(exitCode, Is.EqualTo(0), $"Test project build failed with exit code {exitCode}.\nOutput: {output}\nErrors: {errors}");

        var warnings = ExtractWarningsFromBuildOutput(output + "\n" + errors);
        
        if (warnings.Any())
        {
            var warningDetails = string.Join("\n", warnings.Select((w, i) => $"{i+1}. {w}"));
            Assert.Fail($"Test project contains {warnings.Count} warning(s):\n{warningDetails}");
        }

        Assert.Pass("Test project builds cleanly without warnings.");
    }

    [Test]
    [Category("RegressionTest")]
    public void RegressionTest_WarningFixesIntact_NoRegressionDetected()
    {
        // This test serves as a canary - if it fails, warning fixes have regressed
        
        var warningIndicators = new[]
        {
            "warning CS8602:", // Null reference dereference
            "warning CS8604:", // Null reference argument  
            "warning CS4014:", // Unawaited async call
            "warning CS1998:", // Async without await
            "warning CS0168:", // Unused variable
            "warning CS0219:"  // Unused assignment
        };

        if (!File.Exists(Path.Combine(ProjectRoot, "COA.CodeSearch.McpServer.sln")))
        {
            Assert.Ignore("Solution file not found - skipping regression test");
            return;
        }

        var (exitCode, output, errors) = RunDotNetBuild(ProjectRoot);
        var allOutput = output + "\n" + errors;

        var regressionWarnings = warningIndicators
            .Where(indicator => allOutput.Contains(indicator, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (regressionWarnings.Any())
        {
            Assert.Fail($"REGRESSION DETECTED: Warning fixes have been undone. Found warning indicators:\n- {string.Join("\n- ", regressionWarnings)}");
        }

        Assert.Pass("No regression detected - warning fixes are intact.");
    }

    #region Helper Methods

    private static string GetProjectRoot()
    {
        var currentDir = TestContext.CurrentContext.WorkDirectory;
        var dir = new DirectoryInfo(currentDir);
        
        // Walk up the directory tree to find the solution file
        while (dir != null && !dir.GetFiles("*.sln").Any())
        {
            dir = dir.Parent;
        }
        
        return dir?.FullName ?? Directory.GetCurrentDirectory();
    }

    private static (int exitCode, string output, string errors) RunDotNetBuild(string workingDirectory, string verbosity = "quiet")
    {
        var processStartInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build --verbosity {verbosity}",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = processStartInfo };
        process.Start();

        var output = process.StandardOutput.ReadToEnd();
        var errors = process.StandardError.ReadToEnd();

        process.WaitForExit();

        return (process.ExitCode, output, errors);
    }

    private static List<string> ExtractWarningsFromBuildOutput(string buildOutput)
    {
        var warnings = new List<string>();
        var lines = buildOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            // Look for warning patterns in build output
            if (line.Contains("warning", StringComparison.OrdinalIgnoreCase) && 
                (line.Contains("CS") || line.Contains("MSB")))
            {
                warnings.Add(line.Trim());
            }
        }

        return warnings;
    }

    private static List<string> ExtractSpecificWarnings(string buildOutput, List<string> warningCodes)
    {
        var warnings = new List<string>();
        var lines = buildOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            foreach (var warningCode in warningCodes)
            {
                if (line.Contains(warningCode, StringComparison.OrdinalIgnoreCase))
                {
                    warnings.Add(line.Trim());
                    break;
                }
            }
        }

        return warnings.Distinct().ToList();
    }

    private static int CountLines(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return text.Split('\n').Length;
    }

    #endregion
}