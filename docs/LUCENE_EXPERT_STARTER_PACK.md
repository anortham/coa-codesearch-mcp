# Lucene Expert Starter Pack

## Quick Start

### 1. Environment Setup
```bash
# Clone and build
git clone [repository-url]
cd "COA CodeSearch MCP"
dotnet build -c Debug

# Run tests to verify setup
dotnet test
```

### 2. Key Files to Review

#### Core Implementation
- **FlexibleMemoryService.cs** (Services/) - Main memory service with Lucene integration
  - Line 580: `BuildQuery()` method - Query construction logic
  - Line 46: `_analyzer = new StandardAnalyzer(LUCENE_VERSION)`
  - Search implementation in `SearchIndexAsync()`

#### Query Building Logic
- **BuildQuery() method** (FlexibleMemoryService.cs:601+)
  - BooleanQuery construction
  - Field boosting (Content: 2.0)
  - Natural language parsing
  - Type and facet filtering

#### Test Coverage
- **FlexibleMemoryQueryTests.cs** - Comprehensive query building tests
- Shows expected behavior for all query types

### 3. Current Lucene Configuration

```csharp
// Version
LuceneVersion.LUCENE_48 (4.8.0-beta00017)

// Analyzer
new StandardAnalyzer(LUCENE_VERSION)

// Index Fields
- Id (StringField, Store.YES)
- Type (StringField, Store.YES)  
- Content (TextField, Store.YES)
- Hash (StringField, Store.YES)
- Scope (StringField, Store.YES)
- Created/Modified (NumericDocValuesField)
- CustomFields (TextField, Store.YES) - JSON blob
```

### 4. Query Examples

#### Simple Text Query
```csharp
// User: "authentication bug"
// Becomes: BooleanQuery with MUST clauses
```

#### Complex Query with Filters
```csharp
// User: Query="refactor", Types=["TechnicalDebt"], DateRange="last-7-days"
// Becomes: BooleanQuery with text + type filter + numeric range
```

### 5. Performance Baselines

- Index size: ~10MB base + 1-5KB per memory
- Search latency: 10-100ms
- Memory count: Tested with 10,000+ memories
- Concurrent operations: SearcherManager handles refresh

### 6. Custom Implementations to Review

#### QueryExpansionService
- Manual synonym expansion
- Domain-specific terms
- Question: Native Lucene alternative?

#### Relationship Queries
- Custom graph traversal in code
- Stored as JSON in CustomFields
- Question: Graph query support?

#### Confidence Scoring
- Post-processing score distribution
- Dynamic result limiting
- Question: Custom scoring functions?

### 7. Running Local Tests

```bash
# Run memory-specific tests
dotnet test --filter "FullyQualifiedName~FlexibleMemory"

# Create test memories
# Use the MCP tools in Claude Code to create/search memories
```

### 8. Key Questions We Need Answered

1. **Query Construction**
   - Are we using optimal query types?
   - Should we use different analyzers per field?
   - Better boosting strategies?

2. **Native Features**
   - Synonym support vs QueryExpansionService?
   - MoreLikeThis for similar memories?
   - Faceting vs custom filtering?
   - Highlighting support?

3. **Performance**
   - Index configuration optimization?
   - Caching strategies?
   - Segment management?

4. **Advanced Features**
   - Term vectors for similarity?
   - Spell checking/suggestions?
   - Custom scoring for relevance?

### 9. How to Document Findings

Create `docs/LUCENE_EXPERT_FINDINGS.md` with:
1. Executive summary
2. Analysis per area (query, features, performance)
3. Code examples (before/after)
4. Priority recommendations
5. Migration complexity

### 10. Contact for Questions

- Review the [LUCENE_EXPERT_BRIEF.md](LUCENE_EXPERT_BRIEF.md) for full context
- Check [MEMORY_ARCHITECTURE.md](MEMORY_ARCHITECTURE.md) for system design
- Current implementation uses confidence-based limiting from search tools

## Next Steps

1. Review FlexibleMemoryService.cs focusing on BuildQuery()
2. Run tests to understand current behavior
3. Identify Lucene features we're not using
4. Document findings with code examples
5. Prioritize recommendations by impact/effort