import { Parser } from 'web-tree-sitter';
import path from 'path';
import { fileURLToPath } from 'url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));

async function debugAST() {
  // Initialize parser
  await Parser.init({
    locateFile: (scriptName) => {
      if (scriptName === 'tree-sitter.wasm') {
        return path.join(__dirname, 'node_modules/web-tree-sitter/tree-sitter.wasm');
      }
      return scriptName;
    }
  });

  // Load TypeScript language
  const langPath = path.join(__dirname, 'node_modules/@vscode/tree-sitter-wasm/wasm/tree-sitter-typescript.wasm');
  const TypeScript = await Parser.Language.load(langPath);

  const parser = new Parser();
  parser.setLanguage(TypeScript);

  // Parse the interface with extends
  const code = `export interface AdminUser extends User {
  adminLevel: number;
  permissions: string[];
}`;

  const tree = parser.parse(code);
  const rootNode = tree.rootNode;

  // Find the interface declaration
  function findInterface(node) {
    if (node.type === 'interface_declaration') {
      return node;
    }
    for (let i = 0; i < node.childCount; i++) {
      const child = node.child(i);
      if (child) {
        const found = findInterface(child);
        if (found) return found;
      }
    }
    return null;
  }

  const interfaceNode = findInterface(rootNode);
  if (interfaceNode) {
    console.log("Interface node structure:");
    printNodeStructure(interfaceNode, code, 0);

    // Find extends clause
    console.log("\n\nLooking for extends clause:");
    for (let i = 0; i < interfaceNode.childCount; i++) {
      const child = interfaceNode.child(i);
      if (child) {
        console.log(`Child ${i}: ${child.type}`);
        if (child.type === 'extends_clause' || child.type.includes('extends')) {
          console.log("  Found extends clause!");
          printNodeStructure(child, code, 1);
        }
      }
    }
  }

  function printNodeStructure(node, source, depth) {
    const indent = '  '.repeat(depth);
    const text = source.substring(node.startIndex, node.endIndex);
    const preview = text.length > 40 ? text.substring(0, 40) + '...' : text;
    console.log(`${indent}${node.type} [${node.startPosition.row}:${node.startPosition.column}] "${preview.replace(/\n/g, '\\n')}"`);

    for (let i = 0; i < node.childCount; i++) {
      const child = node.child(i);
      if (child) {
        printNodeStructure(child, source, depth + 1);
      }
    }
  }

  tree.delete();
}

debugAST().catch(console.error);