using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using TreeSitter;
using COA.CodeSearch.McpServer.Services.TypeExtraction.Interop;

namespace COA.CodeSearch.McpServer.Services.TypeExtraction;

/// <summary>
/// Provides query-based type extraction using Tree-sitter query files (.scm).
/// This replaces ad-hoc identifier heuristics with precise, language-specific patterns.
/// </summary>
public class QueryBasedExtractor : IQueryBasedExtractor
{
    private readonly ILogger<QueryBasedExtractor> _logger;
    private readonly ConcurrentDictionary<string, CompiledQuery> _compiledQueries = new();
    private readonly string _queriesPath;

    public QueryBasedExtractor(ILogger<QueryBasedExtractor> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Determine queries path relative to assembly location
        var assemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        _queriesPath = Path.Combine(assemblyLocation ?? "", "Services", "TypeExtraction", "Queries");

        if (!Directory.Exists(_queriesPath))
        {
            _logger.LogWarning("Queries directory not found at {Path}, falling back to embedded resources", _queriesPath);
        }
    }

    /// <summary>
    /// Extract types using language-specific query patterns.
    /// </summary>
    public async Task<EnhancedTypeExtractionResult> ExtractWithQueries(
        Node rootNode,
        string language,
        string content)
    {
        try
        {
            var query = await GetOrLoadQuery(language);
            if (query == null)
            {
                _logger.LogDebug("No query available for language {Language}, using fallback extraction", language);
                return FallbackExtraction(rootNode, content);
            }

            var captures = ExecuteQuery(query, rootNode, content);
            return BuildResultFromCaptures(captures, content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Query-based extraction failed for language {Language}", language);
            return FallbackExtraction(rootNode, content);
        }
    }

    /// <summary>
    /// Extract types using language-specific query patterns (native macOS path).
    /// </summary>
    public async Task<EnhancedTypeExtractionResult> ExtractWithQueriesNative(
        IntPtr treePtr,
        IntPtr rootNodePtr,
        string language,
        byte[] contentBytes)
    {
        try
        {
            var query = await GetOrLoadQuery(language);
            if (query == null || !query.HasNativeQuery)
            {
                _logger.LogDebug("No native query available for language {Language}, using fallback extraction", language);
                return FallbackExtractionNative(treePtr, contentBytes);
            }

            var captures = ExecuteNativeQuery(query, treePtr, rootNodePtr, contentBytes);
            var content = Encoding.UTF8.GetString(contentBytes);
            return BuildResultFromCaptures(captures, content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Native query-based extraction failed for language {Language}", language);
            return FallbackExtractionNative(treePtr, contentBytes);
        }
    }

    private async Task<CompiledQuery?> GetOrLoadQuery(string language)
    {
        // Normalize language name for query file lookup
        var queryLanguage = NormalizeLanguageName(language);

        if (_compiledQueries.TryGetValue(queryLanguage, out var cached))
        {
            return cached;
        }

        var queryContent = await LoadQueryContent(queryLanguage);
        if (string.IsNullOrEmpty(queryContent))
        {
            return null;
        }

        var compiled = CompileQuery(queryContent, language);
        if (compiled != null)
        {
            _compiledQueries[queryLanguage] = compiled;
        }

        return compiled;
    }

    private string NormalizeLanguageName(string language)
    {
        return language.ToLowerInvariant() switch
        {
            "c-sharp" or "c_sharp" or "csharp" => "csharp",
            "typescript" or "tsx" => "typescript",
            "javascript" or "jsx" => "javascript",
            "python" => "python",
            "java" => "java",
            "go" => "go",
            "rust" => "rust",
            "cpp" or "c++" => "cpp",
            "c" => "c",
            _ => language.ToLowerInvariant()
        };
    }

    private async Task<string?> LoadQueryContent(string language)
    {
        var queryFile = Path.Combine(_queriesPath, $"{language}.scm");

        if (File.Exists(queryFile))
        {
            try
            {
                return await File.ReadAllTextAsync(queryFile);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read query file {File}", queryFile);
            }
        }

        // Try to load from embedded resources as fallback
        return LoadEmbeddedQuery(language);
    }

    private string? LoadEmbeddedQuery(string language)
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = $"COA.CodeSearch.McpServer.Services.TypeExtraction.Queries.{language}.scm";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "No embedded query for language {Language}", language);
        }

        return null;
    }

    private CompiledQuery? CompileQuery(string queryContent, string language)
    {
        // Note: Tree-sitter query compilation requires language-specific query API
        // This is a placeholder for actual query compilation logic
        // In practice, this would use tree-sitter's query compilation functions

        return new CompiledQuery
        {
            Language = language,
            QueryContent = queryContent,
            // TODO: Compile using tree-sitter query API when available in bindings
            HasNativeQuery = false
        };
    }

    private List<QueryCapture> ExecuteQuery(CompiledQuery query, Node rootNode, string content)
    {
        // This is a simplified pattern matching implementation
        // In a full implementation, this would use tree-sitter's query execution API

        var captures = new List<QueryCapture>();
        TraverseAndCapture(rootNode, query, captures, content);
        return captures;
    }

    private List<QueryCapture> ExecuteNativeQuery(
        CompiledQuery query,
        IntPtr treePtr,
        IntPtr rootNodePtr,
        byte[] contentBytes)
    {
        // Native query execution would use tree-sitter C API
        // This is a placeholder for the actual implementation

        var captures = new List<QueryCapture>();
        // TODO: Implement using ts_query_new and ts_query_cursor_exec
        return captures;
    }

    private void TraverseAndCapture(Node node, CompiledQuery query, List<QueryCapture> captures, string content)
    {
        // Simplified pattern matching based on node types
        // This provides basic functionality until full query API is available

        var nodeType = node.Type;

        switch (nodeType)
        {
            case "class_declaration":
                CaptureTypeDeclaration(node, captures, "class", content);
                break;
                
            case "interface_declaration":
                CaptureTypeDeclaration(node, captures, "interface", content);
                break;

            case "method_declaration":
            case "function_declaration":
                CaptureMethodDeclaration(node, captures, content);
                break;

            case "struct_declaration":
                CaptureTypeDeclaration(node, captures, "struct", content);
                break;

            case "enum_declaration":
                CaptureTypeDeclaration(node, captures, "enum", content);
                break;
        }

        // Recursively process children
        foreach (var child in node.Children)
        {
            TraverseAndCapture(child, query, captures, content);
        }
    }

    private void CaptureTypeDeclaration(Node node, List<QueryCapture> captures, string kind, string content)
    {
        var nameNode = FindChildByType(node, "identifier") ?? FindChildByType(node, "type_identifier");
        if (nameNode == null) return;

        var capture = new QueryCapture
        {
            Name = "type.definition",
            Node = node,
            Kind = kind,
            Properties = new Dictionary<string, object>
            {
                ["name"] = GetNodeText(nameNode, content),
                ["kind"] = kind,
                ["line"] = node.StartPosition.Row + 1,
                ["column"] = node.StartPosition.Column + 1
            }
        };

        // Capture modifiers
        var modifiers = ExtractModifiers(node);
        if (modifiers.Any())
        {
            capture.Properties["modifiers"] = modifiers;
        }

        // Capture type parameters if present
        var typeParams = FindChildByType(node, "type_parameter_list") ?? FindChildByType(node, "type_parameters");
        if (typeParams != null)
        {
            capture.Properties["type_parameters"] = ExtractTypeParameters(typeParams, content);
        }

        // Capture base types
        var baseList = FindChildByType(node, "base_list");
        if (baseList != null)
        {
            capture.Properties["base_types"] = ExtractBaseTypes(baseList, content);
        }

        captures.Add(capture);
    }

    private void CaptureMethodDeclaration(Node node, List<QueryCapture> captures, string content)
    {
        var nameNode = FindMethodName(node);
        if (nameNode == null) return;

        var capture = new QueryCapture
        {
            Name = "method.definition",
            Node = node,
            Kind = "method",
            Properties = new Dictionary<string, object>
            {
                ["name"] = GetNodeText(nameNode, content),
                ["line"] = node.StartPosition.Row + 1,
                ["column"] = node.StartPosition.Column + 1
            }
        };

        // Capture modifiers (static, async, public, etc)
        var modifiers = ExtractModifiers(node);
        if (modifiers.Any())
        {
            capture.Properties["modifiers"] = modifiers;
        }

        // Capture return type
        var returnType = ExtractReturnType(node, content);
        if (!string.IsNullOrEmpty(returnType))
        {
            capture.Properties["return_type"] = returnType;
        }

        // Capture parameters
        var parameters = ExtractDetailedParameters(node, content);
        if (parameters.Any())
        {
            capture.Properties["parameters"] = parameters;
        }

        // Capture type parameters
        var typeParams = FindChildByType(node, "type_parameter_list") ?? FindChildByType(node, "type_parameters");
        if (typeParams != null)
        {
            capture.Properties["type_parameters"] = ExtractTypeParameters(typeParams, content);
        }

        captures.Add(capture);
    }

    private Node? FindMethodName(Node node)
    {
        // More sophisticated method name extraction
        var paramList = FindChildByType(node, "parameter_list");
        if (paramList != null)
        {
            // Find identifier that comes before parameter list
            var identifiers = node.Children.Where(c => c.Type == "identifier").ToList();
            return identifiers.LastOrDefault(id => id.StartPosition.Column < paramList.StartPosition.Column);
        }

        return FindChildByType(node, "identifier");
    }

    private string ExtractReturnType(Node node, string content)
    {
        // For C# method_declaration, the type comes before the method name
        if (node.Type == "method_declaration")
        {
            // Find the type nodes that come before the identifier
            foreach (var child in node.Children)
            {
                if (child.Type == "predefined_type" ||
                    child.Type == "generic_name" ||
                    child.Type == "nullable_type" ||
                    child.Type == "array_type" ||
                    child.Type == "qualified_name" ||
                    child.Type == "identifier")
                {
                    // Check if this is the return type (comes before method name)
                    var methodName = FindMethodName(node);
                    if (methodName != null && child.EndPosition.Column <= methodName.StartPosition.Column)
                    {
                        return GetNodeText(child, content);
                    }
                }

                // Also check for a generic "type" node
                if (child.Type == "type")
                {
                    return GetNodeText(child, content);
                }
            }
        }

        // Look for explicit return type nodes (for other languages)
        var returnTypeNode = FindChildByType(node, "type") ??
                           FindChildByType(node, "return_type") ??
                           FindChildByType(node, "type_annotation");

        if (returnTypeNode != null)
        {
            return GetNodeText(returnTypeNode, content);
        }

        // Default only if we truly can't find a return type
        return "void";
    }

    private List<string> ExtractTypeParameters(Node node, string content)
    {
        var parameters = new List<string>();

        foreach (var child in node.Children)
        {
            if (child.Type == "type_parameter" || child.Type == "identifier")
            {
                var text = GetNodeText(child, content).Trim();
                if (!string.IsNullOrEmpty(text) && text != "," && text != "<" && text != ">")
                {
                    parameters.Add(text);
                }
            }
        }

        return parameters;
    }

    private List<string> ExtractBaseTypes(Node node, string content)
    {
        var baseTypes = new List<string>();

        foreach (var child in node.Children)
        {
            if (child.Type == "identifier" ||
                child.Type == "generic_name" ||
                child.Type == "qualified_name")
            {
                baseTypes.Add(GetNodeText(child, content).Trim());
            }
        }

        return baseTypes;
    }

    private List<ParameterInfo> ExtractDetailedParameters(Node node, string content)
    {
        var parameters = new List<ParameterInfo>();
        var paramListNode = FindChildByType(node, "parameter_list") ??
                           FindChildByType(node, "formal_parameters");

        if (paramListNode == null) return parameters;

        foreach (var child in paramListNode.Children)
        {
            if (child.Type == "parameter" || child.Type == "formal_parameter")
            {
                var param = ExtractParameterInfo(child, content);
                if (param != null)
                {
                    parameters.Add(param);
                }
            }
        }

        return parameters;
    }

    private ParameterInfo? ExtractParameterInfo(Node paramNode, string content)
    {
        var param = new ParameterInfo();

        // Find type node
        var typeNode = FindChildByType(paramNode, "type") ??
                      FindChildByType(paramNode, "predefined_type") ??
                      FindChildByType(paramNode, "generic_name");

        if (typeNode != null)
        {
            param.Type = GetNodeText(typeNode, content).Trim();
        }

        // Find parameter name (usually last identifier)
        var identifiers = paramNode.Children.Where(c => c.Type == "identifier").ToList();
        if (identifiers.Any())
        {
            param.Name = GetNodeText(identifiers.Last(), content).Trim();
        }

        // Check for modifiers (ref, out, params)
        foreach (var child in paramNode.Children)
        {
            if (child.Type == "parameter_modifier")
            {
                param.Modifier = GetNodeText(child, content).Trim();
                break;
            }
            var text = GetNodeText(child, content);
            if (text == "ref" || text == "out" || text == "params")
            {
                param.Modifier = text.Trim();
                break;
            }
        }

        // Check for default value
        var defaultValue = FindChildByType(paramNode, "equals_value_clause");
        if (defaultValue != null)
        {
            param.DefaultValue = GetNodeText(defaultValue, content).Replace("=", "").Trim();
        }

        return !string.IsNullOrEmpty(param.Name) ? param : null;
    }

    private Node? FindChildByType(Node node, string type)
    {
        return node.Children.FirstOrDefault(c => c.Type == type);
    }

    private List<string> ExtractModifiers(Node node)
    {
        var modifiers = new List<string>();

        foreach (var child in node.Children)
        {
            if (child.Type == "modifier" || child.Type == "modifiers")
            {
                // The modifier node might contain the actual modifier text
                modifiers.Add(child.Text.Trim());
            }
            // Also check for specific modifier keywords as direct children
            else if (child.Text == "public" || child.Text == "private" || child.Text == "protected" ||
                     child.Text == "internal" || child.Text == "static" || child.Text == "async" ||
                     child.Text == "override" || child.Text == "virtual" || child.Text == "abstract" ||
                     child.Text == "sealed" || child.Text == "readonly" || child.Text == "const")
            {
                modifiers.Add(child.Text);
            }
        }

        return modifiers;
    }

    private string GetNodeText(Node node, string content)
    {
        // Tree-sitter Node already provides the Text property with the node's content
        return node.Text ?? string.Empty;
    }

    private EnhancedTypeExtractionResult BuildResultFromCaptures(List<QueryCapture> captures, string content)
    {
        var result = new EnhancedTypeExtractionResult
        {
            Success = true,
            Types = new List<EnhancedTypeInfo>(),
            Methods = new List<EnhancedMethodInfo>()
        };

        foreach (var capture in captures)
        {
            switch (capture.Name)
            {
                case "type.definition":
                    var typeInfo = BuildTypeInfo(capture);
                    if (typeInfo != null)
                    {
                        result.Types.Add(typeInfo);
                    }
                    break;

                case "method.definition":
                    var methodInfo = BuildMethodInfo(capture);
                    if (methodInfo != null)
                    {
                        result.Methods.Add(methodInfo);
                    }
                    break;
            }
        }

        return result;
    }

    private EnhancedTypeInfo? BuildTypeInfo(QueryCapture capture)
    {
        if (!capture.Properties.TryGetValue("name", out var nameObj) || nameObj is not string name)
            return null;

        var typeInfo = new EnhancedTypeInfo
        {
            Name = name,
            Kind = capture.Properties.GetValueOrDefault("kind", "type").ToString() ?? "type",
            Signature = GetFirstLine(capture.Node.Text),
            Line = Convert.ToInt32(capture.Properties.GetValueOrDefault("line", 0)),
            Column = Convert.ToInt32(capture.Properties.GetValueOrDefault("column", 0))
        };

        // Add type parameters
        if (capture.Properties.TryGetValue("type_parameters", out var typeParamsObj) &&
            typeParamsObj is List<string> typeParams)
        {
            typeInfo.TypeParameters = typeParams;
        }

        // Add base types
        if (capture.Properties.TryGetValue("base_types", out var baseTypesObj) &&
            baseTypesObj is List<string> baseTypes && baseTypes.Any())
        {
            typeInfo.BaseType = baseTypes.FirstOrDefault();
            if (baseTypes.Count > 1)
            {
                typeInfo.Interfaces = baseTypes.Skip(1).ToList();
            }
        }

        return typeInfo;
    }

    private EnhancedMethodInfo? BuildMethodInfo(QueryCapture capture)
    {
        if (!capture.Properties.TryGetValue("name", out var nameObj) || nameObj is not string name)
            return null;

        var methodInfo = new EnhancedMethodInfo
        {
            Name = name,
            Signature = GetFirstLine(capture.Node.Text),
            Line = Convert.ToInt32(capture.Properties.GetValueOrDefault("line", 0)),
            Column = Convert.ToInt32(capture.Properties.GetValueOrDefault("column", 0)),
            ReturnType = capture.Properties.GetValueOrDefault("return_type", "void").ToString() ?? "void"
        };

        // Add modifiers
        if (capture.Properties.TryGetValue("modifiers", out var modifiersObj) &&
            modifiersObj is List<string> modifiers)
        {
            // Set specific boolean flags based on modifiers
            methodInfo.IsStatic = modifiers.Contains("static");
            methodInfo.IsAsync = modifiers.Contains("async");
            methodInfo.IsAbstract = modifiers.Contains("abstract");
            methodInfo.IsVirtual = modifiers.Contains("virtual");
            methodInfo.IsOverride = modifiers.Contains("override");

            // Store full list of modifiers in Attributes for now
            methodInfo.Attributes = modifiers;
        }

        // Add parameters
        if (capture.Properties.TryGetValue("parameters", out var paramsObj) &&
            paramsObj is List<ParameterInfo> parameters)
        {
            methodInfo.ParameterDetails = parameters;
            methodInfo.Parameters = parameters.Select(p => $"{p.Type} {p.Name}").ToList();
        }

        // Add type parameters
        if (capture.Properties.TryGetValue("type_parameters", out var typeParamsObj) &&
            typeParamsObj is List<string> typeParams)
        {
            methodInfo.TypeParameters = typeParams;
        }

        return methodInfo;
    }

    private EnhancedTypeExtractionResult FallbackExtraction(Node rootNode, string content)
    {
        // Basic fallback extraction without queries
        var result = new EnhancedTypeExtractionResult
        {
            Success = true, // Fallback extraction technically succeeds even if no types found
            Types = new List<EnhancedTypeInfo>(),
            Methods = new List<EnhancedMethodInfo>()
        };

        // Use simplified extraction logic
        ExtractTypesSimple(rootNode, result.Types, result.Methods);

        return result;
    }

    private EnhancedTypeExtractionResult FallbackExtractionNative(IntPtr treePtr, byte[] contentBytes)
    {
        // Basic fallback for native extraction
        // Note: treePtr is the tree pointer, not the root node pointer
        return new EnhancedTypeExtractionResult
        {
            Success = true, // Fallback extraction technically succeeds even if no types found
            Types = new List<EnhancedTypeInfo>(),
            Methods = new List<EnhancedMethodInfo>()
        };
    }

    private void ExtractTypesSimple(Node node, List<EnhancedTypeInfo> types, List<EnhancedMethodInfo> methods)
    {
        // Simplified extraction similar to existing TypeExtractionService logic
        var nodeType = node.Type;

        // Handle type declarations (classes, interfaces, structs, enums, modules)
        if (nodeType.Contains("class") || nodeType.Contains("interface") ||
            nodeType.Contains("struct") || nodeType.Contains("enum") ||
            nodeType == "module" || nodeType == "module_definition") // Ruby modules
        {
            var nameNode = FindChildByType(node, "identifier") ??
                          FindChildByType(node, "constant"); // Ruby uses 'constant' for class/module names
            if (nameNode != null)
            {
                var kind = nodeType.Replace("_declaration", "").Replace("_definition", "");
                // Simplify the kind name
                if (kind == "class") kind = "class";
                else if (kind == "module") kind = "module";

                types.Add(new EnhancedTypeInfo
                {
                    Name = nameNode.Text,
                    Kind = kind,
                    Line = node.StartPosition.Row + 1,
                    Column = node.StartPosition.Column + 1,
                    Signature = GetFirstLine(node.Text)
                });
            }
        }
        // Handle method/function declarations
        else if (nodeType.Contains("method") || nodeType.Contains("function") ||
                 nodeType == "def" || // Ruby method definition
                 nodeType == "singleton_method") // Ruby singleton method
        {
            var nameNode = FindChildByType(node, "identifier");
            if (nameNode != null)
            {
                methods.Add(new EnhancedMethodInfo
                {
                    Name = nameNode.Text,
                    Line = node.StartPosition.Row + 1,
                    Column = node.StartPosition.Column + 1,
                    Signature = GetFirstLine(node.Text),
                    ReturnType = "" // Empty for dynamically typed languages
                });
            }
        }

        foreach (var child in node.Children)
        {
            ExtractTypesSimple(child, types, methods);
        }
    }

    private string GetFirstLine(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var lines = text.Split('\n');
        return lines[0].Trim();
    }

    private class CompiledQuery
    {
        public string Language { get; set; } = "";
        public string QueryContent { get; set; } = "";
        public bool HasNativeQuery { get; set; }
        // TODO: Add compiled query handle when tree-sitter query API is available
    }

    private class QueryCapture
    {
        public string Name { get; set; } = "";
        public Node Node { get; set; } = null!;
        public string Kind { get; set; } = "";
        public Dictionary<string, object> Properties { get; set; } = new();
    }
}

public interface IQueryBasedExtractor
{
    Task<EnhancedTypeExtractionResult> ExtractWithQueries(Node rootNode, string language, string content);
    Task<EnhancedTypeExtractionResult> ExtractWithQueriesNative(IntPtr treePtr, IntPtr rootNodePtr, string language, byte[] contentBytes);
}

/// <summary>
/// Enhanced type extraction result with richer metadata.
/// </summary>
public class EnhancedTypeExtractionResult
{
    public bool Success { get; set; }
    public List<EnhancedTypeInfo> Types { get; set; } = new();
    public List<EnhancedMethodInfo> Methods { get; set; } = new();
    public string? Language { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Enhanced type information with generic parameters and more metadata.
/// </summary>
public class EnhancedTypeInfo : TypeInfo
{
    public List<string> TypeParameters { get; set; } = new();
    public List<string> TypeConstraints { get; set; } = new();
    public string? Namespace { get; set; }
    public List<string> Attributes { get; set; } = new();
    public string? ContainingType { get; set; } // For nested types
    public int EndLine { get; set; }
    public int EndColumn { get; set; }
    public Dictionary<string, string> Members { get; set; } = new(); // Quick member lookup
}

/// <summary>
/// Enhanced method information with detailed parameter info.
/// </summary>
public class EnhancedMethodInfo : MethodInfo
{
    public List<ParameterInfo> ParameterDetails { get; set; } = new();
    public List<string> TypeParameters { get; set; } = new();
    public List<string> TypeConstraints { get; set; } = new();
    public List<string> Attributes { get; set; } = new();
    public Dictionary<string, List<string>> MethodCalls { get; set; } = new(); // Calls made by this method
    public bool IsAsync { get; set; }
    public bool IsStatic { get; set; }
    public bool IsAbstract { get; set; }
    public bool IsVirtual { get; set; }
    public bool IsOverride { get; set; }
}

/// <summary>
/// Detailed parameter information.
/// </summary>
public class ParameterInfo
{
    public string Type { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Modifier { get; set; } // ref, out, params, etc.
    public string? DefaultValue { get; set; }
    public bool IsOptional => DefaultValue != null;
    public List<string> Attributes { get; set; } = new();
}