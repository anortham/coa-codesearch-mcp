using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using TreeSitter;

namespace COA.CodeSearch.McpServer.Services.TypeExtraction;

/// <summary>
/// Analyzes Vue Single File Components (.vue) by extracting script blocks
/// and parsing them with TypeScript or JavaScript parsers
/// </summary>
public class VueFileAnalyzer : ILanguageFileAnalyzer
{
    private readonly ILogger<VueFileAnalyzer> _logger;
    private readonly ITypeExtractionService _typeExtractionService;
    
    // Regex patterns for extracting Vue script blocks
    private static readonly Regex ScriptBlockRegex = new(
        @"<script(?:\s+lang=[""']([^""']*)[""'])?(?:\s+setup)?[^>]*>(.*?)</script>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled
    );
    
    public string Language => "vue";
    
    public VueFileAnalyzer(ILogger<VueFileAnalyzer> logger, ITypeExtractionService typeExtractionService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _typeExtractionService = typeExtractionService ?? throw new ArgumentNullException(nameof(typeExtractionService));
    }
    
    public async Task<TypeExtractionResult> ExtractTypes(string content, string filePath)
    {
        try
        {
            var scriptBlock = ExtractScriptBlock(content);
            if (scriptBlock == null)
            {
                _logger.LogDebug("No script block found in Vue file: {FilePath}", filePath);
                return new TypeExtractionResult 
                { 
                    Success = true, 
                    Types = new List<TypeInfo>(),
                    Methods = new List<MethodInfo>(),
                    Language = Language 
                };
            }
            
            // Create a temporary file path with the appropriate extension for the embedded language
            var tempFilePath = scriptBlock.IsTypeScript 
                ? $"{filePath}.ts" 
                : $"{filePath}.js";
            
            // Use the existing type extraction service to parse the script content
            var scriptResult = await _typeExtractionService.ExtractTypes(scriptBlock.Content, tempFilePath);
            
            if (scriptResult.Success)
            {
                // Enhance the results with Vue-specific information
                var vueResult = new TypeExtractionResult
                {
                    Success = true,
                    Language = Language,
                    Types = EnhanceTypesWithVueInfo(scriptResult.Types, scriptBlock),
                    Methods = EnhanceMethodsWithVueInfo(scriptResult.Methods, scriptBlock)
                };
                
                // Add component-level information
                AddVueComponentInfo(vueResult, content, filePath);
                
                return vueResult;
            }
            else
            {
                _logger.LogDebug("Failed to extract types from Vue script block in: {FilePath}", filePath);
                return new TypeExtractionResult { Success = false, Language = Language };
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error analyzing Vue file: {FilePath}", filePath);
            return new TypeExtractionResult { Success = false, Language = Language };
        }
    }
    
    private ScriptBlock? ExtractScriptBlock(string vueContent)
    {
        var match = ScriptBlockRegex.Match(vueContent);
        if (!match.Success)
            return null;
        
        var langAttribute = match.Groups[1].Value;
        var scriptContent = match.Groups[2].Value.Trim();
        
        if (string.IsNullOrEmpty(scriptContent))
            return null;
        
        return new ScriptBlock
        {
            Content = scriptContent,
            IsTypeScript = IsTypeScriptLang(langAttribute, vueContent),
            IsSetup = vueContent.Contains("setup", StringComparison.OrdinalIgnoreCase)
        };
    }
    
    private static bool IsTypeScriptLang(string langAttribute, string content)
    {
        // Check explicit lang attribute
        if (!string.IsNullOrEmpty(langAttribute))
        {
            return langAttribute.Equals("ts", StringComparison.OrdinalIgnoreCase) ||
                   langAttribute.Equals("typescript", StringComparison.OrdinalIgnoreCase);
        }
        
        // Check for TypeScript indicators in the script tag
        return content.Contains("lang=\"ts\"", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("lang='ts'", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("lang=\"typescript\"", StringComparison.OrdinalIgnoreCase);
    }
    
    private List<TypeInfo> EnhanceTypesWithVueInfo(List<TypeInfo> types, ScriptBlock scriptBlock)
    {
        var enhancedTypes = new List<TypeInfo>();
        
        foreach (var type in types)
        {
            var enhanced = new TypeInfo
            {
                Name = type.Name,
                Kind = DetermineVueTypeKind(type, scriptBlock),
                Signature = type.Signature,
                Line = type.Line,
                Column = type.Column,
                Modifiers = type.Modifiers,
                BaseType = type.BaseType,
                Interfaces = type.Interfaces
            };
            
            enhancedTypes.Add(enhanced);
        }
        
        return enhancedTypes;
    }
    
    private List<MethodInfo> EnhanceMethodsWithVueInfo(List<MethodInfo> methods, ScriptBlock scriptBlock)
    {
        var enhancedMethods = new List<MethodInfo>();
        
        foreach (var method in methods)
        {
            var enhanced = new MethodInfo
            {
                Name = method.Name,
                Signature = method.Signature,
                ReturnType = method.ReturnType,
                Line = method.Line,
                Column = method.Column,
                ContainingType = method.ContainingType,
                Parameters = method.Parameters,
                Modifiers = DetermineVueMethodModifiers(method, scriptBlock)
            };
            
            enhancedMethods.Add(enhanced);
        }
        
        return enhancedMethods;
    }
    
    private string DetermineVueTypeKind(TypeInfo type, ScriptBlock scriptBlock)
    {
        // If it's a default export in Vue, it's likely a component
        if (type.Name == "default" || type.Signature?.Contains("export default") == true)
        {
            return scriptBlock.IsSetup ? "vue-setup-component" : "vue-component";
        }
        
        return type.Kind;
    }
    
    private List<string> DetermineVueMethodModifiers(MethodInfo method, ScriptBlock scriptBlock)
    {
        var modifiers = new List<string>(method.Modifiers);
        
        // Add Vue-specific context
        if (scriptBlock.IsSetup)
        {
            modifiers.Add("composition-api");
        }
        else
        {
            modifiers.Add("options-api");
        }
        
        return modifiers;
    }
    
    private void AddVueComponentInfo(TypeExtractionResult result, string content, string filePath)
    {
        // Add a synthetic type representing the Vue component itself
        var componentName = Path.GetFileNameWithoutExtension(filePath);
        
        var componentType = new TypeInfo
        {
            Name = componentName,
            Kind = "vue-file",
            Signature = $"Vue component: {componentName}",
            Line = 1,
            Column = 1,
            Modifiers = new List<string> { "vue", "component" }
        };
        
        result.Types.Insert(0, componentType);
    }
}

/// <summary>
/// Represents an extracted script block from a Vue file
/// </summary>
public class ScriptBlock
{
    public string Content { get; set; } = string.Empty;
    public bool IsTypeScript { get; set; }
    public bool IsSetup { get; set; }
}