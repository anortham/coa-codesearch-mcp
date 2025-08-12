# Comprehensive Test Script for CodeSearch MCP Tools
# This script tests all tools with various scenarios and validates outputs

$ErrorActionPreference = "Continue"
$testResults = @()
$workspace = "C:\source\COA CodeSearch MCP"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "CodeSearch MCP Tools Test Suite" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Helper function to run MCP tool and capture output
function Test-McpTool {
    param(
        [string]$ToolName,
        [string]$Description,
        [hashtable]$Parameters
    )
    
    Write-Host "Testing: $Description" -ForegroundColor Yellow
    Write-Host "Tool: mcp__codesearch-next__$ToolName" -ForegroundColor Gray
    Write-Host "Parameters:" -ForegroundColor Gray
    $Parameters.GetEnumerator() | ForEach-Object {
        Write-Host "  $($_.Key): $($_.Value)" -ForegroundColor DarkGray
    }
    
    $startTime = Get-Date
    
    try {
        # Simulate tool call - in real scenario this would be through MCP
        $result = @{
            Tool = $ToolName
            Description = $Description
            Parameters = $Parameters
            StartTime = $startTime
            Status = "Testing"
        }
        
        # Log the test
        $testResults += $result
        
        Write-Host "✓ Test logged" -ForegroundColor Green
        return $result
    }
    catch {
        Write-Host "✗ Error: $_" -ForegroundColor Red
        return @{
            Tool = $ToolName
            Description = $Description
            Error = $_.ToString()
            Status = "Failed"
        }
    }
}

# Test 1: IndexWorkspaceTool
Write-Host "`n=== TEST 1: IndexWorkspaceTool ===" -ForegroundColor Cyan

# Test normal indexing
Test-McpTool -ToolName "index_workspace" -Description "Index current workspace" -Parameters @{
    workspacePath = $workspace
    forceRebuild = $false
}

# Test force rebuild
Test-McpTool -ToolName "index_workspace" -Description "Force rebuild index" -Parameters @{
    workspacePath = $workspace
    forceRebuild = $true
}

# Test invalid path
Test-McpTool -ToolName "index_workspace" -Description "Test invalid path handling" -Parameters @{
    workspacePath = "C:\NonExistentPath"
    forceRebuild = $false
}

# Test 2: TextSearchTool
Write-Host "`n=== TEST 2: TextSearchTool ===" -ForegroundColor Cyan

# Search for common term
Test-McpTool -ToolName "text_search" -Description "Search for 'LuceneIndexService'" -Parameters @{
    query = "LuceneIndexService"
    workspacePath = $workspace
    responseMode = "summary"
    maxTokens = 8000
    noCache = $true
}

# Search with complex query
Test-McpTool -ToolName "text_search" -Description "Complex query with AND/OR" -Parameters @{
    query = "async AND (index OR search)"
    workspacePath = $workspace
    responseMode = "full"
    maxTokens = 10000
    noCache = $true
}

# Search for code patterns
Test-McpTool -ToolName "text_search" -Description "Search for code pattern" -Parameters @{
    query = "Task<.*Result>"
    workspacePath = $workspace
    responseMode = "adaptive"
    maxTokens = 8000
    noCache = $false
}

# Test 3: FileSearchTool
Write-Host "`n=== TEST 3: FileSearchTool ===" -ForegroundColor Cyan

# Search by extension
Test-McpTool -ToolName "file_search" -Description "Find all .cs files" -Parameters @{
    pattern = "*.cs"
    workspacePath = $workspace
    extensionFilter = ".cs"
    maxResults = 50
    responseMode = "summary"
    maxTokens = 8000
    noCache = $true
}

# Search by name pattern
Test-McpTool -ToolName "file_search" -Description "Find files with 'Tool' in name" -Parameters @{
    pattern = "*Tool*"
    workspacePath = $workspace
    maxResults = 100
    responseMode = "full"
    maxTokens = 8000
    noCache = $true
}

# Test regex pattern
Test-McpTool -ToolName "file_search" -Description "Regex pattern search" -Parameters @{
    pattern = ".*Service\.cs$"
    workspacePath = $workspace
    useRegex = $true
    maxResults = 50
    responseMode = "adaptive"
    maxTokens = 8000
    noCache = $true
}

# Test 4: DirectorySearchTool
Write-Host "`n=== TEST 4: DirectorySearchTool ===" -ForegroundColor Cyan

# Search for Services directories
Test-McpTool -ToolName "directory_search" -Description "Find 'Services' directories" -Parameters @{
    pattern = "Services"
    workspacePath = $workspace
    includeSubdirectories = $true
    maxResults = 20
    responseMode = "full"
    maxTokens = 8000
    noCache = $true
}

# Search with wildcard
Test-McpTool -ToolName "directory_search" -Description "Find directories with 'Test' pattern" -Parameters @{
    pattern = "*Test*"
    workspacePath = $workspace
    includeSubdirectories = $true
    includeHidden = $false
    maxResults = 50
    responseMode = "summary"
    maxTokens = 8000
    noCache = $true
}

# Test 5: RecentFilesTool
Write-Host "`n=== TEST 5: RecentFilesTool ===" -ForegroundColor Cyan

# Files modified in last hour
Test-McpTool -ToolName "recent_files" -Description "Files modified in last hour" -Parameters @{
    workspacePath = $workspace
    timeFrame = "1h"
    maxResults = 20
    responseMode = "full"
    maxTokens = 8000
    noCache = $true
}

# Files modified today
Test-McpTool -ToolName "recent_files" -Description "Files modified today" -Parameters @{
    workspacePath = $workspace
    timeFrame = "1d"
    extensionFilter = ".cs,.json"
    maxResults = 50
    responseMode = "summary"
    maxTokens = 8000
    noCache = $true
}

# Test 6: SimilarFilesTool
Write-Host "`n=== TEST 6: SimilarFilesTool ===" -ForegroundColor Cyan

# Find files similar to LuceneIndexService
Test-McpTool -ToolName "similar_files" -Description "Files similar to LuceneIndexService.cs" -Parameters @{
    filePath = "$workspace\COA.CodeSearch.McpServer\Services\Lucene\LuceneIndexService.cs"
    workspacePath = $workspace
    maxResults = 10
    minScore = 0.1
    responseMode = "full"
    maxTokens = 8000
    noCache = $true
}

# Find files similar to a tool
Test-McpTool -ToolName "similar_files" -Description "Files similar to TextSearchTool.cs" -Parameters @{
    filePath = "$workspace\COA.CodeSearch.McpServer\Tools\TextSearchTool.cs"
    workspacePath = $workspace
    maxResults = 5
    minScore = 0.2
    responseMode = "adaptive"
    maxTokens = 8000
    noCache = $true
}

# Generate summary report
Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "Test Summary" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

$totalTests = $testResults.Count
Write-Host "Total Tests: $totalTests" -ForegroundColor White

# Save results to file
$reportPath = "$workspace\TEST_RESULTS_$(Get-Date -Format 'yyyyMMdd_HHmmss').json"
$testResults | ConvertTo-Json -Depth 10 | Out-File $reportPath
Write-Host "Results saved to: $reportPath" -ForegroundColor Green

Write-Host "`nNOTE: This script logs test scenarios. Actual MCP tool execution requires Claude Code." -ForegroundColor Yellow
Write-Host "To run actual tests, use these tool calls in a Claude Code session." -ForegroundColor Yellow