# Memory Re-indexing Guide

## Why Re-index?

We've removed MemoryAnalyzer (which did synonym expansion and stemming) and replaced it with StandardAnalyzer. Your existing memories were indexed with the old analyzer, so they need to be re-indexed for optimal search performance with the new analyzer.

## Process Overview

1. **Backup memories** to JSON (preserves all data)
2. **Exit Claude Code** 
3. **Delete old index directories**
4. **Start Claude Code** with new build
5. **Restore memories** from JSON (re-indexes with StandardAnalyzer)

## Step-by-Step Instructions

### Step 1: Backup Your Memories

In your current Claude Code session (BEFORE updating):

```bash
# This creates a timestamped JSON backup of all your memories
mcp__codesearch__backup_memories
```

This will create a file like:
- `.codesearch/backups/memories_backup_20250108_143022.json`

**Important**: Note the exact filename returned by the command.

### Step 2: Exit Claude Code

Close your Claude Code session completely.

### Step 3: Build and Install the New Code

```bash
cd "C:\source\COA CodeSearch MCP"
dotnet build -c Release
# Follow your normal installation process
```

### Step 4: Delete Old Memory Indexes

Delete these directories:
- `.codesearch/project-memory/`
- `.codesearch/local-memory/`

**Note**: Only delete these two directories. Do NOT delete:
- `.codesearch/backups/` (contains your backup)
- `.codesearch/index/` (workspace indexes)

### Step 5: Start Claude Code

Start a new Claude Code session with the updated MCP server.

### Step 6: Restore Your Memories

```bash
# This will automatically find and restore from the most recent backup
mcp__codesearch__restore_memories
```

The restore process will:
- Read memories from the JSON backup
- Re-index them using StandardAnalyzer
- Preserve all metadata, relationships, and custom fields

## What Changes?

### Before (MemoryAnalyzer)
- Query "auth" would match: authentication, authorization, login, jwt, oauth
- Query "running" would match: run, runs, ran

### After (StandardAnalyzer)  
- Query "auth" matches only: auth
- Query "running" matches only: running
- Use explicit queries: `auth*` or `auth OR authentication OR authorization`

## Verification

After restoration, verify your memories are accessible:

```bash
# Count memories
mcp__codesearch__memory_dashboard

# Search for a known memory
mcp__codesearch__search_memories --query "YOUR_SEARCH_TERM"
```

## Troubleshooting

**Q: What if I forget to backup before updating?**
A: Your memories are stored in the Lucene index. While you can't search them optimally, the data isn't lost. You could theoretically downgrade, backup, then upgrade again.

**Q: Can I selectively re-index?**
A: No, the backup/restore process handles all memories at once. This ensures consistency.

**Q: Will this affect my workspace indexes?**
A: No, only memory indexes need re-indexing. Workspace indexes (for code search) are unaffected.

## Alternative: Keep Using Old Indexes

If you choose not to re-index:
- Your existing memories remain searchable
- New memories will be indexed with StandardAnalyzer
- You'll have mixed analyzer behavior (not recommended)
- Some queries may not find older memories as expected

## Best Practice

Re-index immediately after upgrading to ensure consistent search behavior across all memories.