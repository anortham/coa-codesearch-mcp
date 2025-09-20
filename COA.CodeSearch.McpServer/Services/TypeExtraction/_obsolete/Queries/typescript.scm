; TypeScript Tree-sitter Query File for Type Extraction
; This query file defines patterns to extract type information from TypeScript/TSX code

; ============================================================================
; Classes with decorators and inheritance
; ============================================================================
(class_declaration
  (decorator)* @class.decorators
  name: (type_identifier) @class.name
  type_parameters: (type_parameters)? @class.type_parameters
  (class_heritage
    (extends_clause value: (_) @class.extends)?
    (implements_clause [(type_identifier) (generic_type)]* @class.implements)?)?
  body: (class_body) @class.body) @class.definition

; ============================================================================
; Interfaces with type parameters
; ============================================================================
(interface_declaration
  name: (type_identifier) @interface.name
  type_parameters: (type_parameters)? @interface.type_parameters
  (extends_type_clause [(type_identifier) (generic_type)]* @interface.extends)?
  body: (object_type) @interface.body) @interface.definition

; ============================================================================
; Type aliases
; ============================================================================
(type_alias_declaration
  name: (type_identifier) @type_alias.name
  type_parameters: (type_parameters)? @type_alias.type_parameters
  value: (_) @type_alias.value) @type_alias.definition

; ============================================================================
; Enums with const modifier
; ============================================================================
(enum_declaration
  (["const"] @enum.const)?
  name: (identifier) @enum.name
  body: (enum_body
    (enum_assignment
      name: (property_identifier) @enum_member.name
      value: (_)? @enum_member.value)?)) @enum.definition

; ============================================================================
; Function declarations with overloads
; ============================================================================
(function_declaration
  (decorator)* @function.decorators
  (["export"] @function.export)?
  (["async"] @function.async)?
  (["function"] @function.keyword)
  name: (identifier) @function.name
  type_parameters: (type_parameters)? @function.type_parameters
  parameters: (formal_parameters
    (required_parameter
      (decorator)* @param.decorators
      pattern: [(identifier) (rest_pattern) (object_pattern) (array_pattern)] @param.pattern
      type_annotation: (type_annotation)? @param.type
      value: (_)? @param.default)?
    (optional_parameter
      pattern: (identifier) @param.name
      type_annotation: (type_annotation)? @param.type)?)?
  return_type: (type_annotation)? @function.return_type
  body: (statement_block) @function.body) @function.definition

; ============================================================================
; Arrow functions with type annotations
; ============================================================================
(lexical_declaration
  (variable_declarator
    name: (identifier) @arrow.name
    type_annotation: (type_annotation)? @arrow.type
    value: (arrow_function
      (["async"] @arrow.async)?
      type_parameters: (type_parameters)? @arrow.type_parameters
      parameters: [(identifier) (formal_parameters)] @arrow.parameters
      return_type: (type_annotation)? @arrow.return_type
      body: [(expression) (statement_block)] @arrow.body))) @arrow.definition

; ============================================================================
; Method signatures in classes
; ============================================================================
(class_body
  (method_signature
    (accessibility_modifier)? @method_sig.accessibility
    (["static"] @method_sig.static)?
    (["readonly"] @method_sig.readonly)?
    (["async"] @method_sig.async)?
    name: [(property_identifier) (computed_property_name)] @method_sig.name
    type_parameters: (type_parameters)? @method_sig.type_parameters
    parameters: (formal_parameters) @method_sig.parameters
    return_type: (type_annotation)? @method_sig.return_type)) @method_sig.definition

; ============================================================================
; Method definitions in classes
; ============================================================================
(class_body
  (method_definition
    (decorator)* @method.decorators
    (accessibility_modifier)? @method.accessibility
    (["static"] @method.static)?
    (["readonly"] @method.readonly)?
    (["async"] @method.async)?
    (["override"] @method.override)?
    name: [(property_identifier) (computed_property_name) (private_property_identifier)] @method.name
    type_parameters: (type_parameters)? @method.type_parameters
    parameters: (formal_parameters) @method.parameters
    return_type: (type_annotation)? @method.return_type
    body: (statement_block) @method.body)) @method.definition

; ============================================================================
; Constructor
; ============================================================================
(class_body
  (method_definition
    (accessibility_modifier)? @constructor.accessibility
    name: "constructor" @constructor.name
    parameters: (formal_parameters) @constructor.parameters
    body: (statement_block) @constructor.body)) @constructor.definition

; ============================================================================
; Properties with accessors
; ============================================================================
(class_body
  [(public_field_definition) (private_field_definition)]
    (decorator)* @property.decorators
    (accessibility_modifier)? @property.accessibility
    (["static"] @property.static)?
    (["readonly"] @property.readonly)?
    (["declare"] @property.declare)?
    (property_identifier) @property.name
    (["?"] @property.optional)?
    type_annotation: (type_annotation)? @property.type
    value: (_)? @property.initializer) @property.definition

; ============================================================================
; Getters and Setters
; ============================================================================
(class_body
  (accessor_definition
    (["get" "set"] @accessor.kind)
    name: (property_identifier) @accessor.name
    parameters: (formal_parameters)? @accessor.parameters
    return_type: (type_annotation)? @accessor.return_type
    body: (statement_block) @accessor.body)) @accessor.definition

; ============================================================================
; Namespaces/Modules
; ============================================================================
(module
  name: [(identifier) (nested_identifier) (string)] @namespace.name
  body: (statement_block) @namespace.body) @namespace.definition

(namespace_declaration
  name: [(identifier) (nested_identifier)] @namespace.name
  body: (statement_block) @namespace.body) @namespace.definition

; ============================================================================
; Import statements for dependency tracking
; ============================================================================
(import_statement
  (import_clause
    [(identifier) @import.default]?
    (named_imports
      (import_specifier
        (identifier) @import.name
        (["as"] (identifier) @import.alias)?)?)?)?
  source: (string) @import.source) @import.statement

; ============================================================================
; Export statements
; ============================================================================
(export_statement
  (["default"] @export.default)?
  declaration: [
    (class_declaration)
    (function_declaration)
    (interface_declaration)
    (type_alias_declaration)
    (enum_declaration)
    (variable_declaration)
  ] @export.declaration) @export.statement

(export_statement
  (export_clause
    (export_specifier
      name: (identifier) @export.name
      alias: (identifier)? @export.alias)?)) @export.named

; ============================================================================
; JSX/TSX Components (React)
; ============================================================================
(function_declaration
  name: (identifier) @component.name
  (#match? @component.name "^[A-Z]")
  parameters: (formal_parameters) @component.props
  return_type: (type_annotation
    (generic_type
      (type_identifier) @component.return_type
      (#match? @component.return_type "^(JSX\\.Element|ReactElement|ReactNode)")?))?
  body: (statement_block
    (return_statement
      (jsx_element) @component.jsx)?)) @component.definition

; ============================================================================
; Type guards
; ============================================================================
(function_declaration
  name: (identifier) @type_guard.name
  parameters: (formal_parameters) @type_guard.parameters
  return_type: (type_predicate_annotation
    (identifier) @type_guard.param_name
    (type_annotation) @type_guard.type)
  body: (statement_block) @type_guard.body) @type_guard.definition

; ============================================================================
; Generic constraints
; ============================================================================
(type_parameters
  (type_parameter
    (type_identifier) @type_param.name
    (constraint
      (type_identifier) @type_param.constraint)?
    (default_type
      (_) @type_param.default)?)) @type_params

; ============================================================================
; Decorators (for Angular/NestJS)
; ============================================================================
(decorator
  (call_expression
    function: (identifier) @decorator.name
    arguments: (arguments)? @decorator.args)) @decorator.expression