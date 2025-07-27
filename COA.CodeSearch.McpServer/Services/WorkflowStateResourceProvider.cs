using COA.Mcp.Protocol;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Resource provider that tracks workflow state between tool calls,
/// helping AI agents maintain context and understand their progress.
/// </summary>
public class WorkflowStateResourceProvider : IResourceProvider
{
    private readonly ILogger<WorkflowStateResourceProvider> _logger;
    private readonly ConcurrentDictionary<string, WorkflowState> _workflowStates = new();
    private readonly Timer _cleanupTimer;

    public string Scheme => "codesearch-workflow";
    public string Name => "Workflow State";
    public string Description => "Tracks workflow state and context between tool calls for AI agents";

    public WorkflowStateResourceProvider(ILogger<WorkflowStateResourceProvider> logger)
    {
        _logger = logger;
        
        // Clean up old workflow states every hour
        _cleanupTimer = new Timer(CleanupOldWorkflows, null, TimeSpan.FromHours(1), TimeSpan.FromHours(1));
    }

    /// <inheritdoc />
    public Task<List<Resource>> ListResourcesAsync(CancellationToken cancellationToken = default)
    {
        var resources = new List<Resource>();

        foreach (var kvp in _workflowStates)
        {
            var workflowId = kvp.Key;
            var state = kvp.Value;

            resources.Add(new Resource
            {
                Uri = $"{Scheme}://{workflowId}",
                Name = $"Workflow: {state.Goal}",
                Description = $"Active workflow started {state.StartedAt:g} with {state.Steps.Count} steps",
                MimeType = "application/json"
            });

            // Add sub-resources for specific aspects
            resources.Add(new Resource
            {
                Uri = $"{Scheme}://{workflowId}/current",
                Name = $"Current State: {state.Goal}",
                Description = "Current step and progress in the workflow",
                MimeType = "application/json"
            });

            resources.Add(new Resource
            {
                Uri = $"{Scheme}://{workflowId}/history",
                Name = $"History: {state.Goal}",
                Description = "Complete history of steps taken in this workflow",
                MimeType = "application/json"
            });

            resources.Add(new Resource
            {
                Uri = $"{Scheme}://{workflowId}/context",
                Name = $"Context: {state.Goal}",
                Description = "Accumulated context and findings from this workflow",
                MimeType = "application/json"
            });
        }

        _logger.LogDebug("Listed {Count} workflow state resources", resources.Count);
        return Task.FromResult(resources);
    }

    /// <inheritdoc />
    public Task<ReadResourceResult?> ReadResourceAsync(string uri, CancellationToken cancellationToken = default)
    {
        if (!CanHandle(uri))
            return Task.FromResult<ReadResourceResult?>(null);

        try
        {
            var parts = uri.Replace($"{Scheme}://", "").Split('/');
            var workflowId = parts[0];

            if (!_workflowStates.TryGetValue(workflowId, out var state))
            {
                _logger.LogWarning("Workflow state not found: {WorkflowId}", workflowId);
                return Task.FromResult<ReadResourceResult?>(null);
            }

            // Update last accessed time
            state.LastAccessed = DateTime.UtcNow;

            object responseData;
            if (parts.Length > 1)
            {
                // Sub-resource requested
                responseData = parts[1] switch
                {
                    "current" => GetCurrentState(state),
                    "history" => GetHistory(state),
                    "context" => GetContext(state),
                    _ => GetFullState(state)
                };
            }
            else
            {
                // Full workflow state
                responseData = GetFullState(state);
            }

            var result = new ReadResourceResult();
            result.Contents.Add(new ResourceContent
            {
                Uri = uri,
                MimeType = "application/json",
                Text = JsonSerializer.Serialize(responseData, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                })
            });

            _logger.LogDebug("Retrieved workflow state {WorkflowId}", workflowId);
            return Task.FromResult<ReadResourceResult?>(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading workflow state resource {Uri}", uri);
            return Task.FromResult<ReadResourceResult?>(null);
        }
    }

    /// <inheritdoc />
    public bool CanHandle(string uri)
    {
        return uri.StartsWith($"{Scheme}://", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Creates or updates a workflow state and returns a URI for accessing it.
    /// </summary>
    public string CreateOrUpdateWorkflow(
        string goal,
        string? workflowId = null,
        string? currentStep = null,
        Dictionary<string, object>? metadata = null)
    {
        workflowId ??= GenerateWorkflowId(goal);
        
        if (!_workflowStates.TryGetValue(workflowId, out var state))
        {
            state = new WorkflowState
            {
                Id = workflowId,
                Goal = goal,
                StartedAt = DateTime.UtcNow,
                LastAccessed = DateTime.UtcNow,
                Metadata = metadata ?? new Dictionary<string, object>()
            };
            _workflowStates[workflowId] = state;
            _logger.LogInformation("Created new workflow {WorkflowId} for goal: {Goal}", workflowId, goal);
        }
        else
        {
            state.LastAccessed = DateTime.UtcNow;
            if (metadata != null)
            {
                foreach (var kvp in metadata)
                {
                    state.Metadata[kvp.Key] = kvp.Value;
                }
            }
        }

        if (!string.IsNullOrEmpty(currentStep))
        {
            state.CurrentStep = currentStep;
        }

        return $"{Scheme}://{workflowId}";
    }

    /// <summary>
    /// Adds a step to an existing workflow.
    /// </summary>
    public void AddWorkflowStep(
        string workflowId,
        string operation,
        object? input = null,
        object? output = null,
        string? status = "completed",
        List<string>? insights = null)
    {
        if (!_workflowStates.TryGetValue(workflowId, out var state))
        {
            _logger.LogWarning("Cannot add step to non-existent workflow: {WorkflowId}", workflowId);
            return;
        }

        var step = new WorkflowStep
        {
            StepNumber = state.Steps.Count + 1,
            Operation = operation,
            Input = input,
            Output = output,
            Status = status ?? "completed",
            Timestamp = DateTime.UtcNow,
            Insights = insights ?? new List<string>()
        };

        state.Steps.Add(step);
        state.LastAccessed = DateTime.UtcNow;

        // Update accumulated context
        if (insights != null && insights.Any())
        {
            state.AccumulatedInsights.AddRange(insights);
        }

        if (output != null)
        {
            state.AccumulatedFindings.Add(new Finding
            {
                Operation = operation,
                Result = output,
                Timestamp = DateTime.UtcNow
            });
        }

        _logger.LogDebug("Added step {StepNumber} to workflow {WorkflowId}", step.StepNumber, workflowId);
    }

    /// <summary>
    /// Gets the current workflow for a specific goal or creates a new one.
    /// </summary>
    public string GetOrCreateWorkflowForGoal(string goal)
    {
        // Check if there's an existing workflow for this goal
        var existingWorkflow = _workflowStates.Values
            .Where(w => w.Goal.Equals(goal, StringComparison.OrdinalIgnoreCase) && 
                       w.LastAccessed > DateTime.UtcNow.AddHours(-24))
            .OrderByDescending(w => w.LastAccessed)
            .FirstOrDefault();

        if (existingWorkflow != null)
        {
            existingWorkflow.LastAccessed = DateTime.UtcNow;
            return $"{Scheme}://{existingWorkflow.Id}";
        }

        // Create new workflow
        return CreateOrUpdateWorkflow(goal);
    }

    private string GenerateWorkflowId(string goal)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var hash = goal.GetHashCode();
        return $"wf_{Math.Abs(hash):x8}_{timestamp}";
    }

    private object GetFullState(WorkflowState state)
    {
        return new
        {
            id = state.Id,
            goal = state.Goal,
            status = DetermineWorkflowStatus(state),
            currentStep = state.CurrentStep,
            startedAt = state.StartedAt,
            lastAccessed = state.LastAccessed,
            duration = (DateTime.UtcNow - state.StartedAt).TotalMinutes,
            stepCount = state.Steps.Count,
            steps = state.Steps,
            metadata = state.Metadata,
            summary = GenerateWorkflowSummary(state),
            nextActions = GenerateNextActions(state)
        };
    }

    private object GetCurrentState(WorkflowState state)
    {
        var lastStep = state.Steps.LastOrDefault();
        return new
        {
            workflowId = state.Id,
            goal = state.Goal,
            currentStep = state.CurrentStep,
            lastOperation = lastStep?.Operation,
            lastOperationTime = lastStep?.Timestamp,
            status = DetermineWorkflowStatus(state),
            progress = new
            {
                stepsCompleted = state.Steps.Count(s => s.Status == "completed"),
                totalSteps = state.Steps.Count,
                duration = (DateTime.UtcNow - state.StartedAt).TotalMinutes
            },
            recentInsights = state.AccumulatedInsights.TakeLast(5).ToList(),
            nextActions = GenerateNextActions(state)
        };
    }

    private object GetHistory(WorkflowState state)
    {
        return new
        {
            workflowId = state.Id,
            goal = state.Goal,
            timeline = state.Steps.Select(s => new
            {
                step = s.StepNumber,
                operation = s.Operation,
                timestamp = s.Timestamp,
                duration = s.StepNumber > 1 ? 
                    (s.Timestamp - state.Steps[s.StepNumber - 2].Timestamp).TotalSeconds : 0,
                status = s.Status,
                hasInsights = s.Insights.Any()
            }).ToList(),
            totalDuration = (DateTime.UtcNow - state.StartedAt).TotalMinutes,
            operationCounts = state.Steps
                .GroupBy(s => s.Operation)
                .ToDictionary(g => g.Key, g => g.Count())
        };
    }

    private object GetContext(WorkflowState state)
    {
        return new
        {
            workflowId = state.Id,
            goal = state.Goal,
            accumulatedInsights = state.AccumulatedInsights.Distinct().ToList(),
            keyFindings = state.AccumulatedFindings
                .OrderByDescending(f => f.Timestamp)
                .Take(10)
                .ToList(),
            metadata = state.Metadata,
            workingSets = ExtractWorkingSets(state),
            patterns = ExtractPatterns(state)
        };
    }

    private string DetermineWorkflowStatus(WorkflowState state)
    {
        if (state.Steps.Any(s => s.Status == "failed"))
            return "partial-failure";
        if (state.Steps.Any(s => s.Status == "in-progress"))
            return "in-progress";
        if (state.LastAccessed < DateTime.UtcNow.AddHours(-1))
            return "idle";
        return "active";
    }

    private object GenerateWorkflowSummary(WorkflowState state)
    {
        var operations = state.Steps.GroupBy(s => s.Operation)
            .ToDictionary(g => g.Key, g => g.Count());

        return new
        {
            totalOperations = state.Steps.Count,
            uniqueOperations = operations.Count,
            topOperations = operations
                .OrderByDescending(kvp => kvp.Value)
                .Take(3)
                .ToList(),
            successRate = state.Steps.Count > 0 ? 
                (double)state.Steps.Count(s => s.Status == "completed") / state.Steps.Count : 1.0,
            averageStepDuration = CalculateAverageStepDuration(state)
        };
    }

    private List<string> GenerateNextActions(WorkflowState state)
    {
        var actions = new List<string>();

        // Analyze recent operations to suggest next steps
        var recentOps = state.Steps.TakeLast(3).Select(s => s.Operation).ToList();

        if (recentOps.Contains("index_workspace") && !recentOps.Contains("text_search"))
        {
            actions.Add("Perform text_search to explore indexed content");
        }

        if (recentOps.Count(op => op.Contains("search")) > 2)
        {
            actions.Add("Consider refining search criteria or using pattern_detector");
        }

        if (state.AccumulatedFindings.Count > 5)
        {
            actions.Add("Synthesize findings into memories for future reference");
        }

        if (state.Steps.Count > 10)
        {
            actions.Add("Review workflow progress and consider summarizing results");
        }

        return actions;
    }

    private Dictionary<string, List<string>> ExtractWorkingSets(WorkflowState state)
    {
        var workingSets = new Dictionary<string, List<string>>();

        // Extract files from operations
        var files = new HashSet<string>();
        var directories = new HashSet<string>();
        var queries = new HashSet<string>();

        foreach (var step in state.Steps)
        {
            // Extract based on operation type
            if (step.Input is JsonElement inputElement)
            {
                if (inputElement.TryGetProperty("filePath", out var filePath))
                    files.Add(filePath.GetString() ?? "");
                if (inputElement.TryGetProperty("workspacePath", out var workspace))
                    directories.Add(workspace.GetString() ?? "");
                if (inputElement.TryGetProperty("query", out var query))
                    queries.Add(query.GetString() ?? "");
            }
        }

        if (files.Any())
            workingSets["files"] = files.ToList();
        if (directories.Any())
            workingSets["workspaces"] = directories.ToList();
        if (queries.Any())
            workingSets["queries"] = queries.ToList();

        return workingSets;
    }

    private List<string> ExtractPatterns(WorkflowState state)
    {
        var patterns = new List<string>();

        // Analyze operation sequences
        var opPairs = new Dictionary<string, int>();
        for (int i = 0; i < state.Steps.Count - 1; i++)
        {
            var pair = $"{state.Steps[i].Operation} → {state.Steps[i + 1].Operation}";
            opPairs[pair] = opPairs.GetValueOrDefault(pair, 0) + 1;
        }

        foreach (var kvp in opPairs.Where(kv => kv.Value > 1).OrderByDescending(kv => kv.Value))
        {
            patterns.Add($"Repeated sequence: {kvp.Key} ({kvp.Value} times)");
        }

        return patterns;
    }

    private double CalculateAverageStepDuration(WorkflowState state)
    {
        if (state.Steps.Count <= 1)
            return 0;

        var durations = new List<double>();
        for (int i = 1; i < state.Steps.Count; i++)
        {
            var duration = (state.Steps[i].Timestamp - state.Steps[i - 1].Timestamp).TotalSeconds;
            durations.Add(duration);
        }

        return durations.Any() ? durations.Average() : 0;
    }

    private void CleanupOldWorkflows(object? state)
    {
        try
        {
            var cutoffTime = DateTime.UtcNow.AddDays(-1); // Keep workflows for 24 hours
            var toRemove = _workflowStates
                .Where(kvp => kvp.Value.LastAccessed < cutoffTime)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var id in toRemove)
            {
                if (_workflowStates.TryRemove(id, out var workflow))
                {
                    _logger.LogDebug("Cleaned up old workflow {WorkflowId} for goal '{Goal}'", 
                        id, workflow.Goal);
                }
            }

            if (toRemove.Count > 0)
            {
                _logger.LogInformation("Cleaned up {Count} old workflows", toRemove.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during workflow cleanup");
        }
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
    }
}

/// <summary>
/// Represents the state of a workflow
/// </summary>
public class WorkflowState
{
    public string Id { get; set; } = null!;
    public string Goal { get; set; } = null!;
    public string? CurrentStep { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime LastAccessed { get; set; }
    public List<WorkflowStep> Steps { get; set; } = new();
    public List<string> AccumulatedInsights { get; set; } = new();
    public List<Finding> AccumulatedFindings { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// Represents a single step in a workflow
/// </summary>
public class WorkflowStep
{
    public int StepNumber { get; set; }
    public string Operation { get; set; } = null!;
    public object? Input { get; set; }
    public object? Output { get; set; }
    public string Status { get; set; } = "completed";
    public DateTime Timestamp { get; set; }
    public List<string> Insights { get; set; } = new();
}

/// <summary>
/// Represents a finding or result from a workflow step
/// </summary>
public class Finding
{
    public string Operation { get; set; } = null!;
    public object Result { get; set; } = null!;
    public DateTime Timestamp { get; set; }
}