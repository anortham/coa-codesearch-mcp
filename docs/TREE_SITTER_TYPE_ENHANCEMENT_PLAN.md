# Tree-sitter Type Enhancement Plan for CodeSearch MCP

## Executive Summary
Enhance CodeSearch with automatic type information extraction using tree-sitter, providing rich type context in search results without requiring separate navigation tools. This approach leverages Claude's natural preference for using CodeSearch while adding the type awareness that makes code navigation more effective.

## Problem Statement
- Claude consistently avoids using CodeNav tools, even with perfect UX and unified interfaces
- Claude naturally uses CodeSearch for code exploration
- Type information is valuable but currently requires separate tools that don't get used
- We need type context without changing the tools Claude prefers

## Solution: Embed Type Information in CodeSearch

### Core Insight
- CodeSearch already reads and indexes every file during `CreateDocumentFromFileAsync`
- We can extract type information during this existing process
- Tree-sitter provides lightweight, multi-language parsing without heavy dependencies
- Type info becomes part of the search index, not a separate system

## Implementation Plan

### Phase 1: Tree-sitter Infrastructure

#### 1.1 Add NuGet Package
```xml
<!-- In COA.CodeSearch.McpServer.csproj -->
<PackageReference Include="TreeSitter.DotNet" Version="1.0.1" />
```
- Includes native parsers for 28+ languages
- Supports both Windows and Linux
- Lightweight compared to Roslyn

#### 1.2 Create Type Extraction Service
Create `Services/TypeExtractionService.cs`:

```csharp
namespace COA.CodeSearch.McpServer.Services;

public class TypeExtractionService : ITypeExtractionService
{
    private readonly ILogger<TypeExtractionService> _logger;
    
    // Language mapping based on file extensions
    private static readonly Dictionary<string, string> ExtensionToLanguage = new()
    {
        // Primary languages
        { ".cs", "c_sharp" },
        { ".ts", "typescript" }, 
        { ".tsx", "tsx" },
        { ".js", "javascript" },
        { ".jsx", "javascript" },
        { ".py", "python" },
        { ".java", "java" },
        { ".go", "go" },
        { ".rs", "rust" },
        { ".cpp", "cpp" },
        { ".cc", "cpp" },
        { ".cxx", "cpp" },
        { ".c", "c" },
        { ".h", "c" },  // Could be C or C++, default to C
        { ".hpp", "cpp" },
        { ".rb", "ruby" },
        { ".php", "php" },
        { ".swift", "swift" },
        { ".kt", "kotlin" },
        { ".scala", "scala" },
        { ".r", "r" },
        { ".m", "objective_c" },
        { ".mm", "objective_cpp" },
        { ".lua", "lua" },
        { ".dart", "dart" },
        { ".zig", "zig" },
        { ".elm", "elm" },
        { ".clj", "clojure" },
        { ".ex", "elixir" },
        { ".exs", "elixir" }
    };
    
    public TypeExtractionResult ExtractTypes(string content, string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        
        if (!ExtensionToLanguage.TryGetValue(extension, out var language))
        {
            return new TypeExtractionResult { Success = false };
        }
        
        try
        {
            using var parser = new Parser(GetLanguage(language));
            using var tree = parser.Parse(content);
            
            var types = new List<TypeInfo>();
            var methods = new List<MethodInfo>();
            
            // Walk the syntax tree and extract type definitions
            ExtractFromNode(tree.RootNode, types, methods, content);
            
            return new TypeExtractionResult
            {
                Success = true,
                Types = types,
                Methods = methods,
                Language = language
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to extract types from {FilePath}", filePath);
            return new TypeExtractionResult { Success = false };
        }
    }
    
    private void ExtractFromNode(Node node, List<TypeInfo> types, List<MethodInfo> methods, string content)
    {
        // Language-specific extraction logic
        // This will vary by language but general patterns:
        // - class_declaration, interface_declaration, struct_declaration
        // - function_declaration, method_declaration
        // - type_alias_declaration, enum_declaration
    }
}

public class TypeInfo
{
    public string Name { get; set; }
    public string Kind { get; set; } // class, interface, struct, enum, type, trait
    public string Signature { get; set; } // Full signature including generics
    public int Line { get; set; }
    public int Column { get; set; }
    public List<string> Modifiers { get; set; } // public, private, abstract, etc.
    public string? BaseType { get; set; } // For inheritance
    public List<string>? Interfaces { get; set; } // Implemented interfaces
}

public class MethodInfo
{
    public string Name { get; set; }
    public string Signature { get; set; } // Full method signature
    public string ReturnType { get; set; }
    public int Line { get; set; }
    public int Column { get; set; }
    public string? ContainingType { get; set; } // The class/interface this belongs to
    public List<string> Parameters { get; set; }
    public List<string> Modifiers { get; set; } // public, async, static, etc.
}
```

### Phase 2: Smart Query Detection

#### 2.1 Query Pattern Recognition
Create or enhance `Services/QueryTypeDetector.cs`:

```csharp
public class QueryTypeDetector
{
    // Patterns that indicate type-related searches
    private static readonly Regex[] TypePatterns = new[]
    {
        new Regex(@"\b(class|interface|struct|enum|type|trait|impl|abstract)\s+\w+", RegexOptions.IgnoreCase),
        new Regex(@"\bI[A-Z]\w+\b"), // Interface naming convention (IUserService)
        new Regex(@"\b\w+(Service|Controller|Repository|Manager|Factory|Builder|Handler|Provider)\b"),
        new Regex(@"<[^>]+>"), // Generic types
        new Regex(@"::\w+"), // Namespace/type references
        new Regex(@"\w+\.\w+\("), // Method calls
        new Regex(@"(public|private|protected|internal)\s+(class|interface|struct)"),
        new Regex(@"extends\s+\w+"), // Inheritance
        new Regex(@"implements\s+\w+"), // Interface implementation
        new Regex(@"func\s+\w+"), // Go functions
        new Regex(@"def\s+\w+"), // Python functions
        new Regex(@"fn\s+\w+"), // Rust functions
    };
    
    // Patterns that indicate NON-type searches
    private static readonly Regex[] NonTypePatterns = new[]
    {
        new Regex(@"\d{8}\.log"), // Log files
        new Regex(@"\d{4}-\d{2}-\d{2}"), // Dates
        new Regex(@"(error|exception|failed|warning|bug|issue)", RegexOptions.IgnoreCase),
        new Regex(@"(TODO|FIXME|HACK|NOTE|XXX)"),
        new Regex(@"https?://"), // URLs
        new Regex(@"[\\/]"), // File paths
        new Regex(@"\.(txt|log|md|json|xml|yml|yaml)$"), // Non-code file extensions
        new Regex(@"^\d+$"), // Pure numbers
        new Regex(@"^[a-f0-9]{32,}$", RegexOptions.IgnoreCase), // Hashes
    };
    
    public bool IsLikelyTypeQuery(string query)
    {
        // Check for non-type patterns first (early exit)
        if (NonTypePatterns.Any(p => p.IsMatch(query)))
            return false;
        
        // Check for type patterns
        return TypePatterns.Any(p => p.IsMatch(query));
    }
    
    public TypeQueryIntent AnalyzeIntent(string query)
    {
        // More detailed analysis for different intents
        if (query.Contains("class ") || query.Contains("interface "))
            return TypeQueryIntent.FindDefinition;
        if (query.Contains("extends ") || query.Contains("implements "))
            return TypeQueryIntent.FindInheritance;
        if (query.Contains("(") && query.Contains(")"))
            return TypeQueryIntent.FindMethod;
        // ... etc
    }
}
```

### Phase 3: Enhanced Document Creation

#### 3.1 Modify FileIndexingService
Update `FileIndexingService.CreateDocumentFromFileAsync`:

```csharp
private async Task<Document?> CreateDocumentFromFileAsync(
    string filePath, 
    string workspacePath, 
    CancellationToken cancellationToken)
{
    try
    {
        // ... existing code to read file content ...
        
        // NEW: Extract type information
        var typeData = _typeExtractionService.ExtractTypes(content, filePath);
        
        // Create Lucene document with existing fields
        var document = new Document
        {
            // ... existing fields ...
            new StringField("path", filePath, Field.Store.YES),
            new TextField("content", content, Field.Store.YES),
            // ... etc ...
        };
        
        // NEW: Add type-specific fields if extraction succeeded
        if (typeData.Success && (typeData.Types.Any() || typeData.Methods.Any()))
        {
            // Searchable field with all type names
            var allTypeNames = typeData.Types.Select(t => t.Name)
                .Concat(typeData.Methods.Select(m => m.Name))
                .Distinct();
            document.Add(new TextField("type_names", string.Join(" ", allTypeNames), Field.Store.NO));
            
            // Stored field with full type information (JSON)
            var typeJson = JsonSerializer.Serialize(new
            {
                types = typeData.Types,
                methods = typeData.Methods,
                language = typeData.Language
            });
            document.Add(new StoredField("type_info", typeJson));
            
            // Add individual type definition fields for boosting
            foreach (var type in typeData.Types)
            {
                document.Add(new TextField("type_def", $"{type.Kind} {type.Name}", Field.Store.NO));
            }
            
            // Count fields for statistics
            document.Add(new Int32Field("type_count", typeData.Types.Count, Field.Store.YES));
            document.Add(new Int32Field("method_count", typeData.Methods.Count, Field.Store.YES));
        }
        
        return document;
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Failed to create document for file {FilePath}", filePath);
        return null;
    }
}
```

### Phase 4: Enhanced Search Response

#### 4.1 Modify TextSearchTool
Update search result processing to include type information:

```csharp
protected override async Task<AIOptimizedResponse<SearchResult>> ExecuteInternalAsync(
    TextSearchParameters parameters,
    CancellationToken cancellationToken)
{
    // ... existing query processing ...
    
    // NEW: Detect if this is a type-related query
    var isTypeQuery = _queryTypeDetector.IsLikelyTypeQuery(parameters.Query);
    
    // ... perform search ...
    
    // NEW: Enhance results with type information if relevant
    if (isTypeQuery && searchResult.Hits != null)
    {
        foreach (var hit in searchResult.Hits)
        {
            // Retrieve stored type information
            var typeInfoJson = hit.Document?.Get("type_info");
            if (!string.IsNullOrEmpty(typeInfoJson))
            {
                var typeData = JsonSerializer.Deserialize<StoredTypeInfo>(typeInfoJson);
                
                // Find relevant types near the match
                var relevantTypes = FindRelevantTypes(typeData, hit.LineNumber);
                
                // Add to hit metadata or context
                hit.TypeContext = new TypeContext
                {
                    NearbyTypes = relevantTypes.Types,
                    NearbyMethods = relevantTypes.Methods,
                    ContainingType = DetermineContainingType(typeData, hit.LineNumber)
                };
                
                // Format for display
                if (hit.TypeContext.ContainingType != null)
                {
                    hit.EnhancedSnippet = $"[{hit.TypeContext.ContainingType}]\n{hit.Snippet}";
                }
            }
        }
    }
    
    // ... continue with response building ...
}
```

#### 4.2 Enhanced Response Format
Example of enhanced search results with type information:

```json
{
  "hits": [
    {
      "filePath": "UserService.cs",
      "lineNumber": 45,
      "snippet": "public async Task<User> GetUser(int id)",
      "typeContext": {
        "containingType": "class UserService : IUserService",
        "nearbyTypes": [
          {
            "name": "UserService",
            "kind": "class",
            "line": 10,
            "signature": "public class UserService : IUserService"
          }
        ],
        "nearbyMethods": [
          {
            "name": "GetUser",
            "signature": "public async Task<User> GetUser(int id)",
            "line": 45
          },
          {
            "name": "CreateUser",
            "signature": "public async Task<User> CreateUser(UserDto dto)",
            "line": 52
          }
        ]
      }
    }
  ]
}
```

### Phase 5: Scoring Enhancement

#### 5.1 Add Type-Aware Scoring Factor
Create `Scoring/TypeDefinitionBoostFactor.cs`:

```csharp
public class TypeDefinitionBoostFactor : IScoringFactor
{
    public float CalculateBoost(Document doc, string queryText, ScoringContext context)
    {
        // Check if this document contains type definitions matching the query
        var typeNames = doc.Get("type_names");
        if (string.IsNullOrEmpty(typeNames))
            return 1.0f;
        
        // Boost if query terms match type definitions
        var queryTerms = queryText.ToLower().Split(' ');
        var typeTerms = typeNames.ToLower().Split(' ');
        
        var matchCount = queryTerms.Count(qt => typeTerms.Contains(qt));
        if (matchCount > 0)
        {
            // Higher boost for more matching terms
            return 1.0f + (0.5f * matchCount);
        }
        
        return 1.0f;
    }
}
```

## Performance Considerations

### Indexing Performance
- Tree-sitter parsing adds ~10-50ms per file
- Only parse files with supported extensions
- Cache parsed results during batch indexing
- Consider parallel processing for large workspaces

### Query Performance
- Type detection regex is fast (<1ms)
- Type field searches use existing Lucene index
- JSON deserialization only for type-query results
- Minimal impact on non-type searches

### Memory Usage
- Tree-sitter parsers are lightweight (~1-5MB per language)
- Lazy load parsers as needed
- Type info adds ~10-20% to index size
- Consider compression for stored JSON

## Configuration

Add to `appsettings.json`:

```json
{
  "CodeSearch": {
    "TypeExtraction": {
      "Enabled": true,
      "MaxTypesPerFile": 100,
      "MaxMethodsPerFile": 500,
      "IncludePrivateMembers": false,
      "Languages": ["c_sharp", "typescript", "javascript", "python", "go", "rust", "java"],
      "EnableSmartDetection": true,
      "ContextLineRadius": 50
    }
  }
}
```

## Testing Strategy

### Unit Tests
1. Test type extraction for each supported language
2. Test query type detection accuracy
3. Test scoring factor calculations
4. Test JSON serialization/deserialization

### Integration Tests
1. Index test workspace with known types
2. Search for class names, verify type info appears
3. Search for method names, verify signatures appear
4. Search for non-type queries, verify no overhead
5. Test with mixed language codebases

### Performance Tests
1. Measure indexing time with/without type extraction
2. Measure search response time impact
3. Test memory usage with large workspaces
4. Test with 10K+ file repositories

### Sample Test Cases

```csharp
// Test: C# class extraction
Input: "public class UserService : IUserService { ... }"
Expected: TypeInfo { Name: "UserService", Kind: "class", BaseType: "IUserService" }

// Test: TypeScript interface extraction  
Input: "export interface User { id: number; name: string; }"
Expected: TypeInfo { Name: "User", Kind: "interface" }

// Test: Python function extraction
Input: "def calculate_total(items: List[Item]) -> float:"
Expected: MethodInfo { Name: "calculate_total", ReturnType: "float" }

// Test: Query detection
Input: "class UserService"
Expected: IsLikelyTypeQuery = true

Input: "20250829.log"
Expected: IsLikelyTypeQuery = false
```

## Rollout Plan

### Phase 1: Minimal Implementation (Week 1)
- Add tree-sitter package
- Create basic TypeExtractionService
- Extract only class/interface names
- Add to text_search results only

### Phase 2: Enhanced Extraction (Week 2)
- Add method extraction
- Support more languages
- Add inheritance information
- Implement smart query detection

### Phase 3: Scoring and Optimization (Week 3)
- Add type-aware scoring
- Optimize performance
- Add configuration options
- Comprehensive testing

### Phase 4: Monitoring and Iteration
- Monitor Claude's usage patterns
- Measure search quality improvements
- Gather performance metrics
- Iterate based on findings

## Success Metrics

1. **Adoption**: Claude uses text_search with same frequency
2. **Relevance**: Type-related searches return better results
3. **Performance**: <100ms impact on indexing per file
4. **Coverage**: 80%+ of type definitions extracted accurately
5. **User Satisfaction**: Reduced need for separate navigation tools

## Rollback Plan

If issues arise:
1. Disable type extraction via configuration flag
2. Type fields in index are ignored (no breaking changes)
3. Search continues to work with existing fields
4. Can remove type fields in next reindex

## Future Enhancements

Once proven successful:
1. **Call hierarchy**: Track method calls during parsing
2. **Reference tracking**: Store where types are used
3. **Smart suggestions**: "Did you mean class X?"
4. **Cross-file relationships**: Track imports/dependencies
5. **File_search integration**: Show contained types in file results
6. **Incremental updates**: Only reparse changed sections

## Key Advantages

1. **Zero behavior change** - Claude continues using text_search naturally
2. **Automatic enrichment** - Type context appears when relevant
3. **Multi-language** - Single solution for all languages
4. **Lightweight** - No heavy Roslyn/TypeScript compiler dependencies
5. **Progressive** - Can start simple and enhance over time
6. **Graceful degradation** - Works even if type extraction fails

## Implementation Notes

- Use existing `CodeAnalyzer` for tokenization consistency
- Leverage existing `FileIndexingService` infrastructure
- Reuse `BatchIndexingService` for performance
- Follow existing error handling patterns
- Use existing logging infrastructure
- Maintain backwards compatibility

## Dependencies

- TreeSitter.DotNet 1.0.1 (NuGet)
- No additional framework dependencies
- Compatible with existing Lucene.Net setup
- Works with existing MCP framework

## Contact

For questions or clarifications about this plan, reference the original discussion in the CodeNav MCP session from 2025-08-30.

---

*This plan represents a lightweight, incremental approach to adding type awareness to CodeSearch without changing the tool interface that Claude naturally prefers.*