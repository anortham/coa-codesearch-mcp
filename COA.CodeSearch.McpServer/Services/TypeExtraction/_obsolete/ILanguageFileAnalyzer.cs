namespace COA.CodeSearch.McpServer.Services.TypeExtraction;

/// <summary>
/// Interface for specialized analyzers that handle multi-language files
/// like Vue (.vue) and Razor (.cshtml/.razor) by extracting embedded code blocks
/// and parsing them with appropriate language-specific parsers
/// </summary>
public interface ILanguageFileAnalyzer
{
    /// <summary>
    /// The language this analyzer handles (e.g., "vue", "razor")
    /// </summary>
    string Language { get; }
    
    /// <summary>
    /// Extract type information from a multi-language file by parsing embedded code blocks
    /// </summary>
    /// <param name="content">Full file content</param>
    /// <param name="filePath">File path for context and logging</param>
    /// <returns>Type extraction result with discovered types and methods</returns>
    Task<TypeExtractionResult> ExtractTypes(string content, string filePath);
}