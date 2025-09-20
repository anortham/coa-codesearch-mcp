/**
 * Generic type extractor - fallback for all languages
 */

import * as Parser from 'web-tree-sitter';
import { TypeExtractionResult, ExtractedType, ExtractedMethod } from '../types';

export class GenericExtractor {
  async extract(
    rootNode: Parser.SyntaxNode,
    content: string,
    language: string
  ): Promise<TypeExtractionResult> {
    const types: ExtractedType[] = [];
    const methods: ExtractedMethod[] = [];

    this.traverse(rootNode, content, types, methods);

    return {
      success: true,
      types,
      methods,
      language
    };
  }

  protected traverse(
    node: Parser.SyntaxNode,
    content: string,
    types: ExtractedType[],
    methods: ExtractedMethod[]
  ): void {
    const nodeType = node.type;

    // Common patterns across languages
    switch (nodeType) {
      case 'class_declaration':
      case 'class_definition':
      case 'interface_declaration':
      case 'struct_declaration':
      case 'enum_declaration':
      case 'type_declaration':
      case 'type_definition':
      case 'type_alias':
        this.extractType(node, content, types);
        break;

      case 'function_declaration':
      case 'function_definition':
      case 'method_declaration':
      case 'method_definition':
      case 'constructor_declaration':
      case 'arrow_function':
      case 'function_expression':
        this.extractMethod(node, content, methods);
        break;
    }

    // Traverse children
    for (let i = 0; i < node.childCount; i++) {
      const child = node.child(i);
      if (child) {
        this.traverse(child, content, types, methods);
      }
    }
  }

  protected extractType(
    node: Parser.SyntaxNode,
    content: string,
    types: ExtractedType[]
  ): void {
    const nameNode = this.findNameNode(node);
    if (!nameNode) return;

    const name = this.getNodeText(nameNode, content);
    if (!name) return;

    const kind = this.mapNodeTypeToKind(node.type);

    // Generate a basic signature from the node text
    // This can be overridden by language-specific extractors for better formatting
    const nodeText = this.getNodeText(node, content);
    const signature = nodeText.length > 100
      ? nodeText.substring(0, 100) + '...'
      : nodeText;

    types.push({
      name,
      kind,
      signature,
      line: node.startPosition.row + 1,
      column: node.startPosition.column + 1,
      endLine: node.endPosition.row + 1,
      endColumn: node.endPosition.column + 1
    });
  }

  protected extractMethod(
    node: Parser.SyntaxNode,
    content: string,
    methods: ExtractedMethod[]
  ): void {
    const nameNode = this.findNameNode(node);
    if (!nameNode) return;

    const name = this.getNodeText(nameNode, content);
    if (!name) return;

    // Try to build a signature
    const signature = this.buildMethodSignature(node, content, name);

    methods.push({
      name,
      signature,
      line: node.startPosition.row + 1,
      column: node.startPosition.column + 1,
      endLine: node.endPosition.row + 1,
      endColumn: node.endPosition.column + 1
    });
  }

  protected findNameNode(node: Parser.SyntaxNode): Parser.SyntaxNode | null {
    // Common name node types
    const nameTypes = ['identifier', 'name', 'type_identifier', 'field_identifier'];

    for (let i = 0; i < node.childCount; i++) {
      const child = node.child(i);
      if (child && nameTypes.includes(child.type)) {
        return child;
      }
    }

    // Try to find nested name
    for (let i = 0; i < node.childCount; i++) {
      const child = node.child(i);
      if (child && (child.type === 'name' || child.type.includes('name'))) {
        const nameChild = this.findNameNode(child);
        if (nameChild) return nameChild;
      }
    }

    return null;
  }

  protected getNodeText(node: Parser.SyntaxNode, content: string): string {
    const start = node.startIndex;
    const end = node.endIndex;
    return content.substring(start, end).trim();
  }

  protected mapNodeTypeToKind(nodeType: string): ExtractedType['kind'] {
    if (nodeType.includes('class')) return 'class';
    if (nodeType.includes('interface')) return 'interface';
    if (nodeType.includes('struct')) return 'struct';
    if (nodeType.includes('enum')) return 'enum';
    return 'type';
  }

  protected buildMethodSignature(
    node: Parser.SyntaxNode,
    content: string,
    name: string
  ): string {
    // Try to extract the full method text as signature
    const methodText = this.getNodeText(node, content);

    // If it's too long, just use the first line
    const firstLine = methodText.split('\n')[0];
    if (firstLine.length < 100) {
      return firstLine;
    }

    // Fallback to just the name with parentheses
    return `${name}()`;
  }

  // Additional helper methods for derived extractors
  protected findNodeByType(node: Parser.SyntaxNode, type: string): Parser.SyntaxNode | null {
    if (node.type === type) return node;

    for (let i = 0; i < node.childCount; i++) {
      const child = node.child(i);
      if (child) {
        const found = this.findNodeByType(child, type);
        if (found) return found;
      }
    }
    return null;
  }
}