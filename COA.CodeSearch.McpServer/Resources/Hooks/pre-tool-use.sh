#!/usr/bin/env bash
# Claude Memory System - Pre Tool Use Hook (Unix/Linux/macOS)
# Checks for known failures and suggests alternatives

# Parse parameters
params=$(echo "$CLAUDE_TOOL_PARAMS" | jq -r 2>/dev/null)

# For Bash commands, check for known failure patterns
if [ "$CLAUDE_TOOL_NAME" = "Bash" ] && [ -n "$params" ]; then
    command=$(echo "$params" | jq -r '.command' 2>/dev/null)
    base_command=$(echo "$command" | awk '{print $1}')
    failure_key="bash:$base_command"
    
    # Search memory for this failure pattern
    memories=$(coa-codesearch-mcp recall_context "Tool failure: $failure_key" 2>/dev/null | jq -r 2>/dev/null)
    
    if [ -n "$memories" ]; then
        echo "⚠️ Previous failure detected with '$base_command'"
    fi
    
    # Platform detection for common issues
    if [ "$(uname -s)" = "Darwin" ] || [ "$(uname -s)" = "Linux" ]; then
        # Unix-specific tips for Windows commands
        windows_commands=("taskkill" "dir" "cls" "type")
        if [[ " ${windows_commands[@]} " =~ " $base_command " ]]; then
            echo "🔄 Cross-platform tip: Consider using Unix equivalents or MCP tools"
        fi
    fi
fi

# Load context for navigation tools
if [[ "$CLAUDE_TOOL_NAME" =~ find_references|go_to_definition|rename_symbol ]]; then
    if [ -n "$params" ]; then
        file_path=$(echo "$params" | jq -r '.filePath' 2>/dev/null)
        if [ -n "$file_path" ]; then
            file_name=$(basename "$file_path")
            coa-codesearch-mcp recall_context "$file_name" 2>/dev/null
        fi
    fi
fi

# Suggest faster alternatives for search operations
case "$CLAUDE_TOOL_NAME" in
    "Grep")
        echo "⚡ Performance tip: Consider fast_text_search for indexed search (requires index_workspace)"
        ;;
    "Task")
        echo "⚡ Performance tip: For specific file/text searches, use fast_text_search or fast_file_search"
        ;;
esac

exit 0