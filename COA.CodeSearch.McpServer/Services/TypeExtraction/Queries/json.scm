; Tree-sitter query for JSON structure extraction
; This query captures the main structural elements of JSON files

; Capture object pairs (key-value)
(pair
  key: (string) @key
  value: (_) @value) @property

; Capture root object
(document
  (object) @root.object)

; Capture root array
(document
  (array) @root.array)

; Capture nested objects
(object) @object

; Capture arrays
(array) @array

; Capture string values
(string) @string.value

; Capture number values
(number) @number.value

; Capture boolean values
[(true) (false)] @boolean.value

; Capture null values
(null) @null.value

; Special handling for configuration patterns
; Capture objects with specific keys like "name", "type", "config"
(pair
  key: (string) @config.key
  (#match? @config.key "^\"(name|type|config|settings|options)\"$")
  value: (_) @config.value) @configuration

; Capture array of objects (common in config files)
(array
  (object) @array.object)