# Lucene vs FTS5: Quality Analysis for text_search

**Critical Question**: Can we replace Lucene with SQLite FTS5 without degrading search quality?

## What We Built in Lucene (Quality Features)

### 1. CodeAnalyzer.cs (~1,000 lines of custom code)

**CamelCase Splitting:**
```
Query: "User"
Matches: UserService, IUserRepository, getCurrentUser()
```
- Tokenizes `UserService` → `User`, `Service`
- Essential for finding symbols by partial name

**Code Pattern Preservation:**
```
Preserves: ": ITool", "std::cout", "->method", "[Fact]", "List<T>"
Without this: Broken into meaningless tokens
```
- Custom regex: `@"(::\w+|:\s*\w+(<[^>]+>)?|->...)`
- Handles: C++ namespaces, type annotations, generics, attributes, decorators

**Multi-Field Strategies:**
- `content` - Standard code tokenization
- `content_symbols` - Symbol-only (always CamelCase split)
- `content_patterns` - Pattern-preserving (whitespace-only splits)

**Custom Filters:**
- `CamelCaseFilter` - Splits camelCase and snake_case
- `CodeLengthFilter` - Removes very short tokens (but keeps operators)
- `LowerCaseFilter` - Optional case normalization

### 2. QueryPreprocessor.cs (Smart Query Building)

**Multiple Search Modes:**
- `wildcard` - `User*Service` with proper escaping
- `fuzzy` - `Usre~` (handles typos)
- `phrase` - `"exact phrase match"`
- `regex` - `async.*Task` patterns
- `literal/code` - Special char handling
- `standard` - CamelCase + fuzzy

**Code Operator Preservation:**
```
Allowed: =>, ??, ?., ::, ->, +=, ==, !=, >=, <=, &&, ||, <<, >>
```
- Escapes Lucene special chars but preserves code operators
- Handles generics without breaking: `IRepository<T>`

### 3. MultiFactorScoreQuery.cs (Relevance Scoring)

**Custom scoring factors** (not just text frequency):
- File name matches (higher weight)
- Symbol proximity
- Code structure awareness

---

## What FTS5 Provides (Out of Box)

### Native Features:
- ✅ Full-text search (good for literals)
- ✅ `snippet()` highlighting
- ✅ `offsets()` positions
- ✅ Boolean operators (AND, OR, NOT)
- ✅ Phrase queries `"exact match"`
- ✅ Prefix matching `User*`

### What FTS5 Does NOT Have:
- ❌ **CamelCase splitting** - Won't find "User" in "UserService"
- ❌ **Code pattern preservation** - Breaks on special chars
- ❌ **Fuzzy matching** - No typo tolerance
- ❌ **Custom tokenization** - Natural language tokenizer only
- ❌ **Multi-field strategies** - One tokenizer for everything
- ❌ **Custom scoring** - TF-IDF only, no code-aware ranking

---

## The Degradation Risk

### Queries That Would Break:

**1. Partial Symbol Search:**
```
Lucene (✅): "User" → finds UserService, IUserRepository
FTS5 (❌):   "User" → only exact "User" token
```

**2. Code Pattern Search:**
```
Lucene (✅): ": ITool" → preserved as single token
FTS5 (❌):   ": ITool" → broken into ":", "ITool" (might not match)
```

**3. Fuzzy Search:**
```
Lucene (✅): "Usre~" → finds "User" (typo tolerance)
FTS5 (❌):   No fuzzy operator
```

**4. Generic Type Search:**
```
Lucene (✅): "IRepository<T>" → handled correctly
FTS5 (❌):   Special chars might break tokenization
```

**5. CamelCase Queries:**
```
Lucene (✅): "getCurrentUser" → finds get_current_user, GetCurrentUser
FTS5 (❌):   Exact match only
```

---

## Potential Hybrid Approaches

### Option 1: Pre-tokenize for FTS5 (Complex)
- Run CodeAnalyzer on content BEFORE inserting into FTS5
- Store both original and tokenized versions
- **Problem**: FTS5 still can't do fuzzy, can't score by symbol proximity

### Option 2: FTS5 for Literals, Lucene for Intelligence (Hybrid)
- FTS5: Literal patterns, special chars, exact phrases
- Lucene: CamelCase, fuzzy, code-aware searches
- **Benefit**: Use strengths of each
- **Cost**: Maintain both indexes

### Option 3: FTS5 with Custom Tokenizer (Requires C Extension)
- Build custom FTS5 tokenizer in C that mimics CodeAnalyzer
- **Problem**: Extremely complex, cross-platform nightmare
- **Benefit**: Single index

### Option 4: Keep Lucene, Just Use FTS5 for Positions
- Use FTS5 `offsets()` for clean line positions
- Keep Lucene for actual search quality
- Delete LineAwareSearchService hacks, keep search quality
- **Benefit**: Best of both - clean positions, quality search
- **Cost**: Maintain both indexes (but we already do!)

---

## Recommendation: Don't Touch text_search Quality Yet

**Why:**
1. **text_search is our most-used tool** - users depend on it
2. **~1,000 lines of custom code provides real value**:
   - CamelCase splitting is essential for code search
   - Code pattern preservation is unique
   - Fuzzy search helps with typos
3. **FTS5 can't replace Lucene quality** without massive work

**What We SHOULD Do:**

### Phase 1: Use FTS5 for Position Cleanup Only
- Keep Lucene for search
- Use FTS5 `offsets()` for line positions in SmartSnippetService
- Delete LineAwareSearchService (324 lines of JSON hacks)
- **Impact**: Clean code, same search quality

### Phase 2: Target Low-Complexity Tools First
- ✅ GoToDefinitionTool → SQLite symbols (exact lookup, no fuzzy needed)
- ✅ GetSymbolsOverviewTool → SQLite symbols (no search, just fetch)
- ✅ RecentFilesTool → SQLite query (no full-text needed)

### Phase 3: Evaluate Lucene Usage After Migration
Once other tools migrate, measure:
- What % of text_search queries use CamelCase?
- What % need fuzzy?
- What % need code patterns?
- Is Lucene worth maintaining for JUST text_search?

---

## The Real Question

**Is maintaining Lucene worth it for ONE tool?**

After Phase 2 migration, if Lucene is ONLY used by text_search:
- Lucene provides: CamelCase, fuzzy, code patterns, custom scoring
- Cost: Index maintenance, complexity, ~1,000 lines of analyzer code

**Trade-off Analysis:**
- **If 80%+ of searches use these features** → Keep Lucene
- **If <20% use them** → Consider simplifying to FTS5 + lose features
- **Middle ground** → Keep both, use FTS5 for positions only

---

## Action Plan

1. ✅ Create this analysis doc
2. ✅ Add FTS5 position support to SmartSnippetService (delete LineAwareSearchService)
3. ✅ Migrate simple tools (GoToDefinition, GetSymbolsOverview, RecentFiles)
4. ⏸️ **DO NOT** touch text_search quality until we have data
5. 📊 Add telemetry: Track which search modes are actually used
6. 🔬 After 2-4 weeks: Evaluate if Lucene quality is worth the cost

---

## Conclusion

**Don't rush to delete Lucene just because we can.**

Our custom CodeAnalyzer provides real value that FTS5 can't replicate without massive effort. The smart move:
- Use FTS5 for what it's good at (positions, literals)
- Keep Lucene for what IT's good at (CamelCase, fuzzy, code patterns)
- Delete the hacky position code (~600 lines)
- Keep the quality search code (~1,000 lines)

We can revisit after migration data shows actual usage patterns.
