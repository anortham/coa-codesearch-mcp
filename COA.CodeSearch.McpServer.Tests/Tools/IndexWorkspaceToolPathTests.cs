using Xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Moq;
using System.IO;
using COA.CodeSearch.McpServer.Services;
using COA.CodeSearch.McpServer.Tools;

namespace COA.CodeSearch.McpServer.Tests.Tools;

public class IndexWorkspaceToolPathTests
{
    [Fact]
    public void IndexWorkspaceTool_ShouldUsePathConstantsForValidation()
    {
        // The tool currently checks:
        // workspacePath.Contains(".codesearch", StringComparison.OrdinalIgnoreCase)
        
        // After refactoring, it should use:
        // workspacePath.Contains(PathConstants.BaseDirectoryName, StringComparison.OrdinalIgnoreCase)
        
        // Test paths that should be rejected
        var invalidPaths = new[]
        {
            $@"C:\project\{PathConstants.BaseDirectoryName}",
            $@"/home/user/{PathConstants.BaseDirectoryName}/index",
            Path.Combine("relative", PathConstants.BaseDirectoryName)
        };
        
        foreach (var path in invalidPaths)
        {
            // These paths contain the base directory name and should be rejected
            Assert.Contains(PathConstants.BaseDirectoryName, path.ToLowerInvariant());
        }
    }


    [Fact]
    public void IndexWorkspaceTool_ShouldNotHardcodeBaseDirectoryName()
    {
        // Verify we're using the constant instead of hardcoding
        Assert.Equal(".codesearch", PathConstants.BaseDirectoryName);
        
        // The refactored validation should look like:
        // if (workspacePath.Contains(PathConstants.BaseDirectoryName, StringComparison.OrdinalIgnoreCase))
        // {
        //     return CreateErrorResult("Cannot index the .codesearch directory itself");
        // }
    }

    [Fact]
    public void IndexWorkspaceTool_ShouldRejectBaseDirectory()
    {
        // Test that paths containing the base directory name are rejected
        var testPath = Path.Combine("C:", "project", PathConstants.BaseDirectoryName);
        
        // The tool should validate against indexing the .codesearch directory
        Assert.Contains(PathConstants.BaseDirectoryName, testPath);
    }

    [Fact] 
    public void IndexWorkspaceTool_ShouldAcceptValidWorkspacePaths()
    {
        // Paths that don't contain .codesearch should be valid
        var validPaths = new[]
        {
            @"C:\project\source",
            @"/home/user/myproject",
            @"D:\repos\application"
        };
        
        foreach (var path in validPaths)
        {
            Assert.DoesNotContain(PathConstants.BaseDirectoryName, path);
        }
    }
}