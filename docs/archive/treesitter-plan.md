# Tree-sitter Cross-Platform Strategy

**Date**: 2025-09-06  
**Context**: Planning cross-platform Tree-sitter support for CodeSearch MCP  
**Goal**: Unified Tree-sitter package with latest versions across Windows, Linux, and macOS

## Current Situation

### Existing Package Analysis
- **TreeSitter.Bindings**: Currently used, no macOS native libraries
- **TreeSitterSharp**: Older (2021), potentially outdated grammars
- **TreeSitter.NET**: Abandoned (2020)
- **Platform availability**: Windows ✓, Linux ✓, macOS ❌

### macOS Status
- Tree-sitter installed via brew (latest version ~0.22.x/0.23.x)
- Likely newer than Windows package bindings
- All language grammars available via npm/GitHub

## Strategic Options

### Option A: Fork & Upgrade TreeSitter.Bindings
**Approach**: Fork existing package, add macOS support
- ✅ Proven API and documentation
- ✅ Minimal learning curve
- ❌ Inherit existing technical debt
- ❌ Potentially outdated Tree-sitter version

**Effort**: 2-3 days
**Files to add**: 
- `runtimes/osx-x64/native/*.dylib`
- `runtimes/osx-arm64/native/*.dylib`
- macOS-specific P/Invoke declarations

### Option B: Create Fresh "TreeSitter.Universal" Package (Recommended)
**Approach**: Build clean cross-platform package from scratch
- ✅ Latest Tree-sitter core (0.23.x from brew)
- ✅ Latest language grammars
- ✅ Clean API designed for our needs
- ✅ No legacy technical debt
- ❌ More initial development work

**Effort**: 1-2 weeks
**Architecture**:
```
TreeSitter.Universal/
├── src/TreeSitter.Universal/
│   ├── TreeSitterParser.cs
│   ├── Native/NativeLibraryLoader.cs
│   └── Languages/LanguageLoader.cs
├── runtimes/
│   ├── win-x64/native/
│   ├── linux-x64/native/
│   ├── osx-x64/native/
│   └── osx-arm64/native/
```

### Option C: Hybrid Progressive Approach
**Approach**: Start with enhanced current solution, build managed incrementally
- Phase 1: Add macOS to current package
- Phase 2: Build managed parser for C# only
- Phase 3: Gradually add more languages
- Phase 4: Full managed implementation

## Implementation Strategy

### Version Alignment
**Target Versions** (based on brew latest):
- Tree-sitter Core: 0.23.x
- C# Grammar: 0.21.3
- TypeScript Grammar: 0.23.0
- Python Grammar: 0.23.0
- JavaScript Grammar: 0.23.0

### Build Process

**macOS (using existing brew installation)**:
```bash
# Core library
brew list tree-sitter
# Copy .dylib files from brew location

# Language grammars
git clone https://github.com/tree-sitter/tree-sitter-c-sharp
cd tree-sitter-c-sharp
npm install
npx tree-sitter build
# Produces tree-sitter-c-sharp.dylib
```

**Windows**:
```powershell
# Build latest Tree-sitter
git clone https://github.com/tree-sitter/tree-sitter
cd tree-sitter
cargo build --release
# Or download pre-built binaries

# Build grammars
git clone language repos
npm install && npx tree-sitter build
```

**Linux**:
```bash
# Similar to macOS
git clone https://github.com/tree-sitter/tree-sitter
make
# Build all grammars
```

## Smart Loading Strategy

```csharp
public static class NativeLibraryLoader
{
    static NativeLibraryLoader()
    {
        LoadPlatformSpecificLibrary();
    }
    
    private static void LoadPlatformSpecificLibrary()
    {
        var rid = RuntimeInformation.RuntimeIdentifier;
        var (coreLib, extension) = rid switch
        {
            var r when r.StartsWith("win") => ("tree-sitter.dll", ".dll"),
            var r when r.StartsWith("osx") => ("libtree-sitter.dylib", ".dylib"),
            var r when r.StartsWith("linux") => ("libtree-sitter.so", ".so"),
            _ => throw new PlatformNotSupportedException($"Platform {rid} not supported")
        };
        
        NativeLibrary.Load(coreLib);
    }
    
    public static IntPtr LoadLanguageGrammar(Language language)
    {
        var libraryName = GetLanguageLibraryName(language);
        return NativeLibrary.Load(libraryName);
    }
}
```

## API Design Improvements

### Modern Parser Interface
```csharp
public class TreeSitterParser : IDisposable
{
    private readonly IntPtr _parser;
    private readonly Language _language;
    
    public TreeSitterParser(Language language)
    {
        _parser = Native.ts_parser_new();
        var langPtr = NativeLibraryLoader.LoadLanguageGrammar(language);
        Native.ts_parser_set_language(_parser, langPtr);
        _language = language;
    }
    
    public ParseTree Parse(string code)
    {
        var tree = Native.ts_parser_parse_string(_parser, code);
        return new ParseTree(tree, code, _language);
    }
    
    public ParseTree ParseIncremental(string code, Edit edit)
    {
        // Support incremental parsing for performance
        // Critical for real-time code analysis
    }
}
```

### Enhanced ParseTree
```csharp
public class ParseTree : IDisposable
{
    private readonly IntPtr _tree;
    private readonly string _sourceCode;
    private readonly Language _language;
    
    public ParseTree(IntPtr tree, string sourceCode, Language language)
    {
        _tree = tree;
        _sourceCode = sourceCode;
        _language = language;
    }
    
    public Node RootNode => new Node(Native.ts_tree_root_node(_tree), _sourceCode);
    
    public IEnumerable<Node> FindNodes(string nodeType)
    {
        // Smart node finding with type filtering
    }
    
    public IEnumerable<Node> FindSymbols()
    {
        // Extract all symbols (functions, classes, variables)
        // Language-specific implementation
    }
    
    public Node? FindNodeAt(int line, int column)
    {
        // Find node at specific position
        // Critical for CodeSearch's line-number precision
    }
}
```

## Integration with CodeSearch

### Semantic Editing Tools
With rock-solid line numbers + Tree-sitter positions:

```csharp
public class SemanticEditTool : McpToolBase<EditParams, EditResult>
{
    private readonly TreeSitterService _treeSitter;
    
    public override string Description => 
        "USE FIRST - Insert code at semantic locations without reading entire files. " +
        "Tree-sitter powered for 100% accurate positioning.";
    
    public async Task<EditResult> ExecuteAsync(EditParams parameters)
    {
        // 1. Parse file with Tree-sitter
        var parseTree = await _treeSitter.ParseFileAsync(parameters.FilePath);
        
        // 2. Find semantic location (after class, before method, etc.)
        var insertPoint = parseTree.FindInsertionPoint(parameters.Target);
        
        // 3. Insert without reading entire file
        await _fileService.InsertAtAsync(
            parameters.FilePath,
            insertPoint.Line,
            insertPoint.Column,
            parameters.Code
        );
        
        return new EditResult
        {
            Success = true,
            InsertedAt = $"Line {insertPoint.Line}",
            Context = GetSurroundingLines(insertPoint, 3)
        };
    }
}
```

### Smart Context Tools
```csharp
[Tool("get_symbols_overview")]
public async Task<SymbolOverview> GetSymbolsOverview(string filePath)
{
    var parseTree = await _treeSitter.ParseFileAsync(filePath);
    
    return new SymbolOverview
    {
        Classes = parseTree.FindNodes("class_declaration").Select(ExtractClassInfo),
        Methods = parseTree.FindNodes("method_declaration").Select(ExtractMethodInfo),
        Interfaces = parseTree.FindNodes("interface_declaration").Select(ExtractInterfaceInfo),
        // etc.
    };
}

[Tool("find_patterns")]
public async Task<PatternResults> FindPatterns(string pattern, string directory)
{
    // Use Tree-sitter to find semantic patterns across files
    // Example: "async methods without ConfigureAwait"
    // Example: "catch blocks that swallow exceptions"
}
```

## Timeline & Milestones

### Phase 1: Investigation (This Week)
- [ ] Audit existing packages for hidden cross-platform support
- [ ] Check Tree-sitter versions in current vs brew
- [ ] Test compilation on all platforms

### Phase 2: Implementation (Next 2 Weeks)
- [ ] Create TreeSitter.Universal package structure
- [ ] Build native libraries for all platforms
- [ ] Implement modern C# API
- [ ] Package and test cross-platform

### Phase 3: Integration (Week 3)
- [ ] Integrate into CodeSearch
- [ ] Build semantic editing tools
- [ ] Test with behavioral adoption framework
- [ ] Performance benchmarking

### Phase 4: Enhancement (Ongoing)
- [ ] Add more language grammars
- [ ] Implement incremental parsing
- [ ] Build pattern detection features
- [ ] Consider WASM builds for browser scenarios

## Success Criteria

1. **Cross-Platform**: Works on Windows, Linux, macOS (Intel + Apple Silicon)
2. **Performance**: Parsing operations <50ms for typical files
3. **Reliability**: No crashes, graceful degradation on parse errors
4. **Completeness**: Supports C#, TypeScript, Python, JavaScript at minimum
5. **Integration**: Seamless integration with CodeSearch's Lucene indexing

## Risk Mitigation

1. **Build Complexity**: Start with pre-built binaries, then custom builds
2. **Grammar Updates**: Automated update process for language grammars
3. **Platform Differences**: Comprehensive testing matrix
4. **Performance**: Benchmarking and optimization from day one
5. **Backwards Compatibility**: Abstraction layer allows switching providers

---

**Decision Point**: After Phase 1 investigation, choose between Fork (Option A) or Fresh Package (Option B) based on findings.

**Estimated Total Effort**: 2-3 weeks for complete implementation
**Priority**: High (enables advanced CodeSearch features and cross-platform support)

*Last Updated: 2025-09-06*