# Claude Memory System Hooks

This directory contains cross-platform hooks for the Claude Memory System.

## Hook Files

Each hook has both PowerShell (.ps1) and Bash (.sh) versions:
- `user-prompt-submit` - Initializes memory system at session start
- `pre-tool-use` - Checks for known failures before tool execution
- `post-tool-use` - Captures tool failures for learning
- `stop` - Tracks session duration
- `pre-compact` - Saves context before compaction

## Platform Configuration

The hooks are configured in `.claude/settings.local.json`. Claude Code should automatically select the appropriate script based on the platform.

### Windows
Uses PowerShell scripts (.ps1) with `-ExecutionPolicy Bypass`

### Linux/macOS
Uses Bash scripts (.sh) with execute permissions

## Manual Configuration

If automatic platform detection doesn't work, you can manually update `settings.local.json`:

**For Windows:**
```json
"command": "powershell -ExecutionPolicy Bypass -File \".claude/hooks/hook-name.ps1\""
```

**For Unix/Linux/macOS:**
```json
"command": "bash .claude/hooks/hook-name.sh"
```

## Hook Purposes

1. **UserPromptSubmit**: Loads architectural decisions and recent work context
2. **PreToolUse**: Prevents known tool failures, suggests alternatives
3. **PostToolUse**: Learns from failures to improve future suggestions
4. **Stop**: Tracks session time, suggests saving insights for long sessions
5. **PreCompact**: Preserves important context before compaction

## Performance

All hooks are optimized for speed:
- No git operations in Stop hook
- Fast pattern matching in PreToolUse
- Minimal file I/O
- Early exits when not needed