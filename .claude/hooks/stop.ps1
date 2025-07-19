#!/usr/bin/env pwsh
# Claude Memory System - Stop Hook (Windows)
# Tracks work units after each Claude response
# More granular than session tracking

# Debug logging
$debugLog = Join-Path $PSScriptRoot "hook-debug.log"
$timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
Add-Content -Path $debugLog -Value "`n[$timestamp] stop.ps1 started"

# Track work in a daily log file
$today = Get-Date -Format "yyyy-MM-dd"
$workLog = Join-Path $PSScriptRoot "work-log-$today.txt"

# Get git status to understand what was changed
$changedFiles = @()
if (Test-Path .git) {
    $changedFiles = @(git diff --name-only 2>$null) + @(git diff --cached --name-only 2>$null) | Select-Object -Unique
}

if ($changedFiles.Count -gt 0) {
    $entry = "[$timestamp] Modified: $($changedFiles -join ', ')"
    Add-Content -Path $workLog -Value $entry
    Add-Content -Path $debugLog -Value "Logged work: $entry"
    
    # If significant changes, suggest creating a memory
    if ($changedFiles.Count -gt 3) {
        Write-Host "💭 Consider documenting this work session:" -ForegroundColor Yellow
        Write-Host "mcp__codesearch__remember_session 'Description of changes'" -ForegroundColor Gray
    }
}

Add-Content -Path $debugLog -Value "[$timestamp] stop.ps1 completed"
exit 0