#!/bin/bash
# Claude Memory System - File Edit Hook (Unix/Linux/macOS)
# Detects patterns and architectural decisions in edited files
# Updated to work with Claude Code

# Check if we have file path and operation (may not be available in Claude Code)
if [ -n "$CLAUDE_FILE_PATH" ] && [ -n "$CLAUDE_FILE_OPERATION" ]; then
    if [[ "$CLAUDE_FILE_OPERATION" == "edit" ]] || [[ "$CLAUDE_FILE_OPERATION" == "create" ]]; then
        FILE_NAME=$(basename "$CLAUDE_FILE_PATH")
        
        # Only analyze code files
        if [[ "$CLAUDE_FILE_PATH" =~ \.(cs|ts|js|tsx|jsx|py|java|go|cpp|c|h|hpp)$ ]]; then
            # Detect architectural patterns
            if grep -qE 'class\s+\w+(Repository|Service|Controller)' "$CLAUDE_FILE_PATH" 2>/dev/null; then
                echo -e "\033[33mDetected architectural pattern in $FILE_NAME\033[0m"
                echo -e "\033[90mConsider documenting this pattern with: remember_pattern\033[0m"
            fi
            
            # Detect security implementations
            if grep -qE 'Authorize|Authentication|Encryption|HIPAA|Security|Crypto' "$CLAUDE_FILE_PATH" 2>/dev/null; then
                echo -e "\033[33mDetected security-related code in $FILE_NAME\033[0m"
                echo -e "\033[90mConsider documenting with: remember_security_rule\033[0m"
            fi
            
            # Detect test patterns
            if grep -qE '@Test|describe\(|it\(|test\(|\[Test\]|\[Fact\]' "$CLAUDE_FILE_PATH" 2>/dev/null; then
                echo -e "\033[33mDetected test code in $FILE_NAME\033[0m"
                echo -e "\033[90mGood practice: Adding tests for new functionality\033[0m"
            fi
        fi
    fi
else
    # If no file info available, check current git changes
    if [ -d .git ]; then
        CHANGED_FILES=$(git diff --name-only 2>/dev/null)
        if [ -n "$CHANGED_FILES" ]; then
            echo -e "\033[36mFiles changed in this session:\033[0m"
            echo "$CHANGED_FILES" | head -5
        fi
    fi
fi

exit 0