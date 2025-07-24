using COA.CodeSearch.McpServer.Configuration;
using COA.CodeSearch.McpServer.Infrastructure;
using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using FluentAssertions;
using System.Text.Json;
using System.IO;
using Moq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using COA.CodeSearch.McpServer.Services;

namespace COA.CodeSearch.McpServer.Tests.Tools;

/// <summary>
/// Simple tests that verify basic tool functionality without complex workspace loading
/// </summary>
public class SimpleToolTests : TestBase
{
    [Fact]
    public async Task GoToDefinitionTool_Should_Return_Error_For_Missing_Document()
    {
        // Arrange
        var tool = new GoToDefinitionTool(
            ServiceProvider.GetRequiredService<ILogger<GoToDefinitionTool>>(),
            WorkspaceService,
            ServiceProvider.GetRequiredService<TypeScriptGoToDefinitionTool>());
        
        // Act
        var result = await tool.ExecuteAsync("NonExistentFile.cs", 1, 1);
        
        // Assert
        var json = JsonSerializer.Serialize(result);
        var jsonDoc = JsonDocument.Parse(json);
        var root = jsonDoc.RootElement;
        
        root.GetProperty("success").GetBoolean().Should().BeFalse();
        root.GetProperty("error").GetString().Should().Contain("Could not find document");
    }
    
    [Fact]
    public async Task FindReferencesTool_Should_Return_Error_For_Missing_Document()
    {
        // Arrange
        var tool = new FindReferencesTool(
            ServiceProvider.GetRequiredService<ILogger<FindReferencesTool>>(),
            WorkspaceService);
        
        // Act
        var result = await tool.ExecuteAsync("NonExistentFile.cs", 1, 1, false);
        
        // Assert
        var json = JsonSerializer.Serialize(result);
        var jsonDoc = JsonDocument.Parse(json);
        var root = jsonDoc.RootElement;
        
        root.GetProperty("success").GetBoolean().Should().BeFalse();
        root.GetProperty("error").GetString().Should().Contain("Could not find document");
    }
    
    
    [Fact]
    public async Task GetHoverInfoTool_Should_Return_Error_For_Missing_Document()
    {
        // Arrange
        var mockPathResolution = new Mock<IPathResolutionService>();
        mockPathResolution.Setup(x => x.GetTypeScriptInstallPath())
            .Returns(Path.Combine(Path.GetTempPath(), "typescript-test"));
        var mockInstaller = new TypeScriptInstaller(
            Mock.Of<ILogger<TypeScriptInstaller>>(),
            mockPathResolution.Object,
            null); // null for httpClientFactory is OK
        var mockTsService = new Mock<TypeScriptAnalysisService>(
            Mock.Of<ILogger<TypeScriptAnalysisService>>(),
            Mock.Of<IConfiguration>(),
            mockInstaller);
        var tsHoverTool = new TypeScriptHoverInfoTool(
            Mock.Of<ILogger<TypeScriptHoverInfoTool>>(),
            mockTsService.Object);
            
        var tool = new GetHoverInfoTool(
            ServiceProvider.GetRequiredService<ILogger<GetHoverInfoTool>>(),
            WorkspaceService,
            tsHoverTool);
        
        // Act
        var result = await tool.ExecuteAsync("NonExistentFile.cs", 1, 1);
        
        // Assert
        var json = JsonSerializer.Serialize(result);
        var jsonDoc = JsonDocument.Parse(json);
        var root = jsonDoc.RootElement;
        
        root.GetProperty("success").GetBoolean().Should().BeFalse();
        root.GetProperty("error").GetString().Should().Contain("Could not find document");
    }
    
    [Fact]
    public async Task GetDocumentSymbolsTool_Should_Return_Error_For_Missing_Document()
    {
        // Arrange
        var tool = new GetDocumentSymbolsTool(
            ServiceProvider.GetRequiredService<ILogger<GetDocumentSymbolsTool>>(),
            WorkspaceService);
        
        // Act
        var result = await tool.ExecuteAsync("NonExistentFile.cs");
        
        // Assert
        var json = JsonSerializer.Serialize(result);
        var jsonDoc = JsonDocument.Parse(json);
        var root = jsonDoc.RootElement;
        
        root.GetProperty("success").GetBoolean().Should().BeFalse();
        root.GetProperty("error").GetString().Should().Contain("Could not find document");
    }
}