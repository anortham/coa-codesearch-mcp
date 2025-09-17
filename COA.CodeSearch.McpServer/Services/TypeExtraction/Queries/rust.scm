; Rust Tree-sitter Query File for Type Extraction
; This query file defines patterns to extract type information from Rust code

; ============================================================================
; Struct declarations with generics and lifetimes
; ============================================================================
(struct_item
  (visibility_modifier)? @struct.visibility
  name: (type_identifier) @struct.name
  type_parameters: (type_parameters
    (lifetime) @struct.lifetime
    (type_identifier) @struct.type_param
    (constrained_type_parameter
      left: (type_identifier) @struct.type_param
      bounds: (trait_bounds) @struct.type_bounds)?)? @struct.generics
  body: [
    (field_declaration_list
      (field_declaration
        (visibility_modifier)? @field.visibility
        name: (field_identifier) @field.name
        type: (_) @field.type)?)
    (ordered_field_declaration_list
      (visibility_modifier)? @tuple_field.visibility
      type: (_) @tuple_field.type)?
  ]? @struct.body) @struct.definition

; ============================================================================
; Enum declarations with variants
; ============================================================================
(enum_item
  (visibility_modifier)? @enum.visibility
  name: (type_identifier) @enum.name
  type_parameters: (type_parameters)? @enum.generics
  body: (enum_variant_list
    (enum_variant
      (visibility_modifier)? @variant.visibility
      name: (identifier) @variant.name
      body: [
        (field_declaration_list) @variant.fields
        (ordered_field_declaration_list) @variant.tuple_fields
      ]?
      value: (_)? @variant.discriminant)?)) @enum.definition

; ============================================================================
; Trait declarations
; ============================================================================
(trait_item
  (visibility_modifier)? @trait.visibility
  name: (type_identifier) @trait.name
  type_parameters: (type_parameters)? @trait.generics
  bounds: (trait_bounds)? @trait.supertraits
  body: (declaration_list
    (function_signature_item
      (visibility_modifier)? @trait_method.visibility
      (function_modifiers)? @trait_method.modifiers
      name: (identifier) @trait_method.name
      type_parameters: (type_parameters)? @trait_method.generics
      parameters: (parameters) @trait_method.parameters
      return_type: (_)? @trait_method.return_type
      where_clause: (where_clause)? @trait_method.where)?
    (associated_type
      name: (type_identifier) @assoc_type.name
      type_parameters: (type_parameters)? @assoc_type.generics
      bounds: (trait_bounds)? @assoc_type.bounds)?)) @trait.definition

; ============================================================================
; Implementation blocks
; ============================================================================
(impl_item
  type_parameters: (type_parameters)? @impl.generics
  trait: (type_identifier)? @impl.trait
  type: (_) @impl.type
  where_clause: (where_clause)? @impl.where
  body: (declaration_list
    (function_item
      (visibility_modifier)? @method.visibility
      (function_modifiers)? @method.modifiers
      name: (identifier) @method.name
      type_parameters: (type_parameters)? @method.generics
      parameters: (parameters
        (self_parameter
          (mutable_specifier)? @method.mut_self)? @method.self
        (parameter
          pattern: (_) @parameter.pattern
          type: (_) @parameter.type)?) @method.parameters
      return_type: (_)? @method.return_type
      body: (block)? @method.body)?
    (const_item
      (visibility_modifier)? @const.visibility
      name: (identifier) @const.name
      type: (_) @const.type
      value: (_)? @const.value)?)) @impl.definition

; ============================================================================
; Function declarations
; ============================================================================
(function_item
  (visibility_modifier)? @function.visibility
  (function_modifiers)? @function.modifiers
  name: (identifier) @function.name
  type_parameters: (type_parameters)? @function.generics
  parameters: (parameters
    (parameter
      pattern: (_) @parameter.pattern
      type: (_) @parameter.type)?) @function.parameters
  return_type: (_)? @function.return_type
  where_clause: (where_clause)? @function.where
  body: (block)? @function.body) @function.definition

; ============================================================================
; Type aliases
; ============================================================================
(type_item
  (visibility_modifier)? @type_alias.visibility
  name: (type_identifier) @type_alias.name
  type_parameters: (type_parameters)? @type_alias.generics
  value: (_) @type_alias.target) @type_alias.definition

; ============================================================================
; Constants and statics
; ============================================================================
(const_item
  (visibility_modifier)? @const.visibility
  name: (identifier) @const.name
  type: (_) @const.type
  value: (_)? @const.value) @const.definition

(static_item
  (visibility_modifier)? @static.visibility
  (mutable_specifier)? @static.mutable
  name: (identifier) @static.name
  type: (_) @static.type
  value: (_)? @static.value) @static.definition

; ============================================================================
; Modules
; ============================================================================
(mod_item
  (visibility_modifier)? @module.visibility
  name: (identifier) @module.name
  body: (declaration_list)? @module.body) @module.definition

; ============================================================================
; Use statements (imports)
; ============================================================================
(use_declaration
  (visibility_modifier)? @use.visibility
  argument: [
    (scoped_identifier) @use.path
    (identifier) @use.simple
    (use_list) @use.list
    (use_as_clause
      path: (_) @use.path
      alias: (identifier) @use.alias)
    (use_wildcard
      (scoped_identifier) @use.glob_path)
  ]) @use.statement

; ============================================================================
; Macros
; ============================================================================
(macro_definition
  name: (identifier) @macro.name
  (macro_rule
    left: (_) @macro.pattern
    right: (_) @macro.expansion)?) @macro.definition

(macro_invocation
  macro: [
    (identifier) @macro_call.name
    (scoped_identifier) @macro_call.path
  ]
  (token_tree) @macro_call.arguments) @macro_call

; ============================================================================
; Attributes
; ============================================================================
(attribute_item
  (attribute
    (identifier) @attribute.name
    arguments: (token_tree)? @attribute.arguments)) @attribute

(inner_attribute_item
  (attribute
    (identifier) @inner_attribute.name
    arguments: (token_tree)? @inner_attribute.arguments)) @inner_attribute

; ============================================================================
; Lifetime parameters
; ============================================================================
(lifetime
  (identifier) @lifetime.name) @lifetime

(type_parameters
  (lifetime
    (identifier) @lifetime_param.name)
  (type_identifier) @type_param.name
  (constrained_type_parameter
    left: (type_identifier) @constrained_param.name
    bounds: (trait_bounds
      (type_identifier) @bound.trait
      (lifetime) @bound.lifetime)?)) @type_params

; ============================================================================
; Associated types
; ============================================================================
(associated_type
  name: (type_identifier) @assoc_type.name
  type_parameters: (type_parameters)? @assoc_type.generics
  bounds: (trait_bounds)? @assoc_type.bounds
  value: (_)? @assoc_type.default) @assoc_type.definition

; ============================================================================
; Closures
; ============================================================================
(closure_expression
  parameters: (closure_parameters
    (pattern) @closure_param.pattern
    (parameter
      pattern: (_) @closure_param.pattern
      type: (_) @closure_param.type)?) @closure.parameters
  return_type: (_)? @closure.return_type
  body: (_) @closure.body) @closure.definition

; ============================================================================
; Async blocks and functions
; ============================================================================
(function_modifiers
  "async" @function.async)

(async_block
  body: (block) @async.body) @async.block

; ============================================================================
; Unsafe blocks and functions
; ============================================================================
(function_modifiers
  "unsafe" @function.unsafe)

(unsafe_block
  body: (block) @unsafe.body) @unsafe.block

; ============================================================================
; Union types
; ============================================================================
(union_item
  (visibility_modifier)? @union.visibility
  name: (type_identifier) @union.name
  type_parameters: (type_parameters)? @union.generics
  body: (field_declaration_list
    (field_declaration
      (visibility_modifier)? @union_field.visibility
      name: (field_identifier) @union_field.name
      type: (_) @union_field.type)?)) @union.definition

; ============================================================================
; Extern blocks (FFI)
; ============================================================================
(extern_crate_declaration
  name: (identifier) @extern_crate.name
  alias: (identifier)? @extern_crate.alias) @extern_crate

(foreign_mod_item
  abi: (string_literal)? @extern.abi
  body: (declaration_list
    (function_signature_item
      (visibility_modifier)? @extern_fn.visibility
      name: (identifier) @extern_fn.name
      parameters: (parameters) @extern_fn.parameters
      return_type: (_)? @extern_fn.return_type)?)) @extern.block