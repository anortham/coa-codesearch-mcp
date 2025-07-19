#!/bin/bash
# Claude Memory System - Pre Tool Use Hook (Unix/macOS)
# Loads relevant context before MCP tool execution
# Only runs when MCP tools are actually being used

TOOL_NAME="${CLAUDE_TOOL_NAME:-}"
TOOL_PARAMS="${CLAUDE_TOOL_PARAMS:-}"

# Debug logging
DEBUG_LOG="$(dirname "$0")/hook-debug.log"
TIMESTAMP=$(date '+%Y-%m-%d %H:%M:%S')
echo "" >> "$DEBUG_LOG"
echo "[$TIMESTAMP] pre-tool-use.sh started" >> "$DEBUG_LOG"
echo "Tool: $TOOL_NAME" >> "$DEBUG_LOG"

# Only process for MCP codesearch tools
if [[ ! "$TOOL_NAME" =~ ^mcp__codesearch__ ]]; then
    echo "Not a codesearch tool, skipping" >> "$DEBUG_LOG"
    exit 0
fi

# Parse tool parameters to find file paths or search queries
FILE_PATH=""
QUERY=""

if [ -n "$TOOL_PARAMS" ]; then
    # Try to extract filePath and query using jq if available
    if command -v jq &> /dev/null; then
        FILE_PATH=$(echo "$TOOL_PARAMS" | jq -r '.filePath // empty' 2>/dev/null)
        QUERY=$(echo "$TOOL_PARAMS" | jq -r '.query // .searchPattern // .pattern // empty' 2>/dev/null)
        echo "Extracted - File: $FILE_PATH, Query: $QUERY" >> "$DEBUG_LOG"
    else
        echo "jq not available, cannot parse tool params" >> "$DEBUG_LOG"
    fi
fi

# Tools that might benefit from context loading
CONTEXT_TOOLS=(
    "mcp__codesearch__find_references"
    "mcp__codesearch__rename_symbol"
    "mcp__codesearch__dependency_analysis"
    "mcp__codesearch__get_implementations"
    "mcp__codesearch__fast_text_search"
    "mcp__codesearch__search_symbols"
)

# Check if current tool is in context tools list
for tool in "${CONTEXT_TOOLS[@]}"; do
    if [ "$TOOL_NAME" = "$tool" ]; then
        # Build context query based on tool and parameters
        CONTEXT_QUERY=""
        
        if [ -n "$FILE_PATH" ]; then
            # Extract file name without extension for context search
            FILENAME=$(basename "$FILE_PATH" | sed 's/\.[^.]*$//')
            CONTEXT_QUERY="$FILENAME"
        elif [ -n "$QUERY" ]; then
            CONTEXT_QUERY="$QUERY"
        fi
        
        if [ -n "$CONTEXT_QUERY" ]; then
            echo -e "\033[36m💡 Loading relevant memories for: $CONTEXT_QUERY\033[0m"
            echo -e "\033[90mConsider using: mcp__codesearch__recall_context '$CONTEXT_QUERY'\033[0m"
            echo "Suggested context query: $CONTEXT_QUERY" >> "$DEBUG_LOG"
        fi
        break
    fi
done

# For memory backup/restore operations, provide helpful reminders
if [ "$TOOL_NAME" = "mcp__codesearch__backup_memories_to_sqlite" ]; then
    echo -e "\033[32m📦 Backing up memories to SQLite...\033[0m"
    echo -e "\033[90mTip: The backup file (memories.db) can be checked into source control\033[0m"
elif [ "$TOOL_NAME" = "mcp__codesearch__restore_memories_from_sqlite" ]; then
    echo -e "\033[32m📥 Restoring memories from SQLite backup...\033[0m"
    echo -e "\033[90mTip: This is useful when setting up on a new machine\033[0m"
fi

echo "[$TIMESTAMP] pre-tool-use.sh completed" >> "$DEBUG_LOG"
exit 0