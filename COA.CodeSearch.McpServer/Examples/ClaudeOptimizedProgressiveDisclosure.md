# Progressive Disclosure for Claude AI

This document describes how the progressive disclosure pattern is optimized for Claude as the primary consumer of the MCP server.

## Design Principles for Claude

As an AI assistant, I need:
1. **Clear decision points** - Tell me exactly what options I have and their costs
2. **Actionable summaries** - Give me enough context to make intelligent decisions
3. **Efficient drill-down** - Let me get specific details without multiple round trips
4. **Smart defaults** - Anticipate my most likely next questions

## Optimized Response Structure

### Initial Response Pattern

When I call a tool that might return large results, the response should automatically include:

```json
{
  "success": true,
  "mode": "summary",  // Clear indicator of response mode
  "data": {
    // Core information I need to understand the scope
    "overview": {
      "totalItems": 156,
      "affectedFiles": 39,
      "estimatedFullResponseTokens": 45000,
      "keyInsights": [
        "Most changes in Controllers/ directory (45 occurrences)",
        "ICmsService is injected in 12 different classes",
        "3 Razor components directly reference this interface"
      ]
    },
    // Actionable summary that helps me decide next steps
    "summary": {
      "byCategory": {
        "controllers": { "files": 8, "occurrences": 45 },
        "services": { "files": 5, "occurrences": 23 },
        "pages": { "files": 15, "occurrences": 67 },
        "tests": { "files": 11, "occurrences": 21 }
      },
      "hotspots": [
        { "file": "Controllers/CmsController.cs", "occurrences": 12, "complexity": "high" },
        { "file": "Services/CmsService.cs", "occurrences": 8, "complexity": "medium" }
      ]
    }
  },
  "nextActions": {
    // Clear, Claude-friendly next steps
    "recommended": [
      {
        "action": "get_hotspot_details",
        "description": "Get detailed changes for the 5 most affected files",
        "estimatedTokens": 3500,
        "command": {
          "detailLevel": "hotspots",
          "includeContext": true
        }
      },
      {
        "action": "get_category_details", 
        "description": "Get all changes in a specific category (e.g., 'controllers')",
        "estimatedTokens": 8000,
        "command": {
          "detailLevel": "category",
          "category": "controllers"
        }
      }
    ],
    "available": [
      {
        "action": "get_all_details",
        "description": "Get complete details (may be truncated)",
        "estimatedTokens": 45000,
        "warning": "Response will be truncated to 20k tokens"
      },
      {
        "action": "get_file_list",
        "description": "Get just the list of affected files",
        "estimatedTokens": 2000
      }
    ]
  },
  "context": {
    // Help me understand what this means
    "impact": "high",
    "riskFactors": [
      "Interface is widely used across the codebase",
      "Changes affect both backend and frontend components"
    ],
    "suggestions": [
      "Consider reviewing the hotspot files first",
      "Test changes thoroughly in CmsController.cs"
    ]
  }
}
```

## Intelligent Detail Requests

When I request details, the response should be structured for my workflow:

### Pattern 1: Smart Batching

Instead of making me request details file by file, let me request intelligently grouped details:

```json
// My request
{
  "detailLevel": "smart_batch",
  "criteria": {
    "minOccurrences": 5,      // Files with 5+ changes
    "categories": ["controllers", "services"],
    "maxTokens": 10000        // Stay within my working limit
  }
}

// Response
{
  "success": true,
  "batchInfo": {
    "filesIncluded": 8,
    "filesOmitted": 12,
    "reason": "Token limit would be exceeded",
    "totalTokensUsed": 9800
  },
  "details": [
    {
      "file": "Controllers/CmsController.cs",
      "changes": [...],
      "summary": "12 occurrences, mostly in action methods"
    }
    // ... more files
  ],
  "stillAvailable": {
    "remainingFiles": 12,
    "command": { "detailLevel": "remaining", "offset": 8 }
  }
}
```

### Pattern 2: Context-Aware Details

When I ask for details, include context that helps me understand the changes:

```json
{
  "file": "Controllers/CmsController.cs",
  "changes": [
    {
      "location": { "line": 45, "column": 20 },
      "change": {
        "from": "ICmsService",
        "to": "IContentManagementService"
      },
      "context": {
        "method": "GetContent(int id)",
        "usage": "Dependency injection in constructor",
        "surroundingCode": "public CmsController(ICmsService cmsService, ILogger<CmsController> logger)"
      },
      "impact": {
        "level": "safe",
        "reason": "Simple interface rename in DI"
      }
    }
  ]
}
```

## Tool Design Guidelines for Claude

### 1. Automatic Mode Selection

Tools should automatically choose the right mode based on expected response size:

```csharp
public async Task<object> ExecuteAsync(/* params */)
{
    var estimatedTokens = EstimateResponseSize(results);
    
    // Automatically use summary mode for large results
    if (estimatedTokens > 5000 && mode == ResponseMode.Full)
    {
        Logger.LogInformation("Auto-switching to summary mode due to large result set");
        mode = ResponseMode.Summary;
    }
    
    // Include a note in the response
    return new
    {
        success = true,
        mode = mode.ToString().ToLower(),
        autoModeSwitch = estimatedTokens > 5000,
        data = CreateResponse(results, mode)
    };
}
```

### 2. Progressive Enhancement in Responses

Include partial details in summaries when it doesn't add much overhead:

```json
{
  "summary": {
    "totalFiles": 39,
    "preview": {
      // Include a taste of the actual changes
      "topChanges": [
        {
          "file": "Controllers/CmsController.cs",
          "line": 15,
          "preview": "constructor(ICmsService → IContentManagementService)",
          "fullContext": false
        }
      ],
      "getFullContext": {
        "command": { "detailLevel": "preview", "includeContext": true }
      }
    }
  }
}
```

### 3. Smart Suggestions Based on Results

Analyze the results and suggest the most useful next actions:

```csharp
private object CreateNextActions(AnalysisResults results)
{
    var suggestions = new List<object>();
    
    // If there are test failures, prioritize those
    if (results.TestFiles.Any())
    {
        suggestions.Add(new
        {
            action = "focus_on_tests",
            reason = "Changes affect test files - review these first",
            priority = "high"
        });
    }
    
    // If changes are concentrated, suggest targeted review
    if (results.TopFiles.First().ChangeCount > results.TotalChanges * 0.3)
    {
        suggestions.Add(new
        {
            action = "review_hotspots",
            reason = $"{results.TopFiles.First().Path} has 30%+ of all changes",
            priority = "high"
        });
    }
    
    return suggestions;
}
```

## Example Claude Workflow

Here's how I would ideally interact with the rename tool:

```typescript
// 1. Initial request - I just want to understand the scope
const result = await rename_symbol({
  filePath: "ICmsService.cs",
  line: 10,
  column: 15,
  newName: "IContentManagementService"
  // No need to specify mode - it auto-selects based on size
});

// 2. I see it auto-switched to summary mode
console.log(result.autoModeSwitch); // true
console.log(result.data.overview.keyInsights);
// ["Most changes in Controllers/", "12 classes affected", ...]

// 3. I want to see the most important changes first
const hotspots = await rename_symbol_details({
  ...result.nextActions.recommended[0].command
});

// 4. Based on what I see, I might want specific categories
const controllerDetails = await rename_symbol_details({
  detailLevel: "smart_batch",
  criteria: {
    categories: ["controllers"],
    includeContext: true,
    maxTokens: 8000
  }
});

// 5. Finally, I can make an informed decision
if (shouldProceed) {
  await rename_symbol({
    ...originalParams,
    preview: false,  // Actually apply the changes
    confirmationToken: result.confirmationToken
  });
}
```

## Key Improvements for Claude

1. **Auto-mode selection** - Don't make me guess if I need summary mode
2. **Actionable summaries** - Give me insights, not just counts
3. **Smart batching** - Let me get logical groups of details
4. **Context inclusion** - Show me enough context to make decisions
5. **Progressive enhancement** - Include samples in summaries
6. **Clear next actions** - Tell me exactly what commands to run
7. **Token awareness** - Always show token estimates
8. **Intelligent suggestions** - Analyze results and guide my workflow

This design minimizes the number of tool calls I need to make while maximizing the useful information in each response.