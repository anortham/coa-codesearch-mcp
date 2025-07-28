# Memory System Optimization Implementation Guide

## Table of Contents
1. [Executive Summary](#executive-summary)
2. [Prerequisites](#prerequisites)
3. [Phase 1: Quick Wins (Weeks 1-2)](#phase-1-quick-wins-weeks-1-2)
4. [Phase 2: Core Improvements (Weeks 3-5)](#phase-2-core-improvements-weeks-3-5)
5. [Phase 3: Advanced Features (Weeks 6-11)](#phase-3-advanced-features-weeks-6-11)
6. [Testing Strategy](#testing-strategy)
7. [Rollout Plan](#rollout-plan)
8. [Success Metrics](#success-metrics)

## Executive Summary

This guide provides a step-by-step implementation plan for optimizing the COA CodeSearch MCP memory system based on expert findings. The plan combines Lucene technical improvements with AI usability enhancements to achieve:

- **40-60% better search relevance**
- **3-5x performance improvement**
- **50-70% token reduction**
- **40% → 80%+ AI success rate**
- **30% less custom code**

Total effort: 324 hours across 8-11 weeks.

## Prerequisites

### Technical Requirements
- [ ] .NET 9.0 SDK installed
- [ ] Lucene.NET 4.8.0-beta00017 packages
- [ ] Access to COA CodeSearch MCP repository
- [ ] Development environment with Claude Code

### Knowledge Requirements
- [ ] Understanding of Lucene.NET basics
- [ ] Familiarity with MCP tool development
- [ ] Understanding of AI agent workflows
- [ ] C# async/await patterns

### Setup Tasks
- [ ] Clone repository and build successfully
- [ ] Run existing test suite (all passing)
- [ ] Create feature branch: `feature/memory-optimization`
- [ ] Review expert findings documents

## Phase 1: Quick Wins (Weeks 1-2)

### 1.1 Replace QueryExpansionService with SynonymFilter (16 hours)

#### Background
Currently using custom QueryExpansionService that manually expands queries. Lucene's SynonymFilter provides this natively with better performance.

#### Implementation Checklist

**Day 1-2: Create Custom Analyzer (8 hours)**
- [ ] Create `MemoryAnalyzer.cs` in Services folder
- [ ] Implement synonym map builder
- [ ] Add domain-specific synonyms
- [ ] Configure per-field analysis

```csharp
// Services/MemoryAnalyzer.cs
public class MemoryAnalyzer : Analyzer
{
    private readonly SynonymMap _synonymMap;
    
    public MemoryAnalyzer()
    {
        var builder = new SynonymMap.Builder(true);
        
        // Add all synonyms from QueryExpansionService
        builder.Add(new CharsRef("auth"), new CharsRef("authentication"), true);
        builder.Add(new CharsRef("auth"), new CharsRef("authorization"), true);
        builder.Add(new CharsRef("bug"), new CharsRef("defect"), true);
        builder.Add(new CharsRef("bug"), new CharsRef("issue"), true);
        // ... add remaining synonyms
        
        _synonymMap = builder.Build();
    }
    
    protected override TokenStreamComponents CreateComponents(string fieldName, TextReader reader)
    {
        var tokenizer = new StandardTokenizer(LuceneVersion.LUCENE_48, reader);
        TokenStream stream = tokenizer;
        
        stream = new LowerCaseFilter(LuceneVersion.LUCENE_48, stream);
        stream = new StopFilter(LuceneVersion.LUCENE_48, stream, StopAnalyzer.ENGLISH_STOP_WORDS_SET);
        
        if (fieldName == "content" || fieldName == "_all")
        {
            stream = new SynonymFilter(stream, _synonymMap, true);
        }
        
        stream = new PorterStemFilter(stream);
        
        return new TokenStreamComponents(tokenizer, stream);
    }
}
```

**Day 2-3: Update FlexibleMemoryService (8 hours)**
- [ ] Replace StandardAnalyzer with MemoryAnalyzer
- [ ] Remove QueryExpansionService dependency
- [ ] Update BuildQuery to use new analyzer
- [ ] Update index writer configuration

```csharp
// In FlexibleMemoryService constructor
- _analyzer = new StandardAnalyzer(LUCENE_VERSION);
+ _analyzer = new MemoryAnalyzer();
```

**Testing Checklist**
- [ ] Create unit tests for MemoryAnalyzer
- [ ] Test synonym expansion works correctly
- [ ] Verify search results include synonym matches
- [ ] Performance benchmark before/after

**Validation Criteria**
- [ ] All existing tests pass
- [ ] Synonym queries return expected results
- [ ] No performance degradation
- [ ] QueryExpansionService fully removed

### 1.2 Implement Highlighting for Search Results (8 hours)

#### Background
Highlighting shows why results matched, improving both search quality understanding and token efficiency for AI agents.

#### Implementation Checklist

**Day 3-4: Add Highlighter Support (8 hours)**
- [ ] Add highlighting to FlexibleMemoryService
- [ ] Create highlight formatter
- [ ] Update search response model
- [ ] Configure fragment size

```csharp
// Services/MemoryHighlightService.cs
public class MemoryHighlightService
{
    private readonly Analyzer _analyzer;
    
    public Dictionary<string, string[]> GetHighlights(
        Query query, 
        TopDocs results, 
        IndexSearcher searcher)
    {
        var highlighter = new Highlighter(
            new SimpleHTMLFormatter("<mark>", "</mark>"),
            new QueryScorer(query)
        );
        
        highlighter.TextFragmenter = new SimpleSpanFragmenter(
            new QueryScorer(query), 
            100  // Fragment size
        );
        
        var highlights = new Dictionary<string, string[]>();
        
        foreach (var scoreDoc in results.ScoreDocs)
        {
            var doc = searcher.Doc(scoreDoc.Doc);
            var content = doc.Get("content");
            var id = doc.Get("id");
            
            var tokenStream = TokenSources.GetAnyTokenStream(
                searcher.IndexReader, 
                scoreDoc.Doc, 
                "content", 
                _analyzer
            );
            
            var fragments = highlighter.GetBestFragments(
                tokenStream, 
                content, 
                3  // Max fragments
            );
            
            highlights[id] = fragments;
        }
        
        return highlights;
    }
}
```

**Update Response Models**
- [ ] Add Highlights property to search results
- [ ] Update JSON serialization
- [ ] Add highlight options to request model

```csharp
public class EnhancedMemorySearchResult
{
    public List<FlexibleMemorySearchResult> Results { get; set; }
    public Dictionary<string, string[]> Highlights { get; set; }
    public int TotalHits { get; set; }
    public long SearchTimeMs { get; set; }
}
```

**Testing Checklist**
- [ ] Test highlighting with various queries
- [ ] Verify HTML formatting is correct
- [ ] Test fragment extraction logic
- [ ] Ensure no XSS vulnerabilities

**Validation Criteria**
- [ ] Highlights show relevant context
- [ ] Performance impact < 10ms
- [ ] AI agents can parse highlights
- [ ] Fragments are meaningful

### 1.3 Add Action-Oriented Response Format (12 hours)

#### Background
Current responses are verbose and don't guide AI agents on next steps. Need concise, action-oriented format.

#### Implementation Checklist

**Day 4-5: Design Response Format (4 hours)**
- [ ] Define dual-format response structure
- [ ] Create response builder service
- [ ] Add action suggestion logic
- [ ] Implement token counting

```csharp
// Models/AIOptimizedResponse.cs
public class AIOptimizedResponse
{
    // For AI parsing
    public AIResponseData Data { get; set; }
    
    // For user explanation
    public string Summary { get; set; }
    
    // Progressive disclosure
    public ProgressiveOptions More { get; set; }
}

public class AIResponseData
{
    public List<MemorySummary> Primary { get; set; }  // Top 3-5
    public List<AIAction> Actions { get; set; }
    public float Confidence { get; set; }
}

public class AIAction
{
    public string Id { get; set; }
    public string Description { get; set; }
    public string Command { get; set; }
    public int EstimatedTokens { get; set; }
}
```

**Day 5-6: Implement Response Builder (8 hours)**
- [ ] Create ResponseBuilderService
- [ ] Add action suggestion logic
- [ ] Implement token estimation
- [ ] Add summary generation

```csharp
// Services/AIResponseBuilderService.cs
public class AIResponseBuilderService
{
    public AIOptimizedResponse BuildResponse(
        FlexibleMemorySearchResult searchResult,
        FlexibleMemorySearchRequest request)
    {
        var response = new AIOptimizedResponse
        {
            Data = new AIResponseData
            {
                Primary = searchResult.Memories
                    .Take(5)
                    .Select(m => new MemorySummary
                    {
                        Id = m.Id,
                        Type = m.Type,
                        Summary = ExtractSummary(m.Content),
                        Relevance = CalculateRelevance(m, request)
                    })
                    .ToList(),
                    
                Actions = GenerateActions(searchResult, request),
                Confidence = CalculateConfidence(searchResult)
            },
            
            Summary = GenerateSummary(searchResult),
            
            More = searchResult.Memories.Count > 5 
                ? new ProgressiveOptions
                {
                    Token = CacheResults(searchResult),
                    EstimatedTokens = EstimateRemainingTokens(searchResult)
                }
                : null
        };
        
        return response;
    }
    
    private List<AIAction> GenerateActions(
        FlexibleMemorySearchResult result, 
        FlexibleMemorySearchRequest request)
    {
        var actions = new List<AIAction>();
        
        // Suggest exploring relationships
        if (result.Memories.Any(m => m.RelatedTo?.Any() == true))
        {
            actions.Add(new AIAction
            {
                Id = "explore_relationships",
                Description = "Explore related memories",
                Command = "memory_graph_navigator --startPoint='{primaryId}'",
                EstimatedTokens = 500
            });
        }
        
        // Suggest filtering by type if multiple types
        var types = result.Memories.Select(m => m.Type).Distinct().ToList();
        if (types.Count > 1)
        {
            actions.Add(new AIAction
            {
                Id = "filter_by_type",
                Description = $"Filter by specific type",
                Command = $"search_memories --query='{request.Query}' --types=['{types.First()}']",
                EstimatedTokens = 300
            });
        }
        
        return actions;
    }
}
```

**Testing Checklist**
- [ ] Test response generation with various result sets
- [ ] Verify action suggestions are relevant
- [ ] Test token estimation accuracy
- [ ] Ensure summaries are concise

**Validation Criteria**
- [ ] Response size reduced by 50%+
- [ ] Actions are contextually relevant
- [ ] AI agents successfully parse format
- [ ] Summary accurately represents results

### 1.4 Implement Basic Context Auto-Loading (20 hours)

#### Background
AI agents currently need 5-10 tool calls to restore context. Auto-loading based on directory and patterns will streamline this.

#### Implementation Checklist

**Day 6-8: Create Context Service (12 hours)**
- [ ] Create AIContextService
- [ ] Implement directory-based loading
- [ ] Add pattern recognition
- [ ] Create working set concept

```csharp
// Services/AIContextService.cs
public class AIContextService
{
    private readonly FlexibleMemoryService _memoryService;
    private readonly ILogger<AIContextService> _logger;
    
    public async Task<AIWorkingContext> LoadContextAsync(
        string workingDirectory,
        string sessionId = null)
    {
        var context = new AIWorkingContext
        {
            SessionId = sessionId ?? Guid.NewGuid().ToString(),
            WorkingDirectory = workingDirectory,
            LoadedAt = DateTime.UtcNow
        };
        
        // 1. Load memories for current directory
        var directoryMemories = await LoadDirectoryMemoriesAsync(workingDirectory);
        
        // 2. Load recent session memories
        var sessionMemories = sessionId != null 
            ? await LoadSessionMemoriesAsync(sessionId)
            : new List<FlexibleMemoryEntry>();
        
        // 3. Load related project memories
        var projectMemories = await LoadProjectMemoriesAsync(workingDirectory);
        
        // 4. Score and rank all memories
        var allMemories = directoryMemories
            .Concat(sessionMemories)
            .Concat(projectMemories)
            .Distinct(new MemoryIdComparer());
        
        var rankedMemories = RankMemoriesByRelevance(allMemories, workingDirectory);
        
        // 5. Build working context
        context.PrimaryMemories = rankedMemories.Take(5).ToList();
        context.SecondaryMemories = rankedMemories.Skip(5).Take(10).ToList();
        context.AvailableMemories = rankedMemories.Skip(15).Take(20).ToList();
        
        // 6. Generate suggestions
        context.SuggestedActions = GenerateContextActions(context);
        
        return context;
    }
    
    private async Task<List<FlexibleMemoryEntry>> LoadDirectoryMemoriesAsync(
        string directory)
    {
        // Find memories related to files in directory
        var files = Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories)
            .Where(f => !f.Contains("\\.codesearch\\"))
            .Take(50);  // Limit for performance
        
        var memories = new List<FlexibleMemoryEntry>();
        
        foreach (var file in files)
        {
            var fileMemories = await _memoryService.GetMemoriesForFileAsync(file);
            memories.AddRange(fileMemories);
        }
        
        return memories;
    }
    
    private List<FlexibleMemoryEntry> RankMemoriesByRelevance(
        IEnumerable<FlexibleMemoryEntry> memories,
        string workingDirectory)
    {
        return memories
            .Select(m => new
            {
                Memory = m,
                Score = CalculateRelevanceScore(m, workingDirectory)
            })
            .OrderByDescending(x => x.Score)
            .Select(x => x.Memory)
            .ToList();
    }
    
    private float CalculateRelevanceScore(
        FlexibleMemoryEntry memory, 
        string workingDirectory)
    {
        float score = 0;
        
        // Recency boost
        var age = DateTime.UtcNow - memory.Created;
        score += (float)(1.0 / (1.0 + age.TotalDays / 7.0));
        
        // File relevance
        if (memory.FilesInvolved?.Any(f => f.StartsWith(workingDirectory)) == true)
        {
            score += 2.0f;
        }
        
        // Type priority
        score += memory.Type switch
        {
            "ArchitecturalDecision" => 1.5f,
            "TechnicalDebt" => 1.2f,
            "WorkSession" => 0.8f,
            _ => 1.0f
        };
        
        // Access frequency
        var accessCount = memory.GetField<int>("accessCount");
        score += Math.Min(accessCount / 10.0f, 2.0f);
        
        return score;
    }
}
```

**Day 8-9: Create Auto-Loading Tool (8 hours)**
- [ ] Create new MCP tool for context loading
- [ ] Add caching for loaded contexts
- [ ] Implement incremental loading
- [ ] Add context refresh logic

```csharp
// Tools/LoadContextTool.cs
[Tool("load_context")]
public class LoadContextTool
{
    private readonly AIContextService _contextService;
    private readonly IMemoryCache _cache;
    
    public async Task<LoadContextResult> ExecuteAsync(
        LoadContextParams parameters,
        CancellationToken cancellationToken)
    {
        // Check cache first
        var cacheKey = $"context_{parameters.WorkingDirectory}_{parameters.SessionId}";
        if (_cache.TryGetValue<AIWorkingContext>(cacheKey, out var cached))
        {
            return new LoadContextResult
            {
                Context = cached,
                FromCache = true
            };
        }
        
        // Load fresh context
        var context = await _contextService.LoadContextAsync(
            parameters.WorkingDirectory ?? Directory.GetCurrentDirectory(),
            parameters.SessionId
        );
        
        // Cache for 5 minutes
        _cache.Set(cacheKey, context, TimeSpan.FromMinutes(5));
        
        return new LoadContextResult
        {
            Context = context,
            FromCache = false,
            Summary = $"Loaded {context.PrimaryMemories.Count} primary memories " +
                     $"and {context.SecondaryMemories.Count} secondary memories"
        };
    }
}
```

**Testing Checklist**
- [ ] Test context loading for various directories
- [ ] Verify memory ranking algorithm
- [ ] Test caching behavior
- [ ] Ensure performance < 500ms

**Validation Criteria**
- [ ] Context loads in single tool call
- [ ] Most relevant memories appear first
- [ ] Caching reduces repeated calls
- [ ] AI agents adopt the new workflow

## Phase 1 Completion Checklist

### Code Complete
- [ ] All Phase 1 tasks implemented
- [ ] Code reviewed by team
- [ ] Documentation updated
- [ ] Tests passing

### Integration Testing
- [ ] End-to-end search tests
- [ ] AI agent workflow tests
- [ ] Performance benchmarks
- [ ] Memory usage analysis

### Metrics Baseline
- [ ] Search relevance baseline recorded
- [ ] Token usage baseline recorded
- [ ] Performance metrics captured
- [ ] AI success rate measured

### Phase 1 Sign-off
- [ ] Product owner review
- [ ] Technical lead approval
- [ ] No critical bugs
- [ ] Ready for Phase 2

## Phase 2: Core Improvements (Weeks 3-5)

### 2.1 Fix Query Construction with MultiFieldQueryParser (20 hours)

#### Background
Current manual query building is error-prone and doesn't support advanced features. MultiFieldQueryParser provides proper query parsing with field boosting.

#### Implementation Checklist

**Day 10-11: Implement QueryParser (12 hours)**
- [ ] Replace BuildQuery method
- [ ] Configure field boosts
- [ ] Add query validation
- [ ] Implement fallback handling

```csharp
// Update FlexibleMemoryService.BuildQuery
private Query BuildQuery(FlexibleMemorySearchRequest request)
{
    if (string.IsNullOrWhiteSpace(request.Query) || request.Query == "*")
    {
        return new MatchAllDocsQuery();
    }
    
    // Configure parser with multiple fields and boosts
    var fields = new[] { "content", "type", "_all" };
    var boosts = new Dictionary<string, float>
    {
        ["content"] = 2.0f,
        ["type"] = 1.5f,
        ["_all"] = 1.0f
    };
    
    var parser = new MultiFieldQueryParser(
        LUCENE_VERSION,
        fields,
        _analyzer,
        boosts
    );
    
    // Configure for natural language
    parser.DefaultOperator = QueryParserBase.Operator.OR;
    parser.PhraseSlop = 2;  // Allow word reordering
    parser.FuzzyMinSim = 0.8f;
    parser.AllowLeadingWildcard = true;
    
    Query textQuery;
    try
    {
        textQuery = parser.Parse(request.Query);
    }
    catch (ParseException ex)
    {
        _logger.LogWarning(ex, "Failed to parse query: {Query}", request.Query);
        
        // Fallback to simple parser
        var simpleParser = new SimpleQueryParser(_analyzer, fields);
        textQuery = simpleParser.Parse(request.Query);
    }
    
    // Apply filters as FILTER clauses
    var filterQuery = new BooleanQuery.Builder();
    filterQuery.Add(textQuery, Occur.MUST);
    
    // Type filter
    if (request.Types?.Any() == true)
    {
        var typeQuery = new BooleanQuery.Builder();
        foreach (var type in request.Types)
        {
            typeQuery.Add(new TermQuery(new Term("type", type)), Occur.SHOULD);
        }
        filterQuery.Add(typeQuery.Build(), Occur.FILTER);
    }
    
    // Date range filter
    if (request.DateRange != null)
    {
        var dateQuery = NumericRangeQuery.NewInt64Range(
            "created",
            request.DateRange.From?.Ticks,
            request.DateRange.To?.Ticks,
            true, true
        );
        filterQuery.Add(new ConstantScoreQuery(dateQuery), Occur.FILTER);
    }
    
    // Facet filters
    if (request.Facets?.Any() == true)
    {
        foreach (var facet in request.Facets)
        {
            var facetQuery = new TermQuery(new Term($"facet_{facet.Key}", facet.Value));
            filterQuery.Add(new ConstantScoreQuery(facetQuery), Occur.FILTER);
        }
    }
    
    return filterQuery.Build();
}
```

**Day 11-12: Add Query Features (8 hours)**
- [ ] Implement phrase query support
- [ ] Add wildcard query handling
- [ ] Enable proximity searches
- [ ] Add field-specific queries

```csharp
// Add query preprocessing
private string PreprocessQuery(string query)
{
    // Handle special syntax
    var processed = query;
    
    // Convert natural language to Lucene syntax
    if (processed.Contains(" near "))
    {
        // "auth near security" -> "auth security"~5
        processed = Regex.Replace(processed, @"(\w+)\s+near\s+(\w+)", "$1 $2~5");
    }
    
    // Handle field-specific searches
    if (processed.Contains(":"))
    {
        // Already has field syntax, leave as-is
        return processed;
    }
    
    // Add fuzzy matching for single terms
    if (!processed.Contains(" ") && !processed.Contains("~"))
    {
        processed += "~0.8";
    }
    
    return processed;
}
```

**Testing Checklist**
- [ ] Test various query types (phrase, wildcard, fuzzy)
- [ ] Verify field boosting works
- [ ] Test error handling and fallback
- [ ] Benchmark parsing performance

**Validation Criteria**
- [ ] Complex queries parse correctly
- [ ] Better relevance than manual building
- [ ] No parsing errors in production
- [ ] Query time < 5ms

### 2.2 Optimize DocValues Usage (24 hours)

#### Background
Currently storing fields twice (stored + DocValues). Proper DocValues usage will improve performance 3-5x for sorting and faceting.

#### Implementation Checklist

**Day 12-14: Refactor Field Storage (16 hours)**
- [ ] Audit current field usage
- [ ] Separate display vs operation fields
- [ ] Update document creation
- [ ] Migrate existing indexes

```csharp
// Update document creation in FlexibleMemoryService
private Document CreateOptimizedDocument(FlexibleMemoryEntry memory)
{
    var doc = new Document();
    
    // Store only fields needed for display
    doc.Add(new StringField("id", memory.Id, Field.Store.YES));
    doc.Add(new TextField("content", memory.Content, Field.Store.YES));
    doc.Add(new StringField("type", memory.Type, Field.Store.YES));
    
    // Add searchable content field (not stored)
    doc.Add(new TextField("_all", BuildSearchableContent(memory), Field.Store.NO));
    
    // Use DocValues for sorting/filtering (not stored)
    doc.Add(new NumericDocValuesField("created_dv", memory.Created.Ticks));
    doc.Add(new NumericDocValuesField("modified_dv", memory.Modified.Ticks));
    doc.Add(new SortedDocValuesField("type_dv", new BytesRef(memory.Type)));
    doc.Add(new NumericDocValuesField("access_count_dv", memory.AccessCount));
    
    // Store complex data as compressed JSON
    if (memory.Fields?.Any() == true)
    {
        var fieldsJson = JsonSerializer.Serialize(memory.Fields);
        doc.Add(new StoredField("fields_json", CompressionTools.CompressString(fieldsJson)));
        
        // Add DocValues for faceting
        foreach (var field in memory.Fields)
        {
            if (field.Value is string strValue)
            {
                doc.Add(new SortedDocValuesField($"facet_{field.Key}", new BytesRef(strValue)));
            }
        }
    }
    
    // Binary DocValues for relationships
    if (memory.Relationships?.Any() == true)
    {
        var relJson = JsonSerializer.SerializeToUtf8Bytes(memory.Relationships);
        doc.Add(new BinaryDocValuesField("relationships_dv", new BytesRef(relJson)));
    }
    
    return doc;
}
```

**Day 14-15: Update Search Operations (8 hours)**
- [ ] Modify sorting to use DocValues
- [ ] Update facet counting
- [ ] Optimize field retrieval
- [ ] Add DocValues warmup

```csharp
// Efficient sorting with DocValues
public async Task<FlexibleMemorySearchResult> SearchWithSortAsync(
    FlexibleMemorySearchRequest request)
{
    var query = BuildQuery(request);
    
    // Build sort using DocValues fields
    var sortFields = new List<SortField>();
    
    if (request.OrderBy != null)
    {
        sortFields.Add(request.OrderBy switch
        {
            "created" => new SortField("created_dv", SortFieldType.INT64, request.OrderDescending),
            "modified" => new SortField("modified_dv", SortFieldType.INT64, request.OrderDescending),
            "type" => new SortField("type_dv", SortFieldType.STRING, request.OrderDescending),
            "relevance" => SortField.FIELD_SCORE,
            _ => new SortField(request.OrderBy + "_dv", SortFieldType.STRING, request.OrderDescending)
        });
    }
    
    sortFields.Add(SortField.FIELD_SCORE);  // Secondary sort by relevance
    
    var sort = new Sort(sortFields.ToArray());
    var results = searcher.Search(query, null, request.MaxResults, sort, true, false);
    
    return ProcessResults(results, searcher);
}

// Efficient facet counting with DocValues
private Dictionary<string, Dictionary<string, int>> CountFacetsWithDocValues(
    IndexSearcher searcher,
    Query query,
    string[] facetFields)
{
    var facets = new Dictionary<string, Dictionary<string, int>>();
    
    var collector = TopScoreDocCollector.Create(1000, true);
    searcher.Search(query, collector);
    
    foreach (var facetField in facetFields)
    {
        var fieldCounts = new Dictionary<string, int>();
        var docValuesField = facetField + "_dv";
        
        foreach (var scoreDoc in collector.GetTopDocs().ScoreDocs)
        {
            var docValues = MultiDocValues.GetSortedValues(searcher.IndexReader, docValuesField);
            docValues.SetDocument(scoreDoc.Doc);
            
            var value = docValues.GetBytesRef().Utf8ToString();
            fieldCounts[value] = fieldCounts.GetValueOrDefault(value) + 1;
        }
        
        facets[facetField] = fieldCounts;
    }
    
    return facets;
}
```

**Testing Checklist**
- [ ] Benchmark before/after performance
- [ ] Verify sorting works correctly
- [ ] Test facet counting accuracy
- [ ] Check index size reduction

**Validation Criteria**
- [ ] 3-5x performance improvement
- [ ] 30-40% index size reduction
- [ ] All queries still work
- [ ] No data loss during migration

### 2.3 Implement Native Lucene Faceting (32 hours)

#### Background
Replace manual facet counting with Lucene.Net.Facet module for better performance and features.

#### Implementation Checklist

**Day 15-17: Setup Facet Infrastructure (16 hours)**
- [ ] Add Lucene.Net.Facet package
- [ ] Create taxonomy directory
- [ ] Update indexing for facets
- [ ] Implement FacetsConfig

```csharp
// Services/FacetedMemoryIndexer.cs
public class FacetedMemoryIndexer
{
    private readonly Directory _indexDir;
    private readonly Directory _taxoDir;
    private readonly FacetsConfig _facetsConfig;
    private readonly IndexWriter _indexWriter;
    private readonly DirectoryTaxonomyWriter _taxoWriter;
    
    public FacetedMemoryIndexer(Directory indexDir, Directory taxoDir)
    {
        _indexDir = indexDir;
        _taxoDir = taxoDir;
        
        _facetsConfig = new FacetsConfig();
        _facetsConfig.SetHierarchical("type", true);
        _facetsConfig.SetMultiValued("files", true);
        _facetsConfig.SetMultiValued("tags", true);
        
        var indexConfig = new IndexWriterConfig(LUCENE_VERSION, new MemoryAnalyzer());
        _indexWriter = new IndexWriter(_indexDir, indexConfig);
        _taxoWriter = new DirectoryTaxonomyWriter(_taxoDir);
    }
    
    public void IndexMemory(FlexibleMemoryEntry memory)
    {
        var doc = new Document();
        
        // Regular fields
        doc.Add(new StringField("id", memory.Id, Field.Store.YES));
        doc.Add(new TextField("content", memory.Content, Field.Store.YES));
        
        // Hierarchical facets
        doc.Add(new FacetField("type", memory.Type));
        doc.Add(new FacetField("type", memory.Type, GetSubType(memory)));
        
        // Multi-value facets
        foreach (var file in memory.FilesInvolved ?? Enumerable.Empty<string>())
        {
            doc.Add(new FacetField("files", Path.GetFileName(file)));
        }
        
        // Dynamic facets from fields
        if (memory.Fields != null)
        {
            if (memory.Fields.TryGetValue("status", out var status))
            {
                doc.Add(new FacetField("status", status.ToString()));
            }
            
            if (memory.Fields.TryGetValue("priority", out var priority))
            {
                doc.Add(new FacetField("priority", priority.ToString()));
            }
            
            if (memory.Fields.TryGetValue("tags", out var tags) && tags is string[] tagArray)
            {
                foreach (var tag in tagArray)
                {
                    doc.Add(new FacetField("tags", tag));
                }
            }
        }
        
        // Add drill-down support
        _indexWriter.AddDocument(_facetsConfig.Build(_taxoWriter, doc));
    }
}
```

**Day 17-19: Implement Faceted Search (16 hours)**
- [ ] Create faceted search method
- [ ] Add drill-down support
- [ ] Implement facet result formatting
- [ ] Add facet caching

```csharp
// Services/FacetedMemorySearchService.cs
public class FacetedMemorySearchService
{
    private readonly IndexSearcher _searcher;
    private readonly TaxonomyReader _taxoReader;
    private readonly FacetsConfig _facetsConfig;
    
    public async Task<FacetedSearchResult> SearchWithFacetsAsync(
        FlexibleMemorySearchRequest request)
    {
        // Build base query
        var query = BuildQuery(request);
        
        // Apply drill-down if specified
        DrillDownQuery drillDown = null;
        if (request.DrillDown?.Any() == true)
        {
            drillDown = new DrillDownQuery(_facetsConfig, query);
            foreach (var dd in request.DrillDown)
            {
                drillDown.Add(dd.Dimension, dd.Path);
            }
            query = drillDown;
        }
        
        // Collect facets
        var facetsCollector = new FacetsCollector();
        var topDocsCollector = TopScoreDocCollector.Create(request.MaxResults, true);
        
        _searcher.Search(query, MultiCollector.Wrap(facetsCollector, topDocsCollector));
        
        // Get facet results
        var facets = new FastTaxonomyFacetCounts(_taxoReader, _facetsConfig, facetsCollector);
        
        // Build response
        var result = new FacetedSearchResult
        {
            Hits = ProcessSearchResults(topDocsCollector.GetTopDocs(), _searcher),
            Facets = new Dictionary<string, FacetResult>()
        };
        
        // Get top facets for each dimension
        foreach (var dimension in new[] { "type", "status", "priority", "files", "tags" })
        {
            try
            {
                var facetResult = facets.GetTopChildren(10, dimension);
                if (facetResult != null)
                {
                    result.Facets[dimension] = facetResult;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get facets for {Dimension}", dimension);
            }
        }
        
        // Add drill sideways for better UX
        if (drillDown != null)
        {
            var drillSideways = new DrillSideways(_searcher, _facetsConfig, _taxoReader);
            var sidewaysResult = drillSideways.Search(drillDown, topDocsCollector);
            
            // Update facets with sideways counts
            foreach (var dimension in sidewaysResult.Facets.Keys)
            {
                result.Facets[dimension + "_all"] = sidewaysResult.Facets[dimension];
            }
        }
        
        return result;
    }
}
```

**Testing Checklist**
- [ ] Test facet counting accuracy
- [ ] Verify hierarchical facets work
- [ ] Test drill-down functionality
- [ ] Benchmark faceting performance

**Validation Criteria**
- [ ] Facet counts match manual counts
- [ ] Drill-down maintains context
- [ ] Performance < 50ms for faceting
- [ ] Multi-valued facets work correctly

### 2.4 Add Spell Checking (12 hours)

#### Background
Spell checking will help AI agents with typos and suggest corrections for better search results.

#### Implementation Checklist

**Day 19-20: Implement Spell Checker (12 hours)**
- [ ] Create spell index directory
- [ ] Build spell checker dictionary
- [ ] Add suggestion service
- [ ] Integrate with search

```csharp
// Services/MemorySpellCheckService.cs
public class MemorySpellCheckService
{
    private readonly SpellChecker _spellChecker;
    private readonly Directory _spellDir;
    private readonly Analyzer _analyzer;
    
    public MemorySpellCheckService(string spellIndexPath)
    {
        _spellDir = FSDirectory.Open(spellIndexPath);
        _spellChecker = new SpellChecker(_spellDir);
        _analyzer = new MemoryAnalyzer();
    }
    
    public async Task BuildSpellIndexAsync(IndexReader reader)
    {
        // Build dictionary from content field
        var contentDict = new LuceneDictionary(reader, "content");
        var indexConfig = new IndexWriterConfig(LUCENE_VERSION, _analyzer);
        
        await Task.Run(() => 
            _spellChecker.IndexDictionary(contentDict, indexConfig, false)
        );
        
        // Also add type field for domain terms
        var typeDict = new LuceneDictionary(reader, "type");
        await Task.Run(() => 
            _spellChecker.IndexDictionary(typeDict, indexConfig, false)
        );
        
        _logger.LogInformation("Spell index built successfully");
    }
    
    public string[] GetSuggestions(string query, int maxSuggestions = 5)
    {
        var suggestions = new List<string>();
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var term in terms)
        {
            // Skip short terms
            if (term.Length < 3) continue;
            
            // Get suggestions for term
            var termSuggestions = _spellChecker.SuggestSimilar(
                term, 
                maxSuggestions, 
                null,  // Use all fields
                null,  // No field restriction
                SuggestMode.SUGGEST_MORE_POPULAR
            );
            
            if (termSuggestions.Length > 0)
            {
                // Build corrected queries
                foreach (var suggestion in termSuggestions)
                {
                    var corrected = query.Replace(term, suggestion);
                    suggestions.Add(corrected);
                }
            }
        }
        
        // Also try full phrase suggestions
        if (terms.Length > 1)
        {
            var phraseSuggestions = GetPhraseSuggestions(query, maxSuggestions);
            suggestions.AddRange(phraseSuggestions);
        }
        
        return suggestions.Distinct().Take(maxSuggestions).ToArray();
    }
    
    private string[] GetPhraseSuggestions(string phrase, int max)
    {
        // Use n-gram based suggestion for phrases
        var suggestions = new List<string>();
        
        // Implementation of phrase-level corrections
        // This could use a phrase dictionary or ML model
        
        return suggestions.ToArray();
    }
    
    public async Task<SpellCheckResult> CheckAndSuggestAsync(
        string query,
        FlexibleMemorySearchResult searchResult)
    {
        var result = new SpellCheckResult { OriginalQuery = query };
        
        // Only suggest if few or no results
        if (searchResult.Memories.Count < 3)
        {
            result.Suggestions = GetSuggestions(query);
            
            // Try searching with first suggestion
            if (result.Suggestions.Length > 0)
            {
                var suggestedResults = await _memoryService.SearchMemoriesAsync(
                    new FlexibleMemorySearchRequest 
                    { 
                        Query = result.Suggestions[0] 
                    }
                );
                
                result.SuggestedResultCount = suggestedResults.Memories.Count;
                result.DidYouMean = result.Suggestions[0];
            }
        }
        
        return result;
    }
}
```

**Integration with Search**
- [ ] Add spell check to search pipeline
- [ ] Update response model
- [ ] Add auto-correction option
- [ ] Implement suggestion UI

```csharp
// Update search response
public class EnhancedMemorySearchResult : FlexibleMemorySearchResult
{
    public SpellCheckResult SpellCheck { get; set; }
    public bool AutoCorrected { get; set; }
}

// Update search method
public async Task<EnhancedMemorySearchResult> SearchWithSpellCheckAsync(
    FlexibleMemorySearchRequest request)
{
    var result = await base.SearchMemoriesAsync(request);
    var enhanced = new EnhancedMemorySearchResult
    {
        Memories = result.Memories,
        TotalCount = result.TotalCount
    };
    
    // Check spelling if few results
    if (result.Memories.Count < 3 && !string.IsNullOrEmpty(request.Query))
    {
        enhanced.SpellCheck = await _spellChecker.CheckAndSuggestAsync(
            request.Query, 
            result
        );
        
        // Auto-correct if requested and high confidence
        if (request.AutoCorrect && 
            enhanced.SpellCheck.Suggestions?.Length > 0 &&
            enhanced.SpellCheck.SuggestedResultCount > result.Memories.Count * 2)
        {
            var correctedRequest = request with 
            { 
                Query = enhanced.SpellCheck.Suggestions[0] 
            };
            
            var correctedResult = await base.SearchMemoriesAsync(correctedRequest);
            enhanced.Memories = correctedResult.Memories;
            enhanced.TotalCount = correctedResult.TotalCount;
            enhanced.AutoCorrected = true;
        }
    }
    
    return enhanced;
}
```

**Testing Checklist**
- [ ] Test suggestion accuracy
- [ ] Verify domain terms handled
- [ ] Test auto-correction logic
- [ ] Check performance impact

**Validation Criteria**
- [ ] Suggestions are relevant
- [ ] No false corrections
- [ ] Minimal performance impact
- [ ] AI agents use suggestions

### 2.5 Implement Progressive Disclosure (16 hours)

#### Background
Reduce token usage by showing summaries first with ability to drill down for details.

#### Implementation Checklist

**Day 20-22: Create Progressive Response System (16 hours)**
- [ ] Design progressive response format
- [ ] Implement result caching
- [ ] Add drill-down mechanism
- [ ] Create token estimation

```csharp
// Services/ProgressiveDisclosureService.cs
public class ProgressiveDisclosureService
{
    private readonly IMemoryCache _cache;
    private readonly ITokenCounter _tokenCounter;
    
    public ProgressiveSearchResponse CreateProgressiveResponse(
        FlexibleMemorySearchResult fullResult,
        int initialItems = 5,
        int tokenBudget = 2000)
    {
        var response = new ProgressiveSearchResponse
        {
            Summary = CreateSummary(fullResult),
            InitialResults = new List<ProgressiveMemoryItem>(),
            HasMore = fullResult.Memories.Count > initialItems,
            TotalAvailable = fullResult.Memories.Count
        };
        
        // Calculate tokens used so far
        var tokensUsed = _tokenCounter.Count(response.Summary);
        
        // Add initial results within token budget
        foreach (var memory in fullResult.Memories.Take(initialItems))
        {
            var item = new ProgressiveMemoryItem
            {
                Id = memory.Id,
                Type = memory.Type,
                Summary = ExtractSummary(memory.Content, 100), // 100 char summary
                Created = memory.Created,
                Relevance = memory.Score
            };
            
            var itemTokens = _tokenCounter.Count(item);
            if (tokensUsed + itemTokens > tokenBudget)
            {
                break;
            }
            
            response.InitialResults.Add(item);
            tokensUsed += itemTokens;
        }
        
        // Cache full results for expansion
        if (response.HasMore)
        {
            var cacheKey = Guid.NewGuid().ToString();
            _cache.Set(cacheKey, fullResult, TimeSpan.FromMinutes(10));
            
            response.ExpandOptions = new ExpandOptions
            {
                Token = cacheKey,
                NextBatch = new ExpandCommand
                {
                    Description = $"Get next {Math.Min(5, fullResult.Memories.Count - initialItems)} results",
                    Command = $"expand_results --token='{cacheKey}' --batch='next'",
                    EstimatedTokens = 1500
                },
                SpecificItem = new ExpandCommand
                {
                    Description = "Get full details for specific memory",
                    Command = $"expand_results --token='{cacheKey}' --id='{{id}}'",
                    EstimatedTokens = 500
                },
                AllResults = fullResult.Memories.Count <= 20 
                    ? new ExpandCommand
                    {
                        Description = $"Get all {fullResult.Memories.Count} results",
                        Command = $"expand_results --token='{cacheKey}' --batch='all'",
                        EstimatedTokens = fullResult.Memories.Count * 300
                    }
                    : null
            };
        }
        
        response.TokensUsed = tokensUsed;
        response.TokensSaved = CalculateTokensSaved(fullResult, response);
        
        return response;
    }
    
    public ExpandedResults ExpandResults(string token, ExpandRequest request)
    {
        if (!_cache.TryGetValue<FlexibleMemorySearchResult>(token, out var fullResult))
        {
            throw new InvalidOperationException("Expansion token expired or invalid");
        }
        
        var expanded = new ExpandedResults();
        
        switch (request.Batch)
        {
            case "next":
                var shown = request.AlreadyShown ?? 5;
                expanded.Results = fullResult.Memories
                    .Skip(shown)
                    .Take(5)
                    .Select(m => ConvertToFullMemory(m))
                    .ToList();
                break;
                
            case "all":
                expanded.Results = fullResult.Memories
                    .Select(m => ConvertToFullMemory(m))
                    .ToList();
                break;
                
            default:
                if (!string.IsNullOrEmpty(request.Id))
                {
                    var memory = fullResult.Memories.FirstOrDefault(m => m.Id == request.Id);
                    if (memory != null)
                    {
                        expanded.Results = new List<FlexibleMemoryEntry> { memory };
                        expanded.IncludeRelated = true;
                    }
                }
                break;
        }
        
        // Extend cache lifetime on access
        _cache.Set(token, fullResult, TimeSpan.FromMinutes(10));
        
        return expanded;
    }
    
    private string CreateSummary(FlexibleMemorySearchResult result)
    {
        var summary = new StringBuilder();
        
        summary.AppendLine($"Found {result.TotalCount} memories:");
        
        // Group by type
        var byType = result.Memories
            .GroupBy(m => m.Type)
            .OrderByDescending(g => g.Count());
        
        foreach (var group in byType.Take(3))
        {
            summary.AppendLine($"- {group.Count()} {group.Key}");
        }
        
        // Add relevance indicator
        if (result.Memories.Any())
        {
            var avgScore = result.Memories.Average(m => m.Score);
            var relevance = avgScore > 0.8 ? "High" : avgScore > 0.5 ? "Medium" : "Low";
            summary.AppendLine($"Relevance: {relevance}");
        }
        
        return summary.ToString();
    }
}
```

**Testing Checklist**
- [ ] Test token counting accuracy
- [ ] Verify cache expiration
- [ ] Test expansion commands
- [ ] Check summary generation

**Validation Criteria**
- [ ] 50%+ token reduction
- [ ] Smooth expansion UX
- [ ] Cache handles concurrency
- [ ] Summaries are meaningful

## Phase 2 Completion Checklist

### Code Complete
- [ ] All Phase 2 tasks implemented
- [ ] Code reviewed and refactored
- [ ] Documentation updated
- [ ] All tests passing

### Performance Testing
- [ ] Query parser benchmarks
- [ ] DocValues performance gains verified
- [ ] Faceting performance tested
- [ ] Overall system benchmarks

### Integration Testing
- [ ] AI agent workflows tested
- [ ] Backward compatibility verified
- [ ] Edge cases handled
- [ ] Load testing completed

### Phase 2 Sign-off
- [ ] Technical metrics improved as expected
- [ ] No regression in functionality
- [ ] Ready for Phase 3

## Phase 3: Advanced Features (Weeks 6-11)

### 3.1 Create Unified Memory Interface (40 hours)

#### Background
Replace 13+ memory tools with a single, intent-based interface that AI agents can use naturally.

#### Implementation Checklist

**Day 23-26: Design Unified Interface (16 hours)**
- [ ] Define intent schema
- [ ] Create command parser
- [ ] Map intents to operations
- [ ] Design response format

```csharp
// Models/UnifiedMemoryCommand.cs
public class UnifiedMemoryCommand
{
    public MemoryIntent Intent { get; set; }
    public string Content { get; set; }
    public CommandContext Context { get; set; }
    public Dictionary<string, object> Parameters { get; set; }
}

public enum MemoryIntent
{
    Save,      // Store new memory
    Find,      // Search memories
    Connect,   // Link memories
    Explore,   // Navigate relationships
    Suggest,   // Get recommendations
    Manage     // Update/delete/archive
}

public class CommandContext
{
    public float Confidence { get; set; }
    public string Scope { get; set; }  // project, session, local
    public string WorkingDirectory { get; set; }
    public string SessionId { get; set; }
    public List<string> RelatedFiles { get; set; }
}
```

**Day 26-28: Implement Command Processor (24 hours)**
- [ ] Create UnifiedMemoryService
- [ ] Implement intent detection
- [ ] Add parameter extraction
- [ ] Create operation router

```csharp
// Services/UnifiedMemoryService.cs
public class UnifiedMemoryService
{
    private readonly FlexibleMemoryService _memoryService;
    private readonly AIContextService _contextService;
    private readonly MemoryCreationAssistant _creationAssistant;
    private readonly ILogger<UnifiedMemoryService> _logger;
    
    public async Task<UnifiedMemoryResult> ExecuteAsync(
        UnifiedMemoryCommand command,
        CancellationToken cancellationToken)
    {
        // Detect intent if not specified
        if (command.Intent == MemoryIntent.Unknown)
        {
            command.Intent = DetectIntent(command);
        }
        
        // Route to appropriate handler
        return command.Intent switch
        {
            MemoryIntent.Save => await HandleSaveAsync(command, cancellationToken),
            MemoryIntent.Find => await HandleFindAsync(command, cancellationToken),
            MemoryIntent.Connect => await HandleConnectAsync(command, cancellationToken),
            MemoryIntent.Explore => await HandleExploreAsync(command, cancellationToken),
            MemoryIntent.Suggest => await HandleSuggestAsync(command, cancellationToken),
            MemoryIntent.Manage => await HandleManageAsync(command, cancellationToken),
            _ => throw new NotSupportedException($"Intent {command.Intent} not supported")
        };
    }
    
    private MemoryIntent DetectIntent(UnifiedMemoryCommand command)
    {
        var content = command.Content?.ToLowerInvariant() ?? "";
        
        // Simple keyword-based detection (could use ML model)
        if (ContainsAny(content, "save", "store", "remember", "create"))
            return MemoryIntent.Save;
            
        if (ContainsAny(content, "find", "search", "look for", "get"))
            return MemoryIntent.Find;
            
        if (ContainsAny(content, "connect", "link", "relate", "associate"))
            return MemoryIntent.Connect;
            
        if (ContainsAny(content, "explore", "graph", "relationships", "connected"))
            return MemoryIntent.Explore;
            
        if (ContainsAny(content, "suggest", "recommend", "help", "what should"))
            return MemoryIntent.Suggest;
            
        if (ContainsAny(content, "update", "delete", "archive", "manage"))
            return MemoryIntent.Manage;
            
        // Default to find if unclear
        return MemoryIntent.Find;
    }
    
    private async Task<UnifiedMemoryResult> HandleSaveAsync(
        UnifiedMemoryCommand command,
        CancellationToken cancellationToken)
    {
        // Use creation assistant for guidance
        var guidance = await _creationAssistant.SuggestMemoryCreationAsync(
            command.Content,
            command.Context
        );
        
        // Check for duplicates
        var similar = await FindSimilarMemoriesAsync(command.Content);
        if (similar.Any())
        {
            return new UnifiedMemoryResult
            {
                Success = false,
                Action = "duplicate_check",
                Message = "Found similar existing memories",
                Suggestions = similar.Select(m => new ActionSuggestion
                {
                    Description = $"Update existing: {m.GetSummary()}",
                    Command = $"memory update --id='{m.Id}' --content='{command.Content}'"
                }).ToList()
            };
        }
        
        // Determine if temporary or permanent
        var isTemporary = command.Context.Confidence < 0.8 || 
                         command.Context.Scope == "session";
        
        FlexibleMemoryEntry created;
        if (isTemporary)
        {
            created = await _memoryService.StoreTemporaryMemoryAsync(
                new StoreTemporaryMemoryParams
                {
                    Content = command.Content,
                    ExpiresIn = "4h",
                    SessionId = command.Context.SessionId,
                    Fields = guidance.PrefilledFields
                }
            );
        }
        else
        {
            created = await _memoryService.StoreMemoryAsync(
                new StoreMemoryParams
                {
                    MemoryType = guidance.RecommendedType,
                    Content = command.Content,
                    Files = command.Context.RelatedFiles,
                    Fields = guidance.PrefilledFields,
                    IsShared = command.Context.Scope == "project"
                }
            );
        }
        
        return new UnifiedMemoryResult
        {
            Success = true,
            Action = "saved",
            Memory = created,
            Message = $"Saved as {(isTemporary ? "temporary" : "permanent")} {guidance.RecommendedType}",
            NextSteps = new[]
            {
                new ActionSuggestion
                {
                    Description = "Find related memories",
                    Command = $"memory explore --from='{created.Id}'"
                }
            }
        };
    }
    
    private async Task<UnifiedMemoryResult> HandleFindAsync(
        UnifiedMemoryCommand command,
        CancellationToken cancellationToken)
    {
        // Enhanced search with all improvements
        var searchRequest = new FlexibleMemorySearchRequest
        {
            Query = command.Content,
            MaxResults = 10,
            EnableQueryExpansion = true,
            EnableHighlighting = true,
            EnableSpellCheck = true,
            AutoCorrect = true
        };
        
        // Add context-based filtering
        if (command.Context.RelatedFiles?.Any() == true)
        {
            searchRequest.Files = command.Context.RelatedFiles;
        }
        
        var result = await _memoryService.SearchMemoriesEnhancedAsync(searchRequest);
        
        // Format as unified result
        return new UnifiedMemoryResult
        {
            Success = true,
            Action = "found",
            Memories = result.Results.Select(r => r.Memory).ToList(),
            Highlights = result.Highlights,
            Message = $"Found {result.TotalHits} memories",
            Facets = result.Facets,
            SpellCheck = result.SpellSuggestions?.Any() == true
                ? new SpellCheckInfo
                {
                    DidYouMean = result.SpellSuggestions.First(),
                    AutoCorrected = result.AutoCorrected
                }
                : null,
            NextSteps = GenerateSearchNextSteps(result)
        };
    }
}
```

**Testing Checklist**
- [ ] Test intent detection accuracy
- [ ] Verify all operations work
- [ ] Test parameter extraction
- [ ] Check response consistency

**Validation Criteria**
- [ ] 90%+ intent detection accuracy
- [ ] All memory operations supported
- [ ] Consistent response format
- [ ] AI agents adopt interface

### 3.2 Implement Temporal Scoring (20 hours)

#### Background
Add time-decay scoring to make recent memories more relevant using Lucene's CustomScoreQuery.

#### Implementation Checklist

**Day 29-30: Create Temporal Scorer (12 hours)**
- [ ] Implement CustomScoreProvider
- [ ] Add decay functions
- [ ] Configure decay rates
- [ ] Integrate with search

```csharp
// Scoring/TemporalRelevanceQuery.cs
public class TemporalRelevanceQuery : CustomScoreQuery
{
    private readonly long _nowTicks;
    private readonly TemporalDecayFunction _decayFunction;
    
    public TemporalRelevanceQuery(
        Query subQuery, 
        TemporalDecayFunction decayFunction = null) 
        : base(subQuery)
    {
        _nowTicks = DateTime.UtcNow.Ticks;
        _decayFunction = decayFunction ?? TemporalDecayFunction.Default;
    }
    
    protected override CustomScoreProvider GetCustomScoreProvider(
        AtomicReaderContext context)
    {
        return new TemporalScoreProvider(context, _nowTicks, _decayFunction);
    }
}

public class TemporalScoreProvider : CustomScoreProvider
{
    private readonly NumericDocValues _createdDV;
    private readonly NumericDocValues _modifiedDV;
    private readonly NumericDocValues _accessCountDV;
    private readonly long _nowTicks;
    private readonly TemporalDecayFunction _decay;
    
    public TemporalScoreProvider(
        AtomicReaderContext context,
        long nowTicks,
        TemporalDecayFunction decay) 
        : base(context)
    {
        _createdDV = context.AtomicReader.GetNumericDocValues("created_dv");
        _modifiedDV = context.AtomicReader.GetNumericDocValues("modified_dv");
        _accessCountDV = context.AtomicReader.GetNumericDocValues("access_count_dv");
        _nowTicks = nowTicks;
        _decay = decay;
    }
    
    public override float CustomScore(int doc, float subQueryScore, float valSrcScore)
    {
        // Get temporal data
        var created = _createdDV?.Get(doc) ?? 0;
        var modified = _modifiedDV?.Get(doc) ?? created;
        var accessCount = _accessCountDV?.Get(doc) ?? 0;
        
        // Use most recent date
        var relevantDate = Math.Max(created, modified);
        
        // Calculate age
        var ageInDays = (_nowTicks - relevantDate) / TimeSpan.TicksPerDay;
        
        // Apply decay function
        var temporalBoost = _decay.Calculate(ageInDays);
        
        // Add access frequency boost (logarithmic)
        var accessBoost = 1.0f + (float)(Math.Log10(accessCount + 1) * 0.1);
        
        // Combine scores
        return subQueryScore * temporalBoost * accessBoost;
    }
}

public class TemporalDecayFunction
{
    public float DecayRate { get; set; } = 0.95f;
    public float HalfLife { get; set; } = 30; // days
    public DecayType Type { get; set; } = DecayType.Exponential;
    
    public static TemporalDecayFunction Default => new TemporalDecayFunction();
    
    public static TemporalDecayFunction Aggressive => new TemporalDecayFunction
    {
        DecayRate = 0.9f,
        HalfLife = 7,
        Type = DecayType.Exponential
    };
    
    public static TemporalDecayFunction Gentle => new TemporalDecayFunction
    {
        DecayRate = 0.98f,
        HalfLife = 90,
        Type = DecayType.Linear
    };
    
    public float Calculate(double ageInDays)
    {
        return Type switch
        {
            DecayType.Exponential => (float)Math.Pow(DecayRate, ageInDays / HalfLife),
            DecayType.Linear => Math.Max(0, 1 - (float)(ageInDays / (HalfLife * 10))),
            DecayType.Gaussian => (float)Math.Exp(-Math.Pow(ageInDays / HalfLife, 2)),
            _ => 1.0f
        };
    }
}
```

**Day 31: Configure and Test (8 hours)**
- [ ] Add temporal scoring options
- [ ] Create decay presets
- [ ] Add configuration UI
- [ ] Performance testing

```csharp
// Update search to use temporal scoring
public async Task<FlexibleMemorySearchResult> SearchWithTemporalScoringAsync(
    FlexibleMemorySearchRequest request)
{
    var baseQuery = BuildQuery(request);
    
    // Apply temporal scoring based on request
    Query scoredQuery = request.TemporalScoring switch
    {
        TemporalScoringMode.None => baseQuery,
        TemporalScoringMode.Default => new TemporalRelevanceQuery(
            baseQuery, 
            TemporalDecayFunction.Default
        ),
        TemporalScoringMode.Aggressive => new TemporalRelevanceQuery(
            baseQuery, 
            TemporalDecayFunction.Aggressive
        ),
        TemporalScoringMode.Gentle => new TemporalRelevanceQuery(
            baseQuery, 
            TemporalDecayFunction.Gentle
        ),
        TemporalScoringMode.Custom => new TemporalRelevanceQuery(
            baseQuery,
            new TemporalDecayFunction
            {
                DecayRate = request.CustomDecayRate ?? 0.95f,
                HalfLife = request.CustomHalfLife ?? 30,
                Type = request.CustomDecayType ?? DecayType.Exponential
            }
        ),
        _ => baseQuery
    };
    
    // Search with scored query
    var results = await SearchInternalAsync(scoredQuery, request);
    
    // Add temporal scoring metadata
    foreach (var memory in results.Memories)
    {
        var age = (DateTime.UtcNow - memory.Modified).TotalDays;
        memory.Metadata["temporalScore"] = CalculateTemporalScore(age, request);
        memory.Metadata["ageInDays"] = age;
    }
    
    return results;
}
```

**Testing Checklist**
- [ ] Test decay functions work correctly
- [ ] Verify recent items rank higher
- [ ] Test access count boosting
- [ ] Benchmark performance impact

**Validation Criteria**
- [ ] Recent memories score higher
- [ ] Decay rates configurable
- [ ] Performance impact < 10ms
- [ ] Relevance improved

### 3.3 Add Semantic Search Layer (60 hours)

#### Background
Complement Lucene text search with semantic understanding using embeddings for concept-based search.

#### Implementation Checklist

**Day 32-35: Setup Embedding Infrastructure (24 hours)**
- [ ] Choose embedding model
- [ ] Create embedding service
- [ ] Setup vector storage
- [ ] Implement similarity search

```csharp
// Services/EmbeddingService.cs
public interface IEmbeddingService
{
    Task<float[]> GetEmbeddingAsync(string text);
    Task<List<float[]>> GetEmbeddingsAsync(List<string> texts);
}

// Services/SemanticMemoryIndex.cs
public class SemanticMemoryIndex
{
    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorIndex _vectorIndex;
    private readonly FlexibleMemoryService _memoryService;
    
    public async Task IndexMemoryAsync(FlexibleMemoryEntry memory)
    {
        // Get embedding for content
        var embedding = await _embeddingService.GetEmbeddingAsync(
            memory.Content + " " + memory.Type
        );
        
        // Store in vector index
        await _vectorIndex.AddAsync(memory.Id, embedding, new Dictionary<string, object>
        {
            ["type"] = memory.Type,
            ["created"] = memory.Created
        });
    }
    
    public async Task<List<SemanticSearchResult>> SemanticSearchAsync(
        string query,
        int limit = 50)
    {
        // Get query embedding
        var queryEmbedding = await _embeddingService.GetEmbeddingAsync(query);
        
        // Find similar vectors
        var similar = await _vectorIndex.SearchAsync(
            queryEmbedding, 
            limit,
            filter: null
        );
        
        // Convert to results
        return similar.Select(s => new SemanticSearchResult
        {
            MemoryId = s.Id,
            Similarity = s.Score,
            Metadata = s.Metadata
        }).ToList();
    }
}

// Hybrid search combining Lucene and semantic
public class HybridMemorySearch
{
    private readonly FlexibleMemoryService _luceneSearch;
    private readonly SemanticMemoryIndex _semanticIndex;
    private readonly ILogger<HybridMemorySearch> _logger;
    
    public async Task<HybridSearchResult> HybridSearchAsync(
        string query,
        HybridSearchOptions options = null)
    {
        options ??= HybridSearchOptions.Default;
        
        // Run both searches in parallel
        var luceneTask = _luceneSearch.SearchMemoriesAsync(
            new FlexibleMemorySearchRequest 
            { 
                Query = query,
                MaxResults = options.MaxResults * 2
            }
        );
        
        var semanticTask = _semanticIndex.SemanticSearchAsync(
            query,
            options.MaxResults * 2
        );
        
        await Task.WhenAll(luceneTask, semanticTask);
        
        var luceneResults = luceneTask.Result;
        var semanticResults = semanticTask.Result;
        
        // Merge and rerank results
        var merged = MergeResults(
            luceneResults.Memories,
            semanticResults,
            options
        );
        
        return new HybridSearchResult
        {
            Results = merged,
            LuceneCount = luceneResults.Memories.Count,
            SemanticCount = semanticResults.Count,
            MergeStrategy = options.MergeStrategy.ToString()
        };
    }
    
    private List<RankedMemory> MergeResults(
        List<FlexibleMemoryEntry> luceneResults,
        List<SemanticSearchResult> semanticResults,
        HybridSearchOptions options)
    {
        var rankedResults = new Dictionary<string, RankedMemory>();
        
        // Add Lucene results
        for (int i = 0; i < luceneResults.Count; i++)
        {
            var memory = luceneResults[i];
            rankedResults[memory.Id] = new RankedMemory
            {
                Memory = memory,
                LuceneRank = i + 1,
                LuceneScore = memory.Score,
                CombinedScore = memory.Score * options.LuceneWeight
            };
        }
        
        // Add/update with semantic results
        for (int i = 0; i < semanticResults.Count; i++)
        {
            var result = semanticResults[i];
            
            if (rankedResults.TryGetValue(result.MemoryId, out var ranked))
            {
                // Memory found by both methods - boost score
                ranked.SemanticRank = i + 1;
                ranked.SemanticScore = result.Similarity;
                ranked.CombinedScore = CalculateCombinedScore(
                    ranked,
                    options
                );
            }
            else
            {
                // Only found by semantic search
                var memory = await _memoryService.GetMemoryAsync(result.MemoryId);
                if (memory != null)
                {
                    rankedResults[result.MemoryId] = new RankedMemory
                    {
                        Memory = memory,
                        SemanticRank = i + 1,
                        SemanticScore = result.Similarity,
                        CombinedScore = result.Similarity * options.SemanticWeight
                    };
                }
            }
        }
        
        // Sort by combined score
        return rankedResults.Values
            .OrderByDescending(r => r.CombinedScore)
            .Take(options.MaxResults)
            .ToList();
    }
    
    private float CalculateCombinedScore(
        RankedMemory memory,
        HybridSearchOptions options)
    {
        return options.MergeStrategy switch
        {
            MergeStrategy.Linear => 
                (memory.LuceneScore * options.LuceneWeight) + 
                (memory.SemanticScore * options.SemanticWeight),
                
            MergeStrategy.Reciprocal => 
                1.0f / ((1.0f / (memory.LuceneRank + 60)) + 
                        (1.0f / (memory.SemanticRank + 60))),
                        
            MergeStrategy.Multiplicative => 
                memory.LuceneScore * memory.SemanticScore * 
                (memory.LuceneRank > 0 && memory.SemanticRank > 0 ? 2.0f : 1.0f),
                
            _ => memory.CombinedScore
        };
    }
}
```

**Day 35-38: Implement Vector Storage (20 hours)**
- [ ] Choose vector database
- [ ] Implement storage interface
- [ ] Add indexing pipeline
- [ ] Create migration tool

```csharp
// Vector storage implementation
public interface IVectorIndex
{
    Task AddAsync(string id, float[] vector, Dictionary<string, object> metadata);
    Task<List<VectorMatch>> SearchAsync(float[] query, int limit, Dictionary<string, object> filter);
    Task DeleteAsync(string id);
    Task<bool> ExistsAsync(string id);
}

// Could use FAISS, Qdrant, Pinecone, etc.
public class FaissVectorIndex : IVectorIndex
{
    private readonly Index _index;
    private readonly Dictionary<long, string> _idMapping;
    private readonly Dictionary<string, Dictionary<string, object>> _metadata;
    
    public FaissVectorIndex(int dimensions)
    {
        _index = new IndexFlatL2(dimensions);
        _idMapping = new Dictionary<long, string>();
        _metadata = new Dictionary<string, Dictionary<string, object>>();
    }
    
    public async Task AddAsync(
        string id, 
        float[] vector, 
        Dictionary<string, object> metadata)
    {
        await Task.Run(() =>
        {
            var internalId = _index.Ntotal;
            _index.Add(vector);
            _idMapping[internalId] = id;
            _metadata[id] = metadata;
        });
    }
    
    public async Task<List<VectorMatch>> SearchAsync(
        float[] query, 
        int limit, 
        Dictionary<string, object> filter)
    {
        return await Task.Run(() =>
        {
            var distances = new float[limit];
            var labels = new long[limit];
            
            _index.Search(1, query, limit, distances, labels);
            
            var results = new List<VectorMatch>();
            for (int i = 0; i < limit; i++)
            {
                if (labels[i] >= 0 && _idMapping.TryGetValue(labels[i], out var id))
                {
                    // Apply filter
                    if (filter != null && _metadata.TryGetValue(id, out var meta))
                    {
                        bool match = true;
                        foreach (var kv in filter)
                        {
                            if (!meta.ContainsKey(kv.Key) || 
                                !meta[kv.Key].Equals(kv.Value))
                            {
                                match = false;
                                break;
                            }
                        }
                        if (!match) continue;
                    }
                    
                    results.Add(new VectorMatch
                    {
                        Id = id,
                        Score = 1.0f / (1.0f + distances[i]), // Convert distance to similarity
                        Metadata = _metadata[id]
                    });
                }
            }
            
            return results;
        });
    }
}
```

**Day 38-40: Integration and Testing (16 hours)**
- [ ] Integrate with memory pipeline
- [ ] Add semantic search tool
- [ ] Create benchmarks
- [ ] Test relevance improvements

**Testing Checklist**
- [ ] Test embedding generation
- [ ] Verify similarity search works
- [ ] Test hybrid search merging
- [ ] Benchmark performance

**Validation Criteria**
- [ ] Concept search works
- [ ] Better recall than text-only
- [ ] Acceptable latency (<200ms)
- [ ] Storage requirements reasonable

### 3.4 Implement Memory Quality Validation (24 hours)

#### Background
Ensure AI agents create high-quality memories with proper structure and content.

#### Implementation Checklist

**Day 40-42: Create Quality Validator (16 hours)**
- [ ] Define quality criteria
- [ ] Implement validators
- [ ] Create scoring system
- [ ] Add improvement suggestions

```csharp
// Services/MemoryQualityValidator.cs
public class MemoryQualityValidator
{
    private readonly ILogger<MemoryQualityValidator> _logger;
    private readonly FlexibleMemoryService _memoryService;
    
    public async Task<MemoryQualityReport> ValidateAsync(
        FlexibleMemoryEntry memory,
        ValidationContext context = null)
    {
        var report = new MemoryQualityReport
        {
            MemoryId = memory.Id,
            Timestamp = DateTime.UtcNow,
            Checks = new List<QualityCheck>()
        };
        
        // Run all validators
        var validators = GetValidators(memory.Type);
        foreach (var validator in validators)
        {
            var check = await validator.ValidateAsync(memory, context);
            report.Checks.Add(check);
        }
        
        // Calculate overall score
        report.OverallScore = CalculateScore(report.Checks);
        report.QualityLevel = GetQualityLevel(report.OverallScore);
        
        // Generate improvement suggestions
        report.Suggestions = GenerateSuggestions(report.Checks, memory);
        
        return report;
    }
    
    private List<IMemoryValidator> GetValidators(string memoryType)
    {
        var validators = new List<IMemoryValidator>
        {
            new ContentLengthValidator(),
            new ContentQualityValidator(),
            new MetadataCompletenessValidator(),
            new RelationshipValidator(),
            new DuplicateValidator(_memoryService)
        };
        
        // Add type-specific validators
        switch (memoryType)
        {
            case "TechnicalDebt":
                validators.Add(new TechnicalDebtValidator());
                break;
            case "ArchitecturalDecision":
                validators.Add(new ArchitecturalDecisionValidator());
                break;
            case "CodePattern":
                validators.Add(new CodePatternValidator());
                break;
        }
        
        return validators;
    }
    
    private List<ImprovementSuggestion> GenerateSuggestions(
        List<QualityCheck> checks,
        FlexibleMemoryEntry memory)
    {
        var suggestions = new List<ImprovementSuggestion>();
        
        foreach (var check in checks.Where(c => !c.Passed))
        {
            switch (check.CheckType)
            {
                case "ContentLength":
                    if (check.Details.Contains("too short"))
                    {
                        suggestions.Add(new ImprovementSuggestion
                        {
                            Type = "Expand",
                            Description = "Add more detail to the description",
                            Example = "Include: What, Why, How, Impact"
                        });
                    }
                    break;
                    
                case "MissingMetadata":
                    var missing = check.Details.Split(',');
                    foreach (var field in missing)
                    {
                        suggestions.Add(new ImprovementSuggestion
                        {
                            Type = "AddField",
                            Description = $"Add {field} field",
                            Command = $"memory update --id='{memory.Id}' --field='{field}' --value='...'"
                        });
                    }
                    break;
                    
                case "NoRelationships":
                    suggestions.Add(new ImprovementSuggestion
                    {
                        Type = "Connect",
                        Description = "Link to related memories",
                        Command = $"memory connect --from='{memory.Id}' --find='similar'"
                    });
                    break;
            }
        }
        
        return suggestions;
    }
}

// Specific validators
public class TechnicalDebtValidator : IMemoryValidator
{
    public async Task<QualityCheck> ValidateAsync(
        FlexibleMemoryEntry memory,
        ValidationContext context)
    {
        var check = new QualityCheck
        {
            CheckType = "TechnicalDebtSpecific",
            Weight = 1.5f
        };
        
        var hasImpact = memory.Fields?.ContainsKey("impact") == true;
        var hasEffort = memory.Fields?.ContainsKey("effort") == true;
        var hasPriority = memory.Fields?.ContainsKey("priority") == true;
        
        if (hasImpact && hasEffort && hasPriority)
        {
            check.Passed = true;
            check.Score = 1.0f;
            check.Details = "All required fields present";
        }
        else
        {
            check.Passed = false;
            check.Score = 0.3f;
            
            var missing = new List<string>();
            if (!hasImpact) missing.Add("impact");
            if (!hasEffort) missing.Add("effort");
            if (!hasPriority) missing.Add("priority");
            
            check.Details = $"Missing fields: {string.Join(", ", missing)}";
        }
        
        return check;
    }
}

public class ContentQualityValidator : IMemoryValidator
{
    public async Task<QualityCheck> ValidateAsync(
        FlexibleMemoryEntry memory,
        ValidationContext context)
    {
        var check = new QualityCheck
        {
            CheckType = "ContentQuality",
            Weight = 2.0f
        };
        
        var content = memory.Content;
        
        // Check for quality indicators
        var hasContext = ContainsAny(content, "because", "since", "due to", "in order to");
        var hasSpecifics = Regex.IsMatch(content, @"\b(class|method|function|file|line)\s+\w+");
        var hasMetrics = Regex.IsMatch(content, @"\d+\s*(ms|seconds|minutes|MB|GB|%)");
        
        var qualityScore = 0.0f;
        if (hasContext) qualityScore += 0.4f;
        if (hasSpecifics) qualityScore += 0.3f;
        if (hasMetrics) qualityScore += 0.3f;
        
        check.Score = qualityScore;
        check.Passed = qualityScore >= 0.6f;
        check.Details = $"Context: {hasContext}, Specifics: {hasSpecifics}, Metrics: {hasMetrics}";
        
        return check;
    }
}
```

**Day 42-43: Automated Improvement (8 hours)**
- [ ] Create improvement service
- [ ] Add auto-enhancement
- [ ] Implement learning system
- [ ] Add quality tracking

```csharp
// Services/MemoryImprovementService.cs
public class MemoryImprovementService
{
    private readonly MemoryQualityValidator _validator;
    private readonly FlexibleMemoryService _memoryService;
    private readonly ILogger<MemoryImprovementService> _logger;
    
    public async Task<MemoryImprovementResult> ImproveMemoryAsync(
        FlexibleMemoryEntry memory,
        bool autoApply = false)
    {
        // Validate current state
        var report = await _validator.ValidateAsync(memory);
        
        if (report.QualityLevel >= QualityLevel.Good)
        {
            return new MemoryImprovementResult
            {
                Success = true,
                Message = "Memory quality is already good",
                FinalScore = report.OverallScore
            };
        }
        
        var improvements = new List<MemoryImprovement>();
        
        // Generate improvements based on validation
        foreach (var suggestion in report.Suggestions)
        {
            var improvement = await GenerateImprovementAsync(memory, suggestion);
            if (improvement != null)
            {
                improvements.Add(improvement);
            }
        }
        
        // Apply improvements if requested
        if (autoApply && improvements.Any())
        {
            var updated = await ApplyImprovementsAsync(memory, improvements);
            
            // Re-validate
            var finalReport = await _validator.ValidateAsync(updated);
            
            return new MemoryImprovementResult
            {
                Success = true,
                Message = $"Applied {improvements.Count} improvements",
                OriginalScore = report.OverallScore,
                FinalScore = finalReport.OverallScore,
                Improvements = improvements,
                UpdatedMemory = updated
            };
        }
        
        return new MemoryImprovementResult
        {
            Success = false,
            Message = "Improvements suggested but not applied",
            OriginalScore = report.OverallScore,
            SuggestedImprovements = improvements
        };
    }
    
    private async Task<MemoryImprovement> GenerateImprovementAsync(
        FlexibleMemoryEntry memory,
        ImprovementSuggestion suggestion)
    {
        switch (suggestion.Type)
        {
            case "Expand":
                // Use AI to expand content
                var expanded = await ExpandContentAsync(memory.Content);
                return new MemoryImprovement
                {
                    Field = "content",
                    OldValue = memory.Content,
                    NewValue = expanded,
                    Reason = "Added more context and detail"
                };
                
            case "AddField":
                // Infer field value from content
                var fieldName = ExtractFieldName(suggestion.Description);
                var fieldValue = await InferFieldValueAsync(memory, fieldName);
                
                if (fieldValue != null)
                {
                    return new MemoryImprovement
                    {
                        Field = fieldName,
                        NewValue = fieldValue,
                        Reason = $"Inferred from content"
                    };
                }
                break;
                
            case "Connect":
                // Find and suggest related memories
                var related = await FindRelatedMemoriesAsync(memory);
                if (related.Any())
                {
                    return new MemoryImprovement
                    {
                        Field = "relationships",
                        NewValue = related.Select(r => new Relationship
                        {
                            TargetId = r.Id,
                            Type = "relatedTo"
                        }).ToList(),
                        Reason = "Found related memories"
                    };
                }
                break;
        }
        
        return null;
    }
}
```

**Testing Checklist**
- [ ] Test validators work correctly
- [ ] Verify quality scoring accuracy
- [ ] Test improvement generation
- [ ] Check auto-enhancement

**Validation Criteria**
- [ ] 90%+ memories pass quality
- [ ] Meaningful suggestions
- [ ] Improvements actually help
- [ ] No false positives

### 3.5 Add Caching Strategy (20 hours)

#### Background
Implement multi-level caching to improve performance and reduce repeated computations.

#### Implementation Checklist

**Day 43-45: Implement Cache Layers (20 hours)**
- [ ] Create cache abstraction
- [ ] Implement query cache
- [ ] Add result cache
- [ ] Create invalidation strategy

```csharp
// Services/MemoryCacheService.cs
public class MemoryCacheService
{
    private readonly IMemoryCache _l1Cache; // In-memory
    private readonly IDistributedCache _l2Cache; // Redis/persistent
    private readonly ILogger<MemoryCacheService> _logger;
    
    // Query cache - caches parsed queries
    private readonly LRUQueryCache _queryCache;
    
    public MemoryCacheService(
        IMemoryCache memoryCache,
        IDistributedCache distributedCache)
    {
        _l1Cache = memoryCache;
        _l2Cache = distributedCache;
        _queryCache = new LRUQueryCache(1000, 50_000_000); // 1000 queries, 50MB
    }
    
    // Cache search results
    public async Task<FlexibleMemorySearchResult> GetOrSearchAsync(
        string cacheKey,
        Func<Task<FlexibleMemorySearchResult>> searchFunc,
        CacheOptions options = null)
    {
        options ??= CacheOptions.Default;
        
        // Check L1 cache
        if (_l1Cache.TryGetValue<FlexibleMemorySearchResult>(cacheKey, out var l1Result))
        {
            _logger.LogDebug("L1 cache hit for {Key}", cacheKey);
            return l1Result;
        }
        
        // Check L2 cache
        var l2Data = await _l2Cache.GetAsync(cacheKey);
        if (l2Data != null)
        {
            var l2Result = JsonSerializer.Deserialize<FlexibleMemorySearchResult>(l2Data);
            
            // Populate L1
            _l1Cache.Set(cacheKey, l2Result, options.L1Duration);
            
            _logger.LogDebug("L2 cache hit for {Key}", cacheKey);
            return l2Result;
        }
        
        // Cache miss - execute search
        var result = await searchFunc();
        
        // Cache based on result quality
        if (ShouldCache(result, options))
        {
            // L1 cache
            _l1Cache.Set(cacheKey, result, options.L1Duration);
            
            // L2 cache
            var serialized = JsonSerializer.SerializeToUtf8Bytes(result);
            await _l2Cache.SetAsync(
                cacheKey, 
                serialized,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = options.L2Duration
                }
            );
        }
        
        return result;
    }
    
    // Cache warming
    public async Task WarmCacheAsync()
    {
        var commonQueries = new[]
        {
            "technical debt",
            "architectural decision",
            "performance",
            "security",
            "TODO"
        };
        
        foreach (var query in commonQueries)
        {
            var request = new FlexibleMemorySearchRequest { Query = query };
            var cacheKey = GenerateCacheKey(request);
            
            // Pre-populate cache
            await GetOrSearchAsync(
                cacheKey,
                () => _memoryService.SearchMemoriesAsync(request),
                CacheOptions.LongTerm
            );
        }
    }
    
    // Smart invalidation
    public async Task InvalidateCacheAsync(InvalidationScope scope)
    {
        switch (scope.Type)
        {
            case InvalidationType.Memory:
                // Invalidate specific memory
                await InvalidateMemoryAsync(scope.MemoryId);
                break;
                
            case InvalidationType.Type:
                // Invalidate all queries for a type
                await InvalidateTypeAsync(scope.MemoryType);
                break;
                
            case InvalidationType.Query:
                // Invalidate specific query
                await InvalidateQueryAsync(scope.Query);
                break;
                
            case InvalidationType.All:
                // Clear all caches
                await ClearAllCachesAsync();
                break;
        }
    }
    
    private bool ShouldCache(
        FlexibleMemorySearchResult result,
        CacheOptions options)
    {
        // Don't cache empty results if specified
        if (!options.CacheEmpty && result.Memories.Count == 0)
            return false;
            
        // Don't cache low confidence results
        if (result.Memories.Any() && 
            result.Memories.Average(m => m.Score) < options.MinScoreThreshold)
            return false;
            
        // Don't cache if too few results
        if (result.Memories.Count < options.MinResultsToCache)
            return false;
            
        return true;
    }
    
    private string GenerateCacheKey(FlexibleMemorySearchRequest request)
    {
        // Create deterministic cache key
        var keyBuilder = new StringBuilder("memory:search:");
        
        keyBuilder.Append(HashString(request.Query ?? ""));
        
        if (request.Types?.Any() == true)
        {
            keyBuilder.Append(":types:");
            keyBuilder.Append(string.Join(",", request.Types.OrderBy(t => t)));
        }
        
        if (request.DateRange != null)
        {
            keyBuilder.Append(":date:");
            keyBuilder.Append(request.DateRange.GetHashCode());
        }
        
        keyBuilder.Append(":max:");
        keyBuilder.Append(request.MaxResults);
        
        return keyBuilder.ToString();
    }
}

// Searcher warming
public class SearcherWarmer : IRefreshListener
{
    private readonly IMemoryCacheService _cacheService;
    private readonly ILogger<SearcherWarmer> _logger;
    
    public async void AfterRefresh(bool didRefresh)
    {
        if (!didRefresh) return;
        
        try
        {
            // Warm common queries
            await _cacheService.WarmCacheAsync();
            
            // Pre-compute facets
            await WarmFacetsAsync();
            
            _logger.LogInformation("Searcher warming completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error warming searcher");
        }
    }
}
```

**Testing Checklist**
- [ ] Test cache hit rates
- [ ] Verify invalidation works
- [ ] Test distributed cache
- [ ] Benchmark performance gains

**Validation Criteria**
- [ ] 80%+ cache hit rate
- [ ] <10ms for cached results
- [ ] Proper invalidation
- [ ] No stale data issues

## Phase 3 Completion Checklist

### Final Integration
- [ ] All components integrated
- [ ] End-to-end testing complete
- [ ] Performance goals met
- [ ] AI workflows validated

### Documentation
- [ ] API documentation complete
- [ ] Migration guide written
- [ ] Performance tuning guide
- [ ] Troubleshooting guide

### Production Readiness
- [ ] Load testing complete
- [ ] Monitoring in place
- [ ] Rollback plan ready
- [ ] Feature flags configured

## Testing Strategy

### Unit Testing
Each component needs comprehensive unit tests:

```csharp
[Fact]
public async Task MemoryAnalyzer_ExpandsSynonyms_Correctly()
{
    // Arrange
    var analyzer = new MemoryAnalyzer();
    var text = "auth bug in system";
    
    // Act
    var tokens = GetTokens(analyzer, text);
    
    // Assert
    tokens.Should().Contain("authentication");
    tokens.Should().Contain("authorization");
    tokens.Should().Contain("defect");
    tokens.Should().Contain("issue");
}
```

### Integration Testing
Test complete workflows:

```csharp
[Fact]
public async Task UnifiedInterface_HandlesCompleteWorkflow()
{
    // Arrange
    var command = new UnifiedMemoryCommand
    {
        Content = "Database query in UserRepository.GetActiveUsers() takes 5 seconds",
        Context = new CommandContext 
        { 
            Confidence = 0.9f,
            Scope = "project"
        }
    };
    
    // Act
    var result = await _unifiedService.ExecuteAsync(command);
    
    // Assert
    result.Success.Should().BeTrue();
    result.Memory.Type.Should().Be("TechnicalDebt");
    result.Memory.Fields.Should().ContainKey("impact");
}
```

### Performance Testing
Benchmark key operations:

```csharp
[Benchmark]
public async Task SearchWithHighlighting()
{
    var request = new FlexibleMemorySearchRequest
    {
        Query = "authentication security bug",
        EnableHighlighting = true,
        MaxResults = 50
    };
    
    await _searchService.SearchMemoriesEnhancedAsync(request);
}
```

### AI Agent Testing
Simulate AI workflows:

```csharp
[Fact]
public async Task AIAgent_LoadsContextEfficiently()
{
    // Arrange
    var startTime = DateTime.UtcNow;
    
    // Act
    var context = await _contextService.LoadContextAsync("/project/src");
    
    // Assert
    var elapsed = DateTime.UtcNow - startTime;
    elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(500));
    context.PrimaryMemories.Should().HaveCountGreaterThan(0);
    context.TokensUsed.Should().BeLessThan(2000);
}
```

## Rollout Plan

### Phase 1 Rollout
1. Deploy to development environment
2. Run automated tests
3. Manual testing by team
4. Limited beta with volunteer users
5. Monitor metrics
6. Full rollout if metrics good

### Phase 2 Rollout
1. Feature flag new query parser
2. A/B test with 10% of queries
3. Monitor relevance metrics
4. Gradual increase to 100%
5. Remove old query code

### Phase 3 Rollout
1. Deploy unified interface as opt-in
2. Gather AI agent feedback
3. Iterate on interface
4. Make default after validation
5. Deprecate old tools

### Rollback Strategy
- All changes behind feature flags
- Old code paths preserved
- Database migrations reversible
- Cache can be cleared
- Monitoring alerts configured

## Success Metrics

### Technical Metrics
| Metric | Baseline | Target | Measurement |
|--------|----------|--------|-------------|
| Search Latency P50 | 50ms | 10ms | Prometheus |
| Search Latency P99 | 200ms | 50ms | Prometheus |
| Index Size | 10MB/1K memories | 7MB/1K | Disk usage |
| Cache Hit Rate | 0% | 80%+ | Application logs |
| Memory Write Time | 50ms | 20ms | Prometheus |

### AI Agent Metrics
| Metric | Baseline | Target | Measurement |
|--------|----------|--------|-------------|
| Context Load Time | 5-10 calls | 1 call | Tool usage logs |
| Token Usage | 8000/session | 2000/session | Token counter |
| Memory Creation Quality | 60% valid | 90% valid | Quality validator |
| Tool Success Rate | 40% | 80%+ | Success tracking |
| Session Continuity | Manual | Automatic | Session tracking |

### Business Metrics
| Metric | Baseline | Target | Measurement |
|--------|----------|--------|-------------|
| AI Agent Productivity | Baseline | +50% | Task completion |
| Memory Reuse Rate | 20% | 60% | Access logs |
| Knowledge Discovery | Manual | Proactive | Suggestion usage |
| Cross-Team Knowledge | Limited | High | Shared memory usage |

## Conclusion

This implementation guide provides a comprehensive roadmap for optimizing the COA CodeSearch MCP memory system. By following this plan:

1. **Phase 1** delivers immediate improvements in search quality and AI usability
2. **Phase 2** provides core technical improvements and performance gains  
3. **Phase 3** adds advanced features for a best-in-class memory system

Total effort is 324 hours across 8-11 weeks, with measurable improvements at each phase. The phased approach allows for validation and adjustment while maintaining system stability.

Key success factors:
- Follow the checklist for each task
- Test thoroughly at each stage
- Monitor metrics continuously
- Gather user feedback regularly
- Be prepared to adjust based on findings

The end result will be a memory system that feels like a natural extension of AI reasoning capabilities, with dramatically improved performance and usability.