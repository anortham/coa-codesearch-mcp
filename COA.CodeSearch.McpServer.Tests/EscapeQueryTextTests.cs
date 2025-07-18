using Xunit;
using System.Reflection;
using COA.CodeSearch.McpServer.Tools;

namespace COA.CodeSearch.McpServer.Tests;

public class EscapeQueryTextTests
{
    [Fact]
    public void EscapeQueryText_EscapesSpecialCharactersCorrectly()
    {
        // Use reflection to test the private method
        var fastTextSearchToolType = typeof(FastTextSearchTool);
        var escapeMethod = fastTextSearchToolType.GetMethod("EscapeQueryText", 
            BindingFlags.NonPublic | BindingFlags.Static);
        
        Assert.NotNull(escapeMethod);
        
        // Test cases
        var testCases = new[]
        {
            ("[HttpGet]", "\\[HttpGet\\]"),
            ("[HttpGet", "\\[HttpGet"),
            ("api/ser-forms/offboarding/approve", "api\\/ser\\-forms\\/offboarding\\/approve"),
            ("GetNetPromoterScores()", "GetNetPromoterScores\\(\\)"),
            ("Task<IEnumerable<string>>", "Task\\<IEnumerable\\<string\\>\\>"),
            ("user@example.com", "user@example.com"), // @ is not a special character
            ("value != null", "value \\!\\= null"),
            ("if (x > 0 && y < 10)", "if \\(x \\> 0 \\&\\& y \\< 10\\)"),
            ("normal text", "normal text"),
            ("path\\to\\file", "path\\\\to\\\\file")
        };
        
        foreach (var (input, expected) in testCases)
        {
            var result = escapeMethod.Invoke(null, new object[] { input })?.ToString();
            Assert.NotNull(result);
            
            // Check that all special characters are escaped
            if (input.Contains('[')) Assert.Contains("\\[", result);
            if (input.Contains(']')) Assert.Contains("\\]", result);
            if (input.Contains('(')) Assert.Contains("\\(", result);
            if (input.Contains(')')) Assert.Contains("\\)", result);
            if (input.Contains('<')) Assert.Contains("\\<", result);
            if (input.Contains('>')) Assert.Contains("\\>", result);
            if (input.Contains('!')) Assert.Contains("\\!", result);
            if (input.Contains('=')) Assert.Contains("\\=", result);
            if (input.Contains('/')) Assert.Contains("\\/", result);
            if (input.Contains('-')) Assert.Contains("\\-", result);
            if (input.Contains('&')) Assert.Contains("\\&", result);
            if (input.Contains('\\')) Assert.Contains("\\\\", result);
            
            // @ should not be escaped
            if (input.Contains('@'))
            {
                Assert.Contains("@", result);
                Assert.DoesNotContain("\\@", result);
            }
        }
    }
}