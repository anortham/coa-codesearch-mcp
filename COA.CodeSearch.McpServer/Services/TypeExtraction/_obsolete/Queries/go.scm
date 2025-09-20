; Simplified Go Tree-sitter Query File for Testing
; Basic patterns to extract functions and structs

; Simple function declarations
(function_declaration
  name: (identifier) @function.name) @function.definition

; Simple struct declarations
(type_declaration
  (type_spec
    name: (type_identifier) @struct.name
    type: (struct_type))) @struct.definition