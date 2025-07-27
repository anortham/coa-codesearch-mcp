using COA.CodeSearch.McpServer.Services;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using Xunit;

namespace COA.CodeSearch.McpServer.Tests;

public class WorkflowStateResourceProviderTests
{
    private readonly Mock<ILogger<WorkflowStateResourceProvider>> _mockLogger;
    private readonly WorkflowStateResourceProvider _provider;

    public WorkflowStateResourceProviderTests()
    {
        _mockLogger = new Mock<ILogger<WorkflowStateResourceProvider>>();
        _provider = new WorkflowStateResourceProvider(_mockLogger.Object);
    }

    [Fact]
    public void Constructor_InitializesProvider()
    {
        Assert.Equal("codesearch-workflow", _provider.Scheme);
        Assert.Equal("Workflow State", _provider.Name);
        Assert.Contains("workflow state", _provider.Description.ToLower());
    }

    [Fact]
    public void CanHandle_ReturnsTrueForValidUri()
    {
        // Arrange
        var uri = "codesearch-workflow://wf_12345678_1234567890";

        // Act
        var result = _provider.CanHandle(uri);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void CanHandle_ReturnsFalseForInvalidUri()
    {
        // Arrange
        var uri = "codesearch-types://something";

        // Act
        var result = _provider.CanHandle(uri);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task CreateOrUpdateWorkflow_CreatesNewWorkflow()
    {
        // Arrange
        var goal = "Find all error handling patterns";
        var metadata = new Dictionary<string, object> { { "priority", "high" } };

        // Act
        var uri = _provider.CreateOrUpdateWorkflow(goal, metadata: metadata);

        // Assert
        Assert.NotNull(uri);
        Assert.StartsWith("codesearch-workflow://", uri);

        // Verify we can read the workflow
        var result = await _provider.ReadResourceAsync(uri);
        Assert.NotNull(result);
        Assert.Single(result.Contents);

        var content = result.Contents[0];
        Assert.Equal(uri, content.Uri);
        Assert.Equal("application/json", content.MimeType);

        // Parse and verify the JSON content
        var json = JsonDocument.Parse(content.Text);
        Assert.Equal(goal, json.RootElement.GetProperty("goal").GetString());
        Assert.Equal("active", json.RootElement.GetProperty("status").GetString());
        Assert.Equal(0, json.RootElement.GetProperty("stepCount").GetInt32());
    }

    [Fact]
    public async Task AddWorkflowStep_AddsStepToExistingWorkflow()
    {
        // Arrange
        var goal = "Search for authentication patterns";
        var uri = _provider.CreateOrUpdateWorkflow(goal);
        var workflowId = uri.Replace("codesearch-workflow://", "");

        // Act
        _provider.AddWorkflowStep(
            workflowId,
            "text_search",
            input: new { query = "authentication", workspace = "C:/project" },
            output: new { resultCount = 42 },
            insights: new List<string> { "Found OAuth2 implementation", "JWT tokens in use" }
        );

        // Assert
        var result = await _provider.ReadResourceAsync(uri);
        Assert.NotNull(result);

        var json = JsonDocument.Parse(result.Contents[0].Text);
        Assert.Equal(1, json.RootElement.GetProperty("stepCount").GetInt32());
        
        var steps = json.RootElement.GetProperty("steps");
        Assert.Equal(1, steps.GetArrayLength());
        
        var firstStep = steps[0];
        Assert.Equal("text_search", firstStep.GetProperty("operation").GetString());
        Assert.Equal("completed", firstStep.GetProperty("status").GetString());
        Assert.Equal(2, firstStep.GetProperty("insights").GetArrayLength());
    }

    [Fact]
    public async Task ReadResourceAsync_ReturnsCurrentStateForSubResource()
    {
        // Arrange
        var goal = "Analyze codebase patterns";
        var uri = _provider.CreateOrUpdateWorkflow(goal);
        var workflowId = uri.Replace("codesearch-workflow://", "");
        
        // Add some steps
        _provider.AddWorkflowStep(workflowId, "index_workspace");
        _provider.AddWorkflowStep(workflowId, "text_search", insights: new List<string> { "Found patterns" });

        // Act
        var currentUri = $"{uri}/current";
        var result = await _provider.ReadResourceAsync(currentUri);

        // Assert
        Assert.NotNull(result);
        var json = JsonDocument.Parse(result.Contents[0].Text);
        
        Assert.Equal(goal, json.RootElement.GetProperty("goal").GetString());
        Assert.Equal("text_search", json.RootElement.GetProperty("lastOperation").GetString());
        
        var progress = json.RootElement.GetProperty("progress");
        Assert.Equal(2, progress.GetProperty("stepsCompleted").GetInt32());
        Assert.Equal(2, progress.GetProperty("totalSteps").GetInt32());
    }

    [Fact]
    public async Task ListResourcesAsync_ReturnsWorkflowResources()
    {
        // Arrange
        _provider.CreateOrUpdateWorkflow("Search for bugs");
        _provider.CreateOrUpdateWorkflow("Refactor authentication");

        // Act
        var resources = await _provider.ListResourcesAsync();

        // Assert
        Assert.Equal(8, resources.Count); // 2 workflows * 4 resources each
        
        // Verify main workflow resources (URIs that don't end with sub-resource paths)
        var mainResources = resources.Where(r => 
            !r.Uri.EndsWith("/current") && 
            !r.Uri.EndsWith("/history") && 
            !r.Uri.EndsWith("/context")).ToList();
        Assert.Equal(2, mainResources.Count);
        
        // Verify sub-resources
        var currentResources = resources.Where(r => r.Uri.EndsWith("/current")).ToList();
        Assert.Equal(2, currentResources.Count);
        
        var historyResources = resources.Where(r => r.Uri.EndsWith("/history")).ToList();
        Assert.Equal(2, historyResources.Count);
        
        var contextResources = resources.Where(r => r.Uri.EndsWith("/context")).ToList();
        Assert.Equal(2, contextResources.Count);
    }

    [Fact]
    public void GetOrCreateWorkflowForGoal_ReusesExistingWorkflow()
    {
        // Arrange
        var goal = "Optimize database queries";
        var firstUri = _provider.CreateOrUpdateWorkflow(goal);

        // Act
        var secondUri = _provider.GetOrCreateWorkflowForGoal(goal);

        // Assert
        Assert.Equal(firstUri, secondUri);
    }

    [Fact]
    public async Task WorkflowContext_AccumulatesInsightsAndFindings()
    {
        // Arrange
        var goal = "Analyze security patterns";
        var uri = _provider.CreateOrUpdateWorkflow(goal);
        var workflowId = uri.Replace("codesearch-workflow://", "");

        // Add multiple steps with insights
        _provider.AddWorkflowStep(workflowId, "text_search", 
            output: new { found = "SQL injection vulnerability" },
            insights: new List<string> { "Found SQL concatenation", "Missing parameterization" });
        
        _provider.AddWorkflowStep(workflowId, "pattern_detector",
            output: new { pattern = "hardcoded credentials" },
            insights: new List<string> { "API keys in source code" });

        // Act
        var contextUri = $"{uri}/context";
        var result = await _provider.ReadResourceAsync(contextUri);

        // Assert
        Assert.NotNull(result);
        var json = JsonDocument.Parse(result.Contents[0].Text);
        
        var insights = json.RootElement.GetProperty("accumulatedInsights");
        Assert.Equal(3, insights.GetArrayLength()); // All unique insights
        
        var findings = json.RootElement.GetProperty("keyFindings");
        Assert.Equal(2, findings.GetArrayLength());
    }
}