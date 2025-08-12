# CodeSearch & ProjectKnowledge Integration Guide

## Overview

This document describes how the simplified CodeSearch and ProjectKnowledge MCP servers work together to provide a complete development intelligence system.

## Architecture

```
┌──────────────────────────────────────────────────────────┐
│                     Claude Code                           │
│                   (MCP Client)                           │
└────────────┬──────────────────────┬──────────────────────┘
             │ STDIO                │ STDIO
             ▼                      ▼
┌──────────────────────┐  ┌──────────────────────┐
│   CodeSearch MCP     │  │  ProjectKnowledge    │
│                      │  │       MCP            │
│  • Text search       │  │  • Knowledge storage │
│  • File search       │  │  • Checkpoints      │
│  • Code analysis     │  │  • Checklists       │
│  • Pattern detection │  │  • Technical debt   │
└──────────────────────┘  └───────────┬──────────┘
                                      │
                                      │ HTTP API
                                      │ Port 5100
                                      ▼
                          ┌──────────────────────┐
                          │  Other MCP Servers   │
                          │  (SQL, Web, etc.)    │
                          └──────────────────────┘
```

## Division of Responsibilities

### CodeSearch MCP (Search & Analysis)
- **Text search** with CodeAnalyzer
- **File/directory search** with fuzzy matching
- **Code pattern detection**
- **Recent files** tracking
- **Similar files** detection
- **Batch search operations**
- **Index management**

### ProjectKnowledge MCP (Memory & Intelligence)
- **Knowledge storage** (5 types)
- **Session checkpoints**
- **Task checklists**
- **Technical debt** tracking
- **Project insights** and decisions
- **Work notes** and observations
- **Knowledge federation** from other tools

## Integration Patterns

### Pattern 1: Search → Document Finding

When users find issues during search, they can document them:

```bash
# 1. User searches for anti-pattern
mcp__codesearch__text_search --query "Thread.Sleep" --workspacePath "C:/MyProject"

# 2. CodeSearch returns results
Found 5 instances of Thread.Sleep in:
- Services/PollingService.cs (line 45)
- Workers/BackgroundWorker.cs (line 123)
...

# 3. User documents the finding
mcp__projectknowledge__store_knowledge \
  --type "TechnicalDebt" \
  --content "Found Thread.Sleep anti-pattern in 5 files" \
  --priority "medium" \
  --tags ["performance", "async"]
```

### Pattern 2: Knowledge-Informed Search

Use knowledge to guide search:

```bash
# 1. Recall previous findings
mcp__projectknowledge__search_knowledge --query "performance issues"

# Returns: "Database connection pool exhaustion in OrderService"

# 2. Search for related code
mcp__codesearch__text_search \
  --query "OrderService connection" \
  --workspacePath "C:/MyProject"

# 3. Find and fix the issue
```

### Pattern 3: Checkpoint Before Major Search

Save state before complex search operations:

```bash
# 1. Create checkpoint
mcp__projectknowledge__store_checkpoint \
  --content "Starting refactoring search for IRepository pattern" \
  --sessionId "refactor-2024-01"

# 2. Perform complex searches  
mcp__codesearch__batch_operations \
  --workspacePath "C:/MyProject" \
  --operations '[
    {"operation": "text_search", "query": "IRepository"},
    {"operation": "file_search", "pattern": "*Repository.cs"}
  ]'

# 3. Document findings
mcp__projectknowledge__create_checklist \
  --title "Repository Refactoring Tasks" \
  --items ["Update UserRepository", "Update OrderRepository", ...]
```

## Workflow Examples

### Workflow 1: Bug Investigation

```mermaid
graph LR
    A[Search for Error] --> B[Find Code Location]
    B --> C[Analyze Pattern]
    C --> D[Document as TechnicalDebt]
    D --> E[Create Fix Checklist]
```

**Implementation:**

```python
# Step 1: Search for error
results = codesearch.text_search(
    query="NullReferenceException",
    workspace="C:/MyProject"
)

# Step 2: Analyze pattern
for result in results:
    if "UserService" in result.file_path:
        # Step 3: Document finding
        knowledge_id = projectknowledge.store_knowledge(
            type="TechnicalDebt",
            content=f"Null reference in {result.file_path}:{result.line}",
            metadata={
                "file": result.file_path,
                "line": result.line,
                "severity": "high"
            }
        )

# Step 4: Create fix checklist
checklist = projectknowledge.create_checklist(
    title="Fix NullReference Issues",
    items=[
        f"Add null check in {r.file_path}" 
        for r in results
    ]
)
```

### Workflow 2: Code Review Session

```python
# Start review session
checkpoint = projectknowledge.store_checkpoint(
    content="Code review for PR #123",
    session_id="review-123"
)

# Search for common issues
issues = []

# Check for hardcoded strings
hardcoded = codesearch.text_search(
    query='"http://localhost"',
    workspace="C:/MyProject"
)
if hardcoded:
    issues.append("Hardcoded URLs found")

# Check for TODO comments
todos = codesearch.text_search(
    query="TODO|FIXME|HACK",
    search_type="regex"
)
if todos:
    issues.append(f"Found {len(todos)} TODO comments")

# Document review findings
for issue in issues:
    projectknowledge.store_knowledge(
        type="TechnicalDebt",
        content=issue,
        metadata={"pr": "123", "reviewer": "ai"}
    )
```

### Workflow 3: Architecture Documentation

```python
# Search for architectural patterns
interfaces = codesearch.file_search(
    query="I*.cs",
    search_type="wildcard"
)

repositories = codesearch.text_search(
    query=": IRepository",
    search_type="literal"
)

services = codesearch.file_search(
    query="*Service.cs"
)

# Document architecture
projectknowledge.store_knowledge(
    type="ProjectInsight",
    content="Architecture follows Repository pattern",
    metadata={
        "interfaces_count": len(interfaces),
        "repositories_count": len(repositories),
        "services_count": len(services),
        "pattern": "repository-service"
    }
)
```

## Claude Code Configuration

### Recommended MCP Setup

```json
{
  "mcpServers": {
    "codesearch": {
      "command": "dotnet",
      "args": ["C:/tools/codesearch/COA.CodeSearch.McpServer.dll", "stdio"],
      "env": {
        "CODESEARCH_INDEX_PATH": "C:/indexes"
      }
    },
    "projectknowledge": {
      "command": "dotnet",
      "args": ["C:/tools/knowledge/COA.ProjectKnowledge.McpServer.dll", "stdio"],
      "env": {
        "PROJECTKNOWLEDGE_HTTP_PORT": "5100",
        "PROJECTKNOWLEDGE_DB_PATH": "C:/source/.coa/knowledge/workspace.db"
      }
    }
  }
}
```

## Tool Usage Guidelines

### When to Use CodeSearch

Use CodeSearch for:
- Finding code patterns
- Locating files by name
- Searching file contents
- Analyzing code structure
- Finding similar files
- Recent file tracking

**Examples:**
```bash
mcp__codesearch__text_search --query "async Task"
mcp__codesearch__file_search --query "User*.cs"
mcp__codesearch__recent_files --timeFrame "24h"
```

### When to Use ProjectKnowledge

Use ProjectKnowledge for:
- Storing insights and decisions
- Creating task lists
- Tracking technical debt
- Session checkpoints
- Knowledge from analysis
- Cross-project intelligence

**Examples:**
```bash
mcp__projectknowledge__store_knowledge --type "ProjectInsight"
mcp__projectknowledge__create_checklist --title "Refactoring tasks"
mcp__projectknowledge__store_checkpoint --content "Session state"
```

## Integration API

### CodeSearch → ProjectKnowledge

CodeSearch can optionally call ProjectKnowledge to store findings:

```csharp
// In CodeSearch tool
public class SearchAndDocumentTool
{
    private readonly HttpClient _httpClient;
    
    public async Task<object> ExecuteAsync(SearchAndDocumentParams parameters)
    {
        // Perform search
        var results = await SearchAsync(parameters.Query);
        
        // Optionally document
        if (parameters.DocumentFindings && results.Any())
        {
            await _httpClient.PostAsJsonAsync(
                "http://localhost:5100/api/knowledge/store",
                new
                {
                    type = "TechnicalDebt",
                    content = $"Found {results.Count} instances of '{parameters.Query}'",
                    source = "codesearch",
                    metadata = new
                    {
                        query = parameters.Query,
                        files = results.Select(r => r.FilePath).Distinct()
                    }
                }
            );
        }
        
        return results;
    }
}
```

### ProjectKnowledge → CodeSearch

ProjectKnowledge can trigger searches based on knowledge:

```csharp
// In ProjectKnowledge tool
public class InvestigateKnowledgeTool
{
    public async Task<object> ExecuteAsync(InvestigateParams parameters)
    {
        // Get knowledge entry
        var knowledge = await GetKnowledgeAsync(parameters.KnowledgeId);
        
        // Extract search terms
        var searchTerms = ExtractSearchTerms(knowledge.Content);
        
        // Trigger CodeSearch (via MCP client)
        var searchResults = await mcpClient.CallToolAsync(
            "codesearch",
            "text_search",
            new { query = searchTerms, workspace = knowledge.Workspace }
        );
        
        return new
        {
            Knowledge = knowledge,
            SearchResults = searchResults
        };
    }
}
```

## Best Practices

### 1. Use Both Tools Together

```bash
# Bad: Using only one tool
mcp__codesearch__text_search --query "bug"

# Good: Search and document
mcp__codesearch__text_search --query "NullReferenceException"
mcp__projectknowledge__store_knowledge --type "TechnicalDebt" --content "Found null ref in UserService"
```

### 2. Create Checkpoints for Complex Operations

```bash
# Before major search/refactor
mcp__projectknowledge__store_checkpoint --content "Starting security audit"

# Perform searches
mcp__codesearch__batch_operations --workspacePath "C:/MyProject" --operations '[...]'

# Document findings
mcp__projectknowledge__create_checklist --title "Security fixes"
```

### 3. Use Knowledge to Guide Search

```bash
# Check what you know
mcp__projectknowledge__search_knowledge --query "performance"

# Search for specific issues
mcp__codesearch__text_search --query "Thread.Sleep"
```

### 4. Document Patterns, Not Just Issues

```bash
# Document good patterns too
mcp__projectknowledge__store_knowledge \
  --type "ProjectInsight" \
  --content "Repository pattern well implemented in UserService"
```

## Performance Considerations

### Parallel Operations

Both servers can run operations in parallel:

```python
import asyncio

async def analyze_codebase():
    # Run in parallel
    search_task = codesearch.text_search("TODO")
    knowledge_task = projectknowledge.search_knowledge("technical debt")
    
    search_results, knowledge_results = await asyncio.gather(
        search_task,
        knowledge_task
    )
    
    return combine_results(search_results, knowledge_results)
```

### Caching

- CodeSearch caches search results for 10 minutes
- ProjectKnowledge caches frequently accessed knowledge
- Both use workspace-level caching

## Troubleshooting

### Issue: Can't connect between servers

**Solution:**
```bash
# Check ProjectKnowledge HTTP is running
curl http://localhost:5100/api/knowledge/health

# Check both MCP servers are registered
mcp list
```

### Issue: Duplicate knowledge entries

**Solution:**
```python
# Search before storing
existing = projectknowledge.search_knowledge(content_hash)
if not existing:
    projectknowledge.store_knowledge(...)
```

### Issue: Search not finding recent changes

**Solution:**
```bash
# Re-index workspace
mcp__codesearch__index_workspace --workspacePath "C:/MyProject" --forceRebuild true
```

## Future Enhancements

1. **Automatic Documentation** - CodeSearch automatically documents patterns
2. **Smart Search** - ProjectKnowledge suggests searches based on knowledge
3. **Integrated Workflows** - Single commands that use both servers
4. **Shared Context** - Both servers share workspace context
5. **Unified Reporting** - Combined reports from both servers

## Conclusion

CodeSearch and ProjectKnowledge work together to provide:
- **Complete intelligence** - Search finds, Knowledge remembers
- **Workflow support** - From discovery to documentation
- **Team collaboration** - Shared knowledge across projects
- **AI optimization** - Both servers designed for AI agents

Use CodeSearch for finding, use ProjectKnowledge for remembering.