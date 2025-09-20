; Ruby Tree-sitter Query File for Type Extraction
; This query file defines patterns to extract type information from Ruby code

; ============================================================================
; Classes with inheritance
; ============================================================================
(class
  name: (constant) @class.name
  superclass: (superclass (constant) @class.superclass)?
  body: (body_statement)? @class.body) @class.definition

; ============================================================================
; Modules
; ============================================================================
(module
  name: (constant) @module.name
  body: (body_statement)? @module.body) @module.definition

; ============================================================================
; Methods (instance methods)
; ============================================================================
(method
  name: (identifier) @method.name
  parameters: (method_parameters
    (identifier)* @method.params)?
  (body_statement)? @method.body) @method.definition

; ============================================================================
; Singleton methods (class methods)
; ============================================================================
(singleton_method
  object: (_) @singleton_method.object
  name: (identifier) @singleton_method.name
  parameters: (method_parameters
    (identifier)* @singleton_method.params)?
  (body_statement)? @singleton_method.body) @singleton_method.definition

; ============================================================================
; Attr accessors
; ============================================================================
(call
  method: (identifier) @attr.type
  (#match? @attr.type "^attr_(reader|writer|accessor)$")
  arguments: (argument_list
    (symbol) @attr.name)) @attr.definition

; ============================================================================
; Constants
; ============================================================================
(assignment
  left: (constant) @constant.name
  right: (_) @constant.value) @constant.definition

; ============================================================================
; Module inclusion
; ============================================================================
(call
  method: (identifier) @include.type
  (#match? @include.type "^(include|extend|prepend)$")
  arguments: (argument_list
    (constant) @include.module)) @include.definition

; ============================================================================
; Alias definitions
; ============================================================================
(alias
  (identifier) @alias.new_name
  (identifier) @alias.old_name) @alias.definition

; ============================================================================
; Class variables
; ============================================================================
(assignment
  left: (class_variable) @class_var.name
  right: (_) @class_var.value) @class_var.definition

; ============================================================================
; Instance variables
; ============================================================================
(assignment
  left: (instance_variable) @instance_var.name
  right: (_) @instance_var.value) @instance_var.definition

; ============================================================================
; Blocks with parameters
; ============================================================================
(block
  parameters: (block_parameters
    (identifier)* @block.params)?
  body: (_) @block.body) @block.definition

(do_block
  parameters: (block_parameters
    (identifier)* @do_block.params)?
  body: (body_statement) @do_block.body) @do_block.definition

; ============================================================================
; Lambda definitions
; ============================================================================
(lambda
  parameters: (lambda_parameters
    (identifier)* @lambda.params)?
  body: (_) @lambda.body) @lambda.definition

; ============================================================================
; Structs (using Struct.new)
; ============================================================================
(assignment
  left: (constant) @struct.name
  right: (call
    receiver: (constant) @struct_class
    (#eq? @struct_class "Struct")
    method: (identifier) @new_method
    (#eq? @new_method "new")
    arguments: (argument_list
      (symbol)* @struct.fields))) @struct.definition

; ============================================================================
; Rails-style concerns and modules
; ============================================================================
(module
  name: (constant) @concern.name
  body: (body_statement
    (call
      receiver: (constant) @active_support
      (#match? @active_support "ActiveSupport")
      method: (identifier) @concern_method
      (#eq? @concern_method "Concern"))?)) @concern.definition