using Microsoft.Extensions.Logging;
using TreeSitter;
using System.Text.Json;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Text;
using COA.CodeSearch.McpServer.Services.TypeExtraction.Interop;

namespace COA.CodeSearch.McpServer.Services.TypeExtraction;

public class TypeExtractionService : ITypeExtractionService
{
    private readonly ILogger<TypeExtractionService> _logger;
    private readonly ILanguageRegistry _languageRegistry;
    private readonly Dictionary<string, ILanguageFileAnalyzer> _specializedAnalyzers;
    
    private static readonly Dictionary<string, string> ExtensionToLanguage = new()
    {
        { ".cs", "c-sharp" },
        { ".ts", "typescript" }, 
        { ".tsx", "tsx" },
        { ".js", "javascript" },
        { ".jsx", "javascript" },
        { ".py", "python" },
        { ".java", "java" },
        { ".go", "go" },
        { ".rs", "rust" },
        { ".cpp", "cpp" },
        { ".cc", "cpp" },
        { ".cxx", "cpp" },
        { ".c", "c" },
        { ".h", "c" },
        { ".hpp", "cpp" },
        { ".rb", "ruby" },
        { ".php", "php" },
        { ".swift", "swift" },
        { ".scala", "scala" },
        { ".html", "html" },
        { ".htm", "html" },
        { ".css", "css" },
        { ".scss", "css" },
        { ".json", "json" },
        { ".jsonc", "json" },
        { ".toml", "toml" },
        { ".jl", "julia" },
        { ".hs", "haskell" },
        { ".ml", "ocaml" },
        { ".mli", "ocaml" },
        { ".v", "verilog" },
        { ".vh", "verilog" },
        { ".sv", "verilog" },
        { ".bash", "bash" },
        { ".sh", "bash" },
        { ".vue", "vue" },
        { ".cshtml", "razor" },
        { ".razor", "razor" }
        // Note: Kotlin, R, Objective-C, Lua, Dart, Zig, Elm, Clojure, Elixir 
        // don't have tree-sitter DLLs available in the current build
        // Note: Vue and Razor use specialized multi-language extractors
    };

    
    public TypeExtractionService(ILogger<TypeExtractionService> logger, ILanguageRegistry languageRegistry)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _languageRegistry = languageRegistry ?? throw new ArgumentNullException(nameof(languageRegistry));

        // Initialize specialized analyzers - they will use this service for embedded parsing
        _specializedAnalyzers = new Dictionary<string, ILanguageFileAnalyzer>();
        InitializeSpecializedAnalyzers();
    }
    
    private void InitializeSpecializedAnalyzers()
    {
        // Create analyzers that will delegate back to this service for embedded language parsing
        var vueLogger = _logger as ILogger<VueFileAnalyzer> ?? 
            Microsoft.Extensions.Logging.Abstractions.NullLogger<VueFileAnalyzer>.Instance;
        var razorLogger = _logger as ILogger<RazorFileAnalyzer> ?? 
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RazorFileAnalyzer>.Instance;
            
        _specializedAnalyzers["vue"] = new VueFileAnalyzer(vueLogger, this);
        _specializedAnalyzers["razor"] = new RazorFileAnalyzer(razorLogger, this);
    }
    
    public async Task<TypeExtractionResult> ExtractTypes(string content, string filePath)
    {
        // Handle empty content case - this is considered successful with no types/methods
        if (string.IsNullOrWhiteSpace(content))
        {
            return new TypeExtractionResult
            {
                Success = true,
                Types = new List<TypeInfo>(),
                Methods = new List<MethodInfo>(),
                Language = Path.GetExtension(filePath).ToLowerInvariant().TrimStart('.')
            };
        }

        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        
        if (!ExtensionToLanguage.TryGetValue(extension, out var languageName))
        {
            return new TypeExtractionResult { Success = false };
        }
        
        // Check if we have a specialized analyzer for this language (but avoid recursion for embedded files)
        var fileExtension = Path.GetExtension(filePath).ToLowerInvariant();
        if (_specializedAnalyzers.TryGetValue(languageName, out var analyzer) &&
            fileExtension != ".ts" && fileExtension != ".js" && fileExtension != ".cs")
        {
            try
            {
                return await analyzer.ExtractTypes(content, filePath);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Specialized analyzer failed for {Language} file {FilePath}, falling back to tree-sitter", languageName, filePath);
                // Fall through to tree-sitter parsing
            }
        }
        
        try
        {
            return await ExtractTypesWithLanguageRegistry(content, filePath, languageName);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to extract types from {FilePath}", filePath);
            return new TypeExtractionResult { Success = false };
        }
    }

    private async Task<TypeExtractionResult> ExtractTypesWithLanguageRegistry(string content, string filePath, string languageName)
    {
        // Check if this language is supported by the registry
        if (!_languageRegistry.IsLanguageSupported(languageName))
        {
            _logger.LogDebug("Language {Language} is not supported by Tree-sitter for file {FilePath}", languageName, filePath);
            return new TypeExtractionResult { Success = false };
        }

        // Handle languages that don't have tree-sitter libraries
        if (languageName == "razor")
        {
            // Razor files should be handled by RazorFileAnalyzer, not tree-sitter
            _logger.LogDebug("Razor file {FilePath} reached tree-sitter fallback - specialized analyzer should handle this", filePath);
            return new TypeExtractionResult { Success = false };
        }

        // Get the cached language handle
        var languageHandle = await _languageRegistry.GetLanguageHandleAsync(languageName);
        if (languageHandle == null)
        {
            _logger.LogDebug("Failed to get language handle for {Language} for file {FilePath}", languageName, filePath);
            return new TypeExtractionResult { Success = false };
        }

        try
        {
            var types = new List<TypeInfo>();
            var methods = new List<MethodInfo>();

            if (languageHandle.IsNative)
            {
                // Use native macOS path
                var bytes = Encoding.UTF8.GetBytes(content);
                var tree = TreeSitterNative.ts_parser_parse_string_encoding(
                    languageHandle.NativeParser,
                    IntPtr.Zero,
                    bytes,
                    (uint)bytes.Length,
                    TreeSitterNative.TSInputEncoding.UTF8);

                if (tree == IntPtr.Zero)
                {
                    _logger.LogDebug("Failed to parse file {FilePath}", filePath);
                    return new TypeExtractionResult { Success = false };
                }

                try
                {
                    var root = TreeSitterNative.ts_tree_root_node(tree);
                    var adapter = new NativeNodeAdapter(root, bytes);
                    ExtractFromNodeNative(adapter, types, methods);
                }
                finally
                {
                    TreeSitterNative.ts_tree_delete(tree);
                }
            }
            else
            {
                // Use managed TreeSitter.DotNet path
                if (languageHandle.Parser == null)
                {
                    _logger.LogWarning("Language handle for {Language} has null parser", languageName);
                    return new TypeExtractionResult { Success = false };
                }

                using var tree = languageHandle.Parser.Parse(content);
                if (tree == null)
                {
                    _logger.LogDebug("Failed to parse file {FilePath}", filePath);
                    return new TypeExtractionResult { Success = false };
                }

                ExtractFromNode(tree.RootNode, types, methods);
            }

            return new TypeExtractionResult
            {
                Success = true,
                Types = types,
                Methods = methods,
                Language = languageName
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during type extraction for {Language} file {FilePath}", languageName, filePath);
            return new TypeExtractionResult { Success = false };
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr LanguageFactory();

    private sealed class NativeNodeAdapter
    {
        private readonly TreeSitterNative.TSNode _node;
        private readonly byte[] _bytes;
        private readonly NativeNodeAdapter? _parent;

        public NativeNodeAdapter(TreeSitterNative.TSNode node, byte[] bytes, NativeNodeAdapter? parent = null)
        {
            _node = node;
            _bytes = bytes;
            _parent = parent;
        }

        public string Type
        {
            get
            {
                var ptr = TreeSitterNative.ts_node_type(_node);
                return Marshal.PtrToStringUTF8(ptr) ?? string.Empty;
            }
        }

        public (int Row, int Column) StartPosition
        {
            get
            {
                var p = TreeSitterNative.ts_node_start_point(_node);
                return ((int)p.row, (int)p.column);
            }
        }

        public string Text
        {
            get
            {
                var start = (int)TreeSitterNative.ts_node_start_byte(_node);
                var end = (int)TreeSitterNative.ts_node_end_byte(_node);
                if (start < 0 || end < 0 || start > end || end > _bytes.Length) return string.Empty;
                return Encoding.UTF8.GetString(_bytes, start, end - start);
            }
        }

        public IEnumerable<NativeNodeAdapter> Children
        {
            get
            {
                var count = TreeSitterNative.ts_node_child_count(_node);
                for (uint i = 0; i < count; i++)
                {
                    var child = TreeSitterNative.ts_node_child(_node, i);
                    yield return new NativeNodeAdapter(child, _bytes, this);
                }
            }
        }

        public NativeNodeAdapter? Parent => _parent;
    }

    
    private void ExtractFromNode(Node node, List<TypeInfo> types, List<MethodInfo> methods)
    {
        if (node == null) return;
        
        var nodeType = node.Type;
        
        switch (nodeType)
        {
            // C#, Java, TypeScript, JavaScript, Swift classes and types
            case "class_declaration":
            case "interface_declaration":
            case "struct_declaration":
            case "enum_declaration":
            case "protocol_declaration":  // Swift protocols
            // Python classes and types
            case "class_definition":        
            case "decorated_definition":    
            // Rust types
            case "struct_item":
            case "enum_item":
            case "union_item":
            case "trait_item":
            case "impl_item":
            // Go types
            case "type_declaration":
            case "type_spec":
            // Ruby classes and modules
            case "class":
            case "module":
            case "singleton_class":
                ExtractTypeDeclaration(node, types);
                break;
                
            // C#, Java, TypeScript, JavaScript, Swift functions
            case "method_declaration":
            case "function_declaration":
            case "function_definition":
            case "arrow_function":
            case "method_definition":
            case "init_declaration":      // Swift initializers
            // Rust functions
            case "function_item":
            case "associated_function":
            // Ruby methods
            case "method":
            case "singleton_method":
                ExtractMethodDeclaration(node, methods);
                break;
                
            case "type_alias_declaration":
            case "type_definition":
                ExtractTypeAlias(node, types);
                break;
        }
        
        // Recursively process children using the Children collection
        foreach (var child in node.Children)
        {
            ExtractFromNode(child, types, methods);
        }
    }

    // Native traversal path (macOS)
    private void ExtractFromNodeNative(NativeNodeAdapter node, List<TypeInfo> types, List<MethodInfo> methods)
    {
        var nodeType = node.Type;

        switch (nodeType)
        {
            // C#, Java, TypeScript, JavaScript, Swift classes and types
            case "class_declaration":
            case "interface_declaration":
            case "struct_declaration":
            case "enum_declaration":
            case "protocol_declaration":  // Swift protocols
            // Python classes and types
            case "class_definition":        
            case "decorated_definition":    
            // Rust types
            case "struct_item":
            case "enum_item":
            case "union_item":
            case "trait_item":
            case "impl_item":
            // Go types
            case "type_declaration":
            case "type_spec":
            // Ruby classes and modules
            case "class":
            case "module":
            case "singleton_class":
                ExtractTypeDeclarationNative(node, types);
                break;

            // C#, Java, TypeScript, JavaScript, Swift functions
            case "method_declaration":
            case "function_declaration":
            case "function_definition":
            case "arrow_function":
            case "method_definition":
            case "init_declaration":      // Swift initializers
            // Rust functions
            case "function_item":
            case "associated_function":
            // Ruby methods
            case "method":
            case "singleton_method":
                ExtractMethodDeclarationNative(node, methods);
                break;

            case "type_alias_declaration":
            case "type_definition":
                ExtractTypeAliasNative(node, types);
                break;
        }

        foreach (var child in node.Children)
        {
            ExtractFromNodeNative(child, types, methods);
        }
    }

    private void ExtractTypeDeclarationNative(NativeNodeAdapter node, List<TypeInfo> types)
    {
        var nameNode = FindChildByTypeNative(node, "identifier") ?? FindChildByTypeNative(node, "type_identifier");
        if (nameNode == null) return;

        var name = nameNode.Text;
        var startPos = node.StartPosition;

        var typeInfo = new TypeInfo
        {
            Name = name,
            Kind = node.Type.Replace("_declaration", "").Replace("_", " "),
            Line = startPos.Row + 1,
            Column = startPos.Column + 1,
            Signature = GetFirstLine(nameNode.Text),
            Modifiers = new List<string>()
        };

        types.Add(typeInfo);
    }

    private void ExtractMethodDeclarationNative(NativeNodeAdapter node, List<MethodInfo> methods)
    {
        // For C# methods, we need to find the right identifier
        // Pattern: [modifiers] return_type METHOD_NAME(parameters)
        // We want METHOD_NAME, not the return type identifier
        
        NativeNodeAdapter? nameNode = null;
        var identifiers = node.Children.Where(c => c.Type == "identifier").ToList();
        
        if (identifiers.Count > 1)
        {
            // If there are multiple identifiers, the last one before the parameter list is likely the method name
            var paramList = FindChildByTypeNative(node, "parameter_list");
            if (paramList != null)
            {
                // Find the identifier that comes right before the parameter list
                nameNode = identifiers.LastOrDefault(id => id.StartPosition.Column < paramList.StartPosition.Column);
            }
            else
            {
                // Fallback: use the last identifier
                nameNode = identifiers.LastOrDefault();
            }
        }
        else
        {
            // Single identifier or fallback
            nameNode = FindChildByTypeNative(node, "identifier") ?? FindChildByTypeNative(node, "property_identifier");
        }
        
        if (nameNode == null) return;

        var startPos = node.StartPosition;
        var methodInfo = new MethodInfo
        {
            Name = nameNode.Text,
            Line = startPos.Row + 1,
            Column = startPos.Column + 1,
            Signature = GetFirstLine(node.Text),
            Modifiers = ExtractModifiersNative(node),
            Parameters = ExtractParametersNative(node),
            ReturnType = ExtractReturnTypeNative(node)
        };

        // Find containing type
        var parentClass = FindParentTypeNative(node);
        if (parentClass != null)
        {
            var parentNameNode = FindChildByTypeNative(parentClass, "identifier");
            if (parentNameNode != null)
            {
                methodInfo.ContainingType = parentNameNode.Text;
            }
        }

        methods.Add(methodInfo);
    }

    private void ExtractTypeAliasNative(NativeNodeAdapter node, List<TypeInfo> types)
    {
        var nameNode = FindChildByTypeNative(node, "identifier") ?? FindChildByTypeNative(node, "type_identifier");
        if (nameNode == null) return;

        var startPos = node.StartPosition;
        types.Add(new TypeInfo
        {
            Name = nameNode.Text,
            Kind = "type",
            Line = startPos.Row + 1,
            Column = startPos.Column + 1,
            Signature = GetFirstLine(node.Text),
            Modifiers = new List<string>()
        });
    }

    private NativeNodeAdapter? FindChildByTypeNative(NativeNodeAdapter node, string type)
    {
        foreach (var child in node.Children)
        {
            if (child.Type == type) return child;
        }
        return null;
    }

    private NativeNodeAdapter? FindParentTypeNative(NativeNodeAdapter node)
    {
        var current = node.Parent;
        while (current != null)
        {
            var nodeType = current.Type;
            if (nodeType == "class_declaration" || 
                nodeType == "interface_declaration" || 
                nodeType == "struct_declaration" ||
                nodeType == "enum_declaration" ||
                nodeType == "namespace_declaration")
            {
                return current;
            }
            current = current.Parent;
        }
        return null;
    }

    private List<string> ExtractModifiersNative(NativeNodeAdapter node)
    {
        var modifiers = new List<string>();
        
        foreach (var child in node.Children)
        {
            var childType = child.Type;
            if (childType == "modifiers" || childType == "modifier" || 
                childType == "public" || childType == "private" || childType == "protected" ||
                childType == "static" || childType == "async" || childType == "abstract" ||
                childType == "readonly" || childType == "const" || childType == "final")
            {
                modifiers.Add(child.Text);
            }
        }
        
        return modifiers;
    }

    private List<string> ExtractParametersNative(NativeNodeAdapter node)
    {
        var parameters = new List<string>();
        
        var parameterList = FindChildByTypeNative(node, "parameter_list");
        if (parameterList != null)
        {
            foreach (var child in parameterList.Children)
            {
                if (child.Type == "parameter" || child.Type == "formal_parameter")
                {
                    parameters.Add(child.Text.Trim());
                }
            }
        }
        
        return parameters;
    }

    private string ExtractReturnTypeNative(NativeNodeAdapter node)
    {
        // For C# method_declaration, the return type comes before the identifier
        // Structure is typically: [modifiers] return_type method_name(parameters)
        if (node.Type == "method_declaration")
        {
            // Find the identifier (method name)
            var identifierNode = FindChildByTypeNative(node, "identifier");
            if (identifierNode != null)
            {
                // Look for type nodes that come before the identifier
                foreach (var child in node.Children)
                {
                    // Stop when we reach the identifier (comparing by position since we can't compare objects directly)
                    if (child.StartPosition.Column >= identifierNode.StartPosition.Column &&
                        child.StartPosition.Row >= identifierNode.StartPosition.Row) break;
                    
                    // Check for various type nodes
                    if (child.Type == "predefined_type" || 
                        child.Type == "identifier" && child.StartPosition.Column != identifierNode.StartPosition.Column ||
                        child.Type == "generic_name" ||
                        child.Type == "nullable_type" ||
                        child.Type == "array_type" ||
                        child.Type == "qualified_name")
                    {
                        return child.Text.Trim();
                    }
                }
            }
            
            // For async methods that return Task (not Task<T>), check if async modifier is present
            var modifiers = ExtractModifiersNative(node);
            if (modifiers.Contains("async"))
            {
                // No return type found but it's async, so it returns Task
                return "Task";
            }
        }
        
        // Fallback for other node types (TypeScript, etc.)
        var returnTypeNode = FindChildByTypeNative(node, "type") ?? 
                            FindChildByTypeNative(node, "return_type") ??
                            FindChildByTypeNative(node, "type_annotation");
        
        if (returnTypeNode != null)
        {
            return returnTypeNode.Text.Trim();
        }
        
        return "void";
    }
    
    private void ExtractTypeDeclaration(Node node, List<TypeInfo> types)
    {
        var nameNode = FindChildByType(node, "identifier") ?? FindChildByType(node, "type_identifier");
        if (nameNode == null) return;
        
        var name = nameNode.Text;
        var startPos = node.StartPosition;
        
        var typeInfo = new TypeInfo
        {
            Name = name,
            Kind = node.Type.Replace("_declaration", "").Replace("_", " "),
            Line = startPos.Row + 1,
            Column = startPos.Column + 1,
            Signature = GetFirstLine(node.Text),
            Modifiers = ExtractModifiers(node)
        };
        
        var baseTypeNode = FindChildByType(node, "base_list") ?? FindChildByType(node, "superclass");
        if (baseTypeNode != null)
        {
            typeInfo.BaseType = baseTypeNode.Text.Trim();
        }
        
        types.Add(typeInfo);
    }
    
    private void ExtractMethodDeclaration(Node node, List<MethodInfo> methods)
    {
        // For C# methods, we need to find the right identifier
        // Pattern: [modifiers] return_type METHOD_NAME(parameters)
        // We want METHOD_NAME, not the return type identifier
        
        Node? nameNode = null;
        var identifiers = node.Children.Where(c => c.Type == "identifier").ToList();
        
        if (identifiers.Count > 1)
        {
            // If there are multiple identifiers, the last one before the parameter list is likely the method name
            var paramList = FindChildByType(node, "parameter_list");
            if (paramList != null)
            {
                // Find the identifier that comes right before the parameter list
                nameNode = identifiers.LastOrDefault(id => id.StartPosition.Column < paramList.StartPosition.Column);
            }
            else
            {
                // Fallback: use the last identifier
                nameNode = identifiers.LastOrDefault();
            }
        }
        else
        {
            // Single identifier or fallback
            nameNode = FindChildByType(node, "identifier") ?? FindChildByType(node, "property_identifier");
        }
        
        if (nameNode == null) return;
        
        var name = nameNode.Text;
        var startPos = node.StartPosition;
        
        var methodInfo = new MethodInfo
        {
            Name = name,
            Line = startPos.Row + 1,
            Column = startPos.Column + 1,
            Signature = GetFirstLine(node.Text),
            Modifiers = ExtractModifiers(node),
            Parameters = ExtractParameters(node),
            ReturnType = ExtractReturnType(node)
        };
        
        var parentClass = FindParentType(node);
        if (parentClass != null)
        {
            var parentNameNode = FindChildByType(parentClass, "identifier");
            if (parentNameNode != null)
            {
                methodInfo.ContainingType = parentNameNode.Text;
            }
        }
        
        methods.Add(methodInfo);
    }
    
    private void ExtractTypeAlias(Node node, List<TypeInfo> types)
    {
        var nameNode = FindChildByType(node, "identifier") ?? FindChildByType(node, "type_identifier");
        if (nameNode == null) return;
        
        var name = nameNode.Text;
        var startPos = node.StartPosition;
        
        types.Add(new TypeInfo
        {
            Name = name,
            Kind = "type",
            Line = startPos.Row + 1,
            Column = startPos.Column + 1,
            Signature = GetFirstLine(node.Text),
            Modifiers = new List<string>()
        });
    }
    
    private List<string> ExtractModifiers(Node node)
    {
        var modifiers = new List<string>();
        
        foreach (var child in node.Children)
        {
            var childType = child.Type;
            if (childType == "modifiers" || childType == "modifier" || 
                childType == "public" || childType == "private" || childType == "protected" ||
                childType == "static" || childType == "async" || childType == "abstract" ||
                childType == "readonly" || childType == "const" || childType == "final")
            {
                modifiers.Add(child.Text);
            }
        }
        
        return modifiers;
    }
    
    private List<string> ExtractParameters(Node node)
    {
        var parameters = new List<string>();
        var paramListNode = FindChildByType(node, "parameter_list") ?? 
                           FindChildByType(node, "formal_parameters") ??
                           FindChildByType(node, "parameters");
        
        if (paramListNode != null)
        {
            foreach (var child in paramListNode.Children)
            {
                if (child.Type.Contains("parameter") || child.Type == "identifier")
                {
                    var paramText = child.Text.Trim();
                    if (!string.IsNullOrEmpty(paramText) && paramText != "," && paramText != "(" && paramText != ")")
                    {
                        parameters.Add(paramText);
                    }
                }
            }
        }
        
        return parameters;
    }
    
    private string ExtractReturnType(Node node)
    {
        // For C# method_declaration, the return type comes before the identifier
        // Structure is typically: [modifiers] return_type method_name(parameters)
        if (node.Type == "method_declaration")
        {
            // Find the parameter list to help identify the method name
            var parameterList = FindChildByType(node, "parameter_list");
            Node? methodNameIdentifier = null;
            
            if (parameterList != null)
            {
                // Find the identifier that comes right before the parameter list
                var identifiers = node.Children.Where(c => c.Type == "identifier").ToList();
                methodNameIdentifier = identifiers.LastOrDefault(id => 
                    id.StartPosition.Column < parameterList.StartPosition.Column);
            }
            else
            {
                // Fallback: use the last identifier as method name
                methodNameIdentifier = node.Children.Where(c => c.Type == "identifier").LastOrDefault();
            }
            
            if (methodNameIdentifier != null)
            {
                // Look for type nodes that come before the method name identifier
                foreach (var child in node.Children)
                {
                    // Stop when we reach the method name identifier
                    if (child == methodNameIdentifier) break;
                    
                    // Check for various type nodes
                    if (child.Type == "predefined_type" || 
                        child.Type == "identifier" && child != methodNameIdentifier ||
                        child.Type == "generic_name" ||
                        child.Type == "nullable_type" ||
                        child.Type == "array_type" ||
                        child.Type == "qualified_name")
                    {
                        return child.Text.Trim();
                    }
                }
            }
            
            // For async methods that return Task (not Task<T>), check if async modifier is present
            var modifiers = ExtractModifiers(node);
            if (modifiers.Contains("async"))
            {
                // No return type found but it's async, so it returns Task
                return "Task";
            }
        }
        
        // Fallback for other node types (TypeScript, etc.)
        var returnTypeNode = FindChildByType(node, "type") ?? 
                            FindChildByType(node, "return_type") ??
                            FindChildByType(node, "type_annotation");
        
        if (returnTypeNode != null)
        {
            return returnTypeNode.Text.Trim();
        }
        
        return "void";
    }
    
    private Node? FindChildByType(Node node, string type)
    {
        foreach (var child in node.Children)
        {
            if (child.Type == type)
            {
                return child;
            }
        }
        return null;
    }
    
    private Node? FindParentType(Node node)
    {
        var parent = node.Parent;
        while (parent != null)
        {
            if (parent.Type.Contains("class") || parent.Type.Contains("interface") || parent.Type.Contains("struct"))
            {
                return parent;
            }
            parent = parent.Parent;
        }
        return null;
    }
    
    private string GetFirstLine(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
            
        var lines = text.Split('\n');
        return lines[0].Trim();
    }
}

public interface ITypeExtractionService
{
    Task<TypeExtractionResult> ExtractTypes(string content, string filePath);
}

public class TypeExtractionResult
{
    public bool Success { get; set; }
    public List<TypeInfo> Types { get; set; } = new();
    public List<MethodInfo> Methods { get; set; } = new();
    public string? Language { get; set; }
    
    /// <summary>
    /// Shared JSON deserialization options for TypeExtractionResult.
    /// Uses case-insensitive property matching to handle varying JSON casing from different sources.
    /// </summary>
    public static readonly JsonSerializerOptions DeserializationOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}

public class TypeInfo
{
    public required string Name { get; set; }
    public required string Kind { get; set; }
    public required string Signature { get; set; }
    public int Line { get; set; }
    public int Column { get; set; }
    public List<string> Modifiers { get; set; } = new();
    public string? BaseType { get; set; }
    public List<string>? Interfaces { get; set; }
}

public class MethodInfo
{
    public required string Name { get; set; }
    public required string Signature { get; set; }
    public string ReturnType { get; set; } = "void";
    public int Line { get; set; }
    public int Column { get; set; }
    public string? ContainingType { get; set; }
    public List<string> Parameters { get; set; } = new();
    public List<string> Modifiers { get; set; } = new();
}
