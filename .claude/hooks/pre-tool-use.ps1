#!/usr/bin/env pwsh
# Claude Memory System - Pre Tool Use Hook (Windows)
# Loads relevant context before MCP tool execution
# Only runs when MCP tools are actually being used

param(
    [string]$ToolName = $env:CLAUDE_TOOL_NAME,
    [string]$ToolParams = $env:CLAUDE_TOOL_PARAMS
)

# Debug logging
$debugLog = Join-Path $PSScriptRoot "hook-debug.log"
$timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
Add-Content -Path $debugLog -Value "`n[$timestamp] pre-tool-use.ps1 started"
Add-Content -Path $debugLog -Value "Tool: $ToolName"

# Only process for MCP codesearch tools
if ($ToolName -notlike "mcp__codesearch__*") {
    Add-Content -Path $debugLog -Value "Not a codesearch tool, skipping"
    exit 0
}

# Parse tool parameters to find file paths or search queries
$filePath = $null
$query = $null

if ($ToolParams) {
    try {
        $params = $ToolParams | ConvertFrom-Json
        $filePath = $params.filePath
        $query = $params.query -or $params.searchPattern -or $params.pattern
        Add-Content -Path $debugLog -Value "Extracted - File: $filePath, Query: $query"
    }
    catch {
        Add-Content -Path $debugLog -Value "Failed to parse tool params: $_"
    }
}

# Tools that might benefit from context loading
$contextTools = @(
    "mcp__codesearch__find_references",
    "mcp__codesearch__rename_symbol",
    "mcp__codesearch__dependency_analysis",
    "mcp__codesearch__get_implementations",
    "mcp__codesearch__fast_text_search",
    "mcp__codesearch__search_symbols"
)

if ($ToolName -in $contextTools) {
    # Build context query based on tool and parameters
    $contextQuery = ""
    
    if ($filePath) {
        # Extract file name without extension for context search
        $fileName = [System.IO.Path]::GetFileNameWithoutExtension($filePath)
        $contextQuery = $fileName
    }
    elseif ($query) {
        $contextQuery = $query
    }
    
    if ($contextQuery) {
        Write-Host "💡 Loading relevant memories for: $contextQuery" -ForegroundColor Cyan
        
        # Suggest loading context (can't execute MCP tools from hooks)
        Write-Host "Consider using: mcp__codesearch__recall_context '$contextQuery'" -ForegroundColor Gray
        Add-Content -Path $debugLog -Value "Suggested context query: $contextQuery"
    }
}

# For memory backup/restore operations, provide helpful reminders
if ($ToolName -eq "mcp__codesearch__backup_memories_to_sqlite") {
    Write-Host "📦 Backing up memories to SQLite..." -ForegroundColor Green
    Write-Host "Tip: The backup file (memories.db) can be checked into source control" -ForegroundColor Gray
}
elseif ($ToolName -eq "mcp__codesearch__restore_memories_from_sqlite") {
    Write-Host "📥 Restoring memories from SQLite backup..." -ForegroundColor Green
    Write-Host "Tip: This is useful when setting up on a new machine" -ForegroundColor Gray
}

Add-Content -Path $debugLog -Value "[$timestamp] pre-tool-use.ps1 completed"
exit 0