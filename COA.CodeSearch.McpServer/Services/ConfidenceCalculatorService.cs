using System.Text.RegularExpressions;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Service for calculating confidence scores for search results
/// </summary>
public class ConfidenceCalculatorService
{
    private readonly Dictionary<string, Regex> _definitionPatterns;
    private readonly Dictionary<string, Regex> _usagePatterns;

    public ConfidenceCalculatorService()
    {
        _definitionPatterns = new Dictionary<string, Regex>
        {
            ["class"] = new Regex(@"\b(public|private|protected|internal)?\s*(static|abstract|sealed)?\s*class\s+(\w+)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            ["interface"] = new Regex(@"\b(public|private|protected|internal)?\s*interface\s+(\w+)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            ["method"] = new Regex(@"\b(public|private|protected|internal)?\s*(static|virtual|abstract|override)?\s*\w+\s+(\w+)\s*\(", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            ["property"] = new Regex(@"\b(public|private|protected|internal)?\s*(static|virtual|abstract|override)?\s*\w+\s+(\w+)\s*{\s*(get|set)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            ["field"] = new Regex(@"\b(public|private|protected|internal)?\s*(static|readonly)?\s*\w+\s+(\w+)\s*[=;]", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            ["enum"] = new Regex(@"\b(public|private|protected|internal)?\s*enum\s+(\w+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)
        };

        _usagePatterns = new Dictionary<string, Regex>
        {
            ["instantiation"] = new Regex(@"\bnew\s+(\w+)\s*\(", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            ["variable"] = new Regex(@"\b(\w+)\s+(\w+)\s*[=;]", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            ["method_call"] = new Regex(@"(\w+)\.(\w+)\s*\(", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            ["inheritance"] = new Regex(@":\s*(\w+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)
        };
    }

    /// <summary>
    /// Calculate confidence score for a search result
    /// </summary>
    /// <param name="matchedLine">The line containing the match</param>
    /// <param name="query">The search query</param>
    /// <param name="symbolType">Expected symbol type (class, interface, method, etc.)</param>
    /// <param name="fileName">Name of the file</param>
    /// <returns>Confidence score from 0.0 to 1.0</returns>
    public double CalculateConfidence(string matchedLine, string query, string? symbolType = null, string? fileName = null)
    {
        if (string.IsNullOrWhiteSpace(matchedLine) || string.IsNullOrWhiteSpace(query))
            return 0.0;

        var baseScore = CalculateBaseScore(matchedLine, query, symbolType);
        var contextBonus = CalculateContextBonus(matchedLine, query, symbolType);
        var fileNameBonus = CalculateFileNameBonus(fileName, query);

        var totalScore = Math.Min(1.0, baseScore + contextBonus + fileNameBonus);
        return Math.Round(totalScore, 2);
    }

    private double CalculateBaseScore(string matchedLine, string query, string? symbolType)
    {
        var cleanLine = matchedLine.Trim();
        
        // Very high confidence: Definition line
        if (IsDefinitionLine(cleanLine, query, symbolType))
        {
            return 0.90;
        }

        // High confidence: Exact match at word boundary
        if (IsExactWordMatch(cleanLine, query))
        {
            return 0.75;
        }

        // Medium confidence: Contains the term
        if (cleanLine.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return 0.50;
        }

        // Low confidence: Partial match
        if (cleanLine.Contains(query.Substring(0, Math.Min(query.Length, 3)), StringComparison.OrdinalIgnoreCase))
        {
            return 0.25;
        }

        return 0.10;
    }

    private double CalculateContextBonus(string matchedLine, string query, string? symbolType)
    {
        double bonus = 0.0;
        var cleanLine = matchedLine.Trim();

        // Bonus for being in a comment (negative for definitions, positive for documentation searches)
        if (IsInComment(cleanLine))
        {
            bonus -= 0.20; // Usually not what we're looking for in symbol searches
        }

        // Bonus for correct usage patterns
        if (!string.IsNullOrEmpty(symbolType))
        {
            if (_usagePatterns.TryGetValue(symbolType.ToLower(), out var pattern))
            {
                if (pattern.IsMatch(cleanLine))
                {
                    bonus += 0.10;
                }
            }
        }

        // Bonus for being in a code block (not string literal)
        if (!IsInStringLiteral(cleanLine))
        {
            bonus += 0.05;
        }

        return bonus;
    }

    private double CalculateFileNameBonus(string? fileName, string query)
    {
        if (string.IsNullOrEmpty(fileName))
            return 0.0;

        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        
        // High bonus if file name matches or contains the query
        if (string.Equals(fileNameWithoutExtension, query, StringComparison.OrdinalIgnoreCase))
        {
            return 0.15;
        }

        if (fileNameWithoutExtension.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return 0.10;
        }

        // Small bonus if query is contained in file name
        if (query.Contains(fileNameWithoutExtension, StringComparison.OrdinalIgnoreCase))
        {
            return 0.05;
        }

        return 0.0;
    }

    private bool IsDefinitionLine(string line, string query, string? symbolType)
    {
        if (string.IsNullOrEmpty(symbolType))
        {
            // Check all definition patterns if no specific type
            return _definitionPatterns.Values.Any(pattern => 
            {
                var match = pattern.Match(line);
                return match.Success && match.Groups.Cast<Group>()
                    .Any(g => g.Value.Equals(query, StringComparison.OrdinalIgnoreCase));
            });
        }

        if (_definitionPatterns.TryGetValue(symbolType.ToLower(), out var definitionPattern))
        {
            var match = definitionPattern.Match(line);
            return match.Success && match.Groups.Cast<Group>()
                .Any(g => g.Value.Equals(query, StringComparison.OrdinalIgnoreCase));
        }

        return false;
    }

    private bool IsExactWordMatch(string line, string query)
    {
        var wordBoundaryPattern = new Regex($@"\b{Regex.Escape(query)}\b", RegexOptions.IgnoreCase);
        return wordBoundaryPattern.IsMatch(line);
    }

    private bool IsInComment(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.StartsWith("//") || 
               trimmed.StartsWith("/*") || 
               trimmed.StartsWith("*") ||
               trimmed.StartsWith("///");
    }

    private bool IsInStringLiteral(string line)
    {
        // Simple check - count quotes to see if we're likely inside a string
        // This is not perfect but good enough for confidence scoring
        var doubleQuotes = line.Count(c => c == '"' && !IsEscaped(line, line.IndexOf(c)));
        var singleQuotes = line.Count(c => c == '\'' && !IsEscaped(line, line.IndexOf(c)));
        
        return (doubleQuotes % 2 == 1) || (singleQuotes % 2 == 1);
    }

    private bool IsEscaped(string str, int index)
    {
        if (index == 0) return false;
        return str[index - 1] == '\\';
    }
}

/// <summary>
/// Interface for confidence calculation service
/// </summary>
public interface IConfidenceCalculatorService
{
    double CalculateConfidence(string matchedLine, string query, string? symbolType = null, string? fileName = null);
}