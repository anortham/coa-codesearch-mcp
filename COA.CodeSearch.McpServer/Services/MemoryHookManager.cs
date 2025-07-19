using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
using System.Text;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Manages cross-platform hook scripts for Claude's memory system
/// Generates platform-specific scripts that integrate with Claude Code's hook system
/// </summary>
public class MemoryHookManager
{
    private readonly ILogger<MemoryHookManager> _logger;
    private readonly string _projectRoot;
    private readonly string _hooksDirectory;

    public MemoryHookManager(ILogger<MemoryHookManager> logger, string? projectRoot = null)
    {
        _logger = logger;
        _projectRoot = projectRoot ?? Directory.GetCurrentDirectory();
        _hooksDirectory = Path.Combine(_projectRoot, ".claude", "hooks");
    }

    /// <summary>
    /// Initializes memory hooks for the current project
    /// </summary>
    public async Task<bool> InitializeHooksAsync()
    {
        try
        {
            // Create hooks directory if it doesn't exist
            Directory.CreateDirectory(_hooksDirectory);

            // Generate hooks based on platform
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                await CreateWindowsHooksAsync();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || 
                     RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                await CreateUnixHooksAsync();
            }

            _logger.LogInformation("Memory hooks initialized in {Directory}", _hooksDirectory);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize memory hooks");
            return false;
        }
    }

    private async Task CreateWindowsHooksAsync()
    {
        // Tool call hook - PowerShell
        var toolCallHook = @"#!/usr/bin/env pwsh
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
        Write-Host ""🧠 Loading memories for: $fileName"" -ForegroundColor Cyan
        
        # Call the MCP server to recall context
        & coa-codesearch-mcp recall_context ""$fileName"" 2>$null
    }
}

# Load architectural context for analysis tools
if ($CLAUDE_TOOL_NAME -match 'dependency_analysis|project_structure') {
    Write-Host ""🏗️ Loading architectural decisions..."" -ForegroundColor Cyan
    & coa-codesearch-mcp list_memories_by_type ArchitecturalDecision 2>$null
}

# Exit successfully to allow tool execution
exit 0
";

        // File edit hook - PowerShell
        var fileEditHook = @"#!/usr/bin/env pwsh
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
        Write-Host ""🔍 Detected architectural pattern in $fileName"" -ForegroundColor Yellow
        Write-Host ""💡 Consider documenting this pattern with: remember_pattern"" -ForegroundColor Gray
    }
    
    # Detect security implementations
    if ($content -match 'Authorize|Authentication|Encryption|HIPAA') {
        Write-Host ""🔒 Detected security-related code in $fileName"" -ForegroundColor Yellow
        Write-Host ""💡 Consider documenting with: remember_security_rule"" -ForegroundColor Gray
    }
}

exit 0
";

        // Session end hook - PowerShell
        var sessionEndHook = @"#!/usr/bin/env pwsh
# Claude Memory System - Session End Hook (Windows)
# Automatically summarizes work session

Write-Host ""📝 Session ending - storing work summary..."" -ForegroundColor Green

# Get modified files from git if available
$modifiedFiles = @()
if (Test-Path .git) {
    $modifiedFiles = git diff --name-only 2>$null
}

$sessionSummary = ""Session on $(Get-Date -Format 'yyyy-MM-dd HH:mm')""
if ($modifiedFiles) {
    $sessionSummary += "". Modified: $($modifiedFiles -join ', ')""
}

# Store session summary
& coa-codesearch-mcp remember_session ""$sessionSummary"" $modifiedFiles 2>$null

Write-Host ""✅ Session memory saved"" -ForegroundColor Green
exit 0
";

        // User prompt hook - PowerShell
        var userPromptHook = @"#!/usr/bin/env pwsh
# Claude Memory System - User Prompt Hook (Windows)
# Loads relevant context on first prompt of session

# Check if this is the first prompt in the session
$sessionFile = Join-Path $env:TEMP ""claude_session_$env:CLAUDE_CONVERSATION_ID.txt""
if (-not (Test-Path $sessionFile)) {
    Write-Host ""🚀 New session started - loading context..."" -ForegroundColor Cyan
    
    # Create session marker
    New-Item -ItemType File -Path $sessionFile -Force | Out-Null
    
    # Load recent work sessions
    & coa-codesearch-mcp list_memories_by_type WorkSession --maxResults 3 2>$null
    
    # Load architectural decisions
    & coa-codesearch-mcp list_memories_by_type ArchitecturalDecision --maxResults 5 2>$null
    
    # Search for context based on prompt
    $promptWords = $env:CLAUDE_USER_MESSAGE -split '\s+' | Where-Object { $_.Length -gt 3 }
    if ($promptWords.Count -gt 0) {
        $searchQuery = $promptWords[0..2] -join ' '
        Write-Host ""🔍 Searching for context: $searchQuery"" -ForegroundColor Cyan
        & coa-codesearch-mcp recall_context ""$searchQuery"" 2>$null
    }
}

exit 0
";

        await WriteHookFileAsync("tool-call.ps1", toolCallHook);
        await WriteHookFileAsync("file-edit.ps1", fileEditHook);
        await WriteHookFileAsync("session-end.ps1", sessionEndHook);
        await WriteHookFileAsync("user-prompt-submit.ps1", userPromptHook);
    }

    private async Task CreateUnixHooksAsync()
    {
        // Tool call hook - Bash
        var toolCallHook = @"#!/bin/bash
# Claude Memory System - Tool Call Hook (Unix)
# Automatically loads relevant context before tool execution

# Extract file path from tool parameters
if [[ ""$CLAUDE_TOOL_NAME"" =~ find_references|go_to_definition|rename_symbol ]]; then
    # Parse JSON to get filePath (using jq if available, fallback to grep)
    if command -v jq &> /dev/null; then
        FILE_PATH=$(echo ""$CLAUDE_TOOL_PARAMS"" | jq -r '.filePath // empty')
    else
        FILE_PATH=$(echo ""$CLAUDE_TOOL_PARAMS"" | grep -oP '""filePath""\s*:\s*""\K[^""]+')
    fi
    
    if [[ -n ""$FILE_PATH"" ]]; then
        FILE_NAME=$(basename ""$FILE_PATH"")
        echo ""🧠 Loading memories for: $FILE_NAME""
        coa-codesearch-mcp recall_context ""$FILE_NAME"" 2>/dev/null || true
    fi
fi

# Load architectural context for analysis tools
if [[ ""$CLAUDE_TOOL_NAME"" =~ dependency_analysis|project_structure ]]; then
    echo ""🏗️ Loading architectural decisions...""
    coa-codesearch-mcp list_memories_by_type ArchitecturalDecision 2>/dev/null || true
fi

exit 0
";

        // File edit hook - Bash
        var fileEditHook = @"#!/bin/bash
# Claude Memory System - File Edit Hook (Unix)
# Detects patterns and architectural decisions in edited files

if [[ ""$CLAUDE_FILE_OPERATION"" == ""edit"" ]] || [[ ""$CLAUDE_FILE_OPERATION"" == ""create"" ]]; then
    FILE_NAME=$(basename ""$CLAUDE_FILE_PATH"")
    
    # Only analyze code files
    if [[ ""$CLAUDE_FILE_PATH"" =~ \.(cs|ts|js|tsx|jsx)$ ]]; then
        # Detect architectural patterns
        if grep -qE 'class\s+\w+(Repository|Service|Controller)' ""$CLAUDE_FILE_PATH"" 2>/dev/null; then
            echo ""🔍 Detected architectural pattern in $FILE_NAME""
            echo ""💡 Consider documenting this pattern with: remember_pattern""
        fi
        
        # Detect security implementations
        if grep -qE 'Authorize|Authentication|Encryption|HIPAA' ""$CLAUDE_FILE_PATH"" 2>/dev/null; then
            echo ""🔒 Detected security-related code in $FILE_NAME""
            echo ""💡 Consider documenting with: remember_security_rule""
        fi
    fi
fi

exit 0
";

        // Session end hook - Bash
        var sessionEndHook = @"#!/bin/bash
# Claude Memory System - Session End Hook (Unix)
# Automatically summarizes work session

echo ""📝 Session ending - storing work summary...""

# Get modified files from git if available
MODIFIED_FILES=""""
if [[ -d .git ]]; then
    MODIFIED_FILES=$(git diff --name-only 2>/dev/null | tr '\n' ' ')
fi

SESSION_SUMMARY=""Session on $(date +'%Y-%m-%d %H:%M')""
if [[ -n ""$MODIFIED_FILES"" ]]; then
    SESSION_SUMMARY=""$SESSION_SUMMARY. Modified: $MODIFIED_FILES""
fi

# Store session summary
coa-codesearch-mcp remember_session ""$SESSION_SUMMARY"" $MODIFIED_FILES 2>/dev/null || true

echo ""✅ Session memory saved""
exit 0
";

        // User prompt hook - Bash
        var userPromptHook = @"#!/bin/bash
# Claude Memory System - User Prompt Hook (Unix)
# Loads relevant context on first prompt of session

# Check if this is the first prompt in the session
SESSION_FILE=""/tmp/claude_session_${CLAUDE_CONVERSATION_ID}.txt""
if [[ ! -f ""$SESSION_FILE"" ]]; then
    echo ""🚀 New session started - loading context...""
    
    # Create session marker
    touch ""$SESSION_FILE""
    
    # Load recent work sessions
    coa-codesearch-mcp list_memories_by_type WorkSession --maxResults 3 2>/dev/null || true
    
    # Load architectural decisions
    coa-codesearch-mcp list_memories_by_type ArchitecturalDecision --maxResults 5 2>/dev/null || true
    
    # Search for context based on prompt
    SEARCH_QUERY=$(echo ""$CLAUDE_USER_MESSAGE"" | awk '{print $1, $2, $3}')
    if [[ -n ""$SEARCH_QUERY"" ]]; then
        echo ""🔍 Searching for context: $SEARCH_QUERY""
        coa-codesearch-mcp recall_context ""$SEARCH_QUERY"" 2>/dev/null || true
    fi
fi

exit 0
";

        await WriteHookFileAsync("tool-call.sh", toolCallHook, makeExecutable: true);
        await WriteHookFileAsync("file-edit.sh", fileEditHook, makeExecutable: true);
        await WriteHookFileAsync("session-end.sh", sessionEndHook, makeExecutable: true);
        await WriteHookFileAsync("user-prompt-submit.sh", userPromptHook, makeExecutable: true);
    }

    private async Task WriteHookFileAsync(string fileName, string content, bool makeExecutable = false)
    {
        var filePath = Path.Combine(_hooksDirectory, fileName);
        
        // Normalize line endings for the platform
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && !fileName.EndsWith(".sh"))
        {
            content = content.Replace("\n", "\r\n");
        }
        else
        {
            content = content.Replace("\r\n", "\n");
        }

        await File.WriteAllTextAsync(filePath, content, Encoding.UTF8);

        // Make executable on Unix
        if (makeExecutable && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            MakeFileExecutable(filePath);
        }

        _logger.LogInformation("Created hook: {FileName}", fileName);
    }

    private void MakeFileExecutable(string filePath)
    {
        try
        {
            // Use chmod to make file executable
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "chmod",
                Arguments = $"+x \"{filePath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            process?.WaitForExit();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to make file executable: {FilePath}", filePath);
        }
    }

    /// <summary>
    /// Creates a README for the hooks directory explaining their purpose
    /// </summary>
    public async Task CreateHooksReadmeAsync()
    {
        var readme = @"# Claude Memory System Hooks

These hooks integrate with Claude Code to provide automatic memory management:

## 🎯 Hook Functions

### tool-call
- **Triggers**: Before any MCP tool execution
- **Function**: Loads relevant memories based on the tool and file context
- **Example**: When using `find_references` on UserService.cs, automatically loads previous decisions about UserService

### file-edit  
- **Triggers**: After file edits or creation
- **Function**: Detects architectural patterns and suggests memory storage
- **Example**: Detects new Repository pattern and suggests documenting it

### session-end
- **Triggers**: When Claude Code session ends
- **Function**: Automatically stores session summary with modified files
- **Example**: Saves ""Worked on authentication module, modified UserController.cs""

## 🔧 Configuration

Hooks are platform-specific:
- **Windows**: PowerShell scripts (.ps1)
- **macOS/Linux**: Bash scripts (.sh)

## 📝 Manual Testing

Test hooks manually:
```bash
# Unix/macOS
CLAUDE_TOOL_NAME=""find_references"" CLAUDE_TOOL_PARAMS='{""filePath"":""test.cs""}' ./tool-call.sh

# Windows
$env:CLAUDE_TOOL_NAME=""find_references""; $env:CLAUDE_TOOL_PARAMS='{""filePath"":""test.cs""}'; .\tool-call.ps1
```

## 🚀 Benefits

1. **Zero-effort memory**: Context loads automatically
2. **Pattern detection**: Architectural decisions tracked
3. **Session continuity**: Never lose work progress
4. **Team knowledge**: Shared memories in version control

---
Generated by COA CodeSearch MCP Memory System
";

        await File.WriteAllTextAsync(Path.Combine(_hooksDirectory, "README.md"), readme);
    }
}