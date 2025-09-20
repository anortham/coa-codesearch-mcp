/**
 * C# specific type extractor
 */

import { GenericExtractor } from './generic';
import * as Parser from 'web-tree-sitter';
import { ExtractedType, ExtractedMethod, MethodParameter } from '../types';

export class CSharpExtractor extends GenericExtractor {
  protected traverse(
    node: Parser.SyntaxNode,
    content: string,
    types: ExtractedType[],
    methods: ExtractedMethod[]
  ): void {
    const nodeType = node.type;

    switch (nodeType) {
      case 'class_declaration':
      case 'interface_declaration':
      case 'struct_declaration':
      case 'enum_declaration':
      case 'record_declaration':
        this.extractCSharpType(node, content, types);
        break;

      case 'method_declaration':
      case 'constructor_declaration':
      case 'property_declaration':
      case 'operator_declaration':
        this.extractCSharpMethod(node, content, methods);
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

  private extractCSharpType(
    node: Parser.SyntaxNode,
    content: string,
    types: ExtractedType[]
  ): void {
    const nameNode = this.findNodeByType(node, 'identifier');
    if (!nameNode) return;

    const name = this.getNodeText(nameNode, content);
    const kind = this.mapCSharpNodeTypeToKind(node.type);

    // Extract modifiers
    const modifiers = this.extractModifiers(node, content);

    // Extract base types
    const baseTypes = this.extractBaseTypes(node, content);

    // Extract type parameters
    const typeParameters = this.extractTypeParameters(node, content);

    // Generate signature for the type
    const nodeText = this.getNodeText(node, content);
    const signature = nodeText.length > 150
      ? nodeText.substring(0, 150) + '...'
      : nodeText;

    types.push({
      name,
      kind,
      signature,
      line: node.startPosition.row + 1,
      column: node.startPosition.column + 1,
      endLine: node.endPosition.row + 1,
      endColumn: node.endPosition.column + 1,
      modifiers,
      baseTypes,
      typeParameters
    });
  }

  private extractCSharpMethod(
    node: Parser.SyntaxNode,
    content: string,
    methods: ExtractedMethod[]
  ): void {
    const nameNode = this.findNodeByType(node, 'identifier');
    if (!nameNode) return;

    const name = this.getNodeText(nameNode, content);

    // Extract modifiers
    const modifiers = this.extractModifiers(node, content);

    // Extract return type
    const returnType = this.extractReturnType(node, content);

    // Extract parameters
    const parameters = this.extractParameters(node, content);

    // Check if async
    const isAsync = modifiers.includes('async');

    // Check if constructor
    const isConstructor = node.type === 'constructor_declaration';

    // Build signature
    const signature = this.buildCSharpSignature(
      name,
      returnType,
      parameters,
      modifiers,
      isConstructor
    );

    methods.push({
      name,
      signature,
      line: node.startPosition.row + 1,
      column: node.startPosition.column + 1,
      endLine: node.endPosition.row + 1,
      endColumn: node.endPosition.column + 1,
      modifiers,
      returnType,
      parameters: parameters.map(p => p.name && p.type ? `${p.type} ${p.name}` : p.type || p.name || ''),
      isAsync,
      isConstructor
    });
  }

  private extractModifiers(node: Parser.SyntaxNode, content: string): string[] {
    const modifiers: string[] = [];
    const modifierKeywords = [
      'public', 'private', 'protected', 'internal',
      'static', 'virtual', 'override', 'abstract', 'sealed',
      'async', 'readonly', 'partial', 'new'
    ];

    for (let i = 0; i < node.childCount; i++) {
      const child = node.child(i);
      if (child && child.type === 'modifier') {
        const text = this.getNodeText(child, content);
        if (modifierKeywords.includes(text)) {
          modifiers.push(text);
        }
      }
    }

    return modifiers;
  }

  private extractBaseTypes(node: Parser.SyntaxNode, content: string): string[] {
    const baseTypes: string[] = [];
    const baseList = this.findNodeByType(node, 'base_list');

    if (baseList) {
      for (let i = 0; i < baseList.childCount; i++) {
        const child = baseList.child(i);
        if (child && child.type === 'type') {
          baseTypes.push(this.getNodeText(child, content));
        }
      }
    }

    return baseTypes;
  }

  private extractTypeParameters(node: Parser.SyntaxNode, content: string): string[] {
    const typeParams: string[] = [];
    const typeParamList = this.findNodeByType(node, 'type_parameter_list');

    if (typeParamList) {
      for (let i = 0; i < typeParamList.childCount; i++) {
        const child = typeParamList.child(i);
        if (child && child.type === 'type_parameter') {
          const identifierNode = this.findNodeByType(child, 'identifier');
          if (identifierNode) {
            typeParams.push(this.getNodeText(identifierNode, content));
          }
        }
      }
    }

    return typeParams;
  }

  private extractReturnType(node: Parser.SyntaxNode, content: string): string {
    // Look for return type node
    const returnTypeNode = this.findNodeByType(node, 'type') ||
                          this.findNodeByType(node, 'predefined_type');

    if (returnTypeNode) {
      return this.getNodeText(returnTypeNode, content);
    }

    // For constructors, there's no return type
    if (node.type === 'constructor_declaration') {
      return '';
    }

    return 'void';
  }

  private extractParameters(node: Parser.SyntaxNode, content: string): MethodParameter[] {
    const parameters: MethodParameter[] = [];
    const paramList = this.findNodeByType(node, 'parameter_list');

    if (paramList) {
      for (let i = 0; i < paramList.childCount; i++) {
        const child = paramList.child(i);
        if (child && child.type === 'parameter') {
          const param = this.extractParameter(child, content);
          if (param) {
            parameters.push(param);
          }
        }
      }
    }

    return parameters;
  }

  private extractParameter(node: Parser.SyntaxNode, content: string): MethodParameter | null {
    const identifierNode = this.findNodeByType(node, 'identifier');
    const typeNode = this.findNodeByType(node, 'type') ||
                    this.findNodeByType(node, 'predefined_type');

    if (identifierNode && typeNode) {
      return {
        name: this.getNodeText(identifierNode, content),
        type: this.getNodeText(typeNode, content)
      };
    }

    return null;
  }

  private buildCSharpSignature(
    name: string,
    returnType: string | undefined,
    parameters: MethodParameter[],
    modifiers: string[],
    isConstructor: boolean
  ): string {
    const parts: string[] = [];

    // Add modifiers
    if (modifiers.length > 0) {
      parts.push(modifiers.join(' '));
    }

    // Add return type (unless constructor)
    if (!isConstructor && returnType) {
      parts.push(returnType);
    }

    // Add name
    parts.push(name);

    // Add parameters
    const paramStr = parameters
      .map(p => `${p.type} ${p.name}`)
      .join(', ');
    parts.push(`(${paramStr})`);

    return parts.join(' ');
  }

  private findNodeByType(node: Parser.SyntaxNode, type: string): Parser.SyntaxNode | null {
    for (let i = 0; i < node.childCount; i++) {
      const child = node.child(i);
      if (child && child.type === type) {
        return child;
      }
    }
    return null;
  }

  private mapCSharpNodeTypeToKind(nodeType: string): ExtractedType['kind'] {
    switch (nodeType) {
      case 'class_declaration':
        return 'class';
      case 'interface_declaration':
        return 'interface';
      case 'struct_declaration':
        return 'struct';
      case 'enum_declaration':
        return 'enum';
      case 'record_declaration':
        return 'class'; // Records are special classes
      default:
        return 'type';
    }
  }
}