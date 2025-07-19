#!/usr/bin/env pwsh
# Claude Memory System - Tool Call Hook (Windows)
# Automatically loads relevant context before tool execution

param(
    [string]$CLAUDE_TOOL_NAME,
    [string]$CLAUDE_TOOL_PARAMS
)

# Parse tool parameters to extract file paths
$params = $CLAUDE_TOOL_PARAMS | ConvertFrom-Json -ErrorAction SilentlyContinue

# Load context for code navigation tools
if ($CLAUDE_TOOL_NAME -match 'find_references|go_to_definition|rename_symbol') {
    if ($params.filePath) {
        $fileName = Split-Path -Leaf $params.filePath
        Write-Host "🧠 Loading memories for: $fileName" -ForegroundColor Cyan
        
        # Call the MCP server to recall context
        & coa-codesearch-mcp recall_context "$fileName" 2>$null
    }
}

# Load architectural context for analysis tools
if ($CLAUDE_TOOL_NAME -match 'dependency_analysis|project_structure') {
    Write-Host "🏗️ Loading architectural decisions..." -ForegroundColor Cyan
    & coa-codesearch-mcp list_memories_by_type ArchitecturalDecision 2>$null
}

# Exit successfully to allow tool execution
exit 0
