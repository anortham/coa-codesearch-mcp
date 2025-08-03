# CodeAnalyzer Implementation Guide

## Architecture Overview

The CodeAnalyzer is built on Lucene.NET's analyzer framework and consists of several components working together to tokenize source code while preserving syntactic elements.

## Core Components

### 1. CodeAnalyzer (Main Class)

Located in: `COA.CodeSearch.McpServer/Services/Analysis/CodeAnalyzer.cs`

```csharp
public sealed class CodeAnalyzer : Analyzer
{
    private readonly bool _preserveCase;
    private readonly bool _splitCamelCase;
    private readonly LuceneVersion _version;

    protected override TokenStreamComponents CreateComponents(string fieldName, TextReader reader)
    {
        var tokenizer = new CodeTokenizer(_version, reader);
        TokenStream stream = tokenizer;
        
        if (_splitCamelCase)
            stream = new CamelCaseFilter(stream);
        
        if (!_preserveCase)
            stream = new LowerCaseFilter(_version, stream);
        
        stream = new CodeLengthFilter(stream, minLength: 1);
        
        return new TokenStreamComponents(tokenizer, stream);
    }
}
```

### 2. CodeTokenizer

The tokenizer is responsible for breaking the input text into tokens. Key methods:

#### Token Recognition Logic

```csharp
public override bool IncrementToken()
{
    // Main tokenization loop
    while (true)
    {
        // Fill buffer if needed
        if (_bufferPosition >= _bufferSize)
        {
            _bufferSize = m_input.Read(_buffer, 0, _buffer.Length);
            if (_bufferSize <= 0) return false;
            _bufferPosition = 0;
        }
        
        // Skip whitespace
        while (_bufferPosition < _bufferSize && char.IsWhiteSpace(_buffer[_bufferPosition]))
        {
            _offset++;
            _bufferPosition++;
        }
        
        // Process token based on first character
        var firstCh = _buffer[_bufferPosition];
        
        // Special handling for different patterns...
    }
}
```

#### Pattern-Specific Handling

**Type Annotations (: Type)**
```csharp
if (firstCh == ':' && _bufferPosition < _bufferSize)
{
    var lookAhead = _bufferPosition;
    // Skip whitespace
    while (lookAhead < _bufferSize && char.IsWhiteSpace(_buffer[lookAhead]))
    {
        tokenBuilder.Append(_buffer[lookAhead]);
        lookAhead++;
    }
    
    // Check for identifier after whitespace
    if (lookAhead < _bufferSize && IsTokenChar(_buffer[lookAhead]))
    {
        // Consume identifier
        // Check for generic parameters
        if (_buffer[_bufferPosition] == '<')
        {
            // Handle generic type annotation
        }
    }
}
```

**Attributes ([Attribute])**
```csharp
else if (firstCh == '[' && _bufferPosition < _bufferSize)
{
    while (_bufferPosition < _bufferSize && _buffer[_bufferPosition] != ']')
    {
        tokenBuilder.Append(_buffer[_bufferPosition]);
        _bufferPosition++;
        _offset++;
    }
    
    if (_bufferPosition < _bufferSize && _buffer[_bufferPosition] == ']')
    {
        tokenBuilder.Append(']');
        _bufferPosition++;
        _offset++;
    }
}
```

**Generic Types**
```csharp
if (_bufferPosition < _bufferSize && _buffer[_bufferPosition] == '<')
{
    tokenBuilder.Append('<');
    _bufferPosition++;
    _offset++;
    
    int angleDepth = 1;
    while (_bufferPosition < _bufferSize && angleDepth > 0)
    {
        var ch = _buffer[_bufferPosition];
        tokenBuilder.Append(ch);
        _bufferPosition++;
        _offset++;
        
        if (ch == '<') angleDepth++;
        else if (ch == '>') angleDepth--;
    }
}
```

### 3. Filter Classes

#### CamelCaseFilter
Splits camelCase and snake_case identifiers:
- `UserService` → `["UserService", "User", "Service"]`
- `user_service` → `["user_service", "user", "service"]`

#### CodeLengthFilter
Removes very short tokens except operators:
```csharp
public override bool IncrementToken()
{
    while (m_input.IncrementToken())
    {
        var term = _termAttr.ToString();
        var type = _typeAttr.Type;
        
        // Always keep operators and annotations
        if (type == "OPERATOR" || type == "ANNOTATION")
            return true;
        
        // Filter by length for other tokens
        if (term.Length >= _minLength)
            return true;
    }
    return false;
}
```

## Token Type Classification

Tokens are classified into types for better filtering and processing:

```csharp
private string DetermineTokenType(string token)
{
    if (IsKnownOperator(token))
        return "OPERATOR";
    if (token.StartsWith("@") || (token.StartsWith("[") && token.EndsWith("]")))
        return "ANNOTATION";
    if (token.Contains("::") || token.Contains("."))
        return "QUALIFIED_NAME";
    if (token.Contains("<") && token.Contains(">"))
        return "GENERIC_TYPE";
    if (token.StartsWith(":"))
        return "TYPE_ANNOTATION";
    return "IDENTIFIER";
}
```

## Integration Points

### 1. LuceneIndexService

The analyzer is selected based on configuration:

```csharp
public async Task<Analyzer> GetAnalyzerAsync(string workspacePath, CancellationToken cancellationToken)
{
    var settings = await GetIndexSettingsAsync(workspacePath);
    
    if (settings.UseCodeAnalyzer)
    {
        return new CodeAnalyzer(LuceneVersion.LUCENE_48, 
            preserveCase: false, 
            splitCamelCase: true);
    }
    
    return new StandardAnalyzer(LuceneVersion.LUCENE_48);
}
```

### 2. Search Query Building

Different search types use the analyzer differently:

```csharp
case "literal":
case "code":
    // Use analyzer to tokenize query the same way as indexed content
    using (var tokenStream = analyzer.GetTokenStream("content", new StringReader(queryText)))
    {
        var termAttr = tokenStream.AddAttribute<ICharTermAttribute>();
        tokenStream.Reset();
        
        var tokens = new List<string>();
        while (tokenStream.IncrementToken())
        {
            tokens.Add(termAttr.ToString());
        }
        
        // Build query from tokens...
    }
    break;
```

## Performance Optimizations

### 1. Buffer Management
- Uses a 4KB buffer to minimize I/O operations
- Reads ahead for pattern matching without re-reading

### 2. Compiled Regex
- Regex patterns are compiled and cached as static fields
- Used for validation, not primary tokenization

### 3. Minimal Object Allocation
- Reuses StringBuilder for token construction
- Avoids creating intermediate strings

## Testing Strategy

### Unit Tests
Test individual components:
```csharp
[Fact]
public void Should_Preserve_Generic_Types()
{
    var analyzer = new CodeAnalyzer(Version, preserveCase: true);
    var text = "Repository<T> : IRepository<T>";
    var tokens = GetTokens(analyzer, text);
    
    Assert.Contains("Repository<T>", tokens);
    Assert.Contains(": IRepository<T>", tokens);
}
```

### Integration Tests
Test complete search scenarios:
```csharp
[Fact]
public void Should_Find_Generic_Interface_Implementation()
{
    // Index sample code
    // Search for ": IRepository<T>"
    // Verify results
}
```

## Debugging Tips

### 1. Token Visualization
Add debug output to see how text is tokenized:
```csharp
var tokens = GetTokens(analyzer, "your code here");
foreach (var token in tokens)
{
    Console.WriteLine($"Token: '{token}'");
}
```

### 2. Analyzer Comparison
Compare CodeAnalyzer with StandardAnalyzer:
```csharp
var codeTokens = GetTokens(new CodeAnalyzer(...), text);
var standardTokens = GetTokens(new StandardAnalyzer(...), text);
// Compare differences
```

### 3. Performance Profiling
```csharp
var sw = Stopwatch.StartNew();
for (int i = 0; i < 1000; i++)
{
    var tokens = GetTokens(analyzer, sampleCode);
}
sw.Stop();
Console.WriteLine($"Avg time: {sw.ElapsedMilliseconds / 1000.0}ms");
```

## Known Limitations

1. **Context-Free Tokenization**: Doesn't understand language syntax fully
2. **Fixed Pattern Set**: New patterns require code changes
3. **Unicode Support**: Limited testing with non-ASCII characters
4. **Performance**: Slightly slower than StandardAnalyzer for large documents

## Future Improvements

1. **Pluggable Pattern Handlers**: Allow registering custom patterns
2. **Language-Aware Modes**: Different rules for different languages
3. **Streaming Optimization**: Better handling of very large files
4. **Configurable Operators**: Allow customizing recognized operators