#!/usr/bin/env pwsh
# Claude Memory System - Session End Hook (Windows)
# Automatically summarizes work session

Write-Host "📝 Session ending - storing work summary..." -ForegroundColor Green

# Get modified files from git if available
$modifiedFiles = @()
if (Test-Path .git) {
    $modifiedFiles = git diff --name-only 2>$null
}

$sessionSummary = "Session on $(Get-Date -Format 'yyyy-MM-dd HH:mm')"
if ($modifiedFiles) {
    $sessionSummary += ". Modified: $($modifiedFiles -join ', ')"
}

# Store session summary
& coa-codesearch-mcp remember_session "$sessionSummary" $modifiedFiles 2>$null

Write-Host "✅ Session memory saved" -ForegroundColor Green
exit 0
