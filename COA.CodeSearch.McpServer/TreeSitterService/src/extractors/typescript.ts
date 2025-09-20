/**
 * TypeScript specific type extractor
 * Handles interfaces, generics, decorators, namespaces, type aliases, enums
 */

import { GenericExtractor } from './generic';
import * as Parser from 'web-tree-sitter';
import { ExtractedType, ExtractedMethod, MethodParameter } from '../types';

export class TypeScriptExtractor extends GenericExtractor {
  protected traverse(
    node: Parser.SyntaxNode,
    content: string,
    types: ExtractedType[],
    methods: ExtractedMethod[]
  ): void {
    const nodeType = node.type;

    switch (nodeType) {
      // Type declarations
      case 'interface_declaration':
        this.extractInterface(node, content, types);
        break;
      case 'class_declaration':
        this.extractClass(node, content, types);
        break;
      case 'type_alias_declaration':
        this.extractTypeAlias(node, content, types);
        break;
      case 'enum_declaration':
        this.extractEnum(node, content, types);
        break;
      case 'namespace_declaration':
      case 'module_declaration':
        this.extractNamespace(node, content, types);
        break;

      // Method/Function declarations
      case 'method_definition':
      case 'method_signature':
        this.extractMethod(node, content, methods);
        break;
      case 'function_declaration':
      case 'function_signature':
      case 'generator_function_declaration':  // JavaScript generator functions
        this.extractFunction(node, content, methods);
        break;
      case 'arrow_function':
      case 'function_expression':
      case 'generator_function':  // Generator function expressions
        // Only extract if not inside a variable declaration (to avoid duplicates)
        if (!this.isInsideVariableDeclaration(node)) {
          this.extractFunctionExpression(node, content, methods);
        }
        break;
      case 'lexical_declaration': // const/let declarations that might be functions
      case 'variable_declaration': // var declarations
        this.extractVariableFunction(node, content, methods);
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

  private extractInterface(
    node: Parser.SyntaxNode,
    content: string,
    types: ExtractedType[]
  ): void {
    const nameNode = this.findNodeByType(node, 'type_identifier') ||
                     this.findNodeByType(node, 'identifier');
    if (!nameNode) return;

    const name = this.getNodeText(nameNode, content);

    // Extract modifiers (export, declare, etc.)
    const modifiers = this.extractModifiers(node, content);
    const isExported = this.hasExportKeyword(node, content);

    // Extract extends clause (VS Code WASM uses 'extends_type_clause' for interfaces)
    const baseTypes: string[] = [];
    const extendsClause = this.findNodeByType(node, 'extends_type_clause') ||
                         this.findNodeByType(node, 'extends_clause');
    if (extendsClause) {
      // For extends_type_clause, the text after "extends" is the type
      const clauseText = this.getNodeText(extendsClause, content);
      // Remove "extends" keyword and trim
      const typeText = clauseText.replace(/^extends\s+/, '').trim();
      if (typeText) {
        // Split by comma for multiple extends (though interfaces typically only extend one)
        const types = typeText.split(',').map(t => t.trim());
        baseTypes.push(...types);
      }
    }

    // Extract type parameters
    const typeParameters = this.extractTypeParameters(node, content);

    // Get namespace if inside one
    const namespace = this.getContainingNamespace(node, content);

    // Get full signature - include full first line or up to 200 chars
    const fullText = this.getNodeText(node, content);
    const firstLine = fullText.split('\n')[0];
    const signature = firstLine.length <= 200 ? firstLine : firstLine.substring(0, 200) + '...';

    types.push({
      name,
      kind: 'interface',
      signature,
      line: node.startPosition.row + 1,
      column: node.startPosition.column + 1,
      endLine: node.endPosition.row + 1,
      endColumn: node.endPosition.column + 1,
      modifiers,
      baseTypes,
      typeParameters,
      namespace,
      isExported
    });
  }

  private extractClass(
    node: Parser.SyntaxNode,
    content: string,
    types: ExtractedType[]
  ): void {
    const nameNode = this.findNodeByType(node, 'type_identifier') ||
                     this.findNodeByType(node, 'identifier');
    if (!nameNode) return;

    const name = this.getNodeText(nameNode, content);

    // Extract decorators
    const decorators = this.extractDecorators(node, content);
    const modifiers = [...this.extractModifiers(node, content), ...decorators];
    const isExported = this.hasExportKeyword(node, content);

    // Extract extends and implements
    const baseTypes: string[] = [];
    const interfaces: string[] = [];

    const heritage = this.findNodeByType(node, 'class_heritage');
    if (heritage) {
      const extendsClause = this.findNodeByType(heritage, 'extends_clause');
      if (extendsClause) {
        const extendsType = this.findNodeByType(extendsClause, 'expression_statement') ||
                           this.findNodeByType(extendsClause, 'identifier');
        if (extendsType) {
          baseTypes.push(this.getNodeText(extendsType, content));
        }
      }

      const implementsClause = this.findNodeByType(heritage, 'implements_clause');
      if (implementsClause) {
        const implTypes = this.findNodesOfType(implementsClause, 'type_identifier');
        implTypes.forEach(t => interfaces.push(this.getNodeText(t, content)));
      }
    }

    // Extract type parameters
    const typeParameters = this.extractTypeParameters(node, content);

    // Get namespace if inside one
    const namespace = this.getContainingNamespace(node, content);

    types.push({
      name,
      kind: 'class',
      signature: this.getNodeText(node, content).split('\n')[0].substring(0, 200),
      line: node.startPosition.row + 1,
      column: node.startPosition.column + 1,
      endLine: node.endPosition.row + 1,
      endColumn: node.endPosition.column + 1,
      modifiers,
      baseTypes,
      interfaces,
      typeParameters,
      namespace,
      isExported
    });
  }

  private extractTypeAlias(
    node: Parser.SyntaxNode,
    content: string,
    types: ExtractedType[]
  ): void {
    const nameNode = this.findNodeByType(node, 'type_identifier') ||
                     this.findNodeByType(node, 'identifier');
    if (!nameNode) return;

    const name = this.getNodeText(nameNode, content);
    const modifiers = this.extractModifiers(node, content);
    const isExported = this.hasExportKeyword(node, content);
    const typeParameters = this.extractTypeParameters(node, content);
    const namespace = this.getContainingNamespace(node, content);

    types.push({
      name,
      kind: 'type_alias',
      signature: this.getNodeText(node, content).substring(0, 200),
      line: node.startPosition.row + 1,
      column: node.startPosition.column + 1,
      endLine: node.endPosition.row + 1,
      endColumn: node.endPosition.column + 1,
      modifiers,
      typeParameters,
      namespace,
      isExported
    });
  }

  private extractEnum(
    node: Parser.SyntaxNode,
    content: string,
    types: ExtractedType[]
  ): void {
    const nameNode = this.findNodeByType(node, 'identifier');
    if (!nameNode) return;

    const name = this.getNodeText(nameNode, content);
    const modifiers = this.extractModifiers(node, content);
    const isExported = this.hasExportKeyword(node, content);

    // Check if it's a const enum
    const isConst = modifiers.includes('const');
    const namespace = this.getContainingNamespace(node, content);

    types.push({
      name,
      kind: 'enum',
      signature: this.getNodeText(node, content).split('\n')[0].substring(0, 200),
      line: node.startPosition.row + 1,
      column: node.startPosition.column + 1,
      endLine: node.endPosition.row + 1,
      endColumn: node.endPosition.column + 1,
      modifiers,
      namespace,
      isExported
    });
  }

  private extractNamespace(
    node: Parser.SyntaxNode,
    content: string,
    types: ExtractedType[]
  ): void {
    const nameNode = this.findNodeByType(node, 'identifier') ||
                     this.findNodeByType(node, 'nested_identifier');
    if (!nameNode) return;

    const name = this.getNodeText(nameNode, content);
    const modifiers = this.extractModifiers(node, content);
    const isExported = this.hasExportKeyword(node, content);

    types.push({
      name,
      kind: node.type === 'module_declaration' ? 'module' : 'namespace',
      signature: this.getNodeText(node, content).split('\n')[0].substring(0, 200),
      line: node.startPosition.row + 1,
      column: node.startPosition.column + 1,
      endLine: node.endPosition.row + 1,
      endColumn: node.endPosition.column + 1,
      modifiers,
      isExported
    });
  }

  private extractMethod(
    node: Parser.SyntaxNode,
    content: string,
    methods: ExtractedMethod[]
  ): void {
    const nameNode = this.findNodeByType(node, 'property_identifier') ||
                     this.findNodeByType(node, 'identifier');
    if (!nameNode) return;

    const name = this.getNodeText(nameNode, content);

    // Get containing class
    const classNode = this.findAncestor(node, 'class_declaration');
    const className = classNode ? this.getClassName(classNode, content) : undefined;

    // Extract decorators
    const decorators = this.extractDecorators(node, content);
    const modifiers = [...this.extractModifiers(node, content), ...decorators];

    // Check various method properties
    const isAsync = this.hasAsyncKeyword(node, content);
    const isStatic = modifiers.includes('static');
    const isGenerator = this.hasGeneratorStar(node, content);
    const isExported = this.hasExportKeyword(node, content);

    // Extract return type
    const returnType = this.extractReturnType(node, content);

    // Extract parameters
    const detailedParameters = this.extractMethodParameters(node, content);

    methods.push({
      name,
      signature: this.getNodeText(node, content).split('\n')[0].substring(0, 200),
      line: node.startPosition.row + 1,
      column: node.startPosition.column + 1,
      endLine: node.endPosition.row + 1,
      endColumn: node.endPosition.column + 1,
      modifiers,
      returnType,
      detailedParameters,
      isAsync,
      isStatic,
      isGenerator,
      isExported,
      className,
      containingType: className
    });
  }

  private extractFunction(
    node: Parser.SyntaxNode,
    content: string,
    methods: ExtractedMethod[]
  ): void {
    const nameNode = this.findNodeByType(node, 'identifier');
    if (!nameNode) return;

    const name = this.getNodeText(nameNode, content);
    const modifiers = this.extractModifiers(node, content);
    const isAsync = this.hasAsyncKeyword(node, content);
    // Generator functions have specific node types
    const isGenerator = node.type === 'generator_function_declaration' ||
                       node.type === 'generator_function' ||
                       this.hasGeneratorStar(node, content);
    const isExported = this.hasExportKeyword(node, content);
    const returnType = this.extractReturnType(node, content);
    const detailedParameters = this.extractMethodParameters(node, content);

    methods.push({
      name,
      signature: this.getNodeText(node, content).split('\n')[0].substring(0, 200),
      line: node.startPosition.row + 1,
      column: node.startPosition.column + 1,
      endLine: node.endPosition.row + 1,
      endColumn: node.endPosition.column + 1,
      modifiers,
      returnType,
      detailedParameters,
      isAsync,
      isGenerator,
      isExported
    });
  }

  private extractFunctionExpression(
    node: Parser.SyntaxNode,
    content: string,
    methods: ExtractedMethod[]
  ): void {
    // For arrow functions and anonymous functions assigned to variables
    // Look for parent variable declaration
    const parent = node.parent;
    if (!parent) return;

    let name = 'anonymous';
    let isExported = false;

    // First check if the function expression itself has a name (named function expression)
    const functionNameNode = this.findNodeByType(node, 'identifier');
    if (functionNameNode) {
      name = this.getNodeText(functionNameNode, content);
    }
    // Otherwise, check if it's assigned to a variable
    else if (parent.type === 'variable_declarator') {
      const idNode = this.findNodeByType(parent, 'identifier');
      if (idNode) {
        name = this.getNodeText(idNode, content);
      }

      // Check if the variable declaration is exported
      const lexicalDecl = parent.parent;
      if (lexicalDecl) {
        isExported = this.hasExportKeyword(lexicalDecl, content);
      }
    }

    const isAsync = this.hasAsyncKeyword(node, content);
    const isGenerator = node.type === 'generator_function' ||
                       this.hasGeneratorStar(node, content);
    const returnType = this.extractReturnType(node, content);
    const detailedParameters = this.extractMethodParameters(node, content);

    methods.push({
      name,
      signature: this.getNodeText(node, content).split('\n')[0].substring(0, 200),
      line: node.startPosition.row + 1,
      column: node.startPosition.column + 1,
      endLine: node.endPosition.row + 1,
      endColumn: node.endPosition.column + 1,
      returnType,
      detailedParameters,
      isAsync,
      isGenerator,
      isExported
    });
  }

  private extractVariableFunction(
    node: Parser.SyntaxNode,
    content: string,
    methods: ExtractedMethod[]
  ): void {
    // Handle const/let declarations that contain functions
    const declarators = this.findNodesOfType(node, 'variable_declarator');

    for (const declarator of declarators) {
      const valueNode = this.findNodeByType(declarator, 'arrow_function') ||
                       this.findNodeByType(declarator, 'function_expression');

      if (valueNode) {
        this.extractFunctionExpression(valueNode, content, methods);
      }
    }
  }

  // Helper methods

  private extractModifiers(node: Parser.SyntaxNode, content: string): string[] {
    const modifiers: string[] = [];

    // Check for various modifier keywords
    const modifierKeywords = ['export', 'declare', 'abstract', 'static', 'readonly',
                              'private', 'protected', 'public', 'async', 'const'];

    for (let i = 0; i < node.childCount; i++) {
      const child = node.child(i);
      if (child && modifierKeywords.includes(child.type)) {
        modifiers.push(child.type);
      }
    }

    return modifiers;
  }

  private extractDecorators(node: Parser.SyntaxNode, content: string): string[] {
    const decorators: string[] = [];

    // Look for decorator nodes
    for (let i = 0; i < node.childCount; i++) {
      const child = node.child(i);
      if (child && child.type === 'decorator') {
        const decoratorText = this.getNodeText(child, content);
        decorators.push(decoratorText);
      }
    }

    return decorators;
  }

  private extractTypeParameters(node: Parser.SyntaxNode, content: string): string[] {
    const params: string[] = [];
    const typeParamsNode = this.findNodeByType(node, 'type_parameters');

    if (typeParamsNode) {
      const typeParams = this.findNodesOfType(typeParamsNode, 'type_parameter');
      typeParams.forEach(p => {
        const nameNode = this.findNodeByType(p, 'type_identifier');
        if (nameNode) {
          params.push(this.getNodeText(nameNode, content));
        }
      });
    }

    return params;
  }

  private extractReturnType(node: Parser.SyntaxNode, content: string): string {
    // Look for type annotation
    const typeAnnotation = this.findNodeByType(node, 'type_annotation');
    if (typeAnnotation) {
      // Skip the colon and get the actual type
      for (let i = 0; i < typeAnnotation.childCount; i++) {
        const child = typeAnnotation.child(i);
        if (child && child.type !== ':') {
          return this.getNodeText(child, content);
        }
      }
    }

    return 'void';
  }

  private extractMethodParameters(node: Parser.SyntaxNode, content: string): MethodParameter[] {
    const params: MethodParameter[] = [];
    const parametersNode = this.findNodeByType(node, 'formal_parameters');

    if (parametersNode) {
      for (let i = 0; i < parametersNode.childCount; i++) {
        const child = parametersNode.child(i);
        if (child && (child.type === 'required_parameter' ||
                     child.type === 'optional_parameter' ||
                     child.type === 'rest_parameter')) {
          const param = this.extractParameter(child, content);
          if (param) params.push(param);
        }
      }
    }

    return params;
  }

  private extractParameter(node: Parser.SyntaxNode, content: string): MethodParameter | null {
    const nameNode = this.findNodeByType(node, 'identifier');
    if (!nameNode) return null;

    const name = this.getNodeText(nameNode, content);

    // Extract type
    let type: string | undefined;
    const typeAnnotation = this.findNodeByType(node, 'type_annotation');
    if (typeAnnotation) {
      for (let i = 0; i < typeAnnotation.childCount; i++) {
        const child = typeAnnotation.child(i);
        if (child && child.type !== ':') {
          type = this.getNodeText(child, content);
          break;
        }
      }
    }

    // Check for default value
    let hasDefaultValue = false;
    let defaultValue: string | undefined;
    const initNode = this.findNodeByType(node, 'initializer');
    if (initNode) {
      hasDefaultValue = true;
      // Skip the '=' and get the value
      for (let i = 0; i < initNode.childCount; i++) {
        const child = initNode.child(i);
        if (child && child.type !== '=') {
          defaultValue = this.getNodeText(child, content);
          break;
        }
      }
    }

    // Check if it's a rest parameter
    const isRestParameter = node.type === 'rest_parameter';

    // Check if it's optional (has ? or default value)
    const isOptional = node.type === 'optional_parameter' || hasDefaultValue;

    return {
      name,
      type,
      hasDefaultValue,
      defaultValue,
      isRestParameter,
      isOptional
    };
  }

  private hasExportKeyword(node: Parser.SyntaxNode, content: string): boolean {
    // Check if node or its parent has export keyword
    let current: Parser.SyntaxNode | null = node;
    while (current && current.parent) {
      for (let i = 0; i < current.childCount; i++) {
        const child = current.child(i);
        if (child && child.type === 'export') {
          return true;
        }
      }

      // Don't go too far up
      if (current.type === 'program' || current.type === 'module') break;
      current = current.parent;
    }
    return false;
  }

  private hasAsyncKeyword(node: Parser.SyntaxNode, content: string): boolean {
    for (let i = 0; i < node.childCount; i++) {
      const child = node.child(i);
      if (child && child.type === 'async') {
        return true;
      }
    }
    return false;
  }

  private hasGeneratorStar(node: Parser.SyntaxNode, content: string): boolean {
    // Look for generator functions (function*)
    for (let i = 0; i < node.childCount; i++) {
      const child = node.child(i);
      if (child && child.type === '*') {
        return true;
      }
    }
    return false;
  }

  private getContainingNamespace(node: Parser.SyntaxNode, content: string): string | undefined {
    let current = node.parent;
    while (current) {
      if (current.type === 'namespace_declaration' || current.type === 'module_declaration') {
        const nameNode = this.findNodeByType(current, 'identifier') ||
                        this.findNodeByType(current, 'nested_identifier');
        if (nameNode) {
          return this.getNodeText(nameNode, content);
        }
      }
      current = current.parent;
    }
    return undefined;
  }

  private getClassName(classNode: Parser.SyntaxNode, content: string): string | undefined {
    const nameNode = this.findNodeByType(classNode, 'type_identifier') ||
                     this.findNodeByType(classNode, 'identifier');
    return nameNode ? this.getNodeText(nameNode, content) : undefined;
  }

  private findAncestor(node: Parser.SyntaxNode, type: string): Parser.SyntaxNode | null {
    let current = node.parent;
    while (current) {
      if (current.type === type) {
        return current;
      }
      current = current.parent;
    }
    return null;
  }

  private isInsideVariableDeclaration(node: Parser.SyntaxNode): boolean {
    let current = node.parent;
    while (current) {
      if (current.type === 'variable_declarator' ||
          current.type === 'lexical_declaration' ||
          current.type === 'variable_declaration') {
        return true;
      }
      current = current.parent;
    }
    return false;
  }

  private findNodesOfType(node: Parser.SyntaxNode, type: string): Parser.SyntaxNode[] {
    const results: Parser.SyntaxNode[] = [];

    const traverse = (n: Parser.SyntaxNode) => {
      if (n.type === type) {
        results.push(n);
      }
      for (let i = 0; i < n.childCount; i++) {
        const child = n.child(i);
        if (child) traverse(child);
      }
    };

    traverse(node);
    return results;
  }
}