# Scriban Built-in Functions Reference

## Array Functions

- `array.size` - Returns the number of elements in a list
- `array.add` - Adds a single element to the end of a list
- `array.add_range` - Concatenates two lists
- `array.compact` - Removes null values from a list
- `array.first` - Retrieves the first element
- `array.last` - Retrieves the last element
- `array.filter` - Allows filtering list elements based on a condition
- `array.map` - Transforms list elements
- `array.any` - Checks if any element satisfies a given condition

## Object Functions

- `object.default` - Provides a default value if an object is null
- `object.size` - Returns the number of elements in an object
- `object.keys` - Retrieves an object's keys
- `object.values` - Retrieves an object's values
- `object.has_key` - Checks if an object contains a specific key
- `object.typeof` - Determines the type of an object

## Template Syntax Examples

```scriban
// Array creation
x = [1,2,3,4]

// Iteration
{{ for product in products }}
  <li>{{ product.name }}</li>
{{ end }}

// Array size
{{ products.size }}

// Default values for objects
{{ product.name | object.default "Unknown" }}

// String operations with piping
{{ product.description | string.truncate 15 }}
```

## Key Points

1. Arrays have a built-in `.size` property
2. Use `object.default` for fallback values instead of custom functions
3. Piping with `|` is supported for transformations
4. Collections are handled natively without need for custom functions