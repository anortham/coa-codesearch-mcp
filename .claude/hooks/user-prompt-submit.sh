#!/usr/bin/env bash
# Claude Memory System - Session Start Hook (Unix/Linux/macOS)
# Automatically loads context when a new conversation starts

echo "🧠 Initializing Claude Memory System..."

# Initialize memory hooks for this session
coa-codesearch-mcp init_memory_hooks 2>/dev/null

# Load recent work sessions
echo "📚 Loading recent work context..."
coa-codesearch-mcp list_memories_by_type WorkSession 2>/dev/null

# Load architectural decisions for the project
echo "🏗️ Loading architectural decisions..."
coa-codesearch-mcp list_memories_by_type ArchitecturalDecision 2>/dev/null

echo "✅ Memory system initialized"
exit 0