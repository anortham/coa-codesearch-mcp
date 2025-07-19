#!/usr/bin/env pwsh
# Claude Memory System - User Prompt Hook (Windows)
# Loads relevant context on first prompt of session

# Check if this is the first prompt in the session
$sessionFile = Join-Path $env:TEMP "claude_session_$env:CLAUDE_CONVERSATION_ID.txt"
if (-not (Test-Path $sessionFile)) {
    Write-Host "🚀 New session started - loading context..." -ForegroundColor Cyan
    
    # Create session marker
    New-Item -ItemType File -Path $sessionFile -Force | Out-Null
    
    # Initialize memory hooks for this session
    Write-Host "🔧 Initializing memory hooks..." -ForegroundColor Cyan
    & coa-codesearch-mcp init_memory_hooks --projectRoot "$PWD" 2>$null
    
    # Load recent work sessions
    & coa-codesearch-mcp list_memories_by_type WorkSession --maxResults 3 2>$null
    
    # Load architectural decisions
    & coa-codesearch-mcp list_memories_by_type ArchitecturalDecision --maxResults 5 2>$null
    
    # Search for context based on prompt
    $promptWords = $env:CLAUDE_USER_MESSAGE -split '\s+' | Where-Object { $_.Length -gt 3 }
    if ($promptWords.Count -gt 0) {
        $searchQuery = $promptWords[0..2] -join ' '
        Write-Host "🔍 Searching for context: $searchQuery" -ForegroundColor Cyan
        & coa-codesearch-mcp recall_context "$searchQuery" 2>$null
    }
}

exit 0