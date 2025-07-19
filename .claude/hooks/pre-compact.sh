#!/usr/bin/env bash
# Claude Memory System - Pre Compact Hook (Unix/Linux/macOS)
# Saves important context before compaction

echo "📦 Context compaction triggered ($CLAUDE_TRIGGER)"

# Extract key information from current session to preserve
compact_summary="Context compaction at $(date '+%Y-%m-%d %H:%M')
Trigger: $CLAUDE_TRIGGER"

if [ "$CLAUDE_TRIGGER" = "manual" ] && [ -n "$CLAUDE_CUSTOM_INSTRUCTIONS" ]; then
    compact_summary="${compact_summary}
Instructions: $CLAUDE_CUSTOM_INSTRUCTIONS"
fi

# Save any recent architectural decisions or important context
echo "💾 Preserving architectural decisions..."
coa-codesearch-mcp list_memories_by_type ArchitecturalDecision 2>/dev/null

# Store a memory about what we were working on
coa-codesearch-mcp remember_session "Pre-compact checkpoint: $compact_summary" 2>/dev/null

# Quick check for uncommitted changes that might be lost
if [ -d .git ]; then
    changes=$(git diff --name-only 2>/dev/null)
    if [ -n "$changes" ]; then
        echo "⚠️  Uncommitted changes in: $(echo $changes | tr '\n' ', ')"
        echo "   Consider saving important decisions before compaction"
    fi
fi

echo "✅ Context prepared for compaction"
exit 0