using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Analysis.TokenAttributes;
using Lucene.Net.Util;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace COA.CodeSearch.McpServer.Services.Analysis;

/// <summary>
/// Custom analyzer for source code that preserves programming language punctuation and structure.
/// Unlike StandardAnalyzer which treats punctuation as word boundaries, CodeAnalyzer keeps
/// code patterns intact (e.g., ": ITool", "[Fact]", "std::cout", "->method").
/// </summary>
public sealed class CodeAnalyzer : Analyzer
{
    private readonly bool _preserveCase;
    private readonly bool _splitCamelCase;
    private readonly LuceneVersion _version;

    public CodeAnalyzer(LuceneVersion version, bool preserveCase = false, bool splitCamelCase = true)
    {
        _version = version;
        _preserveCase = preserveCase;
        _splitCamelCase = splitCamelCase;
    }

    protected override TokenStreamComponents CreateComponents(string fieldName, TextReader reader)
    {
        return fieldName switch
        {
            "content" => CreateStandardCodeTokenization(reader),          // General search with code-aware tokenization
            "content_symbols" => CreateSymbolOnlyTokenization(reader),    // Symbol-only search (identifiers, class names)
            "content_patterns" => CreatePatternPreservingTokenization(reader), // Pattern-preserving search (special chars)
            _ => CreateStandardCodeTokenization(reader)                   // Default fallback
        };
    }

    private TokenStreamComponents CreateStandardCodeTokenization(TextReader reader)
    {
        // Use our custom tokenizer that preserves code structure
        var tokenizer = new CodeTokenizer(_version, reader);
        
        TokenStream stream = tokenizer;
        
        // Optional: Split camelCase and snake_case for better searchability
        if (_splitCamelCase)
        {
            stream = new CamelCaseFilter(stream);
        }
        
        // Preserve or lowercase based on configuration
        if (!_preserveCase)
        {
            stream = new LowerCaseFilter(_version, stream);
        }
        
        // Remove very short tokens (single characters except operators)
        stream = new CodeLengthFilter(stream, minLength: 1);
        
        return new TokenStreamComponents(tokenizer, stream);
    }

    /// <summary>
    /// Creates tokenization for pattern-preserving search that maintains special characters
    /// Uses WhitespaceTokenizer to split on whitespace while preserving punctuation patterns
    /// </summary>
    private TokenStreamComponents CreatePatternPreservingTokenization(TextReader reader)
    {
        // Use WhitespaceTokenizer to preserve special character patterns while splitting on whitespace
        var tokenizer = new WhitespaceTokenizer(_version, reader);
        
        TokenStream stream = tokenizer;
        
        // Apply minimal processing to preserve patterns like "IRepository<T>", ": ITool"
        if (!_preserveCase)
        {
            stream = new LowerCaseFilter(_version, stream);
        }
        
        // No length filtering - keep all tokens including short ones with special chars
        return new TokenStreamComponents(tokenizer, stream);
    }


        private TokenStreamComponents CreateSymbolOnlyTokenization(TextReader reader)
        {
            // For symbol search - use standard tokenizer but with minimal filtering
            var tokenizer = new StandardTokenizer(_version, reader);
            
            TokenStream stream = tokenizer;
            
            // Always split camelCase for symbol search
            stream = new CamelCaseFilter(stream);
            
            // Always lowercase for symbol search consistency
            stream = new LowerCaseFilter(_version, stream);
            
            // Filter out very short tokens and non-alphanumeric
            stream = new CodeLengthFilter(stream, minLength: 2);
            
            return new TokenStreamComponents(tokenizer, stream);
        }
}

/// <summary>
/// Custom tokenizer that recognizes and preserves code patterns.
/// It keeps together code constructs like ": ITool", "std::cout", "[Attribute]", etc.
/// </summary>
public sealed class CodeTokenizer : Tokenizer
{
    private readonly LuceneVersion _version;
    private readonly ICharTermAttribute _termAttr;
    private readonly IOffsetAttribute _offsetAttr;
    private readonly ITypeAttribute _typeAttr;
    
    private readonly char[] _buffer = new char[4096];
    private int _bufferSize = 0;
    private int _bufferPosition = 0;
    private int _tokenStart = 0;
    private int _offset = 0;
    
    // Regex patterns for code constructs
    private static readonly Regex CodePatternRegex = new Regex(
        @"(::\w+|" +                    // C++ namespace (::std)
        @":\s*\w+(<[^>]+>)?|" +         // Type annotation with optional generics (: ITool, : IRepository<T>)
        @"->\w*|" +                     // Pointer access (->method)
        @"=>\s*|" +                     // Lambda arrow
        @"\.\.\.|" +                    // Spread operator
        @"\?\.|" +                      // Optional chaining
        @"\?\?|" +                      // Null coalescing
        @"<-|" +                        // Go channel
        @"\|>|" +                       // Pipe operator
        @":=|" +                        // Go assignment
        @"\[\w+\]|" +                   // Attributes ([Fact])
        @"@\w+|" +                      // Decorators (@property)
        @"#\[\w+\]|" +                  // Rust attributes
        @"\w+<[^>]+>|" +                // Generics (List<string>)
        @"\w+(::\w+)*|" +               // Qualified names
        @"\w+(\.\w+)*|" +               // Dotted names
        @"\w+)",                        // Simple identifiers
        RegexOptions.Compiled);

    public CodeTokenizer(LuceneVersion version, TextReader reader) : base(reader)
    {
        _version = version;
        _termAttr = AddAttribute<ICharTermAttribute>();
        _offsetAttr = AddAttribute<IOffsetAttribute>();
        _typeAttr = AddAttribute<ITypeAttribute>();
    }

    public override bool IncrementToken()
    {
        ClearAttributes();
        
        while (true)
        {
            // Fill buffer if needed
            if (_bufferPosition >= _bufferSize)
            {
                _bufferSize = m_input.Read(_buffer, 0, _buffer.Length);
                if (_bufferSize <= 0)
                {
                    return false; // End of input
                }
                _bufferPosition = 0;
            }
            
            // Skip whitespace
            while (_bufferPosition < _bufferSize && char.IsWhiteSpace(_buffer[_bufferPosition]))
            {
                _offset++;
                _bufferPosition++;
            }
            
            if (_bufferPosition >= _bufferSize)
            {
                continue; // Need more input
            }
            
            // Start of a token
            _tokenStart = _offset;
            var tokenBuilder = new System.Text.StringBuilder();
            
            // Get the first character
            var firstCh = _buffer[_bufferPosition];
            tokenBuilder.Append(firstCh);
            _bufferPosition++;
            _offset++;
            
            // Handle special patterns based on first character
            if (firstCh == ':' && _bufferPosition < _bufferSize)
            {
                // Handle ": Type" pattern - look ahead for whitespace followed by identifier
                var lookAhead = _bufferPosition;
                while (lookAhead < _bufferSize && char.IsWhiteSpace(_buffer[lookAhead]))
                {
                    tokenBuilder.Append(_buffer[lookAhead]);
                    lookAhead++;
                }
                
                // If we found whitespace and there's more content, check if it's an identifier
                if (lookAhead < _bufferSize && lookAhead > _bufferPosition && IsTokenChar(_buffer[lookAhead]))
                {
                    // Consume the whitespace
                    while (_bufferPosition < lookAhead)
                    {
                        _bufferPosition++;
                        _offset++;
                    }
                    
                    // Consume the identifier
                    while (_bufferPosition < _bufferSize && IsTokenChar(_buffer[_bufferPosition]))
                    {
                        tokenBuilder.Append(_buffer[_bufferPosition]);
                        _bufferPosition++;
                        _offset++;
                    }
                    
                    // Check for generic type parameters after the identifier
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
                }
                else if (_bufferPosition < _bufferSize && _buffer[_bufferPosition] == ':')
                {
                    // Handle "::" pattern
                    tokenBuilder.Append(':');
                    _bufferPosition++;
                    _offset++;
                    
                    // Continue with identifier if present
                    while (_bufferPosition < _bufferSize && IsTokenChar(_buffer[_bufferPosition]))
                    {
                        tokenBuilder.Append(_buffer[_bufferPosition]);
                        _bufferPosition++;
                        _offset++;
                    }
                }
            }
            else if (firstCh == '-' && _bufferPosition < _bufferSize && _buffer[_bufferPosition] == '>')
            {
                // Handle "->" pattern
                tokenBuilder.Append('>');
                _bufferPosition++;
                _offset++;
                
                // Continue with identifier if present
                while (_bufferPosition < _bufferSize && IsTokenChar(_buffer[_bufferPosition]))
                {
                    tokenBuilder.Append(_buffer[_bufferPosition]);
                    _bufferPosition++;
                    _offset++;
                }
            }
            else if (firstCh == '[' && _bufferPosition < _bufferSize)
            {
                // Handle "[Attribute]" pattern
                while (_bufferPosition < _bufferSize && _buffer[_bufferPosition] != ']')
                {
                    tokenBuilder.Append(_buffer[_bufferPosition]);
                    _bufferPosition++;
                    _offset++;
                }
                
                // Include the closing bracket
                if (_bufferPosition < _bufferSize && _buffer[_bufferPosition] == ']')
                {
                    tokenBuilder.Append(']');
                    _bufferPosition++;
                    _offset++;
                }
            }
            else if (firstCh == '@' && _bufferPosition < _bufferSize)
            {
                // Handle "@decorator" pattern
                while (_bufferPosition < _bufferSize && IsTokenChar(_buffer[_bufferPosition]))
                {
                    tokenBuilder.Append(_buffer[_bufferPosition]);
                    _bufferPosition++;
                    _offset++;
                }
            }
            else if (IsTokenChar(firstCh))
            {
                // Regular identifier - continue until we hit non-token char
                while (_bufferPosition < _bufferSize && IsTokenChar(_buffer[_bufferPosition]))
                {
                    tokenBuilder.Append(_buffer[_bufferPosition]);
                    _bufferPosition++;
                    _offset++;
                }
                
                // Check for generic types
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
                // Check for namespace/member access patterns
                else if (_bufferPosition + 1 < _bufferSize && _buffer[_bufferPosition] == ':' && _buffer[_bufferPosition + 1] == ':')
                {
                    // Handle "::" pattern
                    tokenBuilder.Append("::");
                    _bufferPosition += 2;
                    _offset += 2;
                    
                    // Continue with next identifier
                    while (_bufferPosition < _bufferSize && IsTokenChar(_buffer[_bufferPosition]))
                    {
                        tokenBuilder.Append(_buffer[_bufferPosition]);
                        _bufferPosition++;
                        _offset++;
                    }
                }
            }
            else if (IsOperatorChar(firstCh))
            {
                // Handle operators - check for multi-character operators
                while (_bufferPosition < _bufferSize)
                {
                    var currentToken = tokenBuilder.ToString();
                    if (_bufferPosition >= _bufferSize) break;
                    
                    var nextCh = _buffer[_bufferPosition];
                    var potentialToken = currentToken + nextCh;
                    
                    if (IsKnownOperator(potentialToken))
                    {
                        tokenBuilder.Append(nextCh);
                        _bufferPosition++;
                        _offset++;
                    }
                    else
                    {
                        break;
                    }
                }
            }
            
            var token = tokenBuilder.ToString();
            if (token.Length > 0)
            {
                _termAttr.SetEmpty().Append(token);
                _offsetAttr.SetOffset(_tokenStart, _offset);
                _typeAttr.Type = DetermineTokenType(token);
                return true;
            }
        }
    }
    
    private bool IsTokenChar(char c)
    {
        return char.IsLetterOrDigit(c) || c == '_';
    }
    
    private bool IsOperatorChar(char c)
    {
        return c == ':' || c == '-' || c == '>' || c == '=' || c == '.' || 
               c == '?' || c == '<' || c == '|' || c == '[' || c == ']' ||
               c == '@' || c == '#' || c == '(' || c == ')' || c == '{' || 
               c == '}' || c == '*' || c == '&' || c == '!' || c == '~' ||
               c == '+' || c == '/' || c == '\\' || c == '^' || c == '%';
    }
    
    private bool IsKnownOperator(string op)
    {
        return op switch
        {
            "::" => true,
            "->" => true,
            "=>" => true,
            "..." => true,
            ".." => true,
            "?." => true,
            "??" => true,
            "<-" => true,
            "|>" => true,
            ":=" => true,
            ">=" => true,
            "<=" => true,
            "==" => true,
            "!=" => true,
            "&&" => true,
            "||" => true,
            "++" => true,
            "--" => true,
            "+=" => true,
            "-=" => true,
            "*=" => true,
            "/=" => true,
            "<<" => true,
            ">>" => true,
            "<<<" => true,
            ">>>" => true,
            // Single character operators
            "(" => true,
            ")" => true,
            "{" => true,
            "}" => true,
            "[" => true,
            "]" => true,
            ";" => true,
            "," => true,
            "." => true,
            ":" => true,
            "!" => true,
            "~" => true,
            "@" => true,
            "#" => true,
            "$" => true,
            "%" => true,
            "^" => true,
            "&" => true,
            "*" => true,
            "-" => true,
            "+" => true,
            "=" => true,
            "|" => true,
            "\\" => true,
            "/" => true,
            "?" => true,
            "<" => true,
            ">" => true,
            _ => false
        };
    }
    
    private string DetermineTokenType(string token)
    {
        if (IsKnownOperator(token))
            return "OPERATOR";
        if (token.StartsWith("@") || token.StartsWith("[") && token.EndsWith("]"))
            return "ANNOTATION";
        if (token.Contains("::") || token.Contains("."))
            return "QUALIFIED_NAME";
        if (token.Contains("<") && token.Contains(">"))
            return "GENERIC_TYPE";
        if (token.StartsWith(":"))
            return "TYPE_ANNOTATION";
        return "IDENTIFIER";
    }
    
    public override void Reset()
    {
        base.Reset();
        _bufferSize = 0;
        _bufferPosition = 0;
        _offset = 0;
    }
}

/// <summary>
/// Filter that splits camelCase and snake_case identifiers into multiple tokens.
/// This allows searching for "Service" to find "UserService" or "user_service".
/// </summary>
public sealed class CamelCaseFilter : TokenFilter
{
    private readonly ICharTermAttribute _termAttr;
    private readonly IOffsetAttribute _offsetAttr;
    private readonly ITypeAttribute _typeAttr;
    private readonly IPositionIncrementAttribute _posIncrAttr;
    
    private string? _currentToken;
    private int _tokenPosition;
    private int _subTokenCount;
    private int _startOffset;
    private int _endOffset;
    private string? _tokenType;
    private List<string>? _splitParts;

    public CamelCaseFilter(TokenStream input) : base(input)
    {
        _termAttr = AddAttribute<ICharTermAttribute>();
        _offsetAttr = AddAttribute<IOffsetAttribute>();
        _typeAttr = AddAttribute<ITypeAttribute>();
        _posIncrAttr = AddAttribute<IPositionIncrementAttribute>();
    }

    public override bool IncrementToken()
    {
        // If we have sub-tokens to emit, emit them
        if (_currentToken != null && _tokenPosition < _subTokenCount)
        {
            EmitSubToken();
            return true;
        }
        
        // Get next token from input
        if (!m_input.IncrementToken())
        {
            return false;
        }
        
        // Save original token attributes
        _currentToken = _termAttr.ToString();
        _startOffset = _offsetAttr.StartOffset;
        _endOffset = _offsetAttr.EndOffset;
        _tokenType = _typeAttr.Type;
        
        // Don't split operators or annotations
        if (_tokenType == "OPERATOR" || _tokenType == "ANNOTATION")
        {
            return true;
        }
        
        // Split the token
        _splitParts = SplitCamelCase(_currentToken);
        if (_splitParts.Count > 1)
        {
            _subTokenCount = _splitParts.Count;
            _tokenPosition = 0;
            EmitSubToken();
            return true;
        }
        
        // No splitting needed - emit original token as-is
        return true;
    }
    
    private void EmitSubToken()
    {
        if (_splitParts == null || _tokenPosition >= _splitParts.Count)
            return;
            
        var currentPart = _splitParts[_tokenPosition];
        
        // Set the term to the current split part
        _termAttr.SetEmpty();
        _termAttr.Append(currentPart);
        
        // Set position increment - 1 for first token (original), 0 for subsequent split tokens
        _posIncrAttr.PositionIncrement = _tokenPosition == 0 ? 1 : 0;
        
        // Keep the same offset and type for all split tokens
        _offsetAttr.SetOffset(_startOffset, _endOffset);
        _typeAttr.Type = _tokenType ?? "WORD";
        
        _tokenPosition++;
    }
    
    private List<string> SplitCamelCase(string token)
    {
        var parts = new List<string>();
        
        if (string.IsNullOrEmpty(token))
            return parts;
            
        // Always include the original token first for exact searches
        parts.Add(token);
        
        var splitTokens = new List<string>();
        
        // Handle generic types - extract the base type name first
        if (token.Contains('<') && token.Contains('>'))
        {
            var angleIndex = token.IndexOf('<');
            var closingAngleIndex = token.LastIndexOf('>');
            
            // Validate that we have a proper generic type structure
            if (angleIndex > 0 && closingAngleIndex > angleIndex)
            {
                var baseTypeName = token.Substring(0, angleIndex);
                // Split the base type name (e.g., "McpToolBase" -> ["Mcp", "Tool", "Base"])
                var baseTypeParts = SplitCamelCasePattern(baseTypeName);
                splitTokens.AddRange(baseTypeParts);
                
                // Also add the full base type name if it's not already included
                if (!splitTokens.Contains(baseTypeName))
                {
                    splitTokens.Add(baseTypeName);
                }
                
                // Extract and split generic parameter names (e.g., "TParams, TResult" -> ["TParams", "TResult"])
                var length = closingAngleIndex - angleIndex - 1;
                if (length > 0)
                {
                    var genericPart = token.Substring(angleIndex + 1, length);
                    var genericParams = genericPart.Split(',', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var param in genericParams)
                    {
                        var cleanParam = param.Trim();
                        if (!string.IsNullOrEmpty(cleanParam))
                        {
                            splitTokens.Add(cleanParam);
                            // Also split the generic parameter itself if it's CamelCase
                            var paramParts = SplitCamelCasePattern(cleanParam);
                            splitTokens.AddRange(paramParts);
                        }
                    }
                }
            }
        }
        // Handle snake_case and kebab-case
        else if (token.Contains('_') || token.Contains('-'))
        {
            var separators = new[] { '_', '-' };
            var snakeParts = token.Split(separators, StringSplitOptions.RemoveEmptyEntries);
            splitTokens.AddRange(snakeParts);
        }
        else
        {
            // Handle CamelCase and PascalCase
            splitTokens = SplitCamelCasePattern(token);
        }
        
        // Add split tokens if they differ from the original
        foreach (var splitToken in splitTokens)
        {
            if (!string.IsNullOrEmpty(splitToken) && splitToken != token)
            {
                parts.Add(splitToken);
            }
        }
        
        return parts;
    }
    
    private List<string> SplitCamelCasePattern(string token)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        
        for (int i = 0; i < token.Length; i++)
        {
            char c = token[i];
            
            if (i > 0 && char.IsUpper(c))
            {
                // Split on uppercase letters, but handle sequences like "XMLParser" -> ["XML", "Parser"]
                if (current.Length > 0)
                {
                    // Check if we have a sequence of uppercase letters
                    if (i + 1 < token.Length && char.IsLower(token[i + 1]) && current.Length > 1)
                    {
                        // "XMLParser" case: keep "XML" separate from "Parser"
                        var lastChar = current[current.Length - 1];
                        current.Length--;
                        if (current.Length > 0)
                        {
                            parts.Add(current.ToString());
                        }
                        current.Clear();
                        current.Append(lastChar);
                    }
                    else
                    {
                        // Normal case: "UserService" -> ["User", "Service"]
                        parts.Add(current.ToString());
                        current.Clear();
                    }
                }
            }
            else if (char.IsDigit(c) && current.Length > 0 && !char.IsDigit(current[current.Length - 1]))
            {
                // Split on number boundaries: "OAuth2Provider" -> ["OAuth", "2", "Provider"]
                parts.Add(current.ToString());
                current.Clear();
            }
            else if (!char.IsDigit(c) && current.Length > 0 && char.IsDigit(current[current.Length - 1]))
            {
                // Split after numbers: "2Provider" -> ["2", "Provider"]  
                parts.Add(current.ToString());
                current.Clear();
            }
            
            current.Append(c);
        }
        
        if (current.Length > 0)
        {
            parts.Add(current.ToString());
        }
        
        return parts;
    }
}

/// <summary>
/// Filter that removes very short tokens except for important operators.
/// </summary>
public sealed class CodeLengthFilter : TokenFilter
{
    private readonly int _minLength;
    private readonly ICharTermAttribute _termAttr;
    private readonly ITypeAttribute _typeAttr;
    private readonly IPositionIncrementAttribute _posIncrAttr;

    public CodeLengthFilter(TokenStream input, int minLength) : base(input)
    {
        _minLength = minLength;
        _termAttr = AddAttribute<ICharTermAttribute>();
        _typeAttr = AddAttribute<ITypeAttribute>();
        _posIncrAttr = AddAttribute<IPositionIncrementAttribute>();
    }

    public override bool IncrementToken()
    {
        // Skip tokens that don't meet our criteria
        while (m_input.IncrementToken())
        {
            var term = _termAttr.ToString();
            var type = _typeAttr.Type;
            
            // Always keep operators and annotations
            if (type == "OPERATOR" || type == "ANNOTATION")
            {
                return true;
            }
            
            // Filter by length for other tokens
            if (term.Length >= _minLength)
            {
                return true;
            }
            
            // Skip this token and continue to the next
        }
        
        return false; // No more tokens
    }
}
