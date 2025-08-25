# CodeSearch Cleanup and HTTP API Implementation Plan

## Status: ANALYSIS COMPLETE | READY FOR IMPLEMENTATION

**Created**: 2025-08-25  
**Priority**: CRITICAL - System has fundamental architectural flaws

## Executive Summary

The CodeSearch MCP server has severe architectural issues:
1. **LineSearchTool reads from disk** instead of using Lucene index
2. **Content is stored 3 times** in the index (massive bloat)
3. **Two competing line number systems** (term vectors vs LineData)
4. **HTTP API can't compile** due to misunderstanding of data structures

## Current State (The Truth)

### Triple Storage Problem
Content is stored THREE times in every document:
1. `content` field with `Store.YES` - Full file content
2. `line_data` field - JSON with ALL lines (`string[] Lines`)
3. `content_tv` field - Term vectors for position calculation

### Two Line Number Systems
1. **LineNumberService.cs** - Uses term vectors (supposedly deprecated)
2. **LineAwareSearchService.cs** - Uses LineData JSON (new system)

### Critical Architecture Violation
**LineSearchTool reads files from disk!**
```csharp
// Line 224 in LineSearchTool.cs - THIS IS WRONG!
var lines = await File.ReadAllLinesAsync(hit.FilePath);
```
This completely defeats the purpose of Lucene indexing.

### VS Code Bridge Dependencies
The TextSearchTool sends visualization data that requires:
- `hit.LineNumber` - Line number of match
- `hit.ContextLines` - Surrounding lines for display
- `hit.StartLine` / `hit.EndLine` - Context boundaries
- `hit.Snippet` - Formatted snippet

These are populated by `LineAwareSearchService.GetLineNumber()` which returns a `LineContext` with all this data.

## The Correct Architecture

### Principle: Single Source of Truth
- Store content ONCE in the index
- Calculate everything else on demand
- Never touch the filesystem during search

### What to Keep
1. **`content` field** - Store.YES for full text retrieval
2. **`line_count` field** - For quick statistics
3. **VS Code Bridge visualization** - Critical for UI

### What to Remove
1. **`line_data` field** - Redundant, we have content
2. **`content_tv` field** - Term vectors obsolete
3. **`line_breaks` field** - Can calculate from content
4. **LineNumberService.cs** - Entire file (term vector approach)
5. **Disk reads in LineSearchTool** - Use indexed content

## Implementation Plan

### Phase 1: Fix LineSearchTool (CRITICAL)

**File**: `LineSearchTool.cs`

Replace disk reading with index reading:
```csharp
private async Task<List<LineMatch>> ExtractAllLineMatches(
    SearchHit hit, 
    LineSearchParams parameters)
{
    var matches = new List<LineMatch>();
    
    // Get content from index, NOT from disk!
    string content = null;
    
    // Option 1: Try content field
    if (hit.Fields.TryGetValue("content", out content) && !string.IsNullOrEmpty(content))
    {
        var lines = content.Split('\n');
        return ExtractMatchesFromLines(lines, parameters);
    }
    
    // Option 2: Try line_data field (while it still exists)
    if (hit.Fields.TryGetValue("line_data", out var lineDataJson))
    {
        var lineData = LineIndexer.DeserializeLineData(lineDataJson);
        if (lineData?.Lines != null)
        {
            return ExtractMatchesFromLines(lineData.Lines, parameters);
        }
    }
    
    // No indexed content - this should never happen
    _logger.LogError("No indexed content for {FilePath} - index may be corrupted", hit.FilePath);
    return matches;
}

private List<LineMatch> ExtractMatchesFromLines(string[] lines, LineSearchParams parameters)
{
    var matches = new List<LineMatch>();
    var pattern = parameters.CaseSensitive ? parameters.Pattern : parameters.Pattern.ToLowerInvariant();
    
    for (int i = 0; i < lines.Length; i++)
    {
        var line = lines[i];
        var searchLine = parameters.CaseSensitive ? line : line.ToLowerInvariant();
        
        if (ContainsPattern(searchLine, pattern, parameters.SearchType))
        {
            var contextStart = Math.Max(0, i - parameters.ContextLines);
            var contextEnd = Math.Min(lines.Length - 1, i + parameters.ContextLines);
            
            var contextLines = new List<string>();
            for (int ctx = contextStart; ctx <= contextEnd; ctx++)
            {
                if (ctx != i)
                    contextLines.Add(lines[ctx]);
            }
            
            matches.Add(new LineMatch
            {
                LineNumber = i + 1, // 1-based
                LineContent = line.Trim(),
                ContextLines = contextLines,
                ContextStart = contextStart + 1,
                ContextEnd = contextEnd + 1
            });
        }
    }
    
    return matches;
}
```

### Phase 2: Fix HTTP API SearchController

**File**: `SearchController.cs`

Use SearchHit correctly:
```csharp
[HttpGet("symbol")]
public async Task<ActionResult<SearchResponse>> SearchSymbol(
    [FromQuery, Required] string name,
    [FromQuery] string? type = null,
    [FromQuery] string? workspace = null,
    [FromQuery] int limit = 10)
{
    var stopwatch = Stopwatch.StartNew();
    
    // Build and execute search
    var queryText = BuildSymbolQuery(name, type);
    using var analyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
    var query = _queryPreprocessor.BuildQuery(queryText, "standard", false, analyzer);
    
    var searchResults = await _luceneService.SearchAsync(
        workspace ?? throw new ArgumentException("Workspace required"), 
        query, 
        limit * 2);
    
    var apiResults = new List<Models.Api.SearchResult>();
    
    foreach (var hit in searchResults.Hits.Take(limit))
    {
        // Line number is ALREADY calculated by LuceneIndexService!
        if (!hit.LineNumber.HasValue)
        {
            _logger.LogWarning("No line number for hit in {File}", hit.FilePath);
            continue;
        }
        
        // Get line content for preview (from context or content field)
        string preview = "";
        int column = 1;
        
        if (hit.ContextLines?.Any() == true)
        {
            // Use context lines if available
            preview = hit.ContextLines.First();
            column = CalculateColumn(preview, name);
        }
        else if (hit.Fields.TryGetValue("content", out var content))
        {
            // Fall back to parsing content
            var lines = content.Split('\n');
            if (hit.LineNumber.Value <= lines.Length)
            {
                preview = lines[hit.LineNumber.Value - 1].Trim();
                column = CalculateColumn(preview, name);
            }
        }
        
        apiResults.Add(new Models.Api.SearchResult
        {
            FilePath = hit.FilePath,
            Line = hit.LineNumber.Value,
            Column = column,
            Preview = preview,
            Confidence = CalculateConfidence(preview, name, type, hit.Score),
            SymbolType = DetectSymbolType(preview, name),
            Metadata = new Dictionary<string, object>
            {
                ["score"] = hit.Score,
                ["hasContext"] = hit.ContextLines?.Any() == true
            }
        });
    }
    
    return Ok(new SearchResponse
    {
        Results = apiResults.OrderByDescending(r => r.Confidence).ToList(),
        TotalCount = searchResults.TotalHits,
        SearchTimeMs = stopwatch.ElapsedMilliseconds,
        Query = queryText,
        Workspace = workspace
    });
}
```

### Phase 3: Simplify LineAwareSearchService

**File**: `LineAwareSearchService.cs`

Remove dependency on LineData, use content directly:
```csharp
public LineAwareResult GetLineNumber(Document document, string queryText, 
    IndexSearcher? searcher = null, int? docId = null)
{
    try
    {
        // Get content from document
        var content = document.Get("content");
        if (string.IsNullOrEmpty(content))
        {
            _logger.LogWarning("No content in document for line number calculation");
            return new LineAwareResult { IsAccurate = false };
        }
        
        var lines = content.Split('\n');
        var searchTerms = ExtractSearchTerms(queryText);
        
        // Find first matching line
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (searchTerms.Any(term => 
                line.Contains(term, StringComparison.OrdinalIgnoreCase)))
            {
                // Calculate context
                var contextStart = Math.Max(0, i - 3);
                var contextEnd = Math.Min(lines.Length - 1, i + 3);
                var contextLines = new List<string>();
                
                for (int c = contextStart; c <= contextEnd; c++)
                {
                    if (c != i)
                        contextLines.Add(lines[c]);
                }
                
                return new LineAwareResult
                {
                    LineNumber = i + 1,
                    Context = new LineContext
                    {
                        LineNumber = i + 1,
                        LineText = line,
                        ContextLines = contextLines,
                        StartLine = contextStart + 1,
                        EndLine = contextEnd + 1
                    },
                    IsAccurate = true,
                    IsFromCache = false
                };
            }
        }
        
        // No match found
        return new LineAwareResult { IsAccurate = false };
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error calculating line number");
        return new LineAwareResult { IsAccurate = false };
    }
}
```

### Phase 4: Clean Up Index Storage

**File**: `FileIndexingService.cs`

Remove redundant fields:
```csharp
private async Task<Document?> CreateDocumentFromFileAsync(
    string filePath, string workspacePath, CancellationToken cancellationToken)
{
    // ... existing file reading code ...
    
    var document = new Document
    {
        // Core fields
        new StringField("path", filePath, Field.Store.YES),
        new StringField("relativePath", Path.GetRelativePath(workspacePath, filePath), Field.Store.YES),
        new TextField("content", content, Field.Store.YES), // KEEP - needed for line extraction
        
        // Metadata fields
        new StringField("extension", fileInfo.Extension.ToLowerInvariant(), Field.Store.YES),
        new Int64Field("size", fileInfo.Length, Field.Store.YES),
        new Int64Field("modified", fileInfo.LastWriteTimeUtc.Ticks, Field.Store.YES),
        new StringField("filename", fileInfo.Name, Field.Store.YES),
        new StringField("filename_lower", fileInfo.Name.ToLowerInvariant(), Field.Store.NO),
        
        // Directory fields
        new StringField("directory", directoryPath, Field.Store.YES),
        new StringField("relativeDirectory", relativeDirectoryPath, Field.Store.YES),
        new StringField("directoryName", directoryName, Field.Store.YES),
        
        // Simple line count for statistics
        new Int32Field("line_count", content.Count(c => c == '\n') + 1, Field.Store.YES),
        
        // REMOVE all of these:
        // - content_tv (term vectors)
        // - line_breaks (position array)
        // - line_data (JSON with all lines)
        // - line_data_version (no longer needed)
    };
    
    // ... rest of method ...
}
```

### Phase 5: Remove Obsolete Code

**Files to DELETE**:
1. `LineNumberService.cs` - Entire file (term vector approach)
2. `LineIndexer.cs` - No longer needed
3. `LineAwareIndexingService.cs` - No longer needed

**Files to SIMPLIFY**:
1. `Program.cs` - Remove registrations for deleted services
2. `LuceneIndexService.cs` - Simplify to use new LineAwareSearchService

## Testing Plan

### 1. Verify LineSearchTool
```bash
# Should NOT access filesystem
# Should return multiple matches per file
# Should use indexed content
```

### 2. Test HTTP API
```bash
GET /api/search/symbol?name=UserService&workspace=C:\source\project
# Should return line and column numbers
# Should work without filesystem access
```

### 3. Verify VS Code Bridge
- Ensure visualizations still work
- Check that context lines display correctly
- Verify navigation to line/column works

## Success Metrics

1. **NO disk reads during search** - Everything from index
2. **Index size reduced by ~60%** - Content stored once, not three times
3. **LineSearchTool faster** - No disk I/O
4. **HTTP API functional** - Returns accurate line/column
5. **VS Code Bridge working** - Visualizations intact
6. **Clean codebase** - No dead code, no competing systems

## Migration Notes

- Existing indexes will continue to work (backward compatibility)
- New indexes will be smaller and faster
- No data loss - content field has everything we need

## Timeline

1. **Day 1**: Fix LineSearchTool (stop disk reads)
2. **Day 1**: Fix HTTP API compilation
3. **Day 2**: Test everything works
4. **Day 3**: Remove redundant storage
5. **Day 4**: Delete obsolete code
6. **Day 5**: Final testing and documentation

This plan addresses the real problems without shortcuts or lies.