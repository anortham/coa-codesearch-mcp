using COA.CodeSearch.McpServer.Models;
using COA.CodeSearch.McpServer.Services;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Tools;

/// <summary>
/// Get diagnostics (errors, warnings, hints) for Blazor (.razor) files using Razor Language Server
/// </summary>
public class BlazorGetDiagnosticsTool : ITool
{
    public string ToolName => "blazor_get_diagnostics";
    public string Description => "Get compilation diagnostics (errors, warnings, hints) for Blazor (.razor) files";
    public ToolCategory Category => ToolCategory.Analysis;
    
    private readonly ILogger<BlazorGetDiagnosticsTool> _logger;
    private readonly IRazorAnalysisService _razorAnalysisService;

    public BlazorGetDiagnosticsTool(
        ILogger<BlazorGetDiagnosticsTool> logger,
        IRazorAnalysisService razorAnalysisService)
    {
        _logger = logger;
        _razorAnalysisService = razorAnalysisService;
    }

    /// <summary>
    /// Gets diagnostics for a Blazor file
    /// </summary>
    public async Task<object> ExecuteAsync(
        string filePath,
        string[]? severities = null,
        bool includeHints = false,
        bool refreshDiagnostics = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Blazor GetDiagnostics request for {FilePath}", filePath);

            // Validate file path
            if (string.IsNullOrEmpty(filePath))
            {
                return CreateErrorResponse("File path is required");
            }

            // Check if this is a Razor file
            if (!IsRazorFile(filePath))
            {
                return CreateErrorResponse($"File {filePath} is not a Blazor (.razor) file");
            }

            // Check if file exists
            if (!File.Exists(filePath))
            {
                return CreateErrorResponse($"File not found: {filePath}");
            }

            // Check if Razor analysis service is available
            if (!_razorAnalysisService.IsAvailable)
            {
                // Try to initialize the service
                var initialized = await _razorAnalysisService.InitializeAsync(cancellationToken);
                if (!initialized)
                {
                    return CreateErrorResponse("Razor Language Server is not available. Please install VS Code with the C# extension.");
                }
            }

            // Refresh diagnostics if requested
            if (refreshDiagnostics)
            {
                await _razorAnalysisService.RefreshDiagnosticsAsync(filePath, cancellationToken);
                
                // Small delay to allow diagnostics to be computed
                await Task.Delay(500, cancellationToken);
            }

            // Get diagnostics from Razor analysis service
            var diagnostics = await _razorAnalysisService.GetDiagnosticsAsync(filePath, cancellationToken);
            
            if (diagnostics == null || diagnostics.Length == 0)
            {
                return new
                {
                    success = true,
                    diagnostics = Array.Empty<object>(),
                    count = 0,
                    filePath,
                    summary = new
                    {
                        errors = 0,
                        warnings = 0,
                        hints = 0,
                        total = 0
                    },
                    message = "No diagnostics found for this file",
                    status = "clean",
                    suggestions = new[]
                    {
                        "File appears to compile without errors or warnings",
                        "If expecting diagnostics, try refreshing with refreshDiagnostics=true",
                        "Check that the file contains C# code that could generate diagnostics"
                    },
                    settings = new
                    {
                        severities,
                        includeHints,
                        refreshDiagnostics
                    },
                    metadata = new
                    {
                        tool = "blazor_get_diagnostics",
                        languageServer = "rzls",
                        timestamp = DateTime.UtcNow
                    }
                };
            }

            // Process and filter diagnostics
            var processedDiagnostics = ProcessDiagnostics(diagnostics, severities ?? Array.Empty<string>(), includeHints);

            var result = new
            {
                success = true,
                diagnostics = processedDiagnostics.filtered,
                count = processedDiagnostics.filtered.Length,
                filePath,
                summary = processedDiagnostics.summary,
                status = DetermineFileStatus(processedDiagnostics.summary),
                settings = new
                {
                    severities,
                    includeHints,
                    refreshDiagnostics
                },
                raw = diagnostics, // Include raw response for debugging
                metadata = new
                {
                    tool = "blazor_get_diagnostics",
                    languageServer = "rzls",
                    totalFound = diagnostics.Length,
                    filtered = processedDiagnostics.filtered.Length,
                    timestamp = DateTime.UtcNow
                }
            };

            _logger.LogInformation("Successfully retrieved {Count} diagnostics for {FilePath} (filtered from {Total})", 
                processedDiagnostics.filtered.Length, filePath, diagnostics.Length);

            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Blazor GetDiagnostics operation was cancelled");
            return CreateErrorResponse("Operation was cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Blazor GetDiagnostics for {FilePath}", filePath);
            return CreateErrorResponse($"Error getting diagnostics: {ex.Message}");
        }
    }

    private bool IsRazorFile(string filePath)
    {
        return Path.GetExtension(filePath).Equals(".razor", StringComparison.OrdinalIgnoreCase);
    }

    private (object[] filtered, object summary) ProcessDiagnostics(object[] rawDiagnostics, string[] severityFilter, bool includeHints)
    {
        try
        {
            var processedDiagnostics = new List<object>();
            var errorCount = 0;
            var warningCount = 0;
            var hintCount = 0;

            foreach (var diagnostic in rawDiagnostics)
            {
                var processedDiagnostic = ProcessSingleDiagnostic(diagnostic);
                if (processedDiagnostic != null)
                {
                    var severity = GetDiagnosticSeverity(processedDiagnostic);
                    
                    // Count by severity
                    switch (severity.ToLowerInvariant())
                    {
                        case "error":
                            errorCount++;
                            break;
                        case "warning":
                            warningCount++;
                            break;
                        case "hint":
                        case "info":
                            hintCount++;
                            break;
                    }

                    // Apply severity filter
                    if (severityFilter != null && severityFilter.Length > 0)
                    {
                        if (!severityFilter.Contains(severity, StringComparer.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                    }

                    // Apply hint filter
                    if (!includeHints && (severity.Equals("hint", StringComparison.OrdinalIgnoreCase) || 
                                         severity.Equals("info", StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    processedDiagnostics.Add(processedDiagnostic);
                }
            }

            var summary = new
            {
                errors = errorCount,
                warnings = warningCount,
                hints = hintCount,
                total = errorCount + warningCount + hintCount,
                distribution = new
                {
                    errorPercentage = GetPercentage(errorCount, rawDiagnostics.Length),
                    warningPercentage = GetPercentage(warningCount, rawDiagnostics.Length),
                    hintPercentage = GetPercentage(hintCount, rawDiagnostics.Length)
                }
            };

            return (processedDiagnostics.ToArray(), summary);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error processing diagnostics, returning raw data");
            
            // Fallback: return raw diagnostics with basic summary
            return (rawDiagnostics, new
            {
                errors = 0,
                warnings = 0,
                hints = 0,
                total = rawDiagnostics.Length,
                processed = false,
                error = ex.Message
            });
        }
    }

    private object? ProcessSingleDiagnostic(object rawDiagnostic)
    {
        try
        {
            // Since we're dealing with raw LSP responses, create a structured representation
            // In production, you'd properly parse the LSP Diagnostic JSON structure
            
            return new
            {
                severity = "unknown", // Would extract from LSP response
                message = "Diagnostic message", // Would extract from LSP response
                source = "razor", // Would extract from LSP response
                code = "diagnostic_code", // Would extract code if available
                range = new
                {
                    start = new { line = 0, character = 0 },
                    end = new { line = 0, character = 0 }
                },
                category = "compilation", // Would categorize based on diagnostic type
                note = "Diagnostic parsing requires LSP JSON structure analysis"
            };
        }
        catch
        {
            return null;
        }
    }

    private string GetDiagnosticSeverity(object diagnostic)
    {
        // This would analyze the diagnostic to determine its severity
        // For now, return a default
        return "unknown";
    }

    private string DetermineFileStatus(object summary)
    {
        try
        {
            // Use reflection to get counts from the summary object
            var summaryType = summary.GetType();
            var errors = (int)(summaryType.GetProperty("errors")?.GetValue(summary) ?? 0);
            var warnings = (int)(summaryType.GetProperty("warnings")?.GetValue(summary) ?? 0);

            if (errors > 0)
                return "has_errors";
            else if (warnings > 0)
                return "has_warnings";
            else
                return "clean";
        }
        catch
        {
            return "unknown";
        }
    }

    private double GetPercentage(int count, int total)
    {
        return total > 0 ? Math.Round((double)count / total * 100, 1) : 0.0;
    }

    private object CreateErrorResponse(string message)
    {
        return new
        {
            success = false,
            error = message,
            diagnostics = Array.Empty<object>(),
            count = 0,
            summary = new
            {
                errors = 0,
                warnings = 0,
                hints = 0,
                total = 0
            },
            status = "error",
            metadata = new
            {
                tool = "blazor_get_diagnostics",
                timestamp = DateTime.UtcNow
            }
        };
    }
}