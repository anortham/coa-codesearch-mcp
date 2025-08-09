---
allowed-tools: ["mcp__projectknowledge__save_checkpoint", "mcp__projectknowledge__find_knowledge", "mcp__projectknowledge__store_knowledge"]
description: "Create a checkpoint of current work session with structured format"
---

Create a checkpoint with the following structured information:

$ARGUMENTS

## Checkpoint Format

The checkpoint content MUST follow this exact structure:

```markdown
## Accomplished
- [Specific completed task 1]
- [Specific completed task 2]
- [Be concrete about what was done]

## Current State
[Describe where things stand right now]
[Include any partial progress or work in flight]

## Next Steps
1. [Concrete next action to take]
2. [Another specific task]
3. [Clear actionable items]

## Blockers (if any)
- [Any issues encountered]
- [Problems that need resolution]

## Files Modified
- path/to/file1.ext (what changed)
- path/to/file2.ext (what changed)
```

## Steps:
1. First, search for recent checkpoints to determine the session:
   - Use find_knowledge with query "type:Checkpoint" and maxResults: 1
   - If found, extract sessionId from metadata for continuity
   - If not found, create new session: "{project}-{yyyy-MM-dd}"

2. Create the checkpoint:
   - Use save_checkpoint with the formatted content
   - Include the sessionId for session continuity
   - Include activeFiles array with modified file paths

3. Display results clearly:
   - Show: "✓ Checkpoint created: {ID}"
   - Show: "Session: {sessionId} | Sequence: #{sequenceNumber}"
   - Show: "Time: {timestamp}"

4. If there are blockers mentioned:
   - Also use store_knowledge with type "TechnicalDebt" to track them

5. End with: "📌 Use /resume to continue from this checkpoint"