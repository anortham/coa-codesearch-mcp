using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Services.TypeExtraction;

public class QueryTypeDetector : IQueryTypeDetector
{
    private readonly ILogger<QueryTypeDetector> _logger;
    
    private static readonly Regex[] TypePatterns = new[]
    {
        // Original patterns
        new Regex(@"\b(class|interface|struct|enum|type|trait|impl|abstract)\s+\w+", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\bI[A-Z]\w+\b", RegexOptions.Compiled),
        new Regex(@"\b\w+(Service|Controller|Repository|Manager|Factory|Builder|Handler|Provider)\b", RegexOptions.Compiled),
        new Regex(@"<[^>]+>", RegexOptions.Compiled),
        new Regex(@"::\w+", RegexOptions.Compiled),
        new Regex(@"\w+\.\w+\(", RegexOptions.Compiled),
        new Regex(@"(public|private|protected|internal)\s+(class|interface|struct)", RegexOptions.Compiled),
        new Regex(@"extends\s+\w+", RegexOptions.Compiled),
        new Regex(@"implements\s+\w+", RegexOptions.Compiled),
        new Regex(@"func\s+\w+", RegexOptions.Compiled),
        new Regex(@"def\s+\w+", RegexOptions.Compiled),
        new Regex(@"fn\s+\w+", RegexOptions.Compiled),
        
        // Critical patterns for Claude's workflow - when it needs type info BEFORE writing code
        new Regex(@"\bnew\s+\w+", RegexOptions.IgnoreCase | RegexOptions.Compiled), // "new ClassName" - about to instantiate
        new Regex(@"\b\w+\s*:\s*I\w+", RegexOptions.Compiled), // ": IInterface" - type annotations
        new Regex(@"\bTask<\w+>", RegexOptions.Compiled), // "Task<ReturnType>" - async returns
        new Regex(@"\bList<\w+>", RegexOptions.Compiled), // "List<Type>" - collections
        new Regex(@"\bDictionary<\w+,\s*\w+>", RegexOptions.Compiled), // "Dictionary<K,V>" - dictionaries
        new Regex(@"\bIEnumerable<\w+>", RegexOptions.Compiled), // "IEnumerable<Type>" - LINQ
        new Regex(@"\w+\.Create\w*", RegexOptions.Compiled), // "Factory.Create*" - factory methods
        new Regex(@"\w+\.GetService", RegexOptions.Compiled), // "*.GetService" - DI lookups
        new Regex(@"\w+Async\s*\(", RegexOptions.Compiled), // "MethodAsync(" - async method signatures
        new Regex(@"\b\w+\s+\w+Async\s*\(", RegexOptions.Compiled), // "ReturnType MethodAsync(" - full async signatures
        new Regex(@"public\s+\w+\s+\w+\(", RegexOptions.Compiled), // "public ReturnType Method(" - method definitions
        new Regex(@"private\s+\w+\s+\w+\(", RegexOptions.Compiled), // "private ReturnType Method(" - private methods
        new Regex(@"protected\s+\w+\s+\w+\(", RegexOptions.Compiled), // "protected ReturnType Method(" - protected methods
        new Regex(@"\b\w+\s*\[\]", RegexOptions.Compiled), // "Type[]" - array types
        new Regex(@"\bAwait\s+\w+", RegexOptions.IgnoreCase | RegexOptions.Compiled), // "await Something" - async calls  
        new Regex(@"return\s+new\s+\w+", RegexOptions.IgnoreCase | RegexOptions.Compiled), // "return new Type" - instantiation in returns
        new Regex(@"public\s+async\s+Task", RegexOptions.IgnoreCase | RegexOptions.Compiled), // "public async Task" - async method definitions
        new Regex(@":\s*IDisposable", RegexOptions.Compiled), // ": IDisposable" - interface implementations
    };
    
    private static readonly Regex[] NonTypePatterns = new[]
    {
        new Regex(@"\d{8}\.log", RegexOptions.Compiled),
        new Regex(@"\d{4}-\d{2}-\d{2}", RegexOptions.Compiled),
        new Regex(@"(error|exception|failed|warning|bug|issue)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"(TODO|FIXME|HACK|NOTE|XXX)", RegexOptions.Compiled),
        new Regex(@"https?://", RegexOptions.Compiled),
        new Regex(@"[\\/]", RegexOptions.Compiled),
        new Regex(@"\.(txt|log|md|json|xml|yml|yaml)$", RegexOptions.Compiled),
        new Regex(@"^\d+$", RegexOptions.Compiled),
        new Regex(@"^[a-f0-9]{32,}$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    };
    
    public QueryTypeDetector(ILogger<QueryTypeDetector> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    
    public bool IsLikelyTypeQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return false;
        
        if (NonTypePatterns.Any(p => p.IsMatch(query)))
        {
            _logger.LogDebug("Query '{Query}' matched non-type pattern, not a type query", query);
            return false;
        }
        
        var isTypeQuery = TypePatterns.Any(p => p.IsMatch(query));
        
        if (isTypeQuery)
        {
            _logger.LogDebug("Query '{Query}' identified as type query", query);
        }
        
        return isTypeQuery;
    }
    
    public TypeQueryIntent AnalyzeIntent(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return TypeQueryIntent.Unknown;
            
        if (query.Contains("class ", StringComparison.OrdinalIgnoreCase) || 
            query.Contains("interface ", StringComparison.OrdinalIgnoreCase))
        {
            return TypeQueryIntent.FindDefinition;
        }
        
        if (query.Contains("extends ", StringComparison.OrdinalIgnoreCase) || 
            query.Contains("implements ", StringComparison.OrdinalIgnoreCase) ||
            query.Contains(": ", StringComparison.OrdinalIgnoreCase))
        {
            return TypeQueryIntent.FindInheritance;
        }
        
        if (query.Contains("(") && query.Contains(")"))
        {
            return TypeQueryIntent.FindMethod;
        }
        
        if (query.Contains("func ", StringComparison.OrdinalIgnoreCase) || 
            query.Contains("def ", StringComparison.OrdinalIgnoreCase) ||
            query.Contains("fn ", StringComparison.OrdinalIgnoreCase))
        {
            return TypeQueryIntent.FindMethod;
        }
        
        if (Regex.IsMatch(query, @"\bI[A-Z]\w+\b") || query.EndsWith("Service") || query.EndsWith("Controller"))
        {
            return TypeQueryIntent.FindDefinition;
        }
        
        return TypeQueryIntent.General;
    }
}

public interface IQueryTypeDetector
{
    bool IsLikelyTypeQuery(string query);
    TypeQueryIntent AnalyzeIntent(string query);
}

public enum TypeQueryIntent
{
    Unknown,
    General,
    FindDefinition,
    FindMethod,
    FindInheritance
}