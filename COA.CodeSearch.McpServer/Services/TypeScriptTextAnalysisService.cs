using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Text.RegularExpressions;

namespace COA.CodeSearch.McpServer.Services;

/// <summary>
/// Lightweight TypeScript analysis using text patterns and heuristics
/// This is a simpler alternative to full TSServer integration
/// </summary>
public class TypeScriptTextAnalysisService
{
    private readonly ILogger<TypeScriptTextAnalysisService> _logger;
    private readonly ILuceneIndexService _luceneService;
    
    // TypeScript patterns
    private static readonly Regex InterfacePattern = new(@"interface\s+(\w+)", RegexOptions.Compiled);
    private static readonly Regex ClassPattern = new(@"class\s+(\w+)", RegexOptions.Compiled);
    private static readonly Regex TypePattern = new(@"type\s+(\w+)\s*=", RegexOptions.Compiled);
    private static readonly Regex FunctionPattern = new(@"(?:export\s+)?(?:async\s+)?function\s+(\w+)", RegexOptions.Compiled);
    private static readonly Regex ConstPattern = new(@"(?:export\s+)?const\s+(\w+)", RegexOptions.Compiled);
    private static readonly Regex ImportPattern = new(@"import\s+(?:\{[^}]+\}|\*\s+as\s+\w+|\w+)\s+from\s+['""]([^'""]+)['""]", RegexOptions.Compiled);
    
    public TypeScriptTextAnalysisService(
        ILogger<TypeScriptTextAnalysisService> logger,
        ILuceneIndexService luceneService)
    {
        _logger = logger;
        _luceneService = luceneService;
    }

    /// <summary>
    /// Find TypeScript symbol definitions using text search
    /// </summary>
    public async Task<TypeScriptSymbolLocation?> FindDefinitionAsync(
        string symbolName, 
        string workspacePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Search for common TypeScript definition patterns
            var patterns = new[]
            {
                $@"interface\s+{Regex.Escape(symbolName)}\b",
                $@"class\s+{Regex.Escape(symbolName)}\b",
                $@"type\s+{Regex.Escape(symbolName)}\s*=",
                $@"function\s+{Regex.Escape(symbolName)}\s*\(",
                $@"const\s+{Regex.Escape(symbolName)}\s*[=:]",
                $@"export\s+.*\b{Regex.Escape(symbolName)}\b"
            };

            var searcher = await _luceneService.GetIndexSearcherAsync(workspacePath, cancellationToken);
            
            // Search each pattern
            foreach (var pattern in patterns)
            {
                var analyzer = await _luceneService.GetAnalyzerAsync(workspacePath, cancellationToken);
                var parser = new Lucene.Net.QueryParsers.Classic.QueryParser(
                    Lucene.Net.Util.LuceneVersion.LUCENE_48, "content", analyzer);
                
                // Use wildcard query for pattern matching
                var query = parser.Parse($"content:/{pattern}/");
                var hits = searcher.Search(query, 10);
                
                if (hits.TotalHits > 0)
                {
                    var doc = searcher.Doc(hits.ScoreDocs[0].Doc);
                    var filePath = doc.Get("path");
                    
                    // Find exact line number
                    var content = await File.ReadAllTextAsync(filePath, cancellationToken);
                    var lines = content.Split('\n');
                    
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (Regex.IsMatch(lines[i], pattern))
                        {
                            return new TypeScriptSymbolLocation
                            {
                                FilePath = filePath,
                                Line = i + 1,
                                Column = lines[i].IndexOf(symbolName) + 1,
                                SymbolName = symbolName,
                                SymbolType = DetermineSymbolType(lines[i]),
                                LineText = lines[i].Trim()
                            };
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding TypeScript definition for {Symbol}", symbolName);
        }
        
        return null;
    }

    /// <summary>
    /// Find all references to a TypeScript symbol
    /// </summary>
    public async Task<List<TypeScriptSymbolLocation>> FindReferencesAsync(
        string symbolName,
        string workspacePath,
        CancellationToken cancellationToken = default)
    {
        var references = new List<TypeScriptSymbolLocation>();
        
        try
        {
            var searcher = await _luceneService.GetIndexSearcherAsync(workspacePath, cancellationToken);
            var analyzer = await _luceneService.GetAnalyzerAsync(workspacePath, cancellationToken);
            var parser = new Lucene.Net.QueryParsers.Classic.QueryParser(
                Lucene.Net.Util.LuceneVersion.LUCENE_48, "content", analyzer);
            
            // Search for the symbol name
            var query = parser.Parse($"content:{symbolName}");
            var hits = searcher.Search(query, 100);
            
            foreach (var scoreDoc in hits.ScoreDocs)
            {
                var doc = searcher.Doc(scoreDoc.Doc);
                var filePath = doc.Get("path");
                
                // Skip non-TypeScript files
                var ext = Path.GetExtension(filePath).ToLower();
                if (ext != ".ts" && ext != ".tsx" && ext != ".js" && ext != ".jsx" && ext != ".vue")
                    continue;
                
                // Find exact matches in the file
                var content = await File.ReadAllTextAsync(filePath, cancellationToken);
                var lines = content.Split('\n');
                
                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    
                    // Use word boundary regex to find exact symbol matches
                    var regex = new Regex($@"\b{Regex.Escape(symbolName)}\b");
                    var matches = regex.Matches(line);
                    
                    foreach (Match match in matches)
                    {
                        references.Add(new TypeScriptSymbolLocation
                        {
                            FilePath = filePath,
                            Line = i + 1,
                            Column = match.Index + 1,
                            SymbolName = symbolName,
                            SymbolType = DetermineUsageType(line, match.Index),
                            LineText = line.Trim()
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding TypeScript references for {Symbol}", symbolName);
        }
        
        return references;
    }

    /// <summary>
    /// Detect TypeScript projects in a workspace
    /// </summary>
    public async Task<List<TypeScriptProject>> DetectProjectsAsync(
        string workspacePath,
        CancellationToken cancellationToken = default)
    {
        var projects = new List<TypeScriptProject>();
        
        try
        {
            await Task.Run(() =>
            {
                // Find tsconfig.json files
                var tsconfigFiles = Directory.GetFiles(
                    workspacePath, "tsconfig*.json", SearchOption.AllDirectories)
                    .Where(f => !f.Contains("node_modules", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                
                foreach (var configFile in tsconfigFiles)
                {
                    projects.Add(new TypeScriptProject
                    {
                        ConfigPath = configFile,
                        RootDirectory = Path.GetDirectoryName(configFile) ?? workspacePath,
                        Name = Path.GetFileNameWithoutExtension(configFile)
                    });
                }
                
                // Also check for package.json with TypeScript
                var packageFiles = Directory.GetFiles(
                    workspacePath, "package.json", SearchOption.AllDirectories)
                    .Where(f => !f.Contains("node_modules", StringComparison.OrdinalIgnoreCase));
                
                foreach (var packageFile in packageFiles)
                {
                    try
                    {
                        var content = File.ReadAllText(packageFile);
                        if (content.Contains("\"typescript\"", StringComparison.OrdinalIgnoreCase))
                        {
                            var dir = Path.GetDirectoryName(packageFile) ?? workspacePath;
                            if (!projects.Any(p => p.RootDirectory == dir))
                            {
                                projects.Add(new TypeScriptProject
                                {
                                    ConfigPath = packageFile,
                                    RootDirectory = dir,
                                    Name = Path.GetFileName(dir),
                                    IsInferred = true
                                });
                            }
                        }
                    }
                    catch { }
                }
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error detecting TypeScript projects");
        }
        
        return projects;
    }

    private string DetermineSymbolType(string line)
    {
        if (InterfacePattern.IsMatch(line)) return "interface";
        if (ClassPattern.IsMatch(line)) return "class";
        if (TypePattern.IsMatch(line)) return "type";
        if (FunctionPattern.IsMatch(line)) return "function";
        if (ConstPattern.IsMatch(line)) return "const";
        return "symbol";
    }

    private string DetermineUsageType(string line, int position)
    {
        // Simple heuristics for usage type
        if (line.Contains("import") && line.IndexOf("import") < position) return "import";
        if (line.Contains("extends") && line.IndexOf("extends") < position) return "extends";
        if (line.Contains("implements") && line.IndexOf("implements") < position) return "implements";
        if (line.Contains("new ") && line.IndexOf("new ") < position) return "instantiation";
        if (line.Contains(": ") && line.IndexOf(": ") < position) return "type-annotation";
        return "reference";
    }
}

public class TypeScriptSymbolLocation
{
    public string FilePath { get; set; } = string.Empty;
    public int Line { get; set; }
    public int Column { get; set; }
    public string SymbolName { get; set; } = string.Empty;
    public string SymbolType { get; set; } = string.Empty;
    public string LineText { get; set; } = string.Empty;
}

public class TypeScriptProject
{
    public string ConfigPath { get; set; } = string.Empty;
    public string RootDirectory { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsInferred { get; set; }
}