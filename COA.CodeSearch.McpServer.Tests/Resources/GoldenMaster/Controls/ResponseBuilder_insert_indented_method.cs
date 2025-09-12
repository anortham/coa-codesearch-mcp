using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.TestSample;

/// <summary>
/// Sample response builder class that demonstrates indentation corruption scenarios.
/// This file mimics real production code patterns that were affected by the bug.
/// </summary>
public class ResponseBuilder
{
    private readonly ILogger _logger;
    
    public ResponseBuilder(ILogger logger)
    {
        _logger = logger;
    }
    
    public string BuildSummary(object data)
    {
        return $"Summary for {data}";
    }
    
    /// <summary>
    /// Generate insights for the base response builder pattern
    /// </summary>
    protected virtual List<string> GenerateInsights(object data, string responseMode)
    {
        return new List<string> { "Default insight" };
    }
    
    // Insert point: new method will be added here with pre-formatted indentation
    // This should test the smart indentation logic that prevents double-indentation
}