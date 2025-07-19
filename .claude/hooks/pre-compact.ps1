#!/usr/bin/env pwsh
# Claude Memory System - Pre Compact Hook
# Saves important context before compaction

param(
    [string]$CLAUDE_SESSION_ID,
    [string]$CLAUDE_TRANSCRIPT_PATH,
    [string]$CLAUDE_HOOK_EVENT_NAME,
    [string]$CLAUDE_TRIGGER,
    [string]$CLAUDE_CUSTOM_INSTRUCTIONS
)

Write-Host "📦 Context compaction triggered ($CLAUDE_TRIGGER)" -ForegroundColor Yellow

# Extract key information from current session to preserve
$compactSummary = @"
Context compaction at $(Get-Date -Format "yyyy-MM-dd HH:mm")
Trigger: $CLAUDE_TRIGGER
"@

if ($CLAUDE_TRIGGER -eq "manual" -and $CLAUDE_CUSTOM_INSTRUCTIONS) {
    $compactSummary += "`nInstructions: $CLAUDE_CUSTOM_INSTRUCTIONS"
}

# Save any recent architectural decisions or important context
Write-Host "💾 Preserving architectural decisions..." -ForegroundColor Cyan
& coa-codesearch-mcp list_memories_by_type ArchitecturalDecision 2>$null

# Store a memory about what we were working on
& coa-codesearch-mcp remember_session "Pre-compact checkpoint: $compactSummary" 2>$null

# Quick check for uncommitted changes that might be lost
if (Test-Path .git) {
    $changes = @(git diff --name-only 2>$null)
    if ($changes.Count -gt 0) {
        Write-Host "⚠️  Uncommitted changes in: $($changes -join ', ')" -ForegroundColor Yellow
        Write-Host "   Consider saving important decisions before compaction" -ForegroundColor Gray
    }
}

Write-Host "✅ Context prepared for compaction" -ForegroundColor Green
exit 0