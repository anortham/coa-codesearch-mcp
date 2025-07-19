#!/usr/bin/env pwsh
# Claude Memory System - Stop Hook (Windows)
# Minimal tracking - optimized for speed

# Only track session time - no git, no file I/O unless needed
$sessionFile = Join-Path $PSScriptRoot ".session-start"

if (Test-Path $sessionFile) {
    $sessionStart = Get-Content $sessionFile
    $duration = (Get-Date) - [DateTime]$sessionStart
    
    # Only show message for long sessions
    if ($duration.TotalMinutes -gt 45) {
        Write-Host "⏱️ Long session ($([int]$duration.TotalMinutes)m) - consider saving insights" -ForegroundColor Cyan
        Get-Date | Set-Content $sessionFile  # Reset
    }
} else {
    Get-Date | Set-Content $sessionFile
}

exit 0