#!/usr/bin/env bash
# Claude Memory System - Stop Hook (Unix/Linux/macOS)
# Minimal tracking - optimized for speed

# Only track session time - no git, no file I/O unless needed
session_file="$(dirname "$0")/.session-start"

if [ -f "$session_file" ]; then
    session_start=$(cat "$session_file")
    current_time=$(date +%s)
    duration=$(( (current_time - session_start) / 60 ))
    
    # Only show message for long sessions
    if [ "$duration" -gt 45 ]; then
        echo "⏱️ Long session (${duration}m) - consider saving insights"
        date +%s > "$session_file"  # Reset
    fi
else
    date +%s > "$session_file"
fi

exit 0