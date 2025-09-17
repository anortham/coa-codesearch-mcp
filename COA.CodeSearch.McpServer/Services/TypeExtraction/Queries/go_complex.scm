; Go Tree-sitter Query File for Type Extraction
; This query file defines patterns to extract type information from Go code

; ============================================================================
; Type declarations (structs)
; ============================================================================
(type_declaration
  (type_spec
    name: (type_identifier) @struct.name
    type: (struct_type
      (field_declaration_list
        (field_declaration
          name: (field_identifier) @field.name
          type: (_) @field.type
          tag: (raw_string_literal)? @field.tag)?)) @struct.fields)) @struct.definition

; ============================================================================
; Interface declarations
; ============================================================================
(type_declaration
  (type_spec
    name: (type_identifier) @interface.name
    type: (interface_type
      (method_spec_list
        (method_spec
          name: (field_identifier) @method.name
          parameters: (parameter_list) @method.parameters
          result: (_)? @method.return_type)?)) @interface.methods)) @interface.definition

; ============================================================================
; Type aliases
; ============================================================================
(type_declaration
  (type_alias
    name: (type_identifier) @type_alias.name
    value: (_) @type_alias.target)) @type_alias.definition

; ============================================================================
; Function declarations
; ============================================================================
(function_declaration
  name: (identifier) @function.name
  type_parameters: (type_parameter_list)? @function.type_parameters
  parameters: (parameter_list
    (parameter_declaration
      name: (identifier) @parameter.name
      type: (_) @parameter.type)
    (variadic_parameter_declaration
      name: (identifier)? @variadic.name
      type: (_) @variadic.type)?) @function.parameters
  result: [
    (type_identifier)
    (pointer_type)
    (slice_type)
    (array_type)
    (map_type)
    (channel_type)
    (qualified_type)
    (parameter_list)
  ]? @function.return_type
  body: (block)? @function.body) @function.definition

; ============================================================================
; Method declarations (receiver functions)
; ============================================================================
(method_declaration
  receiver: (parameter_list
    (parameter_declaration
      name: (identifier)? @receiver.name
      type: [
        (type_identifier) @receiver.type
        (pointer_type (type_identifier) @receiver.type)
      ])) @method.receiver
  name: (field_identifier) @method.name
  type_parameters: (type_parameter_list)? @method.type_parameters
  parameters: (parameter_list
    (parameter_declaration
      name: (identifier) @parameter.name
      type: (_) @parameter.type)?) @method.parameters
  result: (_)? @method.return_type
  body: (block)? @method.body) @method.definition

; ============================================================================
; Constants and variables
; ============================================================================
(const_declaration
  (const_spec
    name: (identifier) @const.name
    type: (_)? @const.type
    value: (_)? @const.value)) @const.definition

(var_declaration
  (var_spec
    name: (identifier) @var.name
    type: (_)? @var.type
    value: (_)? @var.value)) @var.definition

; ============================================================================
; Package declaration
; ============================================================================
(package_clause
  (package_identifier) @package.name) @package.declaration

; ============================================================================
; Import declarations
; ============================================================================
(import_declaration
  (import_spec
    name: (package_identifier)? @import.alias
    path: (interpreted_string_literal) @import.path)) @import.statement

(import_declaration
  (import_spec_list
    (import_spec
      name: (package_identifier)? @import.alias
      path: (interpreted_string_literal) @import.path)?)) @import.statements

; ============================================================================
; Type constraints (Go 1.18+ generics)
; ============================================================================
(type_parameter_list
  (type_parameter_constraint
    name: (identifier) @type_param.name
    constraint: (_) @type_param.constraint)) @type_params

; ============================================================================
; Embedded types (composition)
; ============================================================================
(struct_type
  (field_declaration_list
    (field_declaration
      type: (type_identifier) @embedded.type (#not-match? @embedded.type "^[a-z]")))) @embedded.field

; ============================================================================
; Channel types
; ============================================================================
(channel_type
  value: (_) @channel.element_type) @channel.type

(send_channel_type
  value: (_) @send_channel.element_type) @send_channel.type

(receive_channel_type
  value: (_) @receive_channel.element_type) @receive_channel.type

; ============================================================================
; Function types
; ============================================================================
(function_type
  parameters: (parameter_list) @func_type.parameters
  result: (_)? @func_type.return_type) @func_type.definition

; ============================================================================
; Slice and array types
; ============================================================================
(slice_type
  element: (_) @slice.element_type) @slice.type

(array_type
  length: (_) @array.length
  element: (_) @array.element_type) @array.type

; ============================================================================
; Map types
; ============================================================================
(map_type
  key: (_) @map.key_type
  value: (_) @map.value_type) @map.type

; ============================================================================
; Anonymous functions (lambdas)
; ============================================================================
(func_literal
  parameters: (parameter_list
    (parameter_declaration
      name: (identifier)? @lambda_param.name
      type: (_) @lambda_param.type)?) @lambda.parameters
  result: (_)? @lambda.return_type
  body: (block) @lambda.body) @lambda.definition

; ============================================================================
; Defer statements
; ============================================================================
(defer_statement
  (call_expression
    function: (_) @defer.function
    arguments: (argument_list)? @defer.arguments)) @defer.statement

; ============================================================================
; Go statements (goroutines)
; ============================================================================
(go_statement
  (call_expression
    function: (_) @goroutine.function
    arguments: (argument_list)? @goroutine.arguments)) @goroutine.statement

; ============================================================================
; Select statements
; ============================================================================
(select_statement
  (communication_case
    communication: (_) @select.communication
    consequence: (statement_list) @select.body)
  (default_case
    consequence: (statement_list) @select.default)?) @select.statement

; ============================================================================
; Type assertions
; ============================================================================
(type_assertion_expression
  operand: (_) @type_assert.expression
  type: (_) @type_assert.type) @type_assert

; ============================================================================
; Type switches
; ============================================================================
(type_switch_statement
  value: (_) @type_switch.value
  (type_case
    type: (_) @type_case.type
    consequence: (statement_list) @type_case.body)
  (default_case
    consequence: (statement_list) @type_case.default)?) @type_switch.statement