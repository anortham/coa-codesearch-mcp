import { Parser, Language } from 'web-tree-sitter';
import path from 'path';
import { fileURLToPath } from 'url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));

async function analyzeJavaScriptAST() {
  // Initialize parser
  await Parser.init({
    locateFile: (scriptName) => {
      if (scriptName === 'tree-sitter.wasm') {
        return path.join(__dirname, 'node_modules/web-tree-sitter/tree-sitter.wasm');
      }
      return scriptName;
    }
  });

  // Load JavaScript language
  const langPath = path.join(__dirname, 'node_modules/@vscode/tree-sitter-wasm/wasm/tree-sitter-javascript.wasm');
  const JavaScript = await Language.load(langPath);

  const parser = new Parser();
  parser.setLanguage(JavaScript);

  // Test various JavaScript constructs
  const testCases = [
    {
      name: 'Regular function',
      code: 'function calculateSum(a, b) { return a + b; }'
    },
    {
      name: 'Arrow function',
      code: 'const multiply = (x, y) => x * y;'
    },
    {
      name: 'Async function',
      code: 'async function fetchData(url) { return await fetch(url); }'
    },
    {
      name: 'Generator function',
      code: 'function* fibonacci() { yield 1; }'
    },
    {
      name: 'Async generator',
      code: 'async function* asyncGen() { yield await something(); }'
    },
    {
      name: 'Class with methods',
      code: 'class Animal { constructor(name) {} speak() {} static create() {} }'
    },
    {
      name: 'Object method shorthand',
      code: 'const obj = { method() { return 1; } };'
    },
    {
      name: 'IIFE',
      code: '(function init() { console.log("init"); })();'
    },
    {
      name: 'Function with default and rest params',
      code: 'function process(a, b = 10, ...rest) { return rest; }'
    },
    {
      name: 'Prototype method',
      code: 'Animal.prototype.run = function() { return "running"; };'
    },
    {
      name: 'JSDoc annotated function',
      code: `/**
 * @param {number} x
 * @returns {number}
 */
function double(x) { return x * 2; }`
    }
  ];

  for (const testCase of testCases) {
    console.log(`\n=== ${testCase.name} ===`);
    console.log(`Code: ${testCase.code}`);

    const tree = parser.parse(testCase.code);
    const rootNode = tree.rootNode;

    console.log('AST Structure:');
    printRelevantNodes(rootNode, testCase.code, 0);

    tree.delete();
  }

  function printRelevantNodes(node, source, depth) {
    const indent = '  '.repeat(depth);

    // Skip uninteresting nodes
    const interestingTypes = [
      'function_declaration', 'generator_function_declaration',
      'function_expression', 'generator_function',
      'arrow_function', 'method_definition',
      'class_declaration', 'lexical_declaration',
      'variable_declarator', 'assignment_expression',
      'call_expression', 'comment', 'formal_parameters',
      'identifier', 'property_identifier'
    ];

    if (interestingTypes.includes(node.type) ||
        node.type.includes('function') ||
        node.type.includes('generator') ||
        node.type.includes('method') ||
        node.type.includes('class') ||
        depth === 0) {

      const text = source.substring(node.startIndex, node.endIndex);
      const preview = text.length > 40 ? text.substring(0, 40) + '...' : text;
      console.log(`${indent}${node.type}: "${preview.replace(/\n/g, ' ')}"`);

      // For certain nodes, show all children
      if (node.type.includes('declaration') || node.type.includes('function') || node.type.includes('method')) {
        for (let i = 0; i < node.childCount; i++) {
          const child = node.child(i);
          if (child) {
            printRelevantNodes(child, source, depth + 1);
          }
        }
      } else if (depth < 3) {
        // Otherwise, only go deeper for shallow nodes
        for (let i = 0; i < node.childCount; i++) {
          const child = node.child(i);
          if (child) {
            printRelevantNodes(child, source, depth + 1);
          }
        }
      }
    } else {
      // Still traverse children even if we don't print this node
      for (let i = 0; i < node.childCount; i++) {
        const child = node.child(i);
        if (child) {
          printRelevantNodes(child, source, depth);
        }
      }
    }
  }
}

analyzeJavaScriptAST().catch(console.error);