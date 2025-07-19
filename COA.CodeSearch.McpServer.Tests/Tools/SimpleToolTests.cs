using COA.CodeSearch.McpServer.Configuration;
using COA.CodeSearch.McpServer.Infrastructure;
using COA.CodeSearch.McpServer.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using FluentAssertions;
using System.Text.Json;
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
            WorkspaceService);
        
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
    public async Task SearchSymbolsTool_Should_Return_Error_For_Empty_Pattern()
    {
        // Arrange
        var tool = new SearchSymbolsTool(
            ServiceProvider.GetRequiredService<ILogger<SearchSymbolsTool>>(),
            WorkspaceService,
            ServiceProvider.GetRequiredService<IConfiguration>());
        
        // Act
        var result = await tool.ExecuteAsync("", "test.csproj", null, false, 100);
        
        // Assert
        var json = JsonSerializer.Serialize(result);
        var jsonDoc = JsonDocument.Parse(json);
        var root = jsonDoc.RootElement;
        
        root.GetProperty("success").GetBoolean().Should().BeFalse();
        root.GetProperty("error").GetString().Should().Contain("pattern cannot be empty");
    }
    
    [Fact]
    public async Task GetDiagnosticsTool_Should_Return_Error_For_Invalid_Path()
    {
        // Arrange
        var tool = new GetDiagnosticsTool(
            ServiceProvider.GetRequiredService<ILogger<GetDiagnosticsTool>>(),
            WorkspaceService);
        
        // Act
        var result = await tool.ExecuteAsync("NonExistent.cs", null);
        
        // Assert
        var json = JsonSerializer.Serialize(result);
        var jsonDoc = JsonDocument.Parse(json);
        var root = jsonDoc.RootElement;
        
        root.GetProperty("success").GetBoolean().Should().BeFalse();
    }
    
    [Fact]
    public async Task GetHoverInfoTool_Should_Return_Error_For_Missing_Document()
    {
        // Arrange
        var tool = new GetHoverInfoTool(
            ServiceProvider.GetRequiredService<ILogger<GetHoverInfoTool>>(),
            WorkspaceService);
        
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
    
    [Fact]
    public async Task GetImplementationsTool_Should_Return_Error_For_Missing_Document()
    {
        // Arrange
        var tool = new GetImplementationsTool(
            ServiceProvider.GetRequiredService<ILogger<GetImplementationsTool>>(),
            WorkspaceService);
        
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
    public async Task GetCallHierarchyTool_Should_Return_Error_For_Missing_Document()
    {
        // Arrange
        var tool = new GetCallHierarchyTool(
            ServiceProvider.GetRequiredService<ILogger<GetCallHierarchyTool>>(),
            WorkspaceService);
        
        // Act
        var result = await tool.ExecuteAsync("NonExistentFile.cs", 1, 1, "incoming", 5);
        
        // Assert
        var json = JsonSerializer.Serialize(result);
        var jsonDoc = JsonDocument.Parse(json);
        var root = jsonDoc.RootElement;
        
        root.GetProperty("success").GetBoolean().Should().BeFalse();
        root.GetProperty("error").GetString().Should().Contain("Could not find document");
    }
    
    [Fact]
    public async Task RenameSymbolTool_Should_Return_Error_For_Missing_Document()
    {
        // Arrange
        var tool = new RenameSymbolTool(
            ServiceProvider.GetRequiredService<ILogger<RenameSymbolTool>>(),
            WorkspaceService,
            ServiceProvider.GetRequiredService<IResponseSizeEstimator>(),
            ServiceProvider.GetRequiredService<IResultTruncator>(),
            ServiceProvider.GetRequiredService<IOptions<ResponseLimitOptions>>());
        
        // Act
        var result = await tool.ExecuteAsync("NonExistentFile.cs", 1, 1, "NewName", true);
        
        // Assert
        var json = JsonSerializer.Serialize(result);
        var jsonDoc = JsonDocument.Parse(json);
        var root = jsonDoc.RootElement;
        
        root.GetProperty("success").GetBoolean().Should().BeFalse();
        root.GetProperty("error").GetString().Should().Contain("Could not find document");
    }
}