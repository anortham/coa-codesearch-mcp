; C# Tree-sitter Query File for Type Extraction
; This query file defines patterns to extract type information from C# code

; ============================================================================
; Classes with full metadata
; ============================================================================
(class_declaration
  (modifier)* @class.modifiers
  name: (identifier) @class.name
  type_parameters: (type_parameter_list)? @class.type_parameters
  (base_list
    (argument_list
      (identifier) @class.base_type
      (generic_name) @class.base_type
      (qualified_name) @class.base_type)?)? @class.inheritance
  body: (declaration_list) @class.body) @class.definition

; ============================================================================
; Interfaces with inheritance
; ============================================================================
(interface_declaration
  (modifier)* @interface.modifiers
  name: (identifier) @interface.name
  type_parameters: (type_parameter_list)? @interface.type_parameters
  (base_list)? @interface.inheritance
  body: (declaration_list) @interface.body) @interface.definition

; ============================================================================
; Structs
; ============================================================================
(struct_declaration
  (modifier)* @struct.modifiers
  name: (identifier) @struct.name
  type_parameters: (type_parameter_list)? @struct.type_parameters
  (base_list)? @struct.inheritance
  body: (declaration_list) @struct.body) @struct.definition

; ============================================================================
; Enums
; ============================================================================
(enum_declaration
  (modifier)* @enum.modifiers
  name: (identifier) @enum.name
  (base_list)? @enum.base_type
  body: (enum_member_declaration_list
    (enum_member_declaration
      (attribute_list)? @enum_member.attributes
      name: (identifier) @enum_member.name
      value: (integer_literal)? @enum_member.value)?)) @enum.definition

; ============================================================================
; Records (C# 9+)
; ============================================================================
(record_declaration
  (modifier)* @record.modifiers
  name: (identifier) @record.name
  type_parameters: (type_parameter_list)? @record.type_parameters
  (parameter_list)? @record.primary_constructor
  (base_list)? @record.inheritance
  body: (declaration_list)? @record.body) @record.definition

; ============================================================================
; Methods with full signatures
; ============================================================================
(method_declaration
  (attribute_list)? @method.attributes
  (modifier)* @method.modifiers
  type: [
    (predefined_type)
    (identifier)
    (generic_name)
    (array_type)
    (nullable_type)
    (qualified_name)
  ] @method.return_type
  name: (identifier) @method.name
  type_parameters: (type_parameter_list)? @method.type_parameters
  parameters: (parameter_list
    (parameter
      (attribute_list)? @parameter.attributes
      (parameter_modifier)? @parameter.modifier
      type: (_) @parameter.type
      name: (identifier) @parameter.name
      (equals_value_clause)? @parameter.default)?) @method.parameters
  (type_parameter_constraints_clause)* @method.constraints
  body: [(block) (arrow_expression_clause)]? @method.body) @method.definition

; ============================================================================
; Constructors
; ============================================================================
(constructor_declaration
  (modifier)* @constructor.modifiers
  name: (identifier) @constructor.name
  parameters: (parameter_list) @constructor.parameters
  (constructor_initializer)? @constructor.initializer
  body: (block) @constructor.body) @constructor.definition

; ============================================================================
; Properties with accessors
; ============================================================================
(property_declaration
  (attribute_list)? @property.attributes
  (modifier)* @property.modifiers
  type: (_) @property.type
  name: (identifier) @property.name
  (accessor_list
    (accessor_declaration
      (attribute_list)? @accessor.attributes
      (modifier)* @accessor.modifiers
      kind: [(get_accessor) (set_accessor) (init_accessor)] @accessor.kind
      body: [(block) (arrow_expression_clause)]? @accessor.body)?)?
  (equals_value_clause)? @property.initializer) @property.definition

; ============================================================================
; Fields
; ============================================================================
(field_declaration
  (attribute_list)? @field.attributes
  (modifier)* @field.modifiers
  type: (_) @field.type
  (variable_declaration
    (variable_declarator
      name: (identifier) @field.name
      (equals_value_clause)? @field.initializer)?)) @field.definition

; ============================================================================
; Events
; ============================================================================
(event_declaration
  (modifier)* @event.modifiers
  type: (_) @event.type
  name: (identifier) @event.name) @event.definition

; ============================================================================
; Delegates
; ============================================================================
(delegate_declaration
  (modifier)* @delegate.modifiers
  return_type: (_) @delegate.return_type
  name: (identifier) @delegate.name
  type_parameters: (type_parameter_list)? @delegate.type_parameters
  parameters: (parameter_list) @delegate.parameters) @delegate.definition

; ============================================================================
; Type aliases
; ============================================================================
(using_directive
  (using_alias
    name: (identifier) @type_alias.name
    value: (_) @type_alias.target)) @type_alias.definition

; ============================================================================
; Namespaces
; ============================================================================
(namespace_declaration
  name: [(identifier) (qualified_name)] @namespace.name
  body: (declaration_list) @namespace.body) @namespace.definition

(file_scoped_namespace_declaration
  name: [(identifier) (qualified_name)] @namespace.name) @namespace.definition

; ============================================================================
; Generic Type Parameters and Constraints
; ============================================================================
(type_parameter_list
  (type_parameter
    (attribute_list)? @type_param.attributes
    (variance_annotation)? @type_param.variance
    name: (identifier) @type_param.name)) @type_params

(type_parameter_constraints_clause
  target: (identifier) @constraint.target
  constraints: [
    (type_parameter_constraint
      (type_constraint type: (_) @constraint.type))
    (type_parameter_constraint
      (constructor_constraint) @constraint.new)
    (type_parameter_constraint
      [(class_constraint) (struct_constraint)] @constraint.reference_type)
  ]) @constraint.clause

; ============================================================================
; Attributes (for framework detection)
; ============================================================================
(attribute_list
  (attribute
    name: [(identifier) (qualified_name)] @attribute.name
    (attribute_argument_list
      (attribute_argument
        expression: (_) @attribute.argument)?)?)) @attribute.list

; ============================================================================
; Using directives (for import tracking)
; ============================================================================
(using_directive
  [(identifier) (qualified_name)] @using.namespace) @using.directive

; ============================================================================
; Global using directives (C# 10+)
; ============================================================================
(global_using_directive
  [(identifier) (qualified_name)] @global_using.namespace) @global_using.directive