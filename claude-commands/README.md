# Claude Code Custom Commands for COA CodeSearch MCP

These slash commands help you quickly save and recall important context using the memory system.

## Available Commands

### `/save-decision`
Save an architectural decision with reasoning and context.
- Use after making important design choices
- Captures the "why" behind decisions
- Helps future developers understand the codebase

### `/save-pattern` 
Save a code pattern for consistent implementation.
- Use when establishing coding patterns
- Documents how to implement similar features
- Ensures consistency across the team

### `/save-progress`
Save a summary of the current work session.
- Use at the end of a session or major milestone
- Captures what was done and what's next
- Maintains continuity between sessions

### `/recall-context`
Load relevant memories and context.
- Use at the start of a new session
- Retrieves previous decisions, patterns, and work
- Shows any unfinished tasks

## Usage

In Claude Code, simply type the slash command (e.g., `/save-decision`) and Claude will handle the rest.

## Benefits

- 📝 **Persistent Knowledge**: Important decisions and patterns survive between sessions
- 🔄 **Better Continuity**: Easy handoff between sessions or team members  
- 🎯 **Consistent Implementation**: Documented patterns lead to consistent code
- 🧠 **Reduced Cognitive Load**: Don't need to remember everything - just recall it

## Integration

These commands work with the COA CodeSearch MCP Server's memory system, storing information in:
- Project-level memories (checked into git)
- Local session memories (personal workspace)

The memories are searchable and automatically loaded when relevant.