#!/bin/bash
# Claude Memory System - Stop Hook (Unix/macOS)
# Tracks work units after each Claude response
# More granular than session tracking

# Debug logging
DEBUG_LOG="$(dirname "$0")/hook-debug.log"
TIMESTAMP=$(date '+%Y-%m-%d %H:%M:%S')
echo "" >> "$DEBUG_LOG"
echo "[$TIMESTAMP] stop.sh started" >> "$DEBUG_LOG"

# Track work in a daily log file
TODAY=$(date '+%Y-%m-%d')
WORK_LOG="$(dirname "$0")/work-log-$TODAY.txt"

# Get git status to understand what was changed
CHANGED_FILES=()
if [ -d .git ]; then
    mapfile -t CHANGED_FILES < <(git diff --name-only 2>/dev/null; git diff --cached --name-only 2>/dev/null | sort -u)
fi

if [ ${#CHANGED_FILES[@]} -gt 0 ]; then
    ENTRY="[$TIMESTAMP] Modified: ${CHANGED_FILES[*]}"
    echo "$ENTRY" >> "$WORK_LOG"
    echo "Logged work: $ENTRY" >> "$DEBUG_LOG"
    
    # If significant changes, suggest creating a memory
    if [ ${#CHANGED_FILES[@]} -gt 3 ]; then
        echo -e "\033[33m💭 Consider documenting this work session:\033[0m"
        echo -e "\033[90mmcp__codesearch__remember_session 'Description of changes'\033[0m"
    fi
fi

echo "[$TIMESTAMP] stop.sh completed" >> "$DEBUG_LOG"
exit 0