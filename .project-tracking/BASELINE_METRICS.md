# Baseline Metrics Collection

## Instructions for Capturing Baselines

**⚠️ CRITICAL**: Capture these metrics BEFORE starting any implementation!

### 1. Performance Baselines

#### Search Performance (Run 10 times, take average)
```bash
# Run these queries and record times
cd "C:\source\COA CodeSearch MCP"

# Query 1: Simple text search
mcp__codesearch__text_search --query "authentication" --workspacePath "C:\source\COA CodeSearch MCP"

# Query 2: Complex query with filters  
mcp__codesearch__search_memories --query "technical debt authentication" --types ["TechnicalDebt"]

# Query 3: Context loading
mcp__codesearch__recall_context --query "current project status"
```

**Record Results**:
- [ ] Simple search latency: _____ ms
- [ ] Complex search latency: _____ ms  
- [ ] Context loading time: _____ ms
- [ ] Memory usage during search: _____ MB

#### Index Size Metrics
```bash
# Check current index size
du -sh .codesearch/
```
- [ ] Total index size: _____ MB
- [ ] Memory count: _____ memories
- [ ] Size per memory: _____ KB

### 2. AI Agent Workflow Baselines

#### Context Loading Workflow (Simulate AI agent)
```
1. Start new session
2. Try to load context for current work
3. Count tool calls needed
4. Measure token usage
```

**Record Results**:
- [ ] Tool calls for context: _____
- [ ] Tokens used: _____
- [ ] Time to useful context: _____ seconds
- [ ] Success rate (0-10): _____

#### Memory Creation Quality
```
Test with these scenarios:
1. Create technical debt memory
2. Create architectural decision
3. Create code pattern
```

**Record Results**:
- [ ] Memories with all required fields: _____% 
- [ ] Memories with relationships: _____%
- [ ] Manual correction needed: _____%

### 3. Search Quality Baselines

#### Relevance Testing
Test these queries and rate relevance (1-10):

1. "authentication bug" → Rate top 5 results
2. "database performance" → Rate top 5 results  
3. "refactoring TODO" → Rate top 5 results

**Record Results**:
- [ ] Query 1 average relevance: _____/10
- [ ] Query 2 average relevance: _____/10
- [ ] Query 3 average relevance: _____/10
- [ ] False positives in top 5: _____%

#### Token Usage Analysis
For each search, measure:
- Query input tokens
- Response output tokens  
- Total session tokens

**Record Results**:
- [ ] Average tokens per search: _____
- [ ] Session token growth rate: _____
- [ ] Most verbose responses: _____ tokens

## Baseline Collection Checklist

### Before Starting Implementation
- [ ] **🔧 [LUCENE]** Run search performance tests
- [ ] **🔧 [LUCENE]** Measure index size and structure
- [ ] **🤖 [AI-UX]** Test AI agent workflows
- [ ] **🤖 [AI-UX]** Measure token usage patterns
- [ ] **👥 [BOTH]** Rate search result relevance
- [ ] **💻 [DEV]** Capture system resource usage

### Weekly Progress Measurements
- [ ] **Week 1**: Mid-Phase 1 check
- [ ] **Week 2**: Phase 1 completion
- [ ] **Week 3**: Mid-Phase 2 check
- [ ] **Week 5**: Phase 2 completion
- [ ] **Week 8**: Mid-Phase 3 check
- [ ] **Week 11**: Final completion

### Tools for Measurement

#### Performance Testing Script
```bash
#!/bin/bash
# Create performance_test.sh

echo "Running baseline performance tests..."

# Time search operations
time mcp__codesearch__text_search --query "authentication" --workspacePath "$PWD"
time mcp__codesearch__search_memories --query "technical debt"
time mcp__codesearch__recall_context --query "project status"

echo "Tests complete. Record results in BASELINE_METRICS.md"
```

#### Token Counter
```csharp
// Add to test project
public class TokenCounter
{
    public static int CountTokens(string text)
    {
        // Approximate GPT token counting
        return text.Split(' ', '\n', '\t').Length;
    }
}
```

## Expected Improvements (Targets)

Based on expert analysis, we expect:

### Phase 1 Targets
- [ ] Context loading: 5-10 calls → 1 call
- [ ] Token usage: 30% reduction
- [ ] Search quality: 20% improvement
- [ ] No performance regression

### Phase 2 Targets  
- [ ] Search latency: 50ms → 10ms
- [ ] Index size: 30% reduction
- [ ] Query relevance: 40% improvement
- [ ] Cache hit rate: 0% → 60%

### Phase 3 Targets
- [ ] Overall performance: 3-5x improvement
- [ ] AI success rate: 40% → 80%
- [ ] Memory quality: 60% → 90%
- [ ] Tool consolidation: 13+ → 1

---
**Baseline Collection Due**: Before starting Task 1.1
**Next Measurement**: End of Week 1