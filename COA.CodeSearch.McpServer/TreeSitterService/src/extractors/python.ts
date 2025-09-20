/**
 * Python specific type extractor
 */

import { GenericExtractor } from './generic';
import * as Parser from 'web-tree-sitter';
import { ExtractedType, ExtractedMethod, MethodParameter } from '../types';

export class PythonExtractor extends GenericExtractor {
  protected traverse(
    node: Parser.SyntaxNode,
    content: string,
    types: ExtractedType[],
    methods: ExtractedMethod[]
  ): void {
    const nodeType = node.type;

    switch (nodeType) {
      case 'class_definition':
        this.extractPythonClass(node, content, types);
        break;

      case 'function_definition':
        this.extractPythonFunction(node, content, methods);
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

  private extractPythonClass(
    node: Parser.SyntaxNode,
    content: string,
    types: ExtractedType[]
  ): void {
    const nameNode = this.findNodeByType(node, 'identifier');
    if (!nameNode) return;

    const name = this.getNodeText(nameNode, content);

    // Extract base classes
    const baseTypes = this.extractBaseClasses(node, content);

    // Extract decorators as modifiers
    const modifiers = this.extractDecorators(node, content);

    // Generate signature for the type
    const nodeText = this.getNodeText(node, content);
    const signature = nodeText.length > 150
      ? nodeText.substring(0, 150) + '...'
      : nodeText;

    types.push({
      name,
      kind: 'class',
      signature,
      line: node.startPosition.row + 1,
      column: node.startPosition.column + 1,
      endLine: node.endPosition.row + 1,
      endColumn: node.endPosition.column + 1,
      modifiers,
      baseTypes
    });
  }

  private extractPythonFunction(
    node: Parser.SyntaxNode,
    content: string,
    methods: ExtractedMethod[]
  ): void {
    const nameNode = this.findNodeByType(node, 'identifier');
    if (!nameNode) return;

    const name = this.getNodeText(nameNode, content);

    // Skip if this is a nested function (inside another function)
    if (this.isNestedFunction(node)) {
      return;
    }

    // Extract decorators
    const modifiers = this.extractDecorators(node, content);

    // Check if it's async
    const isAsync = this.isAsyncFunction(node);
    if (isAsync) {
      modifiers.push('async');
    }

    // Check if it's a class method
    const isClassMethod = this.isInsideClass(node);

    // Check if it's a constructor
    const isConstructor = name === '__init__' && isClassMethod;

    // Extract parameters
    const parameters = this.extractPythonParameters(node, content);

    // Extract return type hint if present
    const returnType = this.extractReturnTypeHint(node, content);

    // Build signature
    const signature = this.buildPythonSignature(name, parameters, returnType, isAsync);

    // Check for special method types
    if (isClassMethod) {
      if (parameters.length > 0) {
        const firstParam = parameters[0].name;
        if (firstParam === 'self') {
          // Instance method
        } else if (firstParam === 'cls') {
          modifiers.push('classmethod');
        }
      }
    }

    // Check for @staticmethod decorator
    if (modifiers.includes('@staticmethod')) {
      modifiers.push('static');
    }

    methods.push({
      name,
      signature,
      line: node.startPosition.row + 1,
      column: node.startPosition.column + 1,
      endLine: node.endPosition.row + 1,
      endColumn: node.endPosition.column + 1,
      modifiers,
      returnType,
      parameters: parameters.map(p => p.name && p.type ? `${p.name}: ${p.type}` : p.name || p.type || ''),
      isAsync,
      isConstructor
    });
  }

  private extractBaseClasses(node: Parser.SyntaxNode, content: string): string[] {
    const baseClasses: string[] = [];
    const argList = this.findNodeByType(node, 'argument_list');

    if (argList) {
      for (let i = 0; i < argList.childCount; i++) {
        const child = argList.child(i);
        if (child && (child.type === 'identifier' || child.type === 'attribute')) {
          baseClasses.push(this.getNodeText(child, content));
        }
      }
    }

    return baseClasses;
  }

  private extractDecorators(node: Parser.SyntaxNode, content: string): string[] {
    const decorators: string[] = [];

    // Check for preceding decorators
    const parent = node.parent;
    if (parent) {
      const nodeIndex = parent.children.indexOf(node);
      for (let i = nodeIndex - 1; i >= 0; i--) {
        const sibling = parent.child(i);
        if (sibling && sibling.type === 'decorator') {
          const decoratorText = this.getNodeText(sibling, content);
          decorators.unshift(decoratorText);
        } else if (sibling && sibling.type !== 'comment') {
          break; // Stop if we hit something that's not a decorator or comment
        }
      }
    }

    return decorators;
  }

  private extractPythonParameters(node: Parser.SyntaxNode, content: string): MethodParameter[] {
    const parameters: MethodParameter[] = [];
    const paramList = this.findNodeByType(node, 'parameters');

    if (paramList) {
      for (let i = 0; i < paramList.childCount; i++) {
        const child = paramList.child(i);
        if (child) {
          if (child.type === 'identifier') {
            // Simple parameter
            parameters.push({
              name: this.getNodeText(child, content),
              type: 'Any'
            });
          } else if (child.type === 'typed_parameter' || child.type === 'default_parameter') {
            const param = this.extractTypedParameter(child, content);
            if (param) {
              parameters.push(param);
            }
          }
        }
      }
    }

    return parameters;
  }

  private extractTypedParameter(node: Parser.SyntaxNode, content: string): MethodParameter | null {
    const nameNode = this.findNodeByType(node, 'identifier');
    if (!nameNode) return null;

    const name = this.getNodeText(nameNode, content);

    // Look for type annotation
    const typeNode = this.findNodeByType(node, 'type');
    const type = typeNode ? this.getNodeText(typeNode, content) : 'Any';

    // Look for default value
    const defaultNode = this.findNodeByType(node, 'expression');
    const defaultValue = defaultNode ? this.getNodeText(defaultNode, content) : undefined;

    return {
      name,
      type,
      defaultValue
    };
  }

  private extractReturnTypeHint(node: Parser.SyntaxNode, content: string): string | undefined {
    for (let i = 0; i < node.childCount; i++) {
      const child = node.child(i);
      if (child && child.type === 'type') {
        // This is the return type annotation
        return this.getNodeText(child, content);
      }
    }
    return undefined;
  }

  private isAsyncFunction(node: Parser.SyntaxNode): boolean {
    // Check if the function starts with 'async def'
    for (let i = 0; i < node.childCount; i++) {
      const child = node.child(i);
      if (child && child.type === 'async') {
        return true;
      }
    }
    return false;
  }

  private isNestedFunction(node: Parser.SyntaxNode): boolean {
    let parent = node.parent;
    while (parent) {
      if (parent.type === 'function_definition') {
        return true;
      }
      parent = parent.parent;
    }
    return false;
  }

  private isInsideClass(node: Parser.SyntaxNode): boolean {
    let parent = node.parent;
    while (parent) {
      if (parent.type === 'class_definition') {
        return true;
      }
      parent = parent.parent;
    }
    return false;
  }

  private buildPythonSignature(
    name: string,
    parameters: MethodParameter[],
    returnType: string | undefined,
    isAsync: boolean
  ): string {
    const parts: string[] = [];

    if (isAsync) {
      parts.push('async');
    }

    parts.push('def');
    parts.push(name);

    // Build parameter list
    const paramStr = parameters
      .map(p => {
        if (p.type && p.type !== 'Any') {
          return p.defaultValue
            ? `${p.name}: ${p.type} = ${p.defaultValue}`
            : `${p.name}: ${p.type}`;
        }
        return p.defaultValue
          ? `${p.name} = ${p.defaultValue}`
          : p.name;
      })
      .join(', ');

    parts.push(`(${paramStr})`);

    // Add return type if present
    if (returnType) {
      parts.push(`-> ${returnType}`);
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
}