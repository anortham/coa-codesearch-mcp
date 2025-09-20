/**
 * Go specific type extractor
 */

import { GenericExtractor } from './generic';
import * as Parser from 'web-tree-sitter';
import { ExtractedType, ExtractedMethod, MethodParameter } from '../types';

export class GoExtractor extends GenericExtractor {
  protected traverse(
    node: Parser.SyntaxNode,
    content: string,
    types: ExtractedType[],
    methods: ExtractedMethod[]
  ): void {
    const nodeType = node.type;

    switch (nodeType) {
      case 'type_declaration':
        this.extractGoType(node, content, types);
        // Don't traverse children since we handle type_spec inside extractGoType
        return;
      case 'type_spec':
        // Only process if not already handled by parent type_declaration
        if (node.parent?.type !== 'type_declaration') {
          this.extractGoType(node, content, types);
        }
        break;

      case 'function_declaration':
      case 'method_declaration':
        this.extractGoFunction(node, content, methods);
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

  private extractGoType(
    node: Parser.SyntaxNode,
    content: string,
    types: ExtractedType[]
  ): void {
    // For type_declaration, look for type_spec child
    if (node.type === 'type_declaration') {
      const typeSpec = this.findNodeByType(node, 'type_spec');
      if (typeSpec) {
        this.extractGoType(typeSpec, content, types);
      }
      return;
    }

    const nameNode = this.findNodeByType(node, 'type_identifier');
    if (!nameNode) return;

    const name = this.getNodeText(nameNode, content);

    // Determine the kind based on the type definition
    let kind: ExtractedType['kind'] = 'type';
    let baseTypes: string[] = [];

    // Look for the type definition
    for (let i = 0; i < node.childCount; i++) {
      const child = node.child(i);
      if (child) {
        switch (child.type) {
          case 'struct_type':
            kind = 'struct';
            break;
          case 'interface_type':
            kind = 'interface';
            break;
          case 'type_identifier':
            if (child !== nameNode) {
              // This is a type alias
              baseTypes.push(this.getNodeText(child, content));
            }
            break;
        }
      }
    }

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
      baseTypes: baseTypes.length > 0 ? baseTypes : undefined
    });
  }

  private extractGoFunction(
    node: Parser.SyntaxNode,
    content: string,
    methods: ExtractedMethod[]
  ): void {
    const nameNode = this.findNodeByType(node, 'identifier');
    if (!nameNode) return;

    const name = this.getNodeText(nameNode, content);

    // Check if this is a method (has receiver)
    const receiverNode = this.findNodeByType(node, 'parameter_list');
    const hasReceiver = node.type === 'method_declaration';

    // Extract parameters
    const parameters = this.extractGoParameters(node, content, hasReceiver);

    // Extract return type
    const returnType = this.extractGoReturnType(node, content);

    // Build signature
    const signature = this.buildGoSignature(
      name,
      parameters,
      returnType,
      hasReceiver,
      receiverNode,
      content
    );

    methods.push({
      name,
      signature,
      line: node.startPosition.row + 1,
      column: node.startPosition.column + 1,
      endLine: node.endPosition.row + 1,
      endColumn: node.endPosition.column + 1,
      returnType,
      parameters: parameters.map(p => p.name && p.type ? `${p.name} ${p.type}` : p.type || p.name || ''),
      isConstructor: name === 'New' || name.startsWith('New')
    });
  }

  private extractGoParameters(
    node: Parser.SyntaxNode,
    content: string,
    skipFirst: boolean
  ): MethodParameter[] {
    const parameters: MethodParameter[] = [];
    let paramLists = [];

    // Find all parameter lists
    for (let i = 0; i < node.childCount; i++) {
      const child = node.child(i);
      if (child && child.type === 'parameter_list') {
        paramLists.push(child);
      }
    }

    // Skip receiver if this is a method
    if (skipFirst && paramLists.length > 1) {
      paramLists = paramLists.slice(1);
    }

    for (const paramList of paramLists) {
      for (let i = 0; i < paramList.childCount; i++) {
        const child = paramList.child(i);
        if (child && child.type === 'parameter_declaration') {
          const param = this.extractGoParameter(child, content);
          if (param) {
            parameters.push(param);
          }
        }
      }
    }

    return parameters;
  }

  private extractGoParameter(node: Parser.SyntaxNode, content: string): MethodParameter | null {
    // Go parameters can be:
    // - name type
    // - name, name2 type
    // - type (unnamed)

    const identifiers: string[] = [];
    let typeText = '';

    for (let i = 0; i < node.childCount; i++) {
      const child = node.child(i);
      if (child) {
        if (child.type === 'identifier') {
          identifiers.push(this.getNodeText(child, content));
        } else if (child.type.includes('type') || child.type === 'pointer_type') {
          typeText = this.getNodeText(child, content);
        }
      }
    }

    if (typeText) {
      // If we have identifiers, create parameters for each
      if (identifiers.length > 0) {
        // Return only the first, but note that Go allows multiple params with same type
        return {
          name: identifiers[0],
          type: typeText
        };
      } else {
        // Unnamed parameter
        return {
          name: '_',
          type: typeText
        };
      }
    }

    return null;
  }

  private extractGoReturnType(node: Parser.SyntaxNode, content: string): string | undefined {
    // Look for result node
    for (let i = 0; i < node.childCount; i++) {
      const child = node.child(i);
      if (child && child.type === 'result') {
        // Result can be a single type or parameter list
        if (child.childCount === 1) {
          const typeNode = child.child(0);
          if (typeNode) {
            return this.getNodeText(typeNode, content);
          }
        } else {
          // Multiple return values
          const types: string[] = [];
          for (let j = 0; j < child.childCount; j++) {
            const typeChild = child.child(j);
            if (typeChild && typeChild.type === 'parameter_list') {
              // Extract types from parameter list
              for (let k = 0; k < typeChild.childCount; k++) {
                const param = typeChild.child(k);
                if (param && param.type === 'parameter_declaration') {
                  const typeNode = this.findNodeWithType(param);
                  if (typeNode) {
                    types.push(this.getNodeText(typeNode, content));
                  }
                }
              }
            } else if (typeChild) {
              types.push(this.getNodeText(typeChild, content));
            }
          }
          return `(${types.join(', ')})`;
        }
      }
    }

    return undefined;
  }

  private buildGoSignature(
    name: string,
    parameters: MethodParameter[],
    returnType: string | undefined,
    isMethod: boolean,
    receiverNode: Parser.SyntaxNode | null,
    content: string
  ): string {
    const parts: string[] = [];

    parts.push('func');

    // Add receiver if this is a method
    if (isMethod && receiverNode) {
      const receiverText = this.getNodeText(receiverNode, content);
      parts.push(receiverText);
    }

    parts.push(name);

    // Add parameters
    const paramStr = parameters
      .map(p => p.name === '_' ? p.type : `${p.name} ${p.type}`)
      .join(', ');
    parts.push(`(${paramStr})`);

    // Add return type if present
    if (returnType) {
      parts.push(returnType);
    }

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

  private findNodeWithType(node: Parser.SyntaxNode): Parser.SyntaxNode | null {
    for (let i = 0; i < node.childCount; i++) {
      const child = node.child(i);
      if (child && (child.type.includes('type') || child.type === 'pointer_type')) {
        return child;
      }
    }
    return null;
  }
}