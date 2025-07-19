#!/usr/bin/env pwsh
# Claude Memory System - Session End Hook (Windows)
# Automatically summarizes work session

Write-Host "📝 Session ending - storing work summary..." -ForegroundColor Green

# Get modified files from git if available
$modifiedFiles = @()
if (Test-Path .git) {
    $modifiedFiles = @(git diff --name-only 2>$null) + @(git diff --cached --name-only 2>$null) | Select-Object -Unique
}

# Build session summary based on what we can detect
$sessionDate = Get-Date -Format 'yyyy-MM-dd HH:mm'
$summary = "Work session on $sessionDate"

# Add file context if available
if ($modifiedFiles.Count -gt 0) {
    $fileTypes = $modifiedFiles | ForEach-Object { [System.IO.Path]::GetExtension($_) } | Select-Object -Unique
    $summary += ". Worked on $($modifiedFiles.Count) files"
    if ($fileTypes) {
        $summary += " ($($fileTypes -join ', '))"
    }
}

# Convert file array to JSON array format for the command
$filesJson = '[]'
if ($modifiedFiles.Count -gt 0) {
    $filesJson = '[' + (($modifiedFiles | ForEach-Object { "`"$_`"" }) -join ',') + ']'
}

# Store session summary with proper parameter format
$command = "coa-codesearch-mcp remember_session --summary `"$summary`" --filesWorkedOn $filesJson"
Invoke-Expression $command 2>$null

Write-Host "✅ Session memory saved: $summary" -ForegroundColor Green
exit 0