# Custom Code Analyzer Implementation Plan

## Problem Statement

The current Lucene StandardAnalyzer is designed for natural language text and treats many programming punctuation characters as word boundaries. This causes searches for common code patterns to fail:

- `: ITool` returns 0 results (colon is removed during indexing)
- `[Fact]` has issues (brackets are removed)
- `->method()` fails (arrow operator is split)
- `std::cout` fails (double colon is split)
- `List<string>` fails (angle brackets are removed)

## Solution Overview

Create a custom `CodeAnalyzer` that preserves programming language punctuation and structure while still enabling effective searching across multiple programming languages.

## Design Principles

1. **Language Agnostic**: Support common patterns across C#, Java, JavaScript, TypeScript, Python, C++, Go, Rust, etc.
2. **Preserve Code Structure**: Keep operators, punctuation, and identifiers intact
3. **Flexible Searching**: Support both exact pattern matching and fuzzy searches
4. **Performance**: Maintain indexing and search performance

## Technical Architecture

### 1. Core Analyzer Components

```csharp
public class CodeAnalyzer : Analyzer
{
    private readonly bool _preserveCase;
    private readonly bool _splitCamelCase;
    
    public CodeAnalyzer(bool preserveCase = false, bool splitCamelCase = true)
    {
        _preserveCase = preserveCase;
        _splitCamelCase = splitCamelCase;
    }
    
    protected override TokenStreamComponents CreateComponents(string fieldName, TextReader reader)
    {
        // Custom tokenizer that preserves code structure
        var tokenizer = new CodeTokenizer(reader);
        
        TokenStream stream = tokenizer;
        
        // Optional: Split camelCase and snake_case for better searchability
        if (_splitCamelCase)
        {
            stream = new CamelCaseFilter(stream);
        }
        
        // Preserve or lowercase based on configuration
        if (!_preserveCase)
        {
            stream = new LowerCaseFilter(stream);
        }
        
        // Remove very short tokens (single characters except operators)
        stream = new CodeLengthFilter(stream, minLength: 1);
        
        return new TokenStreamComponents(tokenizer, stream);
    }
}
```

### 2. Custom Tokenizer

The `CodeTokenizer` needs to recognize and preserve code patterns:

```csharp
public class CodeTokenizer : CharTokenizer
{
    protected override bool IsTokenChar(int c)
    {
        // Keep together:
        // - Alphanumeric characters
        // - Underscores (for identifiers)
        // - Common operators when part of multi-char operators
        return char.IsLetterOrDigit((char)c) || 
               c == '_' ||
               IsPartOfOperator(c);
    }
    
    private bool IsPartOfOperator(int c)
    {
        // This needs context awareness to handle:
        // :: (C++ namespace)
        // -> (pointer access)
        // => (lambda)
        // .. (range)
        // ... (spread)
        // etc.
    }
}
```

### 3. Token Patterns to Preserve

#### Universal Programming Patterns
- **Type annotations**: `: Type`, `: Interface`, `-> ReturnType`
- **Inheritance**: `: BaseClass`, `extends Base`, `implements Interface`, `< SuperClass`
- **Generics/Templates**: `List<T>`, `Array<string>`, `Map<K,V>`, `Vec<T>`
- **Namespaces/Modules**: `std::cout`, `System.IO`, `numpy.array`, `fmt.Println`
- **Decorators/Attributes**: `@decorator`, `[Attribute]`, `#[derive]`, `@property`
- **Operators**: `->`, `=>`, `::`, `?.`, `??`, `...`, `<-`, `|>`, `::`
- **Method calls**: `object.method()`, `func(param)`, `obj->method()`
- **Comments**: `//`, `/*`, `*/`, `#`, `"""`, `<!--`, `-->`

#### Language-Specific Patterns (Must Support)
- **Python**: `@decorator`, `__init__`, `self.method`, `def method() ->`, `**kwargs`, `lambda:`
- **JavaScript/TypeScript**: `=>`, `...spread`, `?.optional`, `import from`, `export default`
- **Java**: `@Annotation`, `implements`, `extends`, `throws Exception`, `<T extends>`
- **C#**: `[Fact]`, `[HttpGet]`, `: ITool`, `?.`, `??`, `nameof()`, `async/await`
- **C/C++**: `::scope`, `->pointer`, `template<>`, `#include`, `nullptr`, `std::`
- **Go**: `:=`, `<-channel`, `defer`, `func()`, `interface{}`, `...variadic`
- **Rust**: `::path`, `&reference`, `'lifetime`, `impl Trait`, `Box<>`, `Result<>`
- **PHP**: `->method()`, `::static`, `$variable`, `<?php`, `namespace\\Class`
- **Ruby**: `@instance`, `@@class`, `:symbol`, `||=`, `&block`, `module::`
- **Swift**: `?.optional`, `!forced`, `protocol`, `where T:`, `@objc`

### 4. Simplified Indexing Strategy

Since we reindex on demand, we can use a single, optimized approach:

```csharp
public Document CreateDocument(string filePath, string content)
{
    var doc = new Document();
    
    // Primary field: Code-analyzed content (preserves code structure)
    doc.Add(new TextField("content", content, Field.Store.YES));
    
    // Optional: Add field for case-sensitive searches if needed
    // doc.Add(new TextField("content_case", content, Field.Store.NO));
    
    return doc;
}
```

### 5. Query Strategy

With the CodeAnalyzer, queries become much simpler:

```csharp
public Query BuildQuery(string queryText, string searchType)
{
    switch (searchType)
    {
        case "literal":
        case "code":
        case "standard":
            // All use the same code-analyzed field now
            // The analyzer preserves code patterns, so ": ITool" just works
            var parser = new QueryParser(LuceneVersion.LUCENE_48, "content", _codeAnalyzer);
            return parser.Parse(queryText);
            
        case "wildcard":
            // Wildcards still need special handling
            return new WildcardQuery(new Term("content", queryText.ToLower()));
            
        case "regex":
            // Regex operates on raw terms
            return new RegexpQuery(new Term("content", queryText));
            
        // ... other cases
    }
}
```

## Implementation Plan

### Phase 1: Proof of Concept (2-3 days)
1. Create basic `CodeAnalyzer` with simple tokenizer
2. Test with common code patterns (`: ITool`, `[Fact]`, `->method`)
3. Benchmark performance vs StandardAnalyzer

### Phase 2: Full Implementation (3-5 days)
1. Implement sophisticated `CodeTokenizer` with operator recognition
2. Add `CamelCaseFilter` for splitting identifiers
3. Create multi-field indexing strategy
4. Update `FileIndexingService` to use new analyzer

### Phase 3: Query Integration (2-3 days)
1. Update search tools to use CodeAnalyzer
2. Simplify query builders (remove escaping workarounds)
3. Test all search types work correctly

### Phase 4: Testing & Optimization (2-3 days)
1. Comprehensive test suite for various languages
2. Performance testing and optimization
3. Memory usage analysis
4. Edge case handling

## Testing Strategy

### Unit Tests
- Tokenization tests for each language pattern
- Query building tests
- Analyzer configuration tests

### Integration Tests
- Index sample code files from different languages
- Search for common patterns
- Verify file/directory search still works

### Test Cases
```csharp
[Fact]
public void Should_Find_CSharp_Interface_Implementation()
{
    IndexContent("public class UserService : IUserService");
    
    var results = Search(": IUserService");
    Assert.NotEmpty(results);
}

[Fact]
public void Should_Find_JavaScript_Arrow_Function()
{
    IndexContent("const handler = (req, res) => { res.send('OK'); }");
    
    var results = Search("=>");
    Assert.NotEmpty(results);
}

[Fact]
public void Should_Find_CPlusPlus_Namespace()
{
    IndexContent("std::cout << \"Hello World\";");
    
    var results = Search("std::cout");
    Assert.NotEmpty(results);
}
```

## Migration Strategy

Since we reindex workspaces on demand:

1. **Clean Switch**: Replace StandardAnalyzer with CodeAnalyzer
2. **Reindex**: Users will automatically get the new analyzer on next index
3. **No Legacy Support**: Remove all the query escaping workarounds

## Configuration Options

```json
{
  "analyzers": {
    "code": {
      "type": "custom",
      "tokenizer": "code_tokenizer",
      "filters": [
        "camelcase_splitter",
        "lowercase",
        "code_length"
      ],
      "options": {
        "preserveCase": false,
        "splitCamelCase": true,
        "minTokenLength": 1,
        "preserveOperators": true
      }
    }
  }
}
```

## Performance Considerations

1. **Index Size**: Should be similar to current size (single field strategy)
2. **Indexing Speed**: Custom tokenizer may be slightly slower
3. **Search Speed**: Simpler queries should be faster
4. **Memory Usage**: Single analyzer instance, minimal overhead

## Success Criteria

1. Can find `: ITool` patterns
2. Can find `[Attribute]` patterns  
3. Can find `::namespace` patterns
4. Can find `->pointer` patterns
5. File/directory search continues to work
6. No significant performance degradation
7. Supports all major programming languages

## Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Performance degradation | High | Benchmark early, optimize tokenizer |
| Complex tokenizer logic | Medium | Start simple, iterate |
| Edge cases in various languages | Medium | Comprehensive test suite |

## Scope and Design Philosophy

### Stand-Alone Excellence

CodeSearch must be a world-class code search tool that works excellently for ANY programming language. Many users will rely solely on CodeSearch for their development work in Python, JavaScript, Go, Rust, etc.

**Core Capabilities:**
- Find code patterns efficiently (`: Interface`, `[Decorator]`, `::namespace`)
- Support common programming constructs across all languages
- Fast, accurate results without language-specific tools
- Work seamlessly with AI agents for code exploration

### Optional Integration with CodeNav

For users with language-specific tools like CodeNav:
- CodeSearch handles text-based pattern matching
- CodeNav provides semantic understanding (C#, TypeScript)
- Both tools complement each other without dependency

### Regex Limitations

Important: Regex queries in Lucene operate on individual tokens, not raw text:

- ✅ `/ITool/` - matches the token "ITool"
- ✅ `/std::cout/` - matches if preserved as single token
- ❌ `/public.*ITool/` - can't match across tokens
- ❌ `/class\s+\w+\s*:\s*ITool/` - complex patterns fail

This is acceptable because:
1. Complex semantic searches should use CodeNav
2. For rare complex regex needs, users can use ripgrep
3. 99% of searches are simple pattern matches that CodeAnalyzer will handle

## Future Enhancements

1. **Language-specific analyzers**: Optimize for specific languages
2. **Semantic understanding**: Recognize language constructs
3. **Smart query expansion**: Automatically search variations
4. **Context-aware tokenization**: Different rules for strings vs code