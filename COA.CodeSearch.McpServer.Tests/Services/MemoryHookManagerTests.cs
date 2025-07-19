using COA.CodeSearch.McpServer.Services;
using Microsoft.Extensions.Logging;
using Moq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Xunit;

namespace COA.CodeSearch.McpServer.Tests.Services;

public class MemoryHookManagerTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly Mock<ILogger<MemoryHookManager>> _mockLogger;
    private readonly MemoryHookManager _hookManager;

    public MemoryHookManagerTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"HookTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
        
        _mockLogger = new Mock<ILogger<MemoryHookManager>>();
        _hookManager = new MemoryHookManager(_mockLogger.Object, _testDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    [Fact]
    public async Task InitializeHooksAsync_CreatesHooksDirectory()
    {
        // Act
        await _hookManager.InitializeHooksAsync();

        // Assert
        var hooksDir = Path.Combine(_testDirectory, ".claude", "hooks");
        Assert.True(Directory.Exists(hooksDir));
    }

    [Fact]
    public async Task InitializeHooksAsync_CopiesPlatformSpecificScripts()
    {
        // Act
        var result = await _hookManager.InitializeHooksAsync();

        // Assert
        Assert.True(result);
        
        var hooksDir = Path.Combine(_testDirectory, ".claude", "hooks");
        var expectedExtension = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".ps1" : ".sh";
        
        // Check that platform-specific scripts were created
        Assert.True(File.Exists(Path.Combine(hooksDir, $"user-prompt-submit{expectedExtension}")));
        Assert.True(File.Exists(Path.Combine(hooksDir, $"pre-tool-use{expectedExtension}")));
        Assert.True(File.Exists(Path.Combine(hooksDir, $"post-tool-use{expectedExtension}")));
        Assert.True(File.Exists(Path.Combine(hooksDir, $"stop{expectedExtension}")));
        Assert.True(File.Exists(Path.Combine(hooksDir, $"pre-compact{expectedExtension}")));
    }

    [Fact]
    public async Task InitializeHooksAsync_CreatesReadme()
    {
        // Act
        await _hookManager.InitializeHooksAsync();

        // Assert
        var readmePath = Path.Combine(_testDirectory, ".claude", "hooks", "README.md");
        Assert.True(File.Exists(readmePath));
        
        var content = await File.ReadAllTextAsync(readmePath);
        Assert.Contains("Claude Memory System Hooks", content);
    }

    [Fact]
    public async Task InitializeHooksAsync_CreatesSettingsExample()
    {
        // Act
        var result = await _hookManager.InitializeHooksAsync();
        Assert.True(result, "InitializeHooksAsync should succeed");

        // Assert
        var examplePath = Path.Combine(_testDirectory, ".claude", "settings.local.json.example");
        
        // List all files in the .claude directory for debugging
        var claudeDir = Path.Combine(_testDirectory, ".claude");
        if (Directory.Exists(claudeDir))
        {
            var allFiles = Directory.GetFiles(claudeDir, "*", SearchOption.AllDirectories);
            foreach (var file in allFiles)
            {
                Console.WriteLine($"Found file: {Path.GetRelativePath(_testDirectory, file)}");
            }
        }
        
        Assert.True(File.Exists(examplePath), $"Settings example should exist at: {examplePath}");
        
        var content = await File.ReadAllTextAsync(examplePath);
        
        // Debug output for test failures
        if (!content.Contains("hooks"))
        {
            throw new Exception($"Settings example content: {content}");
        }
        
        var json = JsonDocument.Parse(content);
        
        Assert.True(json.RootElement.TryGetProperty("hooks", out var hooks), "Should have 'hooks' property");
        
        // The JSON serializer with CamelCase policy doesn't apply to dictionary keys
        // So we need to check for the original PascalCase keys
        Assert.True(hooks.TryGetProperty("UserPromptSubmit", out _));
        Assert.True(hooks.TryGetProperty("PreToolUse", out _));
        Assert.True(hooks.TryGetProperty("PostToolUse", out _));
        Assert.True(hooks.TryGetProperty("Stop", out _));
        Assert.True(hooks.TryGetProperty("PreCompact", out _));
    }

    [Fact]
    public async Task InitializeHooksAsync_PreservesExistingHooks()
    {
        // Arrange
        var hooksDir = Path.Combine(_testDirectory, ".claude", "hooks");
        Directory.CreateDirectory(hooksDir);
        
        var customHookPath = Path.Combine(hooksDir, "pre-tool-use.ps1");
        var customContent = "# Custom hook content";
        await File.WriteAllTextAsync(customHookPath, customContent);

        // Act
        await _hookManager.InitializeHooksAsync();

        // Assert
        var actualContent = await File.ReadAllTextAsync(customHookPath);
        Assert.Equal(customContent, actualContent);
    }

    [Fact]
    public async Task InitializeHooksAsync_OnlyCreatesPlatformSpecificScripts()
    {
        // Act
        await _hookManager.InitializeHooksAsync();

        // Assert
        var hooksDir = Path.Combine(_testDirectory, ".claude", "hooks");
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        
        if (isWindows)
        {
            // Should only have .ps1 files (plus README)
            var shFiles = Directory.GetFiles(hooksDir, "*.sh");
            Assert.Empty(shFiles);
        }
        else
        {
            // Should only have .sh files (plus README)
            var ps1Files = Directory.GetFiles(hooksDir, "*.ps1");
            Assert.Empty(ps1Files);
        }
    }

    [Fact]
    public void GetHookStatus_ReturnsCorrectStatus()
    {
        // Arrange
        var hooksDir = Path.Combine(_testDirectory, ".claude", "hooks");
        Directory.CreateDirectory(hooksDir);
        
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var extension = isWindows ? ".ps1" : ".sh";
        
        // Create some hooks
        File.WriteAllText(Path.Combine(hooksDir, $"pre-tool-use{extension}"), "# hook");
        File.WriteAllText(Path.Combine(hooksDir, $"stop{extension}"), "# hook");

        // Act
        var status = _hookManager.GetHookStatus();

        // Assert
        Assert.Equal(6, status.Count); // All required hooks
        Assert.True(status["pre-tool-use"].Exists);
        Assert.True(status["stop"].Exists);
        Assert.False(status["post-tool-use"].Exists);
        
        foreach (var hookStatus in status.Values)
        {
            Assert.Equal(extension, hookStatus.Extension);
            Assert.Equal(isWindows ? "Windows" : "Unix/Linux/macOS", hookStatus.Platform);
        }
    }

    [Fact]
    public async Task TestHookAsync_ReturnsFalseForMissingHook()
    {
        // Act
        var result = await _hookManager.TestHookAsync("non-existent-hook");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task InitializeHooksAsync_ChecksForEmbeddedResources()
    {
        // This test verifies that embedded resources are properly included
        var assembly = Assembly.GetAssembly(typeof(MemoryHookManager));
        var resourceNames = assembly?.GetManifestResourceNames() ?? Array.Empty<string>();
        
        var hookResources = resourceNames
            .Where(name => name.Contains("Resources.Hooks"))
            .ToList();
            
        // We should have both .ps1 and .sh versions of each hook, plus README
        Assert.NotEmpty(hookResources);
        
        // Verify we have at least one script of each type
        Assert.Contains(hookResources, r => r.Contains("user-prompt-submit") && r.EndsWith(".ps1"));
        Assert.Contains(hookResources, r => r.Contains("user-prompt-submit") && r.EndsWith(".sh"));
        Assert.Contains(hookResources, r => r.EndsWith("README.md"));
    }

    [SkippingFact(Skip = "Requires execution permissions on Unix")]
    public async Task InitializeHooksAsync_MakesShellScriptsExecutable()
    {
        // This would require actually running on Unix and checking file permissions
        // Skip for now but document the expected behavior
        
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            await _hookManager.InitializeHooksAsync();
            
            var hooksDir = Path.Combine(_testDirectory, ".claude", "hooks");
            var shFiles = Directory.GetFiles(hooksDir, "*.sh");
            
            foreach (var shFile in shFiles)
            {
                // Would check execute permissions here
                // var fileInfo = new UnixFileInfo(shFile);
                // Assert.True(fileInfo.CanExecute);
            }
        }
    }
}

// Helper attribute for skipping tests
public class SkippingFactAttribute : FactAttribute
{
    public SkippingFactAttribute()
    {
        Skip = "Test requires specific environment";
    }
}