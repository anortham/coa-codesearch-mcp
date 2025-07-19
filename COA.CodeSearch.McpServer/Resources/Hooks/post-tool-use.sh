#!/usr/bin/env bash
# Claude Memory System - Post Tool Use Hook (Unix/Linux/macOS)
# Learns from tool failures to prevent repeated mistakes

# Parse results to check for failures
result=$(echo "$CLAUDE_TOOL_RESULT" | jq -r 2>/dev/null)
if [ -z "$result" ]; then exit 0; fi

# Check if the tool failed
failed=false
error_message=""

# Check for error field
if [ "$(echo "$result" | jq -r '.error' 2>/dev/null)" != "null" ]; then
    failed=true
    error_message=$(echo "$result" | jq -r '.error')
elif [ "$CLAUDE_TOOL_NAME" = "Bash" ]; then
    # Check output for common failure patterns
    output=$(echo "$result" | jq -r '.output' 2>/dev/null)
    
    # Common failure patterns across platforms
    if echo "$output" | grep -qE "not recognized as an internal or external command|command not found|No such file or directory|Permission denied|is not recognized as a cmdlet|The term .* is not recognized"; then
        failed=true
        error_message="$output"
    fi
fi

if [ "$failed" = true ]; then
    # Extract the key parts of the failure
    params=$(echo "$CLAUDE_TOOL_PARAMS" | jq -r 2>/dev/null)
    
    # Create a failure signature
    failure_key=""
    if [ "$CLAUDE_TOOL_NAME" = "Bash" ] && [ -n "$params" ]; then
        # Extract the base command
        command=$(echo "$params" | jq -r '.command' 2>/dev/null)
        base_command=$(echo "$command" | awk '{print $1}')
        failure_key="bash:$base_command"
    else
        failure_key="${CLAUDE_TOOL_NAME}:${error_message}"
    fi
    
    # Store this failure pattern in memory
    coa-codesearch-mcp remember_pattern \
        "Tool failure: $failure_key" \
        "Cross-platform compatibility" \
        "Error: $error_message. Consider platform-specific alternatives or MCP tools." \
        "tool-failure,compatibility,$(echo $CLAUDE_TOOL_NAME | tr '[:upper:]' '[:lower:]')" 2>/dev/null
    
    # Quick suggestions for common failures
    case "$failure_key" in
        "bash:kill")
            echo "💡 Cross-platform tip: Use 'kill' (Unix) or 'taskkill /PID' (Windows)"
            ;;
        "bash:grep")
            echo "💡 Cross-platform tip: Use the Grep MCP tool for cross-platform search"
            ;;
        "bash:find")
            echo "💡 Cross-platform tip: Use fast_file_search or Glob MCP tools"
            ;;
        "bash:cat")
            echo "💡 Cross-platform tip: Use the Read MCP tool"
            ;;
        "bash:ls")
            echo "💡 Cross-platform tip: Use the LS MCP tool"
            ;;
    esac
fi

exit 0