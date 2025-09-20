/**
 * Tree-sitter parser wrapper
 */

import { Parser, Language } from 'web-tree-sitter';
import { TypeExtractionResult } from './types';
import { CSharpExtractor } from './extractors/csharp';
import { PythonExtractor } from './extractors/python';
import { GoExtractor } from './extractors/go';
import { TypeScriptExtractor } from './extractors/typescript';
import { GenericExtractor } from './extractors/generic';
import path from 'path';

export class TreeSitterParser {
  private initialized = false;
  private parsers = new Map<string, Parser>();
  private languages = new Map<string, Language>();
  private extractors = new Map<string, any>();

  // Map language names to WASM files
  private languageMap: Record<string, string> = {
    'c-sharp': 'tree-sitter-c-sharp.wasm',
    'csharp': 'tree-sitter-c-sharp.wasm',
    'cs': 'tree-sitter-c-sharp.wasm',
    'python': 'tree-sitter-python.wasm',
    'py': 'tree-sitter-python.wasm',
    'go': 'tree-sitter-go.wasm',
    'golang': 'tree-sitter-go.wasm',
    'javascript': 'tree-sitter-javascript.wasm',
    'js': 'tree-sitter-javascript.wasm',
    'typescript': 'tree-sitter-typescript.wasm',
    'ts': 'tree-sitter-typescript.wasm',
    'tsx': 'tree-sitter-tsx.wasm',
    'java': 'tree-sitter-java.wasm',
    'rust': 'tree-sitter-rust.wasm',
    'rs': 'tree-sitter-rust.wasm',
    'ruby': 'tree-sitter-ruby.wasm',
    'rb': 'tree-sitter-ruby.wasm',
    'cpp': 'tree-sitter-cpp.wasm',
    'c++': 'tree-sitter-cpp.wasm',
    'php': 'tree-sitter-php.wasm',
    'regex': 'tree-sitter-regex.wasm',
    // New language support!
    'razor': 'tree-sitter-razor.wasm',
    'cshtml': 'tree-sitter-razor.wasm',
    'swift': 'tree-sitter-swift.wasm',
    'kotlin': 'tree-sitter-kotlin.wasm',
    'kt': 'tree-sitter-kotlin.wasm'
  };

  async initialize(): Promise<void> {
    if (this.initialized) return;

    try {
      // Detect if running as compiled executable
      const isCompiled = import.meta.dir.includes('~BUN');

      // Initialize Tree-sitter with WASM location
      const wasmPath = isCompiled
        ? path.join(
            path.dirname(process.execPath),
            'node_modules',
            'web-tree-sitter',
            'tree-sitter.wasm'
          )
        : path.join(
            import.meta.dir,
            '..',
            'node_modules',
            'web-tree-sitter',
            'tree-sitter.wasm'
          );

      console.error(`Initializing Parser with WASM path: ${wasmPath}`);

      await Parser.init({
        locateFile: (scriptName: string) => {
          if (scriptName === 'tree-sitter.wasm') {
            return wasmPath;
          }
          return scriptName;
        }
      });

      // Register language-specific extractors
      this.extractors.set('c-sharp', new CSharpExtractor());
      this.extractors.set('python', new PythonExtractor());
      this.extractors.set('go', new GoExtractor());
      this.extractors.set('typescript', new TypeScriptExtractor());
      this.extractors.set('tsx', new TypeScriptExtractor());
      this.extractors.set('javascript', new TypeScriptExtractor()); // JS can use TS extractor

      this.initialized = true;
      console.error('Tree-sitter initialized successfully');
    } catch (error) {
      console.error('Failed to initialize Tree-sitter:', error);
      throw error;
    }
  }

  async getParser(language: string): Promise<Parser | null> {
    const normalizedLang = this.normalizeLanguage(language);

    // Return cached parser if available
    if (this.parsers.has(normalizedLang)) {
      return this.parsers.get(normalizedLang)!;
    }

    // Load language if not cached
    const lang = await this.loadLanguage(normalizedLang);
    if (!lang) {
      return null;
    }

    // Create and cache parser
    const parser = new Parser();
    parser.setLanguage(lang);
    this.parsers.set(normalizedLang, parser);

    return parser;
  }

  private async loadLanguage(language: string): Promise<Language | null> {
    // Return cached language if available
    if (this.languages.has(language)) {
      return this.languages.get(language)!;
    }

    const wasmFile = this.languageMap[language];
    if (!wasmFile) {
      console.error(`No WASM file mapped for language: ${language}`);
      return null;
    }

    try {
      // Detect if running as compiled executable
      const isCompiled = import.meta.dir.includes('~BUN');
      let wasmPath: string;

      if (isCompiled) {
        // When compiled, look for WASM files relative to the executable location
        // The node_modules folder should be copied next to the executable
        wasmPath = path.join(
          path.dirname(process.execPath),
          'node_modules',
          '@vscode',
          'tree-sitter-wasm',
          'wasm',
          wasmFile
        );
      } else {
        // Development mode - use relative path from src directory
        wasmPath = path.join(
          import.meta.dir,
          '..',
          'node_modules',
          '@vscode',
          'tree-sitter-wasm',
          'wasm',
          wasmFile
        );
      }

      console.error(`Loading WASM from: ${wasmPath}`);

      try {
        const lang = await Language.load(wasmPath);
        this.languages.set(language, lang);
        console.error(`Loaded language: ${language}`);
        return lang;
      } catch (loadError) {
        // Try fallback for custom WASM files (Razor, Swift, Kotlin)
        const customLanguages = ['tree-sitter-razor.wasm', 'tree-sitter-swift.wasm', 'tree-sitter-kotlin.wasm'];
        if (customLanguages.includes(wasmFile)) {
          console.error(`Failed to load from node_modules, trying custom wasm directory...`);

          const customWasmPath = isCompiled
            ? path.join(path.dirname(process.execPath), 'wasm', wasmFile)
            : path.join(import.meta.dir, '..', 'wasm', wasmFile);

          console.error(`Loading custom WASM from: ${customWasmPath}`);

          try {
            const lang = await Language.load(customWasmPath);
            this.languages.set(language, lang);
            console.error(`Loaded custom language: ${language}`);
            return lang;
          } catch (customError) {
            console.error(`Failed to load custom WASM ${language}:`, customError);
          }
        }
        throw loadError;
      }
    } catch (error) {
      console.error(`Failed to load language ${language}:`, error);
      return null;
    }
  }

  private normalizeLanguage(language: string): string {
    const lower = language.toLowerCase();

    // Check if we have a direct mapping
    if (this.languageMap[lower]) {
      // Find the canonical name (first key that maps to this WASM file)
      const wasmFile = this.languageMap[lower];
      for (const [key, value] of Object.entries(this.languageMap)) {
        if (value === wasmFile && !key.includes('.')) {
          return key;
        }
      }
    }

    // Map file extensions to languages
    const extMap: Record<string, string> = {
      '.cs': 'c-sharp',
      '.py': 'python',
      '.go': 'go',
      '.js': 'javascript',
      '.jsx': 'javascript',
      '.ts': 'typescript',
      '.tsx': 'tsx',
      '.java': 'java',
      '.rs': 'rust',
      '.rb': 'ruby',
      '.cpp': 'cpp',
      '.cxx': 'cpp',
      '.cc': 'cpp',
      '.php': 'php',
      '.css': 'css',
      '.ini': 'ini',
      // New language support!
      '.razor': 'razor',
      '.cshtml': 'razor',
      '.swift': 'swift',
      '.kt': 'kotlin',
      '.kts': 'kotlin'
    };

    if (extMap[lower]) {
      return extMap[lower];
    }

    return lower;
  }

  async extractTypes(
    content: string,
    language: string,
    filePath?: string
  ): Promise<TypeExtractionResult> {
    try {
      // Handle "unknown" language gracefully - return empty results
      if (language === 'unknown') {
        return {
          success: true,
          types: [],
          methods: [],
          language
        };
      }

      const parser = await this.getParser(language);
      if (!parser) {
        return {
          success: false,
          types: [],
          methods: [],
          language,
          error: `Unsupported language: ${language}`
        };
      }

      // Parse the content
      const tree = parser.parse(content);
      const rootNode = tree.rootNode;

      // Get appropriate extractor
      const normalizedLang = this.normalizeLanguage(language);
      const extractor = this.extractors.get(normalizedLang) || new GenericExtractor();

      // Extract types and methods
      const result = await extractor.extract(rootNode, content, language);

      tree.delete();

      return result;
    } catch (error) {
      return {
        success: false,
        types: [],
        methods: [],
        language,
        error: `Extraction failed: ${error instanceof Error ? error.message : String(error)}`
      };
    }
  }

  getSupportedLanguages(): string[] {
    // Return unique canonical language names
    const languages = new Set<string>();
    for (const [key, value] of Object.entries(this.languageMap)) {
      if (!key.includes('.') && key.length > 2) {
        languages.add(key);
      }
    }
    return Array.from(languages).sort();
  }
}