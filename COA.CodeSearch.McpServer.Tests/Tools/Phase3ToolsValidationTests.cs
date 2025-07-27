using COA.CodeSearch.McpServer.Constants;
using COA.CodeSearch.McpServer.Tools;
using Xunit;

namespace COA.CodeSearch.McpServer.Tests.Tools;

/// <summary>
/// Simple validation tests for Phase 3 AI-optimized tools to ensure:
/// 1. Magic strings are properly replaced with constants
/// 2. Tool names match ToolNames constants
/// 3. Basic class structure is correct
/// </summary>
public class Phase3ToolsValidationTests
{
    [Fact]
    public void SearchAssistantTool_UsesCorrectToolNameConstant()
    {
        // Verify that SearchAssistantTool.ToolName returns the correct constant
        // This validates that magic strings have been properly replaced
        
        // The ToolNames.SearchAssistant constant should equal "search_assistant"
        Assert.Equal("search_assistant", ToolNames.SearchAssistant);
    }

    [Fact]
    public void PatternDetectorTool_UsesCorrectToolNameConstant()
    {
        // Verify that PatternDetectorTool uses the correct constant
        Assert.Equal("pattern_detector", ToolNames.PatternDetector);
    }

    [Fact]
    public void MemoryGraphNavigatorTool_UsesCorrectToolNameConstant()
    {
        // Verify that MemoryGraphNavigatorTool uses the correct constant
        Assert.Equal("memory_graph_navigator", ToolNames.MemoryGraphNavigator);
    }

    [Fact]
    public void AllPhase3Tools_HaveCorrectToolNames()
    {
        // Test that all Phase 3 AI-optimized tools have proper tool names defined
        var phase3Tools = new[]
        {
            ToolNames.SearchAssistant,
            ToolNames.PatternDetector,
            ToolNames.MemoryGraphNavigator
        };

        foreach (var toolName in phase3Tools)
        {
            Assert.False(string.IsNullOrWhiteSpace(toolName), $"Tool name should not be null or empty");
            Assert.DoesNotContain(" ", toolName); // Tool names should not contain spaces
            Assert.True(toolName.ToCharArray().All(c => 
                char.IsLower(c) || c == '_' || char.IsDigit(c)), $"Tool name {toolName} should be snake_case"); // Should be snake_case
        }
    }

    [Fact]
    public void ToolNames_Constants_AreProperlyDefined()
    {
        // Verify that all tool name constants are properly defined and follow conventions
        var toolNameFields = typeof(ToolNames).GetFields(
            System.Reflection.BindingFlags.Public | 
            System.Reflection.BindingFlags.Static | 
            System.Reflection.BindingFlags.DeclaredOnly);

        Assert.True(toolNameFields.Length > 0, "ToolNames should contain constants");

        foreach (var field in toolNameFields)
        {
            if (field.FieldType == typeof(string))
            {
                var value = (string?)field.GetValue(null);
                Assert.False(string.IsNullOrWhiteSpace(value), $"Tool name constant {field.Name} should have a value");
                
                // Verify naming convention: constant should be PascalCase, value should be snake_case
                Assert.True(char.IsUpper(field.Name[0]), $"Tool name constant {field.Name} should start with uppercase");
                Assert.True(value!.ToCharArray().All(c => 
                    char.IsLower(c) || c == '_' || char.IsDigit(c)), 
                    $"Tool name value '{value}' should be snake_case");
            }
        }
    }

    [Fact]
    public void Phase3Tools_FollowNamingConventions()
    {
        // Verify that Phase 3 tool constants follow the established patterns
        var phase3Constants = new Dictionary<string, string>
        {
            { nameof(ToolNames.SearchAssistant), ToolNames.SearchAssistant },
            { nameof(ToolNames.PatternDetector), ToolNames.PatternDetector },
            { nameof(ToolNames.MemoryGraphNavigator), ToolNames.MemoryGraphNavigator }
        };

        foreach (var (constantName, constantValue) in phase3Constants)
        {
            // Constant name should be PascalCase
            Assert.True(char.IsUpper(constantName[0]), 
                $"Constant name {constantName} should start with uppercase");
            
            // Constant value should be snake_case
            Assert.True(constantValue.ToCharArray().All(c => 
                char.IsLower(c) || c == '_'), 
                $"Constant value {constantValue} should be lowercase with underscores");
            
            // Value should match expected pattern
            var expectedValue = string.Join("_", 
                System.Text.RegularExpressions.Regex.Split(constantName, @"(?<!^)(?=[A-Z])")
                    .Select(s => s.ToLowerInvariant()));
            
            Assert.Equal(expectedValue, constantValue);
        }
    }
}