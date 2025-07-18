using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using COA.CodeSearch.McpServer.Services;
using Microsoft.Build.Locator;
using System.Runtime.CompilerServices;
using System.Reflection;

namespace COA.CodeSearch.McpServer.Tests;

public abstract class TestBase : IDisposable
{
    protected readonly IServiceProvider ServiceProvider;
    protected readonly CodeAnalysisService WorkspaceService;
    protected readonly ILogger<TestBase> Logger;
    
    static TestBase()
    {
        // Register MSBuild once for all tests
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }
    }
    
    protected TestBase()
    {
        var services = new ServiceCollection();
        
        // Add logging
        services.AddLogging(builder =>
        {
            builder.AddDebug();
            builder.SetMinimumLevel(LogLevel.Debug);
        });
        
        // Add configuration
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["McpServer:MaxWorkspaces"] = "5",
                ["McpServer:WorkspaceTimeout"] = "00:30:00",
                ["McpServer:ParallelismDegree"] = "4"
            })
            .Build();
        
        services.AddSingleton<IConfiguration>(configuration);
        
        // Add services
        services.AddSingleton<CodeAnalysisService>();
        
        ServiceProvider = services.BuildServiceProvider();
        WorkspaceService = ServiceProvider.GetRequiredService<CodeAnalysisService>();
        Logger = ServiceProvider.GetRequiredService<ILogger<TestBase>>();
        
        // Preload the test workspace
        var testProjectPath = GetTestProjectPath();
        try
        {
            var loadTask = WorkspaceService.GetProjectAsync(testProjectPath);
            loadTask.Wait(TimeSpan.FromSeconds(10));
        }
        catch (Exception ex)
        {
            Logger.LogWarning("Failed to preload test workspace: {Error}", ex.Message);
        }
    }
    
    protected string GetTestDataPath([CallerFilePath] string? sourceFilePath = null)
    {
        var directory = Path.GetDirectoryName(sourceFilePath) ?? throw new InvalidOperationException();
        return Path.Combine(directory, "TestData");
    }
    
    protected string GetTestProjectPath()
    {
        // Get the source directory from compile-time path
        var sourceDirectory = Path.GetDirectoryName(GetTestDataPath()) ?? throw new InvalidOperationException();
        var testProjectPath = Path.GetFullPath(Path.Combine(sourceDirectory, "TestProjects", "TestProject1", "TestProject1.csproj"));
        
        if (!File.Exists(testProjectPath))
        {
            throw new FileNotFoundException($"Test project not found at: {testProjectPath}");
        }
        
        return testProjectPath;
    }
    
    protected string GetTestCodePath()
    {
        var projectPath = GetTestProjectPath();
        var projectDir = Path.GetDirectoryName(projectPath) ?? throw new InvalidOperationException();
        var codePath = Path.Combine(projectDir, "TestCode.cs");
        
        if (!File.Exists(codePath))
        {
            throw new FileNotFoundException($"Test code file not found at: {codePath}");
        }
        
        return codePath;
    }
    
    protected async Task<string> CreateTestProjectAsync(string projectName, string code)
    {
        var testDataPath = GetTestDataPath();
        var projectPath = Path.Combine(testDataPath, projectName);
        
        // Clean up if exists
        if (Directory.Exists(projectPath))
        {
            Directory.Delete(projectPath, true);
        }
        
        Directory.CreateDirectory(projectPath);
        
        // Create project file
        var csprojContent = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <GenerateTargetFrameworkAttribute>false</GenerateTargetFrameworkAttribute>
  </PropertyGroup>
</Project>";
        
        var csprojPath = Path.Combine(projectPath, $"{projectName}.csproj");
        await File.WriteAllTextAsync(csprojPath, csprojContent);
        
        // Create code file
        var codePath = Path.Combine(projectPath, "Program.cs");
        await File.WriteAllTextAsync(codePath, code);
        
        // Create a simple global.json to ensure consistent SDK
        var globalJsonContent = @"{
  ""sdk"": {
    ""version"": ""8.0.100"",
    ""rollForward"": ""latestFeature""
  }
}";
        await File.WriteAllTextAsync(Path.Combine(projectPath, "global.json"), globalJsonContent);
        
        return csprojPath;
    }
    
    public void Dispose()
    {
        WorkspaceService?.Dispose();
        
        if (ServiceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }
        
        // Clean up test data
        var testDataPath = GetTestDataPath();
        if (Directory.Exists(testDataPath))
        {
            try
            {
                Directory.Delete(testDataPath, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}