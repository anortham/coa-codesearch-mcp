#!/usr/bin/env pwsh
# Claude Memory System - File Edit Hook (Windows)
# Detects patterns and architectural decisions in edited files

param(
    [string]$CLAUDE_FILE_PATH,
    [string]$CLAUDE_FILE_OPERATION
)

if ($CLAUDE_FILE_OPERATION -eq 'edit' -or $CLAUDE_FILE_OPERATION -eq 'create') {
    $fileName = Split-Path -Leaf $CLAUDE_FILE_PATH
    $content = Get-Content $CLAUDE_FILE_PATH -Raw -ErrorAction SilentlyContinue
    
    # Detect architectural patterns
    if ($content -match 'class\s+\w+Repository|class\s+\w+Service|class\s+\w+Controller') {
        Write-Host "🔍 Detected architectural pattern in $fileName" -ForegroundColor Yellow
        Write-Host "💡 Consider documenting this pattern with: remember_pattern" -ForegroundColor Gray
    }
    
    # Detect security implementations
    if ($content -match 'Authorize|Authentication|Encryption|HIPAA') {
        Write-Host "🔒 Detected security-related code in $fileName" -ForegroundColor Yellow
        Write-Host "💡 Consider documenting with: remember_security_rule" -ForegroundColor Gray
    }
}

exit 0
