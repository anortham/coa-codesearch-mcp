using Microsoft.Extensions.Logging;
using TreeSitter;
using System.Text.Json;
using System.Linq;

namespace COA.CodeSearch.McpServer.Services.TypeExtraction;

public class TypeExtractionService : ITypeExtractionService
{
    private readonly ILogger<TypeExtractionService> _logger;
    
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
        { ".sh", "bash" }
        // Note: Kotlin, R, Objective-C, Lua, Dart, Zig, Elm, Clojure, Elixir 
        // don't have tree-sitter DLLs available in the current build
    };
    
    public TypeExtractionService(ILogger<TypeExtractionService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    
    public TypeExtractionResult ExtractTypes(string content, string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        
        if (!ExtensionToLanguage.TryGetValue(extension, out var languageName))
        {
            return new TypeExtractionResult { Success = false };
        }
        
        try
        {
            // Special handling for C# due to library/function name mismatch
            Language language;
            if (languageName == "c-sharp")
            {
                // Library: tree-sitter-c-sharp.dll, Function: tree_sitter_c_sharp
                language = new Language("tree-sitter-c-sharp", "tree_sitter_c_sharp");
            }
            else
            {
                language = new Language(languageName);
            }
            
            using (language)
            {
                using var parser = new Parser(language);
                using var tree = parser.Parse(content);
                
                if (tree == null)
                {
                    _logger.LogDebug("Failed to parse file {FilePath}", filePath);
                    return new TypeExtractionResult { Success = false };
                }
                
                var types = new List<TypeInfo>();
                var methods = new List<MethodInfo>();
                
                ExtractFromNode(tree.RootNode, types, methods);
                
                return new TypeExtractionResult
                {
                    Success = true,
                    Types = types,
                    Methods = methods,
                    Language = languageName
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to extract types from {FilePath}", filePath);
            return new TypeExtractionResult { Success = false };
        }
    }
    
    private void ExtractFromNode(Node node, List<TypeInfo> types, List<MethodInfo> methods)
    {
        if (node == null) return;
        
        var nodeType = node.Type;
        
        switch (nodeType)
        {
            case "class_declaration":
            case "interface_declaration":
            case "struct_declaration":
            case "enum_declaration":
                ExtractTypeDeclaration(node, types);
                break;
                
            case "method_declaration":
            case "function_declaration":
            case "function_definition":
            case "arrow_function":
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
            // Find the identifier (method name)
            var identifierNode = FindChildByType(node, "identifier");
            if (identifierNode != null)
            {
                // Look for type nodes that come before the identifier
                foreach (var child in node.Children)
                {
                    // Stop when we reach the identifier
                    if (child == identifierNode) break;
                    
                    // Check for various type nodes
                    if (child.Type == "predefined_type" || 
                        child.Type == "identifier" && child != identifierNode ||
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
    TypeExtractionResult ExtractTypes(string content, string filePath);
}

public class TypeExtractionResult
{
    public bool Success { get; set; }
    public List<TypeInfo> Types { get; set; } = new();
    public List<MethodInfo> Methods { get; set; } = new();
    public string? Language { get; set; }
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