# CodeAnalyzer Documentation

## Overview

The `CodeAnalyzer` is a custom Lucene.NET analyzer designed specifically for indexing and searching source code. Unlike the standard analyzers that treat punctuation as word boundaries, CodeAnalyzer preserves programming language syntax patterns to enable precise code searches.

## Key Features

- **Preserves Code Patterns**: Keeps patterns like `: ITool`, `[Fact]`, `std::cout`, `->method()` intact
- **Generic Type Support**: Handles generic type annotations like `: IRepository<T>` as single searchable tokens
- **Multi-language Support**: Works with C#, C++, Go, JavaScript, Python, and other languages
- **Configurable**: Options for case preservation and camelCase splitting

## How It Works

### 1. CodeTokenizer

The heart of the analyzer is the `CodeTokenizer` class, which handles the lexical analysis of source code:

```csharp
// Example tokenization:
Input:  "public async Task<string> GetDataAsync() : IRepository<User>"
Output: ["public", "async", "Task<string>", "GetDataAsync", "(", ")", ": IRepository<User>"]
```

### 2. Token Processing Pipeline

1. **CodeTokenizer**: Extracts tokens while preserving code structure
2. **CamelCaseFilter** (optional): Splits camelCase identifiers for better searchability  
3. **LowerCaseFilter** (optional): Normalizes case for case-insensitive search
4. **CodeLengthFilter**: Removes very short tokens except operators

## Supported Patterns

### Type Annotations
```csharp
: ITool                  // Interface implementation
: IRepository<T>         // Generic interface
: base                   // Base class call
: this                   // Constructor chaining
```

### Attributes and Decorators
```csharp
[Fact]                   // C# attributes
[HttpGet("api/users")]   // Attributes with parameters
@property                // Python decorators
#[derive(Debug)]         // Rust attributes
```

### Operators and Syntax
```csharp
::                       // C++ namespace
->                       // Pointer access
=>                       // Lambda arrow
...                      // Spread operator
?.                       // Optional chaining
??                       // Null coalescing
<-                       // Go channel
|>                       // Pipe operator
:=                       // Go assignment
```

### Generic Types
```csharp
List<string>             // Simple generics
Dictionary<string, int>  // Multiple type parameters
Func<int, Task<bool>>    // Nested generics
```

## Search Examples

### Literal Search
```bash
# Find exact patterns
text_search --searchType "literal" --query ": ITool"
text_search --searchType "literal" --query "[Fact]"
text_search --searchType "literal" --query "async Task"
```

### Code Search
```bash
# Optimized for code patterns
text_search --searchType "code" --query ": IRepository<T>"
text_search --searchType "code" --query "Task<List<User>>"
```

### Regex Search
```bash
# Pattern matching (converted to phrase queries for multi-token patterns)
text_search --searchType "regex" --query "async.*Task"
text_search --searchType "regex" --query "Get.*Async"
```

## Configuration

### Analyzer Options

```csharp
// Case-sensitive search
var analyzer = new CodeAnalyzer(version, preserveCase: true);

// Disable camelCase splitting
var analyzer = new CodeAnalyzer(version, preserveCase: false, splitCamelCase: false);
```

### Integration with LuceneIndexService

The analyzer is automatically selected based on file extensions:

```csharp
// In appsettings.json
"IndexSettings": {
  "UseCodeAnalyzer": true,
  "CodeAnalyzerExtensions": [".cs", ".java", ".js", ".ts", ".cpp", ".go", ".py"]
}
```

## Implementation Details

### Special Character Handling

The tokenizer recognizes these as operators:
```
: - > = . ? < | [ ] @ # ( ) { } * & ! ~ + / \ ^ %
```

### Multi-character Operators

Recognized patterns include:
```
:: -> => ... .. ?. ?? <- |> := >= <= == != && || ++ -- 
+= -= *= /= << >> <<< >>>
```

### Buffer Management

- Uses a 4KB character buffer for efficient reading
- Handles lookahead for patterns like `: Type`
- Manages state for complex patterns like generics with nested angle brackets

## Performance Considerations

- **Indexing**: Slightly slower than StandardAnalyzer due to pattern recognition
- **Search**: Comparable performance to StandardAnalyzer
- **Memory**: Minimal overhead, uses streaming tokenization

## Troubleshooting

### Pattern Not Found

If a code pattern isn't being found:
1. Check if the pattern is being tokenized as expected using tests
2. Verify the search type (literal vs code vs standard)
3. Ensure the analyzer is enabled for the file type

### Generic Types Split

If generic types are being split:
- This was fixed in the latest version
- Ensure you're using the updated CodeAnalyzer
- Rebuild indexes after updating

### Case Sensitivity Issues

- Default behavior is case-insensitive
- Use `preserveCase: true` for case-sensitive indexing
- Remember to rebuild indexes when changing case settings

## Future Enhancements

1. **Language-specific tokenizers**: Optimize for specific language syntax
2. **Context-aware tokenization**: Better handling of language-specific patterns
3. **Performance optimizations**: Faster pattern matching algorithms
4. **Extended operator support**: Add more language-specific operators

## Testing

The CodeAnalyzer includes comprehensive tests in `CodeAnalyzerTests.cs`:

```bash
# Run all analyzer tests
dotnet test --filter "FullyQualifiedName~CodeAnalyzerTests"

# Run specific pattern tests
dotnet test --filter "FullyQualifiedName~GenericTypeTokenizationTest"
```

## Contributing

When adding new patterns:
1. Add the pattern to the regex or special handling code
2. Add a test case in CodeAnalyzerTests
3. Document the pattern in this file
4. Consider backward compatibility