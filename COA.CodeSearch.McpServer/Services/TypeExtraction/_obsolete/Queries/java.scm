; Java Tree-sitter Query File for Type Extraction
; This query file defines patterns to extract type information from Java code

; ============================================================================
; Classes with full metadata
; ============================================================================
(class_declaration
  (modifiers)? @class.modifiers
  name: (identifier) @class.name
  type_parameters: (type_parameters)? @class.type_parameters
  superclass: (superclass (type_identifier) @class.extends)?
  interfaces: (super_interfaces
    (type_list
      (type_identifier) @class.implements
      (generic_type) @class.implements)?)?
  body: (class_body) @class.body) @class.definition

; ============================================================================
; Interfaces with inheritance
; ============================================================================
(interface_declaration
  (modifiers)? @interface.modifiers
  name: (identifier) @interface.name
  type_parameters: (type_parameters)? @interface.type_parameters
  (extends_interfaces
    (type_list
      (type_identifier) @interface.extends
      (generic_type) @interface.extends)?)?
  body: (interface_body) @interface.body) @interface.definition

; ============================================================================
; Enums with values
; ============================================================================
(enum_declaration
  (modifiers)? @enum.modifiers
  name: (identifier) @enum.name
  interfaces: (super_interfaces
    (type_list
      (type_identifier) @enum.implements)?)?
  body: (enum_body
    (enum_constant
      name: (identifier) @enum_constant.name
      arguments: (argument_list)? @enum_constant.arguments)?)) @enum.definition

; ============================================================================
; Records (Java 14+)
; ============================================================================
(record_declaration
  (modifiers)? @record.modifiers
  name: (identifier) @record.name
  type_parameters: (type_parameters)? @record.type_parameters
  parameters: (formal_parameters) @record.parameters
  interfaces: (super_interfaces
    (type_list
      (type_identifier) @record.implements)?)?
  body: (class_body) @record.body) @record.definition

; ============================================================================
; Methods with full signatures
; ============================================================================
(method_declaration
  (modifiers)? @method.modifiers
  type_parameters: (type_parameters)? @method.type_parameters
  type: [
    (void_type)
    (type_identifier)
    (generic_type)
    (array_type)
    (primitive_type)
  ] @method.return_type
  name: (identifier) @method.name
  parameters: (formal_parameters
    (formal_parameter
      (modifiers)? @parameter.modifiers
      type: (_) @parameter.type
      name: (identifier) @parameter.name)
    (spread_parameter
      type: (_) @varargs.type
      name: (identifier) @varargs.name)?) @method.parameters
  (throws_clause
    (type_identifier) @method.throws
    (scoped_type_identifier) @method.throws)?
  body: (block)? @method.body) @method.definition

; ============================================================================
; Constructors
; ============================================================================
(constructor_declaration
  (modifiers)? @constructor.modifiers
  name: (identifier) @constructor.name
  parameters: (formal_parameters) @constructor.parameters
  (throws_clause
    (type_identifier) @constructor.throws
    (scoped_type_identifier) @constructor.throws)?
  body: (constructor_body) @constructor.body) @constructor.definition

; ============================================================================
; Fields with initialization
; ============================================================================
(field_declaration
  (modifiers)? @field.modifiers
  type: (_) @field.type
  declarator: (variable_declarator
    name: (identifier) @field.name
    value: (_)? @field.initializer)) @field.definition

; ============================================================================
; Annotations (for framework detection)
; ============================================================================
(annotation
  name: (identifier) @annotation.name
  arguments: (annotation_argument_list)? @annotation.arguments) @annotation

(marker_annotation
  name: (identifier) @annotation.name) @annotation

; ============================================================================
; Import statements (for dependency tracking)
; ============================================================================
(import_declaration
  (scoped_identifier) @import.path
  (asterisk)? @import.wildcard) @import.statement

(static_import_declaration
  (scoped_identifier) @static_import.path
  (asterisk)? @static_import.wildcard) @static_import.statement

; ============================================================================
; Package declaration
; ============================================================================
(package_declaration
  (scoped_identifier) @package.name) @package.declaration

; ============================================================================
; Inner classes
; ============================================================================
(class_body
  (class_declaration
    (modifiers)? @inner_class.modifiers
    name: (identifier) @inner_class.name
    type_parameters: (type_parameters)? @inner_class.type_parameters) @inner_class.definition)

; ============================================================================
; Static blocks
; ============================================================================
(static_initializer) @static_block

; ============================================================================
; Instance initializer blocks
; ============================================================================
(instance_initializer) @instance_block

; ============================================================================
; Lambda expressions (for modern Java)
; ============================================================================
(lambda_expression
  parameters: [
    (identifier) @lambda.parameter
    (formal_parameters) @lambda.parameters
    (inferred_parameters) @lambda.parameters
  ]
  body: [(expression) (block)] @lambda.body) @lambda.definition

; ============================================================================
; Method references
; ============================================================================
(method_reference
  object: (_)? @method_ref.object
  method: (identifier) @method_ref.method) @method_ref

; ============================================================================
; Type parameters and bounds
; ============================================================================
(type_parameters
  (type_parameter
    name: (type_identifier) @type_param.name
    (type_bound
      (type_identifier) @type_param.bound
      (generic_type) @type_param.bound)?)) @type_params

; ============================================================================
; Sealed classes (Java 17+)
; ============================================================================
(class_declaration
  (modifiers
    (modifier) @sealed.modifier (#eq? @sealed.modifier "sealed"))
  (permits_clause
    (type_list
      (type_identifier) @sealed.permitted
      (scoped_type_identifier) @sealed.permitted)?)?
  name: (identifier) @sealed.name) @sealed.class