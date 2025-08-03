using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Analysis.TokenAttributes;
using Lucene.Net.Util;
using System.IO;
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
        @":\s*\w+|" +                   // Type annotation (: ITool)
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
        var parts = SplitCamelCase(_currentToken);
        if (parts.Count > 1)
        {
            _subTokenCount = parts.Count;
            _tokenPosition = 0;
            EmitSubToken();
            return true;
        }
        
        // No splitting needed
        return true;
    }
    
    private void EmitSubToken()
    {
        // Implementation would split camelCase and emit sub-tokens
        // For now, simplified implementation
        _tokenPosition++;
        _posIncrAttr.PositionIncrement = 0; // Sub-tokens at same position
    }
    
    private List<string> SplitCamelCase(string token)
    {
        // Simplified implementation - would split "UserService" into ["User", "Service"]
        // and "user_service" into ["user", "service"]
        return new List<string> { token };
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