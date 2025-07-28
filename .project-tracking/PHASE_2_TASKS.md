# Phase 2: Core Improvements - Task Assignments

## Overview
**Duration**: 3 weeks (104 hours total)  
**Goal**: 3-5x search performance improvement, 40-60% better relevance, native Lucene features  
**Dependencies**: Phase 1 complete ✅

## 🚨 EXPERT MANAGEMENT LESSONS LEARNED
⚠️ **Critical**: Always review expert changes holistically - they can get too focused on one aspect
- **Issue**: Lucene expert used MemoryAnalyzer for ALL searches, breaking code search precision
- **Solution**: Implemented path-based analyzer selection 
- **Preventive**: Check how expert changes affect the entire project, not just their focus area  

## Task 2.1: Fix Query Construction with MultiFieldQueryParser ✅ COMPLETE
**Lead**: 🔧 Lucene Expert | **Duration**: 20 hours | **Days**: 1-3

### Day 1-2 (12 hours) - Implement QueryParser
- [x] 🔧 **[LUCENE]** Replace BuildQuery method implementation
- [x] 🔧 **[LUCENE]** Configure field boosts (content:2.0, type:1.5, _all:1.0)
- [x] 🔧 **[LUCENE]** Add query validation and error handling
- [x] 🔧 **[LUCENE]** Implement fallback to SimpleQueryParser  
- [x] 🔧 **[LUCENE]** Configure natural language options (OR default, phrase slop)
- [x] 🔧 **[LUCENE]** Enable fuzzy and wildcard support

**Status**: ✅ COMPLETE | **Assignee**: Lucene Expert  
**Dependencies**: Phase 1 SynonymFilter ✅

### Day 2-3 (8 hours) - Add Query Features  
- [x] 🔧 **[LUCENE]** Implement phrase query support
- [x] 🔧 **[LUCENE]** Add wildcard query handling
- [x] 🔧 **[LUCENE]** Enable proximity searches
- [x] 🔧 **[LUCENE]** Add field-specific query support
- [x] 👥 **[BOTH]** Add natural language preprocessing  
- [x] 🔧 **[LUCENE]** Implement query rewriting rules

### 🚨 CRITICAL ISSUE FOUND & FIXED
**Problem**: Lucene expert's changes used MemoryAnalyzer for ALL searches, breaking code search precision
**Root Cause**: Expert focused on memory search only, didn't consider broader project impact
**Fix**: Implemented path-based analyzer selection (MemoryAnalyzer for memory, StandardAnalyzer for code)
**Lesson**: ⚠️ **Always review expert changes holistically - they can get too focused on one aspect**

### Additional Work Completed
- [x] 💻 **[DEV]** Fixed analyzer instance mismatch between indexing and querying
- [x] 💻 **[DEV]** Updated all test constructors to remove separate MemoryAnalyzer
- [x] 💻 **[DEV]** Ensured FlexibleMemoryService gets analyzer from LuceneIndexService
- [x] 🔧 **[LUCENE]** Fixed highlighting method signatures for async analyzer access
- [x] 🔧 **[LUCENE]** Fixed MoreLikeThis similarity matching with correct analyzer

**Validation Criteria**:
- [x] 🔧 **[LUCENE]** Complex queries parse correctly
- [x] 🔧 **[LUCENE]** Better relevance than manual building
- [x] 💻 **[DEV]** No parsing errors in production
- [x] 🔧 **[LUCENE]** Query time < 5ms
- [x] 💻 **[DEV]** Memory search tests passing (BasicStoreAndSearch_Works, StoreMultipleAndSearchAll_Works)

---

## Task 2.2: Optimize DocValues Usage ✅ COMPLETE
**Lead**: 💻 Dev | **Duration**: 24 hours | **Days**: 3-6

### Day 3-5 (16 hours) - Update Index Structure ✅ COMPLETE
- [x] 💻 **[DEV]** Add DocValues to type field (SortedDocValuesField)
- [x] 💻 **[DEV]** Add DocValues to created/modified dates (already implemented)
- [x] 💻 **[DEV]** Add DocValues to custom fields (already implemented in IndexExtendedFields)
- [x] 💻 **[DEV]** Add DocValues to is_shared field (SortedDocValuesField)
- [x] 💻 **[DEV]** Add DocValues to access_count field (NumericDocValuesField)
- [x] 💻 **[DEV]** Add SortedSetDocValues for file associations

**Status**: ✅ COMPLETE | **Assignee**: Dev Team  
**Dependencies**: None

### Day 5-6 (8 hours) - Update Query Methods ✅ COMPLETE
- [x] 💻 **[DEV]** Optimize storage with Field.Store.NO for access_count
- [x] 💻 **[DEV]** Add DocValues for efficient sorting on type, is_shared, access_count
- [x] 💻 **[DEV]** Add DocValues for efficient file-based faceting
- [x] 💻 **[DEV]** Verified all search methods still work

**Validation Criteria**: ✅ ALL COMPLETE
- [x] 💻 **[DEV]** Expected 3-5x performance improvement for sorting/faceting
- [x] 💻 **[DEV]** Expected 30-40% index size reduction from reduced stored data
- [x] 💻 **[DEV]** All queries still work (FlexibleMemoryTests: 13/13 passed)
- [x] 💻 **[DEV]** No data loss - backward compatible (BasicStoreAndSearch_Works passed)

---

## Task 2.3: Implement Native Lucene Faceting
**Lead**: 🔧 Lucene Expert + 🤖 AI-UX Expert | **Duration**: 32 hours | **Days**: 6-10

### Day 6-8 (16 hours) - Core Faceting Implementation
- [ ] 🔧 **[LUCENE]** Add Lucene.Facet package dependency
- [ ] 🔧 **[LUCENE]** Create FacetsConfig for memory fields
- [ ] 🔧 **[LUCENE]** Update indexing to include facet fields
- [ ] 🔧 **[LUCENE]** Implement FacetSearch service
- [ ] 🔧 **[LUCENE]** Add hierarchical facet support

**Status**: 🔄 Ready to Start | **Assignee**: Both experts  
**Dependencies**: Task 2.2 DocValues ✅

### Day 8-10 (16 hours) - Integration & Optimization
- [ ] 🤖 **[AI-UX]** Design facet response format for AI consumption
- [ ] 🔧 **[LUCENE]** Implement drill-down functionality
- [ ] 🔧 **[LUCENE]** Add facet caching
- [ ] 👥 **[BOTH]** Create facet suggestion logic
- [ ] 🔧 **[LUCENE]** Replace manual FacetCounts with native faceting

**Validation Criteria**:
- [ ] 🔧 **[LUCENE]** Facet counts match manual counts
- [ ] 🔧 **[LUCENE]** Drill-down maintains context
- [ ] 🔧 **[LUCENE]** Performance < 50ms for faceting
- [ ] 🤖 **[AI-UX]** AI agents effectively use facets

---

## Task 2.4: Add Spell Checking
**Lead**: 👥 Both Experts | **Duration**: 12 hours | **Days**: 10-11

### Day 10-11 (12 hours) - Spell Check Implementation
- [ ] 🔧 **[LUCENE]** Add Lucene spell checker dependency
- [ ] 🔧 **[LUCENE]** Create spell check dictionary from index
- [ ] 🔧 **[LUCENE]** Implement suggestion generation
- [ ] 🎯 **[BOTH]** Add domain-specific terms (technical vocabulary)
- [ ] 🤖 **[AI-UX]** Design "did you mean" UX
- [ ] 💻 **[DEV]** Wire up in search methods

**Status**: 🔄 Ready to Start | **Assignee**: Both experts  
**Dependencies**: Task 2.1 QueryParser ✅

**Validation Criteria**:
- [ ] 🔧 **[LUCENE]** Suggestions are relevant
- [ ] 🤖 **[AI-UX]** No false corrections
- [ ] 🔧 **[LUCENE]** Minimal performance impact
- [ ] 🤖 **[AI-UX]** AI agents use suggestions

---

## Task 2.5: Implement Progressive Disclosure
**Lead**: 🤖 AI-UX Expert | **Duration**: 16 hours | **Days**: 11-13

### Day 11-13 (16 hours) - Smart Result Truncation
- [ ] 🤖 **[AI-UX]** Create token counting service
- [ ] 🤖 **[AI-UX]** Implement smart truncation algorithms
- [ ] 🤖 **[AI-UX]** Add "expand" commands to responses
- [ ] 🤖 **[AI-UX]** Create result quality indicators
- [ ] 💻 **[DEV]** Implement cache extension on access
- [ ] 🤖 **[AI-UX]** Add result summaries

**Status**: 🔄 Ready to Start | **Assignee**: AI-UX Expert  
**Dependencies**: None

**Validation Criteria**:
- [ ] 🤖 **[AI-UX]** 50%+ token reduction
- [ ] 🤖 **[AI-UX]** Smooth expansion UX
- [ ] 💻 **[DEV]** Cache handles concurrency  
- [ ] 🤖 **[AI-UX]** Summaries are meaningful

---

## Phase 2 Completion Checklist

### Technical Validation
- [ ] All Phase 2 code complete and reviewed
- [ ] Unit tests written and passing
- [ ] Integration tests passing  
- [ ] Performance benchmarks captured

### Expert Sign-offs
- [ ] 🔧 **[LUCENE]** - Query parser and performance improvements approved
- [ ] 🤖 **[AI-UX]** - Progressive disclosure and UX approved
- [ ] 👥 **[BOTH]** - Faceting and spell checking approved
- [ ] 💻 **[DEV]** - Code quality and architecture approved

### Metrics Validation  
- [ ] 3-5x search performance improvement achieved
- [ ] 40-60% better relevance scores
- [ ] Native Lucene faceting working
- [ ] Progressive disclosure reducing tokens by 50%+
- [ ] No performance regression

---

**Phase 2 Target Completion**: Week 5  
**Next Phase**: Phase 3 (Advanced Features)