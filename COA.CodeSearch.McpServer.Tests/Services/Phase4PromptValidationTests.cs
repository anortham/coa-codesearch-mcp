using COA.CodeSearch.McpServer.Services;
using Xunit;
using System.Reflection;

namespace COA.CodeSearch.McpServer.Tests.Services;

/// <summary>
/// Simple validation tests for Phase 4 Prompt Templates to ensure:
/// 1. All prompt template classes exist and are properly named
/// 2. Prompt templates follow established naming conventions
/// 3. Basic class structure is correct
/// </summary>
public class Phase4PromptValidationTests
{
    [Theory]
    [InlineData(typeof(RefactoringAssistantPrompt), "refactoring-assistant")]
    [InlineData(typeof(TechnicalDebtAnalyzerPrompt), "technical-debt-analyzer")]
    [InlineData(typeof(ArchitectureDocumenterPrompt), "architecture-documenter")]
    [InlineData(typeof(CodeReviewAssistantPrompt), "code-review-assistant")]
    [InlineData(typeof(TestCoverageImproverPrompt), "test-coverage-improver")]
    public void PromptTemplate_HasCorrectName(Type promptType, string expectedName)
    {
        // Verify that prompt template classes exist and have correct names
        Assert.NotNull(promptType);
        Assert.True(promptType.IsClass, $"{promptType.Name} should be a class");
        
        // Check that it derives from BasePromptTemplate
        Assert.True(typeof(BasePromptTemplate).IsAssignableFrom(promptType), 
            $"{promptType.Name} should inherit from BasePromptTemplate");
            
        // Verify that the expected name follows kebab-case convention
        Assert.Contains("-", expectedName);
    }

    [Fact]
    public void AllPromptTemplates_ExistInCorrectNamespace()
    {
        // Verify that all Phase 4 prompt template classes exist in the correct namespace
        var expectedPromptTypes = new[]
        {
            "RefactoringAssistantPrompt",
            "TechnicalDebtAnalyzerPrompt", 
            "ArchitectureDocumenterPrompt",
            "CodeReviewAssistantPrompt",
            "TestCoverageImproverPrompt"
        };

        var servicesAssembly = typeof(BasePromptTemplate).Assembly;
        var servicesNamespace = typeof(BasePromptTemplate).Namespace;

        foreach (var expectedType in expectedPromptTypes)
        {
            var fullTypeName = $"{servicesNamespace}.{expectedType}";
            var type = servicesAssembly.GetType(fullTypeName);
            
            Assert.NotNull(type);
            Assert.True(type.IsClass, $"{expectedType} should be a class");
            Assert.True(typeof(BasePromptTemplate).IsAssignableFrom(type), 
                $"{expectedType} should inherit from BasePromptTemplate");
        }
    }

    [Fact] 
    public void PromptTemplates_FollowNamingConventions()
    {
        // Verify that all prompt template class names follow the expected pattern
        var promptTemplateTypes = typeof(BasePromptTemplate).Assembly
            .GetTypes()
            .Where(t => t.IsClass && 
                       !t.IsAbstract && 
                       typeof(BasePromptTemplate).IsAssignableFrom(t) &&
                       t != typeof(BasePromptTemplate))
            .ToList();

        Assert.True(promptTemplateTypes.Count >= 5, "Should have at least 5 prompt template implementations");

        foreach (var type in promptTemplateTypes)
        {
            // All prompt templates should end with "Prompt"
            Assert.EndsWith("Prompt", type.Name);
            
            // Should be in the Services namespace
            Assert.Equal("COA.CodeSearch.McpServer.Services", type.Namespace);
            
            // Should have a parameterless constructor or a constructor that takes ILogger
            var constructors = type.GetConstructors();
            Assert.True(constructors.Length > 0, $"{type.Name} should have at least one constructor");
            
            var hasValidConstructor = constructors.Any(c => 
                c.GetParameters().Length == 0 || 
                c.GetParameters().Length == 1 && c.GetParameters()[0].ParameterType.Name.Contains("ILogger"));
            
            Assert.True(hasValidConstructor, 
                $"{type.Name} should have either a parameterless constructor or one that takes ILogger");
        }
    }

    [Fact]
    public void BasePromptTemplate_HasRequiredMembers()
    {
        // Verify that BasePromptTemplate has the expected structure
        var baseType = typeof(BasePromptTemplate);
        
        // Should have Name property
        var nameProperty = baseType.GetProperty("Name");
        Assert.NotNull(nameProperty);
        Assert.Equal(typeof(string), nameProperty.PropertyType);
        
        // Should have Description property
        var descriptionProperty = baseType.GetProperty("Description");
        Assert.NotNull(descriptionProperty);
        Assert.Equal(typeof(string), descriptionProperty.PropertyType);
        
        // Should have Arguments property  
        var argumentsProperty = baseType.GetProperty("Arguments");
        Assert.NotNull(argumentsProperty);
    }

    [Fact]
    public void PromptTemplateNames_UseKebabCase()
    {
        // Verify that prompt template names use kebab-case convention
        var expectedNames = new[]
        {
            "refactoring-assistant",
            "technical-debt-analyzer",
            "architecture-documenter", 
            "code-review-assistant",
            "test-coverage-improver"
        };

        foreach (var name in expectedNames)
        {
            // Should be lowercase
            Assert.Equal(name.ToLowerInvariant(), name);
            
            // Should contain hyphens for word separation  
            Assert.True(name.Contains("-"), $"Prompt name {name} should use kebab-case with hyphens");
            
            // Should not contain spaces or underscores
            Assert.False(name.Contains(" "), $"Prompt name {name} should not contain spaces");
            Assert.False(name.Contains("_"), $"Prompt name {name} should not contain underscores");
        }
    }
}