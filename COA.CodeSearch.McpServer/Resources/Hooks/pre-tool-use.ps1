#!/usr/bin/env pwsh
# Claude Memory System - Pre Tool Use Hook (Cross-platform)
# Checks for known failures and suggests alternatives

param(
    [string]$CLAUDE_TOOL_NAME,
    [string]$CLAUDE_TOOL_PARAMS
)

# Parse parameters
$params = $CLAUDE_TOOL_PARAMS | ConvertFrom-Json -ErrorAction SilentlyContinue

# For Bash commands, check for known failure patterns
if ($CLAUDE_TOOL_NAME -eq 'Bash' -and $params.command) {
    $baseCommand = ($params.command -split '\s+')[0]
    $failureKey = "bash:$baseCommand"
    
    # Search memory for this failure pattern
    $memories = & coa-codesearch-mcp recall_context "Tool failure: $failureKey" 2>$null | ConvertFrom-Json -ErrorAction SilentlyContinue
    
    if ($memories -and $memories.results) {
        $relevantMemory = $memories.results | Where-Object { $_.content -like "*$failureKey*" } | Select-Object -First 1
        if ($relevantMemory) {
            Write-Host "⚠️ Previous failure detected with '$baseCommand'" -ForegroundColor Yellow
            Write-Host "   $($relevantMemory.usage)" -ForegroundColor Gray
        }
    }
    
    # Quick platform detection for common issues
    $isWindows = $PSVersionTable.PSVersion -or $env:OS -eq "Windows_NT"
    
    if ($isWindows) {
        # Windows-specific warnings
        $unixCommands = @('kill', 'pkill', 'grep', 'find', 'ps', 'cat', 'head', 'tail', 'which')
        if ($baseCommand -in $unixCommands) {
            Write-Host "🔄 Cross-platform tip: Consider using MCP tools instead of $baseCommand" -ForegroundColor Cyan
        }
    }
}

# Load context for navigation tools (original functionality)
if ($CLAUDE_TOOL_NAME -match 'find_references|go_to_definition|rename_symbol') {
    if ($params.filePath) {
        $fileName = Split-Path -Leaf $params.filePath
        & coa-codesearch-mcp recall_context "$fileName" 2>$null
    }
}

# Suggest faster alternatives for search operations
$searchTools = @{
    'Grep' = "Consider fast_text_search for indexed search (requires index_workspace)"
    'Task' = "For specific file/text searches, use fast_text_search or fast_file_search"
}

if ($searchTools.ContainsKey($CLAUDE_TOOL_NAME)) {
    Write-Host "⚡ Performance tip: $($searchTools[$CLAUDE_TOOL_NAME])" -ForegroundColor Cyan
}

exit 0