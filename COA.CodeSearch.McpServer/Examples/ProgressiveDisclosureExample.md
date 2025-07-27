# Progressive Disclosure Pattern in COA CodeSearch MCP Server

This document demonstrates how the progressive disclosure (drill-down) pattern works in the MCP server, allowing clients to get summary results first and then request specific details as needed.

## Overview

The progressive disclosure pattern allows tools to:
1. Return lightweight summaries for large operations
2. Provide metadata about available detail levels
3. Support targeted detail requests without re-executing the entire operation

## Example Flow: Text Search with Large Result Set

### Step 1: Initial Request (Summary Mode)

**Request:**
```json
{
  "method": "tools/call",
  "params": {
    "name": "text_search",
    "arguments": {
      "query": "ICmsService",
      "workspacePath": "/project/path",
      "responseMode": "summary"
    }
  }
}
```

**Response:**
```json
{
  "success": true,
  "query": "ICmsService",
  "summary": {
    "totalFiles": 39,
    "totalMatches": 156,
    "topFiles": [
      { "filePath": "Controllers/CmsController.cs", "matchCount": 12 },
      { "filePath": "Services/CmsService.cs", "matchCount": 8 },
      { "filePath": "Pages/Admin/Content.razor", "matchCount": 7 },
      // ... top 10 files
    ],
    "message": "Showing top 10 files out of 39 total files with matches"
  },
  "metadata": {
    "totalResults": 39,
    "returnedResults": 0,
    "isTruncated": false,
    "detailRequestToken": "ZXlKemVXMWliMnhPWVcxbCI6SWts...",
    "availableDetailLevels": [
      {
        "id": "files",
        "name": "File List",
        "description": "List of all affected files with change counts",
        "estimatedTokens": 1950,
        "isActive": false
      },
      {
        "id": "matches",
        "name": "Match Details",
        "description": "Detailed matches for specific files with context",
        "estimatedTokens": 15600,
        "isActive": false
      },
      {
        "id": "context",
        "name": "Full Context",
        "description": "Complete matches with surrounding code context",
        "estimatedTokens": 31200,
        "isActive": false
      }
    ]
  }
}
```

### Step 2: Request File List Details

**Request:**
```json
{
  "method": "tools/call",
  "params": {
    "name": "batch_operations",
    "arguments": {
      "detailRequest": {
        "detailRequestToken": "ZXlKemVXMWliMnhPWVcxbCI6SWts...",
        "detailLevel": "files"
      }
    }
  }
}
```

**Response:**
```json
{
  "success": true,
  "detailLevel": "files",
  "files": [
    { "filePath": "Controllers/CmsController.cs", "matchCount": 12 },
    { "filePath": "Services/CmsService.cs", "matchCount": 8 },
    { "filePath": "Pages/Admin/Content.razor", "matchCount": 7 },
    // ... all 39 files
  ],
  "metadata": {
    "totalResults": 39,
    "returnedResults": 39,
    "estimatedTokens": 1823
  }
}
```

### Step 3: Request Specific File Changes

**Request:**
```json
{
  "method": "tools/call",
  "params": {
    "name": "batch_operations",
    "arguments": {
      "detailRequest": {
        "detailRequestToken": "ZXlKemVXMWliMnhPWVcxbCI6SWts...",
        "detailLevel": "matches"
      },
      "operations": [
        {
          "operation": "text_search",
          "query": "ICmsService",
          "workspacePath": "/project/path",
          "filePattern": "Controllers/CmsController.cs"
        }
      ]
    }
  }
}
```

**Response:**
```json
{
  "success": true,
  "detailLevel": "changes",
  "fileChanges": [
    {
      "filePath": "Controllers/CmsController.cs",
      "changes": [
        {
          "line": 15,
          "column": 20,
          "oldText": "ICmsService",
          "newText": "IContentManagementService"
        },
        // ... more changes
      ]
    },
    {
      "filePath": "Services/CmsService.cs",
      "changes": [
        // ... changes for this file
      ]
    }
  ],
  "metadata": {
    "totalResults": 20,
    "returnedResults": 20,
    "estimatedTokens": 2500
  }
}
```

## Benefits

1. **Performance**: Initial responses are fast and lightweight
2. **Flexibility**: Clients can request only the details they need
3. **Token Efficiency**: Avoids sending large responses that might exceed limits
4. **User Experience**: Users see results immediately and can drill down as needed
5. **Caching**: Detail data is cached, making subsequent requests fast

## Implementation Pattern for New Tools

### 1. Support Response Modes

```csharp
public async Task<object> ExecuteAsync(
    // ... tool parameters ...
    ResponseMode mode = ResponseMode.Full,
    DetailRequest? detailRequest = null)
{
    // Handle detail requests
    if (detailRequest != null)
    {
        return await GetDetailsAsync(detailRequest, cancellationToken);
    }
    
    // Handle summary mode
    if (mode == ResponseMode.Summary)
    {
        return CreateSummaryResponse(results);
    }
    
    // Full response with truncation as needed
    return CreateFullResponse(results);
}
```

### 2. Create Summary Response with Detail Levels

```csharp
private object CreateSummaryResponse(Results results)
{
    var token = _detailCache.StoreDetailData(results);
    
    return new
    {
        summary = CreateSummary(results),
        metadata = new ResponseMetadata
        {
            DetailRequestToken = token,
            AvailableDetailLevels = GetAvailableDetailLevels(results)
        }
    };
}
```

### 3. Handle Detail Requests

```csharp
private async Task<object> GetDetailsAsync(DetailRequest request)
{
    var cachedData = _detailCache.GetDetailData<Results>(request.DetailRequestToken);
    if (cachedData == null)
    {
        return CreateErrorResponse("Invalid or expired detail request token");
    }
    
    return request.DetailLevelId switch
    {
        "level1" => GetLevel1Details(cachedData, request.TargetItems),
        "level2" => GetLevel2Details(cachedData, request.TargetItems),
        _ => CreateErrorResponse($"Unknown detail level: {request.DetailLevelId}")
    };
}
```

## Client Usage Pattern

```typescript
// 1. Get summary
const summary = await mcpClient.callTool('text_search', {
  query: 'ICmsService',
  workspacePath: '/project/path',
  responseMode: 'summary'
});

// 2. Check available detail levels
const detailLevels = summary.metadata.availableDetailLevels;
console.log(`Found ${summary.totalMatches} matches`);
console.log(`Available details:`, detailLevels);

// 3. Request specific details based on user action
if (userWantsFileList) {
  const fileDetails = await mcpClient.callTool('batch_operations', {
    detailRequest: {
      detailRequestToken: summary.metadata.detailRequestToken,
      detailLevel: 'files'
    }
  });
}

// 4. Drill down to specific files
if (userSelectsFiles) {
  const matchDetails = await mcpClient.callTool('text_search', {
    query: 'ICmsService',
    workspacePath: '/project/path',
    filePattern: selectedFiles.join(','),
    contextLines: 3
  });
}
```

## Best Practices

1. **Always provide summary mode** for tools that can return large results
2. **Cache detail data** with appropriate expiration (15-30 minutes)
3. **Estimate token usage** for each detail level to help clients decide
4. **Support partial detail requests** (e.g., specific files only)
5. **Clear detail level descriptions** so clients know what they're requesting
6. **Validate tokens** and provide clear error messages for expired tokens
7. **Progressive enhancement** - full mode should still work for backward compatibility