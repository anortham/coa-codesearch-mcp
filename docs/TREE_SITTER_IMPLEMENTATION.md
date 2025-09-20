# Tree-sitter Implementation Documentation

## Overview

The CodeSearch MCP Server uses a Bun-based Tree-sitter service for advanced type extraction across multiple programming languages. This implementation replaces the previous native interop approach with a robust process-based communication model.

## Architecture

### Core Components

1. **BunTreeSitterService (C#)**
   - Location: `COA.CodeSearch.McpServer/Services/TypeExtraction/BunTreeSitterService.cs`
   - Manages the Tree-sitter process lifecycle
   - Communicates via JSON over stdin/stdout
   - Handles process startup, health checks, and graceful shutdown
   - Thread-safe with semaphore-based synchronization

2. **Tree-sitter Service (TypeScript/Bun)**
   - Location: `COA.CodeSearch.McpServer/TreeSitterService/`
   - Compiled to standalone executables for each platform
   - Uses VS Code's Tree-sitter WASM bindings
   - Implements language-specific extractors

3. **Language Extractors**
   - Base: `GenericExtractor` - Default extraction for unsupported languages
   - Specialized:
     - `CSharpExtractor` - Full C# type and method extraction
     - `TypeScriptExtractor` - TypeScript/JavaScript/TSX support
     - `PythonExtractor` - Python class and function extraction
     - `GoExtractor` - Go struct and function extraction

### Communication Protocol

```json
// Request Format
{
  "action": "extract",
  "content": "source code string",
  "language": "typescript",
  "filePath": "optional/path/to/file.ts"
}

// Response Format
{
  "success": true,
  "types": [
    {
      "name": "UserService",
      "type": "class",
      "baseTypes": ["BaseService"],
      "startLine": 5,
      "endLine": 45
    }
  ],
  "methods": [
    {
      "name": "getUserById",
      "parameters": "id: string",
      "returnType": "Promise<User>",
      "isAsync": true,
      "startLine": 10,
      "endLine": 15
    }
  ],
  "language": "typescript"
}
```

## Language Support Matrix

| Language | WASM Grammar | Extractor | Status | Notes |
|----------|-------------|-----------|--------|-------|
| C# | ✅ tree-sitter-c-sharp.wasm | ✅ CSharpExtractor | ✅ Production | Full support including generics, async, interfaces |
| TypeScript | ✅ tree-sitter-typescript.wasm | ✅ TypeScriptExtractor | 🔧 75% Complete | 9/12 tests passing, needs array/tuple fixes |
| JavaScript | ✅ tree-sitter-javascript.wasm | ✅ TypeScriptExtractor | 🔧 75% Complete | Shares extractor with TypeScript |
| TSX | ✅ tree-sitter-tsx.wasm | ✅ TypeScriptExtractor | 🔧 75% Complete | React component support |
| Python | ✅ tree-sitter-python.wasm | ✅ PythonExtractor | ✅ Production | Classes, functions, decorators |
| Go | ✅ tree-sitter-go.wasm | ✅ GoExtractor | ✅ Production | Structs, interfaces, functions |
| Java | ✅ tree-sitter-java.wasm | 🔲 GenericExtractor | ⚠️ Basic | Needs specialized extractor |
| Rust | ✅ tree-sitter-rust.wasm | 🔲 GenericExtractor | ⚠️ Basic | Needs specialized extractor |
| Ruby | ✅ tree-sitter-ruby.wasm | 🔲 GenericExtractor | ⚠️ Basic | Needs specialized extractor |
| C++ | ✅ tree-sitter-cpp.wasm | 🔲 GenericExtractor | ⚠️ Basic | Needs specialized extractor |
| PHP | ✅ tree-sitter-php.wasm | 🔲 GenericExtractor | ⚠️ Basic | Needs specialized extractor |
| **Razor** | ✅ tree-sitter-razor.wasm | 🔲 Pending | 🆕 New | Just integrated, needs extractor |
| **Swift** | ✅ tree-sitter-swift.wasm | 🔲 Pending | 🆕 New | Just integrated, needs extractor |
| **Kotlin** | ✅ tree-sitter-kotlin.wasm | 🔲 Pending | 🆕 New | Just integrated, needs extractor |

## Implementation Checklist

### ✅ Completed

- [x] **Core Infrastructure**
  - [x] Process-based communication model
  - [x] JSON protocol implementation
  - [x] Cross-platform support (Windows, Linux, macOS)
  - [x] Semaphore-based thread safety
  - [x] Graceful process lifecycle management
  - [x] Health check mechanism

- [x] **Build and Deployment**
  - [x] Bun compilation to standalone executables
  - [x] Platform-specific executable detection
  - [x] Development mode support (running TypeScript directly)
  - [x] Working directory configuration for node_modules access

- [x] **Language Integration**
  - [x] VS Code Tree-sitter WASM compatibility
  - [x] 14 language grammars loaded
  - [x] Language detection from file extensions
  - [x] Generic extractor fallback

- [x] **C# Extractor**
  - [x] Classes and interfaces
  - [x] Methods with parameters and return types
  - [x] Async method detection
  - [x] Generic type support
  - [x] Inheritance chains

- [x] **TypeScript/JavaScript Extractor (Partial)**
  - [x] Interface extraction with extends support
  - [x] Class extraction with inheritance
  - [x] Regular function declarations
  - [x] Arrow function expressions
  - [x] Generator functions
  - [x] IIFE (Immediately Invoked Function Expressions)
  - [x] Async/await detection
  - [x] Basic generic support
  - [x] VS Code WASM node type compatibility (`extends_type_clause`)

- [x] **Testing Infrastructure**
  - [x] Comprehensive TypeScript/JavaScript test suite (12 tests)
  - [x] Integration tests for service communication
  - [x] JSON contract validation tests

### 🔧 In Progress

- [ ] **TypeScript/JavaScript Extractor Completion**
  - [ ] Array type extraction (e.g., `string[]`)
  - [ ] Tuple type extraction (e.g., `[string, number]`)
  - [ ] Union/intersection types
  - [ ] Type aliases
  - [ ] Enum extraction
  - [ ] Namespace/module support
  - [ ] Decorator metadata
  - [ ] JSDoc type annotations
  - [ ] Complex generic constraints

### 📋 Pending

- [ ] **New Language Extractors**
  - [ ] RazorExtractor - Blazor component support
  - [ ] SwiftExtractor - Classes, protocols, extensions
  - [ ] KotlinExtractor - Classes, interfaces, data classes
  - [ ] JavaExtractor - Enhanced Java support
  - [ ] RustExtractor - Structs, traits, impl blocks

- [ ] **Enhanced Features**
  - [ ] Method complexity metrics
  - [ ] Dependency graph extraction
  - [ ] Import/export analysis
  - [ ] Comment extraction and association
  - [ ] Line-level precision for all elements
  - [ ] Symbol references and usages

- [ ] **Performance Optimizations**
  - [ ] Caching of parsed results
  - [ ] Batch extraction support
  - [ ] Lazy loading of language grammars
  - [ ] Process pooling for concurrent requests

## Development Workflow

### Setting Up Development Environment

1. **Prerequisites**
   - .NET 8.0 SDK
   - Bun runtime (for development mode)
   - VS Code (recommended)

2. **Building the Service**
   ```bash
   # Navigate to Tree-sitter service directory
   cd COA.CodeSearch.McpServer/TreeSitterService

   # Install dependencies
   bun install

   # Build standalone executables
   bun build src/index.ts --compile --target=bun-windows-x64 --outfile tree-sitter-service.exe
   bun build src/index.ts --compile --target=bun-linux-x64 --outfile tree-sitter-service-linux
   bun build src/index.ts --compile --target=bun-darwin-x64 --outfile tree-sitter-service-macos
   ```

3. **Running Tests**
   ```bash
   # Run all tests
   dotnet test

   # Run specific Tree-sitter tests
   dotnet test --filter "TypeExtraction"
   ```

### Adding a New Language

1. **Add WASM Grammar**
   - Place `.wasm` file in `TreeSitterService/node_modules/@vscode/tree-sitter-wasm/wasm/`
   - Update `languageMap` in `parser.ts`

2. **Create Extractor**
   ```typescript
   // src/extractors/yourlang.ts
   export class YourLangExtractor extends BaseExtractor {
     async extract(rootNode: any, content: string, language: string) {
       // Implement extraction logic
     }
   }
   ```

3. **Register Extractor**
   ```typescript
   // In parser.ts initialize()
   this.extractors.set('yourlang', new YourLangExtractor());
   ```

4. **Update C# Service**
   ```csharp
   // In BunTreeSitterService.DetectLanguage()
   ".ext" => "yourlang",
   ```

## Troubleshooting

### Common Issues

1. **Tree-sitter service not found**
   - Ensure executables are built and in `TreeSitterService/` directory
   - Check `CodeSearch:TreeSitterServicePath` configuration

2. **WASM loading failures**
   - Verify WASM files exist in `node_modules/@vscode/tree-sitter-wasm/wasm/`
   - Check file permissions

3. **Extraction returns empty results**
   - Verify language is supported
   - Check extractor registration
   - Review Tree-sitter service logs

4. **Process communication timeouts**
   - Default timeout is 30 seconds
   - Large files may need increased timeout
   - Check for process crashes in logs

## Performance Characteristics

- **Startup Time**: ~200-500ms (first request includes WASM loading)
- **Extraction Speed**: ~10-50ms for typical files (<1000 lines)
- **Memory Usage**: ~50-100MB per process
- **Concurrent Requests**: Serialized via semaphore (single-threaded)

## Future Roadmap

1. **Phase 1** (Current) - Core extraction for primary languages
2. **Phase 2** - Complete TypeScript/JavaScript support
3. **Phase 3** - Specialized extractors for Razor, Swift, Kotlin
4. **Phase 4** - Performance optimizations and caching
5. **Phase 5** - Advanced semantic analysis features

## Related Documentation

- [CodeSearch MCP Server README](../README.md)
- [VS Code Tree-sitter WASM Project](https://github.com/microsoft/vscode)
- [Tree-sitter Documentation](https://tree-sitter.github.io/tree-sitter/)
- [Bun Runtime Documentation](https://bun.sh/docs)

## Archived Documentation

Previous Tree-sitter implementation documentation has been archived in `docs/archive/`:
- `macos-tree-sitter.md` - Old macOS-specific implementation
- `TREE_SITTER_TYPE_ENHANCEMENT_PLAN.md` - Original enhancement plan
- `TREE_SITTER_TYPE_ENHANCEMENT_PLAN_V2.md` - Second iteration plan
- `treesitter-plan.md` - Initial planning document

---

*Last Updated: 2025-09-19*
*Version: 2.1.8+*
*Status: Active Development*