using System.Text.RegularExpressions;
using COA.CodeSearch.McpServer.Models;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Advanced pattern matcher that handles various matching modes for search and replace operations
/// Addresses common issues with exact whitespace matching and multi-line patterns
/// </summary>
public class AdvancedPatternMatcher
{
    /// <summary>
    /// Attempts to find a match using the specified mode and returns match information
    /// </summary>
    public MatchResult FindMatch(string content, string pattern, SearchAndReplaceParams parameters)
    {
        var mode = ParseMatchMode(parameters.MatchMode);
        
        return mode switch
        {
            SearchAndReplaceMode.Exact => FindExactMatch(content, pattern, parameters),
            SearchAndReplaceMode.WhitespaceInsensitive => FindWhitespaceInsensitiveMatch(content, pattern, parameters),
            SearchAndReplaceMode.MultiLine => FindMultiLineMatch(content, pattern, parameters),
            SearchAndReplaceMode.Fuzzy => FindFuzzyMatch(content, pattern, parameters),
            _ => FindExactMatch(content, pattern, parameters)
        };
    }

    /// <summary>
    /// Performs replacement using the appropriate matching mode
    /// </summary>
    public string PerformReplacement(string content, string pattern, string replacement, SearchAndReplaceParams parameters)
    {
        var matchResult = FindMatch(content, pattern, parameters);
        
        if (!matchResult.Found)
            return content;

        // Perform replacement based on the match result
        return content.Substring(0, matchResult.StartIndex) + 
               replacement + 
               content.Substring(matchResult.StartIndex + matchResult.Length);
    }

    /// <summary>
    /// Parse string match mode to enum
    /// </summary>
    private SearchAndReplaceMode ParseMatchMode(string mode)
    {
        return mode?.ToLowerInvariant() switch
        {
            "exact" => SearchAndReplaceMode.Exact,
            "whitespace_insensitive" => SearchAndReplaceMode.WhitespaceInsensitive,
            "multiline" => SearchAndReplaceMode.MultiLine,
            "fuzzy" => SearchAndReplaceMode.Fuzzy,
            _ => SearchAndReplaceMode.Exact
        };
    }

    /// <summary>
    /// Exact matching (current behavior)
    /// </summary>
    private MatchResult FindExactMatch(string content, string pattern, SearchAndReplaceParams parameters)
    {
        var comparison = parameters.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var index = content.IndexOf(pattern, comparison);
        
        return new MatchResult
        {
            Found = index >= 0,
            StartIndex = index >= 0 ? index : 0,
            Length = index >= 0 ? pattern.Length : 0,
            MatchedText = index >= 0 ? pattern : string.Empty,
            Mode = SearchAndReplaceMode.Exact
        };
    }

    /// <summary>
    /// Whitespace-insensitive matching - normalizes spaces and tabs
    /// </summary>
    private MatchResult FindWhitespaceInsensitiveMatch(string content, string pattern, SearchAndReplaceParams parameters)
    {
        var normalizedContent = NormalizeWhitespace(content);
        var normalizedPattern = NormalizeWhitespace(pattern);
        
        var comparison = parameters.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var normalizedIndex = normalizedContent.IndexOf(normalizedPattern, comparison);
        
        if (normalizedIndex < 0)
        {
            return new MatchResult { Found = false, Mode = SearchAndReplaceMode.WhitespaceInsensitive };
        }

        // Find the actual position in the original content
        var actualMatch = FindActualPosition(content, normalizedContent, normalizedIndex, normalizedPattern.Length);
        
        return new MatchResult
        {
            Found = true,
            StartIndex = actualMatch.Start,
            Length = actualMatch.Length,
            MatchedText = content.Substring(actualMatch.Start, actualMatch.Length),
            Mode = SearchAndReplaceMode.WhitespaceInsensitive
        };
    }

    /// <summary>
    /// Multi-line pattern support
    /// </summary>
    private MatchResult FindMultiLineMatch(string content, string pattern, SearchAndReplaceParams parameters)
    {
        // For multi-line, we can handle newlines and line breaks
        var regexOptions = RegexOptions.Multiline;
        if (!parameters.CaseSensitive)
            regexOptions |= RegexOptions.IgnoreCase;

        try
        {
            // Escape the pattern for regex but allow newlines
            var escapedPattern = Regex.Escape(pattern).Replace(@"\r\n", @"\s*\r?\n\s*").Replace(@"\n", @"\s*\r?\n\s*");
            var regex = new Regex(escapedPattern, regexOptions);
            var match = regex.Match(content);

            return new MatchResult
            {
                Found = match.Success,
                StartIndex = match.Success ? match.Index : 0,
                Length = match.Success ? match.Length : 0,
                MatchedText = match.Success ? match.Value : string.Empty,
                Mode = SearchAndReplaceMode.MultiLine
            };
        }
        catch (ArgumentException)
        {
            // Fallback to exact match if regex fails
            return FindExactMatch(content, pattern, parameters);
        }
    }

    /// <summary>
    /// Fuzzy matching - combines whitespace normalization with multi-line support
    /// </summary>
    private MatchResult FindFuzzyMatch(string content, string pattern, SearchAndReplaceParams parameters)
    {
        // First try whitespace insensitive
        var whitespaceResult = FindWhitespaceInsensitiveMatch(content, pattern, parameters);
        if (whitespaceResult.Found)
        {
            whitespaceResult.Mode = SearchAndReplaceMode.Fuzzy;
            return whitespaceResult;
        }

        // Then try multi-line
        var multilineResult = FindMultiLineMatch(content, pattern, parameters);
        if (multilineResult.Found)
        {
            multilineResult.Mode = SearchAndReplaceMode.Fuzzy;
            return multilineResult;
        }

        return new MatchResult { Found = false, Mode = SearchAndReplaceMode.Fuzzy };
    }

    /// <summary>
    /// Normalize whitespace by converting tabs to spaces and collapsing multiple spaces
    /// </summary>
    private string NormalizeWhitespace(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // Convert tabs to spaces, collapse multiple spaces, but preserve line breaks
        return Regex.Replace(text, @"[ \t]+", " ");
    }

    /// <summary>
    /// Find the actual position in original content based on normalized position
    /// </summary>
    private (int Start, int Length) FindActualPosition(string originalContent, string normalizedContent, int normalizedIndex, int normalizedLength)
    {
        // This is a simplified version - in practice, you'd want more sophisticated position mapping
        int originalPos = 0;
        int normalizedPos = 0;

        // Find start position
        while (normalizedPos < normalizedIndex && originalPos < originalContent.Length)
        {
            if (char.IsWhiteSpace(originalContent[originalPos]))
            {
                // Skip consecutive whitespace in original
                while (originalPos < originalContent.Length && char.IsWhiteSpace(originalContent[originalPos]))
                    originalPos++;
                normalizedPos++; // This represents the single space in normalized
            }
            else
            {
                originalPos++;
                normalizedPos++;
            }
        }

        int startPos = originalPos;
        
        // Find end position
        int remainingNormalizedLength = normalizedLength;
        while (remainingNormalizedLength > 0 && originalPos < originalContent.Length)
        {
            if (char.IsWhiteSpace(originalContent[originalPos]))
            {
                // Skip consecutive whitespace in original
                while (originalPos < originalContent.Length && char.IsWhiteSpace(originalContent[originalPos]))
                    originalPos++;
                remainingNormalizedLength--; // This represents the single space in normalized
            }
            else
            {
                originalPos++;
                remainingNormalizedLength--;
            }
        }

        return (startPos, originalPos - startPos);
    }
}

/// <summary>
/// Result of a pattern matching operation
/// </summary>
public class MatchResult
{
    /// <summary>
    /// Whether a match was found
    /// </summary>
    public bool Found { get; set; }

    /// <summary>
    /// Start index of the match in the original content
    /// </summary>
    public int StartIndex { get; set; }

    /// <summary>
    /// Length of the matched text in the original content
    /// </summary>
    public int Length { get; set; }

    /// <summary>
    /// The actual matched text from the original content
    /// </summary>
    public string MatchedText { get; set; } = string.Empty;

    /// <summary>
    /// The matching mode that was used
    /// </summary>
    public SearchAndReplaceMode Mode { get; set; }
}