using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using System.Text;

namespace COA.CodeSearch.McpServer.Services.TypeExtraction;

/// <summary>
/// Analyzes Razor files (.cshtml/.razor) by extracting @code, @functions blocks
/// and @model declarations, then parsing them with the C# parser
/// </summary>
public class RazorFileAnalyzer : ILanguageFileAnalyzer
{
    private readonly ILogger<RazorFileAnalyzer> _logger;
    private readonly ITypeExtractionService _typeExtractionService;
    
    // Regex patterns for extracting Razor C# code blocks
    private static readonly Regex CodeBlockRegex = new(
        @"@code\s*\{((?:[^{}]|{(?:[^{}]|{[^}]*})*})*)\}",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled
    );
    
    private static readonly Regex FunctionsBlockRegex = new(
        @"@functions\s*\{((?:[^{}]|{(?:[^{}]|{[^}]*})*})*)\}",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled
    );
    
    private static readonly Regex ModelRegex = new(
        @"@model\s+([^\r\n]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );
    
    private static readonly Regex InheritRegex = new(
        @"@inherits\s+([^\r\n]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );
    
    private static readonly Regex InlineCodeRegex = new(
        @"@\{([^}]*)\}",
        RegexOptions.Singleline | RegexOptions.Compiled
    );
    
    public string Language => "razor";
    
    public RazorFileAnalyzer(ILogger<RazorFileAnalyzer> logger, ITypeExtractionService typeExtractionService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _typeExtractionService = typeExtractionService ?? throw new ArgumentNullException(nameof(typeExtractionService));
    }
    
    public TypeExtractionResult ExtractTypes(string content, string filePath)
    {
        try
        {
            var codeBlocks = ExtractAllCodeBlocks(content);
            var modelInfo = ExtractModelInfo(content);
            
            if (codeBlocks.Count == 0 && modelInfo == null)
            {
                _logger.LogDebug("No C# code blocks or model found in Razor file: {FilePath}", filePath);
                return CreateEmptyResult();
            }
            
            // Combine all extracted C# code into a single compilation unit
            var combinedCode = CreateCombinedCSharpCode(codeBlocks, modelInfo, filePath);
            
            if (string.IsNullOrWhiteSpace(combinedCode))
            {
                return CreateEmptyResult();
            }
            
            // Create a temporary C# file path for parsing
            var tempCsFilePath = $"{filePath}.cs";
            
            // Use the existing type extraction service to parse the combined C# code
            var csharpResult = _typeExtractionService.ExtractTypes(combinedCode, tempCsFilePath);
            
            if (csharpResult.Success)
            {
                // Enhance the results with Razor-specific information
                var razorResult = new TypeExtractionResult
                {
                    Success = true,
                    Language = Language,
                    Types = EnhanceTypesWithRazorInfo(csharpResult.Types, filePath),
                    Methods = EnhanceMethodsWithRazorInfo(csharpResult.Methods, filePath)
                };
                
                // Add Razor page/component information
                AddRazorPageInfo(razorResult, content, filePath, modelInfo);
                
                return razorResult;
            }
            else
            {
                _logger.LogDebug("Failed to extract types from Razor C# code in: {FilePath}", filePath);
                return new TypeExtractionResult { Success = false, Language = Language };
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error analyzing Razor file: {FilePath}", filePath);
            return new TypeExtractionResult { Success = false, Language = Language };
        }
    }
    
    private List<CodeBlock> ExtractAllCodeBlocks(string content)
    {
        var blocks = new List<CodeBlock>();
        
        // Extract @code blocks
        var codeMatches = CodeBlockRegex.Matches(content);
        foreach (Match match in codeMatches)
        {
            blocks.Add(new CodeBlock
            {
                Content = match.Groups[1].Value.Trim(),
                Type = "code",
                Line = GetLineNumber(content, match.Index)
            });
        }
        
        // Extract @functions blocks
        var functionMatches = FunctionsBlockRegex.Matches(content);
        foreach (Match match in functionMatches)
        {
            blocks.Add(new CodeBlock
            {
                Content = match.Groups[1].Value.Trim(),
                Type = "functions",
                Line = GetLineNumber(content, match.Index)
            });
        }
        
        // Extract inline @{ } blocks (optional, might be too noisy)
        var inlineMatches = InlineCodeRegex.Matches(content);
        foreach (Match match in inlineMatches)
        {
            var inlineContent = match.Groups[1].Value.Trim();
            if (inlineContent.Length > 20) // Only include substantial inline code
            {
                blocks.Add(new CodeBlock
                {
                    Content = inlineContent,
                    Type = "inline",
                    Line = GetLineNumber(content, match.Index)
                });
            }
        }
        
        return blocks;
    }
    
    private ModelInfo? ExtractModelInfo(string content)
    {
        var modelMatch = ModelRegex.Match(content);
        if (modelMatch.Success)
        {
            return new ModelInfo
            {
                TypeName = modelMatch.Groups[1].Value.Trim(),
                Line = GetLineNumber(content, modelMatch.Index)
            };
        }
        
        var inheritMatch = InheritRegex.Match(content);
        if (inheritMatch.Success)
        {
            return new ModelInfo
            {
                TypeName = inheritMatch.Groups[1].Value.Trim(),
                Line = GetLineNumber(content, inheritMatch.Index),
                IsInherit = true
            };
        }
        
        return null;
    }
    
    private string CreateCombinedCSharpCode(List<CodeBlock> codeBlocks, ModelInfo? modelInfo, string filePath)
    {
        var sb = new StringBuilder();
        
        // Add using statements that are commonly needed in Razor files
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using Microsoft.AspNetCore.Mvc;");
        sb.AppendLine("using Microsoft.AspNetCore.Mvc.RazorPages;");
        sb.AppendLine();
        
        // Create a wrapper class representing the Razor page/component
        var className = Path.GetFileNameWithoutExtension(filePath).Replace(".", "_");
        sb.AppendLine($"public partial class {className}Model");
        sb.AppendLine("{");
        
        // Add model property if present
        if (modelInfo != null && !modelInfo.IsInherit)
        {
            sb.AppendLine($"    public {modelInfo.TypeName} Model {{ get; set; }}");
        }
        
        // Add all code blocks
        foreach (var block in codeBlocks)
        {
            sb.AppendLine($"    // From @{block.Type} block at line {block.Line}");
            sb.AppendLine(IndentCode(block.Content, 4));
            sb.AppendLine();
        }
        
        sb.AppendLine("}");
        
        return sb.ToString();
    }
    
    private static string IndentCode(string code, int spaces)
    {
        var indent = new string(' ', spaces);
        return string.Join("\n", code.Split('\n').Select(line => indent + line));
    }
    
    private static int GetLineNumber(string content, int index)
    {
        return content.Substring(0, index).Count(c => c == '\n') + 1;
    }
    
    private List<TypeInfo> EnhanceTypesWithRazorInfo(List<TypeInfo> types, string filePath)
    {
        var enhancedTypes = new List<TypeInfo>();
        
        foreach (var type in types)
        {
            var enhanced = new TypeInfo
            {
                Name = type.Name,
                Kind = DetermineRazorTypeKind(type, filePath),
                Signature = type.Signature,
                Line = type.Line,
                Column = type.Column,
                Modifiers = new List<string>(type.Modifiers) { "razor" },
                BaseType = type.BaseType,
                Interfaces = type.Interfaces
            };
            
            enhancedTypes.Add(enhanced);
        }
        
        return enhancedTypes;
    }
    
    private List<MethodInfo> EnhanceMethodsWithRazorInfo(List<MethodInfo> methods, string filePath)
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
                Modifiers = new List<string>(method.Modifiers) { "razor" }
            };
            
            enhancedMethods.Add(enhanced);
        }
        
        return enhancedMethods;
    }
    
    private string DetermineRazorTypeKind(TypeInfo type, string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        
        return extension switch
        {
            ".cshtml" => "razor-page",
            ".razor" => "blazor-component", 
            _ => type.Kind
        };
    }
    
    private void AddRazorPageInfo(TypeExtractionResult result, string content, string filePath, ModelInfo? modelInfo)
    {
        var pageName = Path.GetFileNameWithoutExtension(filePath);
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        
        var pageType = new TypeInfo
        {
            Name = pageName,
            Kind = extension == ".cshtml" ? "razor-page" : "blazor-component",
            Signature = $"{(extension == ".cshtml" ? "Razor Page" : "Blazor Component")}: {pageName}",
            Line = 1,
            Column = 1,
            Modifiers = new List<string> { "razor", extension.TrimStart('.') }
        };
        
        if (modelInfo != null)
        {
            pageType.BaseType = modelInfo.IsInherit ? $": {modelInfo.TypeName}" : null;
        }
        
        result.Types.Insert(0, pageType);
    }
    
    private TypeExtractionResult CreateEmptyResult()
    {
        return new TypeExtractionResult 
        { 
            Success = true, 
            Types = new List<TypeInfo>(),
            Methods = new List<MethodInfo>(),
            Language = Language 
        };
    }
}

/// <summary>
/// Represents an extracted C# code block from a Razor file
/// </summary>
public class CodeBlock
{
    public string Content { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // "code", "functions", "inline"
    public int Line { get; set; }
}

/// <summary>
/// Represents model information extracted from @model or @inherits directives
/// </summary>
public class ModelInfo
{
    public string TypeName { get; set; } = string.Empty;
    public int Line { get; set; }
    public bool IsInherit { get; set; }
}