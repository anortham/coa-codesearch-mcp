#!/usr/bin/env pwsh
# Claude Memory System - Post Tool Use Hook (Windows/Cross-platform)
# Learns from tool failures to prevent repeated mistakes

param(
    [string]$CLAUDE_TOOL_NAME,
    [string]$CLAUDE_TOOL_PARAMS,
    [string]$CLAUDE_TOOL_RESULT
)

# Parse results to check for failures
$result = $CLAUDE_TOOL_RESULT | ConvertFrom-Json -ErrorAction SilentlyContinue
if (-not $result) { exit 0 }

# Check if the tool failed
$failed = $false
$errorMessage = ""

if ($result.error) {
    $failed = $true
    $errorMessage = $result.error
} elseif ($result.output -and $CLAUDE_TOOL_NAME -eq 'Bash') {
    # Common failure patterns across platforms
    $failurePatterns = @(
        "not recognized as an internal or external command",  # Windows
        "command not found",                                  # Unix/Linux
        "No such file or directory",
        "Permission denied",
        "is not recognized as a cmdlet",                    # PowerShell
        "The term .* is not recognized"
    )
    
    foreach ($pattern in $failurePatterns) {
        if ($result.output -match $pattern) {
            $failed = $true
            $errorMessage = $result.output
            break
        }
    }
}

if ($failed) {
    # Extract the key parts of the failure
    $params = $CLAUDE_TOOL_PARAMS | ConvertFrom-Json -ErrorAction SilentlyContinue
    
    # Create a failure signature
    $failureKey = ""
    if ($CLAUDE_TOOL_NAME -eq 'Bash' -and $params.command) {
        # Extract the base command (first word)
        $baseCommand = ($params.command -split '\s+')[0]
        $failureKey = "bash:$baseCommand"
    } else {
        $failureKey = "${CLAUDE_TOOL_NAME}:${errorMessage}"
    }
    
    # Store this failure pattern in memory
    & coa-codesearch-mcp remember_pattern `
        "Tool failure: $failureKey" `
        "Cross-platform compatibility" `
        "Error: $errorMessage. Consider platform-specific alternatives or MCP tools." `
        @("tool-failure", "compatibility", $CLAUDE_TOOL_NAME.ToLower()) 2>$null
    
    # Quick suggestions for common failures
    $suggestions = @{
        'bash:kill' = "Use 'taskkill /PID' (Windows) or 'kill' (Unix)"
        'bash:grep' = "Use the Grep MCP tool for cross-platform search"
        'bash:find' = "Use fast_file_search or Glob MCP tools"
        'bash:cat' = "Use the Read MCP tool"
        'bash:ls' = "Use the LS MCP tool"
    }
    
    if ($suggestions.ContainsKey($failureKey)) {
        Write-Host "💡 Cross-platform tip: $($suggestions[$failureKey])" -ForegroundColor Yellow
    }
}

exit 0