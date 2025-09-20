; Python Tree-sitter Query File for Type Extraction
; This query file defines patterns to extract type information from Python code

; ============================================================================
; Classes with decorators and inheritance
; ============================================================================
(class_definition
  (decorator)* @class.decorators
  name: (identifier) @class.name
  superclasses: (argument_list
    [(identifier) (attribute)]* @class.bases)?
  body: (block) @class.body) @class.definition

; ============================================================================
; Functions with type hints
; ============================================================================
(function_definition
  (decorator)* @function.decorators
  name: (identifier) @function.name
  parameters: (parameters
    (identifier)? @param.self
    (typed_parameter
      (identifier) @param.name
      type: (_) @param.type)?
    (typed_default_parameter
      name: (identifier) @param.name
      type: (_)? @param.type
      value: (_) @param.default)?
    (default_parameter
      name: (identifier) @param.name
      value: (_) @param.default)?
    (list_splat_pattern
      (identifier) @param.args)?
    (dictionary_splat_pattern
      (identifier) @param.kwargs)?)?
  return_type: (type)? @function.return_type
  body: (block) @function.body) @function.definition

; ============================================================================
; Async functions
; ============================================================================
(function_definition
  (["async"] @function.async)
  (decorator)* @function.decorators
  name: (identifier) @function.name
  parameters: (parameters) @function.parameters
  return_type: (type)? @function.return_type
  body: (block) @function.body) @async_function.definition

; ============================================================================
; Methods in classes
; ============================================================================
(class_definition
  body: (block
    (function_definition
      (decorator)* @method.decorators
      name: (identifier) @method.name
      parameters: (parameters
        (identifier) @method.self_or_cls
        (typed_parameter)* @method.params)?
      return_type: (type)? @method.return_type
      body: (block) @method.body))) @method.definition

; ============================================================================
; Properties (using @property decorator)
; ============================================================================
(class_definition
  body: (block
    (decorated_definition
      (decorator
        (identifier) @decorator.name
        (#eq? @decorator.name "property"))
      (function_definition
        name: (identifier) @property.name
        parameters: (parameters) @property.parameters
        return_type: (type)? @property.return_type
        body: (block) @property.body)))) @property.definition

; ============================================================================
; Setters (using @prop.setter decorator)
; ============================================================================
(class_definition
  body: (block
    (decorated_definition
      (decorator
        (attribute
          object: (identifier) @setter.property
          attribute: (identifier) @setter.decorator
          (#eq? @setter.decorator "setter")))
      (function_definition
        name: (identifier) @setter.name
        parameters: (parameters) @setter.parameters
        body: (block) @setter.body)))) @setter.definition

; ============================================================================
; Class methods and static methods
; ============================================================================
(class_definition
  body: (block
    (decorated_definition
      (decorator
        (identifier) @decorator.type
        (#match? @decorator.type "^(classmethod|staticmethod)$"))
      (function_definition
        name: (identifier) @special_method.name
        parameters: (parameters) @special_method.parameters
        return_type: (type)? @special_method.return_type
        body: (block) @special_method.body)))) @special_method.definition

; ============================================================================
; Dataclasses
; ============================================================================
(decorated_definition
  (decorator
    (call
      function: (identifier) @decorator.name
      (#eq? @decorator.name "dataclass")
      arguments: (argument_list)? @dataclass.args))
  (class_definition
    name: (identifier) @dataclass.name
    body: (block
      [(expression_statement
        (assignment
          left: (identifier) @field.name
          type: (type)? @field.type
          right: (_)? @field.default))?
       (expression_statement
        (annotated_assignment
          left: (identifier) @field.name
          annotation: (type) @field.type
          right: (_)? @field.default))?]))) @dataclass.definition

; ============================================================================
; Named tuples
; ============================================================================
(assignment
  left: (identifier) @namedtuple.name
  right: (call
    function: (attribute
      object: (identifier) @typing
      (#eq? @typing "typing")
      attribute: (identifier) @namedtuple_func
      (#eq? @namedtuple_func "NamedTuple"))
    arguments: (argument_list
      (string) @namedtuple.type_name
      (list
        (tuple
          (string) @field.name
          (_) @field.type)?)?))) @namedtuple.definition

; ============================================================================
; TypedDict definitions
; ============================================================================
(assignment
  left: (identifier) @typeddict.name
  right: (call
    function: (attribute
      object: (identifier) @typing
      (#eq? @typing "typing")
      attribute: (identifier) @typeddict_func
      (#eq? @typeddict_func "TypedDict"))
    arguments: (argument_list
      (string) @typeddict.type_name
      (dictionary
        (pair
          key: (string) @field.name
          value: (_) @field.type)?)?))) @typeddict.definition

(class_definition
  name: (identifier) @typeddict.name
  superclasses: (argument_list
    (attribute
      object: (identifier) @typing
      (#eq? @typing "typing")
      attribute: (identifier) @typeddict_base
      (#eq? @typeddict_base "TypedDict")))
  body: (block
    (expression_statement
      (annotated_assignment
        left: (identifier) @field.name
        annotation: (type) @field.type))?)) @typeddict.class_definition

; ============================================================================
; Enums
; ============================================================================
(class_definition
  name: (identifier) @enum.name
  superclasses: (argument_list
    [(identifier) (attribute)]
    (#match? @enum.base "Enum|IntEnum|StrEnum|Flag|IntFlag"))
  body: (block
    (expression_statement
      (assignment
        left: (identifier) @enum_member.name
        right: (_) @enum_member.value))?)) @enum.definition

; ============================================================================
; Type aliases
; ============================================================================
(assignment
  left: (identifier) @type_alias.name
  type: (type)? @type_alias.annotation
  right: (_) @type_alias.value
  (#match? @type_alias.value "^(Union|Optional|List|Dict|Tuple|Set|Type)")) @type_alias.definition

(annotated_assignment
  left: (identifier) @type_alias.name
  annotation: (attribute
    object: (identifier) @typing
    (#eq? @typing "typing")
    attribute: (identifier) @type_alias_attr
    (#eq? @type_alias_attr "TypeAlias"))
  right: (_) @type_alias.value) @type_alias.annotated

; ============================================================================
; Protocols (PEP 544)
; ============================================================================
(class_definition
  name: (identifier) @protocol.name
  superclasses: (argument_list
    (attribute
      object: (identifier) @typing
      (#eq? @typing "typing")
      attribute: (identifier) @protocol_base
      (#eq? @protocol_base "Protocol")))
  body: (block) @protocol.body) @protocol.definition

; ============================================================================
; Abstract base classes
; ============================================================================
(class_definition
  name: (identifier) @abc.name
  superclasses: (argument_list
    [(identifier) (attribute)]
    (#match? @abc.base "ABC|ABCMeta"))
  body: (block
    (decorated_definition
      (decorator
        (identifier) @abstractmethod
        (#eq? @abstractmethod "abstractmethod"))
      (function_definition
        name: (identifier) @abstract_method.name))?)) @abc.definition

; ============================================================================
; Global variables with type annotations
; ============================================================================
(module
  (expression_statement
    (annotated_assignment
      left: (identifier) @global_var.name
      annotation: (type) @global_var.type
      right: (_)? @global_var.value))) @global_var.definition

; ============================================================================
; Import statements for dependency tracking
; ============================================================================
(import_statement
  name: (dotted_name) @import.module) @import.statement

(import_from_statement
  module_name: (dotted_name)? @import.module
  name: [(dotted_name) (identifier)]? @import.name
  (aliased_import
    name: [(dotted_name) (identifier)] @import.name
    alias: (identifier) @import.alias)?) @import_from.statement

; ============================================================================
; Docstrings
; ============================================================================
(function_definition
  body: (block
    (expression_statement
      (string) @function.docstring
      (#match? @function.docstring "^(\"\"\"|''')"))))

(class_definition
  body: (block
    (expression_statement
      (string) @class.docstring
      (#match? @class.docstring "^(\"\"\"|''')"))))

; ============================================================================
; Special methods (dunder methods)
; ============================================================================
(class_definition
  body: (block
    (function_definition
      name: (identifier) @special.name
      (#match? @special.name "^__.*__$")
      parameters: (parameters) @special.parameters
      return_type: (type)? @special.return_type
      body: (block) @special.body))) @special.definition

; ============================================================================
; Generators and async generators
; ============================================================================
(function_definition
  name: (identifier) @generator.name
  body: (block
    (expression_statement
      (yield)) @generator.yield)) @generator.definition

(function_definition
  (["async"] @async_generator.async)
  name: (identifier) @async_generator.name
  body: (block
    [(expression_statement (yield))
     (for_statement (yield))] @async_generator.yield)) @async_generator.definition