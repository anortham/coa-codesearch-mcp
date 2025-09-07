========================
CODE SNIPPETS
========================
TITLE: Rendering Template with Object and Renamer (C#)
DESCRIPTION: Provides a concise example using the `Template.Render(object, renamer)` overload to render a template directly with a .NET object and a custom renamer, simplifying the setup process by handling import and context configuration internally.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/runtime.md#_snippet_31

LANGUAGE: C#
CODE:

```
var template = Template.Parse("This is Hello: `{{Hello}}`");
template.Render(new MyObject(), member => member.Name);

```

---

TITLE: math.random Example
DESCRIPTION: Provides an example of using the math.random function to generate a random integer within a specified range (inclusive lower bound, exclusive upper bound).

SOURCE: https://github.com/scriban/scriban/blob/master/doc/builtins.md#_snippet_45

LANGUAGE: scriban-html
CODE:

```
{{ math.random 1 10 }}
```

LANGUAGE: html
CODE:

```
7
```

---

TITLE: Initializing Scriban Object with Members (Multi-line)
DESCRIPTION: Provides an example of initializing a Scriban object with multiple members spread across several lines for readability.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/language.md#_snippet_42

LANGUAGE: Scriban
CODE:

```
{{
  myobject = {
      member1: "yes",
      member2: "no"
  }
}}
```

---

TITLE: Scriban Escape Block Example (Output)
DESCRIPTION: The resulting output from the escape block example, showing the literal text including the un-evaluated Scriban code block syntax.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/language.md#_snippet_9

LANGUAGE: text
CODE:

```
Hello this is {{ name }}
```

---

TITLE: Initializing Scriban Array with Items (Multi-line)
DESCRIPTION: Provides an example of initializing a Scriban array with multiple items spread across several lines for readability.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/language.md#_snippet_50

LANGUAGE: Scriban
CODE:

```
{{
  myarray = [
    1,
    2,
    3,
    4,
  ]
}}
```

---

TITLE: Example: Generating URL Handle from String in Scriban
DESCRIPTION: Demonstrates using the `string.handleize` function to convert a string into a URL-friendly handle. Shows both the input Scriban template and the resulting HTML output.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/builtins.md#_snippet_75

LANGUAGE: scriban-html
CODE:

```
{{ '100% M & Ms!!!' | string.handleize  }}
```

LANGUAGE: html
CODE:

```
100-m-ms
```

---

TITLE: math.uuid Example
DESCRIPTION: Shows how to generate a new UUID using the math.uuid function.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/builtins.md#_snippet_44

LANGUAGE: scriban-html
CODE:

```
{{ math.uuid }}
```

LANGUAGE: html
CODE:

```
1c0a4aa8-680e-4bd6-95e9-cdbec45ef057
```

---

TITLE: Example: Converting String to Lowercase in Scriban
DESCRIPTION: Demonstrates using the `string.downcase` function to convert a string to lowercase. Shows both the input Scriban template and the resulting HTML output.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/builtins.md#_snippet_72

LANGUAGE: scriban-html
CODE:

```
{{ "TeSt" | string.downcase }}
```

LANGUAGE: html
CODE:

```
test
```

---

TITLE: Getting String Length with string.size in Scriban
DESCRIPTION: Provides an example of using the `string.size` function to determine and return the number of characters in a given input string.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/builtins.md#_snippet_87

LANGUAGE: scriban-html
CODE:

```
{{ "test" | string.size }}
```

LANGUAGE: html
CODE:

```
4
```

---

TITLE: object.default Example
DESCRIPTION: Demonstrates how the object.default function returns a default value if the input is null or an empty string.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/builtins.md#_snippet_46

LANGUAGE: scriban-html
CODE:

```
{{ undefined_var | object.default "Yo" }}
```

LANGUAGE: html
CODE:

```
Yo
```

---

TITLE: math.round Example
DESCRIPTION: Demonstrates how to use the math.round function to round numbers to the nearest integer or a specified number of decimal places.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/builtins.md#_snippet_42

LANGUAGE: scriban-html
CODE:

```
{{ 4.6 | math.round }}
{{ 4.3 | math.round }}
{{ 4.5612 | math.round 2 }}
```

LANGUAGE: html
CODE:

```
5
4
4.56
```

---

TITLE: math.times Example
DESCRIPTION: Illustrates the usage of the math.times function to perform multiplication between two values.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/builtins.md#_snippet_43

LANGUAGE: scriban-html
CODE:

```
{{ 2 | math.times 3}}
```

LANGUAGE: html
CODE:

```
6
```

---

TITLE: Parsing and Rendering a Liquid Template in C#
DESCRIPTION: This example shows how to parse a template written in Liquid syntax using the `Template.ParseLiquid` method and render it with provided data, demonstrating Scriban's Liquid compatibility.

SOURCE: https://github.com/scriban/scriban/blob/master/readme.md#_snippet_1

LANGUAGE: C#
CODE:

```
// Parse a liquid template
var template = Template.ParseLiquid("Hello {{name}}!");
var result = template.Render(new { Name = "World" }); // => "Hello World!"
```

---

TITLE: Using the `with` Statement and Global Stack (C# & Scriban)
DESCRIPTION: Illustrates the Scriban `with` statement's behavior, showing how it pushes an object onto the context's global stack, how variable assignments within `with` target that object, and how `end` pops the stack. Includes the C# setup code to demonstrate the effect.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/runtime.md#_snippet_27

LANGUAGE: C#
CODE:

```
var scriptObject1 = new ScriptObject();
var context = new TemplateContext();
context.PushGlobal(scriptObject1);

var template = Template.Parse(@"
   Create a variable
{{
    myvar = {}
    with myvar   # Equivalent of calling context.PushGlobal(myvar)
        x = 5    # Equivalent to set myvar.x = 5
        y = 6
    end          # Equivalent of calling context.PopGlobal()
}}");

template.Render(context);

// Contains 5
Console.WriteLine(((ScriptObject)scriptObject1["myvar"])["x"]);

```

---

TITLE: Example: Capitalizing a String in Scriban
DESCRIPTION: Demonstrates using the `string.capitalize` function to convert the first character of a string to uppercase. Shows both the input Scriban template and the resulting HTML output.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/builtins.md#_snippet_67

LANGUAGE: scriban-html
CODE:

```
{{ "test" | string.capitalize }}
```

LANGUAGE: html
CODE:

```
Test
```

---

TITLE: Scriban Escape Block Example (Input)
DESCRIPTION: Shows how to use {%{ }%}} to escape a Scriban code block, causing it to be treated as literal text rather than being evaluated.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/language.md#_snippet_8

LANGUAGE: scriban
CODE:

```
{%{Hello this is {{ name }}}%}
```

---

TITLE: Scriban: Example Included Template
DESCRIPTION: A sample template (`myinclude.html`) used with the `include` function. It shows how variables (`y`) can be modified and output generated within a template that is included by another.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/language.md#_snippet_105

LANGUAGE: Scriban
CODE:

```
{{ y = y + 1 ~}}
This is a string with the value {{ y }}
```

---

TITLE: Scriban Greedy Whitespace Strip (Both) Input
DESCRIPTION: Example showing the use of {{- and -}} together to remove all preceding and following whitespace around a Scriban code block.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/language.md#_snippet_16

LANGUAGE: scriban-html
CODE:

```
This is a <
{{- name -}}
> text:
```

---

TITLE: Accessing Scriban Array Size
DESCRIPTION: Shows how to use the `.size` property to get the number of elements in a Scriban array.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/language.md#_snippet_55

LANGUAGE: Scriban
CODE:

```
{{
a = [1, 2, 3]
a.size
}}
```

LANGUAGE: HTML
CODE:

```
3
```

---

TITLE: Scriban Text Block Example
DESCRIPTION: Illustrates a text block in Scriban, which is any content outside of {{ }} or {%{ }%}} blocks. Text blocks are outputted directly without processing.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/language.md#_snippet_7

LANGUAGE: scriban
CODE:

```
Hello this is {{ name }}, welcome to scriban!
______________          _____________________
^ text block            ^ text block


```

---

TITLE: Getting Array Size in Scriban
DESCRIPTION: Uses the `array.size` function to return the number of elements in a list. The example shows how to get the size of a numeric list.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/builtins.md#_snippet_17

LANGUAGE: Scriban
CODE:

```
{{ [4, 5, 6] | array.size }}
```

LANGUAGE: HTML
CODE:

```
3
```

---

TITLE: Defining Custom Scriban Functions with Optional and Params Arguments (C#)
DESCRIPTION: Defines a C# class inheriting from `ScriptObject` to expose custom functions to Scriban templates. Includes examples of functions with optional arguments (`HelloOpt`) and variable arguments (`HelloArgs`) using the `params` keyword.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/runtime.md#_snippet_13

LANGUAGE: C#
CODE:

```
// We simply inherit from ScriptObject
// All functions defined in the object will be imported
public class MyCustomFunctions : ScriptObject
{
    // A function an optional argument
    public static string HelloOpt(string text, string option = null)
    {
        return $"hello {text} with option:{option}";
    }

    // A function with params
    public static string HelloArgs(params object[] args)
    {
        return $"hello {(string.Join(",", args))}";
    }
}
```

---

TITLE: Getting Current Year (Scriban)
DESCRIPTION: Provides an example of accessing a property (`.year`) of the `date.now` object, which represents the current date and time, to retrieve the current year.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/builtins.md#_snippet_23

LANGUAGE: scriban-html
CODE:

```
{{ date.now.year }}
```

---

TITLE: Getting Object Keys in Scriban
DESCRIPTION: Retrieves a list of all member names (keys) from the specified object. The example shows sorting the resulting list.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/builtins.md#_snippet_52

LANGUAGE: scriban-html
CODE:

```
{{ product | object.keys | array.sort }}
```

---

TITLE: Scriban Non-Greedy Whitespace Strip (Loop) Input
DESCRIPTION: Example using {~ and ~}} in a loop to remove whitespace and the newline immediately following/preceding the block, while preserving indentation of surrounding lines.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/language.md#_snippet_18

LANGUAGE: scriban
CODE:

```
<ul>
    {{~ for product in products ~}}
    <li>{{ product.name }}</li>
    {{~ end ~}}
</ul>
```

---

TITLE: object.eval Example
DESCRIPTION: Shows how object.eval evaluates a string containing a Scriban expression.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/builtins.md#_snippet_47

LANGUAGE: scriban-html
CODE:

```
{{ "1 + 2" | object.eval }}
```

LANGUAGE: html
CODE:

```
3
```

---

TITLE: Example: Capitalizing Words in a String in Scriban
DESCRIPTION: Demonstrates using the `string.capitalizewords` function to convert the first character of each word in a string to uppercase. Shows both the input Scriban template and the resulting HTML output.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/builtins.md#_snippet_68

LANGUAGE: scriban-html
CODE:

```
{{ "This is easy" | string.capitalizewords }}
```

LANGUAGE: html
CODE:

```
This Is Easy
```

---

TITLE: Splitting a String with string.split in Scriban
DESCRIPTION: Demonstrates how to use the `string.split` function to divide an input string into an array of substrings based on a specified delimiter. The example iterates through the resulting array and outputs each element.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/builtins.md#_snippet_90

LANGUAGE: Scriban
CODE:

```
{{ for word in "Hi, how are you today?" | string.split ' ' ~}}
{{ word }}
{{ end ~}}
```

LANGUAGE: HTML
CODE:

```
Hi,
how
are
you
today?
```

---

TITLE: object.eval_template Example
DESCRIPTION: Illustrates how object.eval_template evaluates a string containing a Scriban template.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/builtins.md#_snippet_48

LANGUAGE: scriban-html
CODE:

```
{{ "This is a template text {{ 1 + 2 }}" | object.eval_template }}
```

LANGUAGE: html
CODE:

```
This is a template text 3
```

---

TITLE: Offsetting an Array in Scriban
DESCRIPTION: Applies the `array.offset` function to return a new list containing elements from the specified index onwards. The example shows offsetting a numeric list by 2, returning elements starting from the third element.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/builtins.md#_snippet_14

LANGUAGE: Scriban
CODE:

```
{{ [4, 5, 6, 7, 8] | array.offset 2 }}
```

LANGUAGE: HTML
CODE:

```
[6, 7, 8]
```

---

TITLE: Example: Checking if String is Empty in Scriban
DESCRIPTION: Demonstrates using the `string.empty` function to check if a string is empty. Shows both the input Scriban template and the resulting HTML output (a boolean value).

SOURCE: https://github.com/scriban/scriban/blob/master/doc/builtins.md#_snippet_70

LANGUAGE: scriban-html
CODE:

```
{{ "" | string.empty }}
```

LANGUAGE: html
CODE:

```
true
```

---

TITLE: Scriban Nested Escape Block (Output)
DESCRIPTION: The resulting output from the nested escape block example, showing the literal escape block syntax.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/language.md#_snippet_11

LANGUAGE: text
CODE:

```
This is an escaped block: }%} here
```

---

TITLE: Extracting Substrings with string.slice1 in Scriban
DESCRIPTION: Illustrates the `string.slice1` function, which extracts a substring starting at a specified index. By default, it returns only one character from the start index, but an optional length parameter can be provided.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/builtins.md#_snippet_89

LANGUAGE: scriban-html
CODE:

```
{{ "hello" | string.slice1 0 }}
{{ "hello" | string.slice1 1 }}
{{ "hello" | string.slice1 1 3 }}
{{ "hello" | string.slice1 1 length: 3 }}
```

LANGUAGE: html
CODE:

```
h
e
ell
ell
```

---

TITLE: Converting String to Double with string.to_double in Scriban
DESCRIPTION: Demonstrates the `string.to_double` function for converting a string into a 64-bit double-precision floating-point number. The example shows arithmetic with the converted double.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/builtins.md#_snippet_97

LANGUAGE: Scriban
CODE:

```
{{ "123.4" | string.to_double + 1 }}
```

LANGUAGE: HTML
CODE:

```
124.4
```

---

TITLE: Appending String in Scriban
DESCRIPTION: Demonstrates the `string.append` function for concatenating two strings. The example appends " World" to "Hello".

SOURCE: https://github.com/scriban/scriban/blob/master/doc/builtins.md#_snippet_66

LANGUAGE: scriban-html
CODE:

```
{{ "Hello" | string.append " World" }}
```

---

TITLE: Scriban: include_join with Template Separator
DESCRIPTION: Another `include_join` example, demonstrating the use of a template (`tpl:separator.html`) as the separator between included templates. The entire output is wrapped in literal `<div>` tags. Requires `ITemplateLoader`.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/language.md#_snippet_107

LANGUAGE: Scriban
CODE:

```
include_join ['myinclude1.html', 'myinclude2.html', 'myinclude3.html'] 'tpl:separator.html' '<div>' '</div>'
```

---

TITLE: Scriban Greedy Whitespace Strip (Left) Input
DESCRIPTION: Example showing the use of {{- to remove all preceding whitespace, including newlines, before a Scriban code block.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/language.md#_snippet_12

LANGUAGE: scriban-html
CODE:

```
This is a <
{{- name}}> text
```

---

TITLE: Formatting Date with date.to_string
DESCRIPTION: Demonstrates parsing a date string and formatting it using the `date.to_string` function with different format patterns. The second example shows formatting with a specific culture ('fr-FR').

SOURCE: https://github.com/scriban/scriban/blob/master/doc/builtins.md#_snippet_28

LANGUAGE: Scriban
CODE:

```
{{ date.parse '2016/01/05' | date.to_string '%d %b %Y' }}
{{ date.parse '2016/01/05' | date.to_string '%d %B %Y' 'fr-FR' }}
```

LANGUAGE: HTML
CODE:

```
05 Jan 2016
05 janvier 2016
```

---

TITLE: Converting String to Float with string.to_float in Scriban
DESCRIPTION: Illustrates how `string.to_float` converts a string into a 32-bit floating-point number. The example performs addition with the resulting float.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/builtins.md#_snippet_96

LANGUAGE: Scriban
CODE:

```
{{ "123.4" | string.to_float + 1 }}
```

LANGUAGE: HTML
CODE:

```
124.4
```

---

TITLE: Example: Case-Insensitive String Comparison in Scriban
DESCRIPTION: Demonstrates using the `string.equals_ignore_case` function to compare two strings ignoring case. Shows both the input Scriban template and the resulting HTML output (a boolean value).

SOURCE: https://github.com/scriban/scriban/blob/master/doc/builtins.md#_snippet_74

LANGUAGE: scriban-html
CODE:

```
{{ "Scriban" | string.equals_ignore_case "SCRIBAN" }}
```

LANGUAGE: html
CODE:

```
true
```

---

TITLE: Splitting String by Regex in Scriban
DESCRIPTION: Demonstrates how to use the `regex.split` function to split a string based on a regular expression pattern. The example splits a comma-separated string, handling optional whitespace around commas.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/builtins.md#_snippet_63

LANGUAGE: scriban-html
CODE:

```
{{ "a, b   , c,    d" | regex.split `\s*,\s*` }}
```

---

TITLE: Rendered Output for Timespan from Minutes Example HTML
DESCRIPTION: Presents the HTML output generated by evaluating the Scriban template `{{ (timespan.from_minutes 5).minutes }}`. The output `5` confirms that a timespan created from 5 minutes has a `.minutes` property value of 5.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/builtins.md#_snippet_117

LANGUAGE: html
CODE:

```
5
```

---

TITLE: Getting Object Values in Scriban
DESCRIPTION: Shows how to use the object.values function to extract the values of members from an object and then sort them using array.sort.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/builtins.md#_snippet_56

LANGUAGE: Scriban
CODE:

```
{{ product | object.values | array.sort }}
```

---

TITLE: Initializing Scriban Object with Members (Simple Syntax)
DESCRIPTION: Illustrates how to initialize a Scriban object with predefined members using a simple key-value syntax.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/language.md#_snippet_40

LANGUAGE: Scriban
CODE:

```
{{ myobject = { member1: "yes", member2: "no" } }}
```

---

TITLE: Example: Checking if String is Empty or Whitespace in Scriban
DESCRIPTION: Demonstrates using the `string.whitespace` function to check if a string is empty or contains only whitespace. Shows both the input Scriban template and the resulting HTML output (a boolean value).

SOURCE: https://github.com/scriban/scriban/blob/master/doc/builtins.md#_snippet_71

LANGUAGE: scriban-html
CODE:

```
{{ "" | string.whitespace }}
```

LANGUAGE: html
CODE:

```
true
```

---

TITLE: Defining .NET Function for Scriban Interop - C#
DESCRIPTION: Example of a static C# method signature that can be called from Scriban. Scriban can map arguments, including named arguments, to the parameters of such methods.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/language.md#_snippet_90

LANGUAGE: C#
CODE:

```
public static string MyProcessor(string left, string right, int count, string options = null)
{
    // ...
}
```

---

TITLE: Parsing Liquid Template in Scriban (C#)
DESCRIPTION: Demonstrates how to parse a Liquid template string using the `Template.ParseLiquid` method and then render it by providing an anonymous object as the data context. The example shows parsing an input string and printing the rendered output.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/runtime.md#_snippet_4

LANGUAGE: c#
CODE:

```
// An Liquid
var inputTemplateAsText = "This is a {{ name }} template";

// Parse the template
var template = Template.ParseLiquid(inputTemplateAsText);

// Renders the template with the variable `name` exposed to the template
var result = template.Render(new { name = "Hello World"});

// Prints the result: "This is a Hello World template"
Console.WriteLine(result);
```

---

TITLE: Getting Object Size/Length in Scriban
DESCRIPTION: Returns the size of the input object. This means length for strings, number of elements for lists/arrays, and number of members for objects.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/builtins.md#_snippet_53

LANGUAGE: scriban-html
CODE:

```
{{ [1, 2, 3] | object.size }}
```

---

TITLE: Converting String to Long with string.to_long in Scriban
DESCRIPTION: Shows the usage of `string.to_long` to convert a string into a 64-bit long integer. The example demonstrates performing arithmetic on the converted long.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/builtins.md#_snippet_95

LANGUAGE: Scriban
CODE:

```
{{ "123678912345678" | string.to_long + 1 }}
```

LANGUAGE: HTML
CODE:

```
123678912345679
```

---

TITLE: Scriban Output: Auto-indentation with Greedy Strip
DESCRIPTION: Shows the output when a greedy right strip is used. The auto-indentation is skipped, and the multi-line value starts at the beginning of the line.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/language.md#_snippet_23

LANGUAGE: text
CODE:

```
test1\ntest2\ntest3\nHello
```

---

TITLE: Reversing an Array in Scriban
DESCRIPTION: Applies the `array.reverse` function to create a new list with the elements in reverse order. The example demonstrates reversing a simple numeric list.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/builtins.md#_snippet_16

LANGUAGE: Scriban
CODE:

```
{{ [4, 5, 6, 7] | array.reverse }}
```

LANGUAGE: HTML
CODE:

```
[7, 6, 5, 4]
```

---

TITLE: Initializing Empty Scriban Object
DESCRIPTION: Shows the basic syntax for creating an empty object in Scriban.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/language.md#_snippet_39

LANGUAGE: Scriban
CODE:

```
{{ myobject = {} }}
```

---

TITLE: Rendered Output for Timespan from Seconds Example HTML
DESCRIPTION: Displays the HTML output produced by the Scriban template `{{ (timespan.from_seconds 5).seconds }}`. The output `5` verifies that creating a timespan from 5 seconds results in a timespan with a `.seconds` property value of 5.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/builtins.md#_snippet_119

LANGUAGE: html
CODE:

```
5
```

---

TITLE: Getting Unique Array Elements (Scriban)
DESCRIPTION: Shows the usage of the `array.uniq` function to remove duplicate elements from a list, returning a new list containing only the unique values.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/builtins.md#_snippet_21

LANGUAGE: scriban-html
CODE:

```
{{ [1, 1, 4, 5, 8, 8] | array.uniq }}
```

---

TITLE: Getting the First Element of a List in Scriban
DESCRIPTION: The `array.first` function returns the first element of the input list. It takes a list as an argument and returns the value of the first element.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/builtins.md#_snippet_8

LANGUAGE: scriban-html
CODE:

```
{{ [4, 5, 6] | array.first }}
```

---

TITLE: Scriban Greedy Whitespace Strip (Right) Input
DESCRIPTION: Example showing the use of -}} to remove all following whitespace, including newlines, after a Scriban code block.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/language.md#_snippet_14

LANGUAGE: scriban-html
CODE:

```
This is a <{{ name -}}
> text:
```

---

TITLE: Getting Object Type in Scriban
DESCRIPTION: Returns a string representing the type of the input object. Possible types include string, boolean, number, array, iterator, and object.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/builtins.md#_snippet_54

LANGUAGE: scriban-html
CODE:

```
{{ null | object.typeof }}
{{ true | object.typeof }}
{{ 1 | object.typeof }}
{{ 1.0 | object.typeof }}
{{ "text" | object.typeof }}
{{ 1..5 | object.typeof }}
{{ [1,2,3,4,5] | object.typeof }}
{{ {} | object.typeof }}
{{ object | object.typeof }}
```

---

TITLE: Rendered Output for Timespan from Hours Example HTML
DESCRIPTION: Shows the HTML output produced by the Scriban template `{{ (timespan.from_hours 5).hours }}`. The output `5` verifies that creating a timespan from 5 hours results in a timespan with an `.hours` property value of 5.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/builtins.md#_snippet_115

LANGUAGE: html
CODE:

```
5
```

---

TITLE: Rendered Output for Timespan from Milliseconds Example HTML
DESCRIPTION: Presents the HTML output generated by evaluating the Scriban template `{{ (timespan.from_milliseconds 5).milliseconds }}`. The output `5` confirms that a timespan created from 5 milliseconds has a `.milliseconds` property value of 5.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/builtins.md#_snippet_121

LANGUAGE: html
CODE:

```
5
```

---

TITLE: Converting String to Integer with string.to_int in Scriban
DESCRIPTION: Explains how to use `string.to_int` to convert a string representation of a number into a 32-bit integer. The example adds 1 to the converted integer.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/builtins.md#_snippet_94

LANGUAGE: Scriban
CODE:

```
{{ "123" | string.to_int + 1 }}
```

LANGUAGE: HTML
CODE:

```
124
```

---

TITLE: Getting the Last Element of a List in Scriban
DESCRIPTION: The `array.last` function returns the last element of the input list. It takes a list as an argument and returns the value of the last element.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/builtins.md#_snippet_11

LANGUAGE: scriban-html
CODE:

```
{{ [4, 5, 6] | array.last }}
```

---

TITLE: Create Timespan from Seconds in Scriban
DESCRIPTION: Uses the `timespan.from_seconds` function to create a timespan object representing 5 seconds. The example then retrieves the second component using the `.seconds` property, demonstrating creation from seconds.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/builtins.md#_snippet_118

LANGUAGE: scriban-html
CODE:

```
{{ (timespan.from_seconds 5).seconds }}
```

---

TITLE: Importing .NET Object with Custom Renamer (C#)
DESCRIPTION: Explains how to import a .NET object into a `ScriptObject` using a custom `MemberRenamerDelegate` (e.g., `member => member.Name`) to control the naming convention of exposed members, preserving original .NET names in this example.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/runtime.md#_snippet_29

LANGUAGE: C#
CODE:

```
var scriptObject1 = new ScriptObject();
// Here the renamer will just return a same member name as the original
// hence importing .NET member name as-is
scriptObject1.Import(new MyObject(), renamer: member => member.Name);

var context = new TemplateContext();
context.PushGlobal(scriptObject1);

var template = Template.Parse("This is Hello: `{{Hello}}`");
var result = template.Render(context);

// Prints This is MyFunctions.Hello: `hello from method!`
Console.WriteLine(result);

```

---

TITLE: Calling Custom Scriban Functions with Various Argument Styles (Scriban)
DESCRIPTION: Demonstrates how to call custom C# functions (`hello_opt`, `hello_args`) imported into a Scriban template context. Shows usage of regular, optional, named, and `params` arguments.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/runtime.md#_snippet_14

LANGUAGE: scriban-html
CODE:

```
{{ hello_opt "test" }}
{{ hello_opt "test" "my_option" }}
{{ hello_opt "test" option: "my_option" }}
{{ hello_opt text: "test"  }}
{{ hello_args "this" "is" "a" "test"}}
{{ hello_args "this" "is" args: "a" args: "test"}}
```

---

TITLE: Initializing Empty Scriban Array
DESCRIPTION: Shows the basic syntax for creating an empty array in Scriban.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/language.md#_snippet_48

LANGUAGE: Scriban
CODE:

```
{{ myarray = [] }}
```

---

TITLE: Rendered Output for Timespan from Days Example HTML
DESCRIPTION: Displays the HTML output generated by evaluating the Scriban template `{{ (timespan.from_days 5).days }}`. The output `5` confirms that creating a timespan from 5 days results in a timespan where the `.days` property is 5.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/builtins.md#_snippet_113

LANGUAGE: html
CODE:

```
5
```

---

TITLE: Initializing Scriban Object with Members (JSON Syntax)
DESCRIPTION: Shows how to initialize a Scriban object with predefined members using a syntax similar to JSON, requiring double quotes for member names.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/language.md#_snippet_41

LANGUAGE: Scriban
CODE:

```
{{ myobject = { "member1": "yes", "member2": "no" } }}
```

---

TITLE: Scriban: Including Multiple Templates with include_join
DESCRIPTION: Shows `include_join` rendering a list of templates (`myinclude1.html`, etc.). It joins their outputs using a specified separator (`<br/>`) and optionally wraps the entire result with begin/end delimiters (here, templates `begin.html` and `end.html`). Requires `ITemplateLoader`.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/language.md#_snippet_106

LANGUAGE: Scriban
CODE:

```
include_join ['myinclude1.html', 'myinclude2.html', 'myinclude3.html'] '<br/>' 'tpl:begin.html' 'tpl:end.html'
```

---

TITLE: Create Timespan from Hours in Scriban
DESCRIPTION: Demonstrates creating a timespan object from 5 hours using `timespan.from_hours`. The example then retrieves the hour component using the `.hours` property. This shows how to construct a timespan directly from an hour value.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/builtins.md#_snippet_114

LANGUAGE: scriban-html
CODE:

```
{{ (timespan.from_hours 5).hours }}
```

---

TITLE: Scriban Non-Greedy Whitespace Strip (Loop) Output
DESCRIPTION: The output from the non-greedy whitespace strip example, showing that the lines containing only the Scriban loop control statements are removed, but the indentation of the list items is preserved.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/language.md#_snippet_19

LANGUAGE: html
CODE:

```
<ul>
    <li>Orange</li>
    <li>Banana</li>
    <li>Apple</li>
</ul>
```

---

TITLE: Initializing Scriban Array with Items (Single Line)
DESCRIPTION: Illustrates how to initialize a Scriban array with predefined items on a single line.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/language.md#_snippet_49

LANGUAGE: Scriban
CODE:

```
{{ myarray = [1, 2, 3, 4] }}
```

---

TITLE: Using Parametric Function with Optional Args (Override) in Scriban
DESCRIPTION: Demonstrates calling a parametric function 'sub_opt' and providing values to override the default values of the optional parameters 'z' and 'w'.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/language.md#_snippet_65

LANGUAGE: Scriban
CODE:

```
{{sub_opt 5 1 0 }}
{{5 | sub_opt 1 0}}
```

---

TITLE: Applying Member Filter During ScriptObject Import (C#)
DESCRIPTION: Shows how to use the `filter` parameter of the `ScriptObject.Import` method to specify a custom `MemberFilterDelegate`. This example filters to include only public properties whose names contain 'Yo' when importing a `MyObject` instance.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/runtime.md#_snippet_33

LANGUAGE: C#
CODE:

```
var scriptObject1 = new ScriptObject();
// Imports only properties that contains the word "Yo"
scriptObject1.Import(new MyObject(), filter: member => member is PropertyInfo && member.Name.Contains("Yo"));
```

---

TITLE: Using Parametric Function in Scriban
DESCRIPTION: Demonstrates calling a parametric function 'sub' with direct arguments and using it as a pipe receiver. Arguments must match the declared parameters.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/language.md#_snippet_62

LANGUAGE: Scriban
CODE:

```
{{sub 5 1}}
{{5 | sub 1}}
```

---

TITLE: Serializing Scriban Value to JSON
DESCRIPTION: Provides examples of using object.to_json to convert various Scriban values (object, boolean, null) into their JSON string representations. This function is available in net7.0+.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/builtins.md#_snippet_58

LANGUAGE: Scriban
CODE:

```
{{ { foo: "bar", baz: [1, 2, 3] } | object.to_json }}
{{ true | object.to_json }}
{{ null | object.to_json }}
```

---

TITLE: Scriban Input: Interpolated String Literal
DESCRIPTION: Demonstrates an interpolated string literal (starting with `$"` or `$'`) which allows embedding Scriban expressions or other strings directly within the string.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/language.md#_snippet_28

LANGUAGE: scriban-html
CODE:

```
{{ $"this is an interpolated string with an expression {1 + 2} and a substring {"Hello"}" }}
```

---

TITLE: Get String Literal (Scriban)
DESCRIPTION: Returns the literal representation of a string, escaping non-printable characters and double quotes. Takes the input string as an argument. Returns the escaped string literal.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/builtins.md#_snippet_76

LANGUAGE: Scriban
CODE:

```
{{ 'Hello\n"World"' | string.literal }}
```

LANGUAGE: HTML
CODE:

```
"Hello\n\"World\""
```

---

TITLE: Converting Scriban AST to Text (Keeping Trivia)
DESCRIPTION: This example shows how to preserve whitespace, comments, and other hidden symbols (trivia) when parsing a template by setting `KeepTrivia = true` in `LexerOptions`. This allows the `ToText()` method to reproduce the original template text more accurately.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/runtime.md#_snippet_39

LANGUAGE: c#
CODE:

```
// Specifying the KeepTrivia allow to keep as much as hidden symbols from the original template (white spaces, newlines...etc.)
var template = Template.Parse(@"This is a {{ name   +   ## With some comment ## '' }} template", lexerOptions: new LexerOptions() { KeepTrivia = true });

// Prints "This is a {{ name   +   ## With some comment ## '' }} template"
Console.WriteLine(template.ToText());
```

---

TITLE: Accessing Variables from Function in Scriban TemplateContext (C#)
DESCRIPTION: Provides an example of how a custom C# function method (Invoke) can access variables defined anywhere in the TemplateContext stack by using the context.GetValue() method with a ScriptVariableGlobal.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/runtime.md#_snippet_25

LANGUAGE: C#
CODE:

```
public virtual object Invoke(TemplateContext context, ScriptNode callerContext, ScriptArray arguments, ScriptBlockStatement blockStatement)
{
    // var1 defined in scriptObject pushed to global anywhere down the stack
    ScriptVariableGlobal scriptVariableGlobal = new ScriptVariableGlobal("var1");

    object contextObject = context.GetValue(scriptVariableGlobal);

    string var1Value = "";
    // cast to the correct type
    if (contextObject != null) {
        var1Value = (string)contextObject;
    }

    return $"var1 is {var1Value}";
}
```

---

TITLE: Implementing Simple Disk Template Loader (C#)
DESCRIPTION: Provides a basic implementation of the `ITemplateLoader` interface that loads templates from the disk. The `GetPath` method constructs a file path based on the template name and current directory, and the `Load` method reads the content of the file at the specified path.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/runtime.md#_snippet_36

LANGUAGE: C#
CODE:

```
/// <summary>
/// A very simple ITemplateLoader loading directly from the disk, without any checks...etc.
/// </summary>
public class MyIncludeFromDisk : ITemplateLoader
{
    string GetPath(TemplateContext context, SourceSpan callerSpan, string templateName)
    {
        return Path.Combine(Environment.CurrentDirectory, templateName);
    }

    string Load(TemplateContext context, SourceSpan callerSpan, string templatePath)
    {
        // Template path was produced by the `GetPath` method above in case the Template has
        // not been loaded yet
        return File.ReadAllText(templatePath);
    }
}
```

---

TITLE: Extracting Substrings with string.slice in Scriban
DESCRIPTION: Demonstrates the `string.slice` function for extracting a substring starting at a specified index. An optional length parameter can define the substring's length; otherwise, it returns the rest of the string.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/builtins.md#_snippet_88

LANGUAGE: scriban-html
CODE:

```
{{ "hello" | string.slice 0 }}
{{ "hello" | string.slice 1 }}
{{ "hello" | string.slice 1 3 }}
{{ "hello" | string.slice 1 length:3 }}
```

LANGUAGE: html
CODE:

```
hello
ello
ell
ell
```

---

TITLE: Example: Checking if String Contains Substring in Scriban
DESCRIPTION: Demonstrates using the `string.contains` function to check if a string contains a specified substring. Shows both the input Scriban template and the resulting HTML output (a boolean value).

SOURCE: https://github.com/scriban/scriban/blob/master/doc/builtins.md#_snippet_69

LANGUAGE: scriban-html
CODE:

```
{{ "This is easy" | string.contains "easy" }}
```

LANGUAGE: html
CODE:

```
true
```

---

TITLE: Output from Calling Custom Scriban Functions (Scriban)
DESCRIPTION: Shows the expected output when rendering a Scriban template that calls the custom C# functions `hello_opt` and `hello_args` using different argument passing styles.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/runtime.md#_snippet_15

LANGUAGE: scriban-html
CODE:

```
hello test with option:
hello test with option:my_option
hello test with option:my_option
hello test with option:
hello this,is,a,test
hello this,is,a,test
```

---

TITLE: Pushing ScriptObjects onto Scriban TemplateContext Stack (C#)
DESCRIPTION: Demonstrates how to create ScriptObject instances, add variables to them, and push them onto the TemplateContext stack using PushGlobal. Shows how variable resolution prioritizes objects higher on the stack, allowing for variable overrides.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/runtime.md#_snippet_22

LANGUAGE: C#
CODE:

```
// Creates scriptObject1
var scriptObject1 = new ScriptObject();
scriptObject1.Add("var1", "Variable 1");
scriptObject1.Add("var2", "Variable 2");

// Creates scriptObject2
var scriptObject2 = new ScriptObject();
// overrides the variable "var2"
scriptObject2.Add("var2", "Variable 2 - from ScriptObject 2");

// Creates a template with (builtins) + scriptObject1 + scriptObject2 variables
var context = new TemplateContext();
context.PushGlobal(scriptObject1);
context.PushGlobal(scriptObject2);

var template = Template.Parse("This is var1: `{{var1}}` and var2: `{{var2}}");
var result = template.Render(context);

// Prints: "This is var1: `Variable 1` and var2: `Variable 2 - from ScriptObject 2"
Console.WriteLine(result);
```

---

TITLE: Example: Checking if String Ends With Substring in Scriban
DESCRIPTION: Demonstrates using the `string.ends_with` function to check if a string ends with a specified substring. Shows both the input Scriban template and the resulting HTML output (a boolean value).

SOURCE: https://github.com/scriban/scriban/blob/master/doc/builtins.md#_snippet_73

LANGUAGE: scriban-html
CODE:

```
{{ "This is easy" | string.ends_with "easy" }}
```

LANGUAGE: html
CODE:

```
true
```

---

TITLE: Scriban: Including External Template
DESCRIPTION: Demonstrates the `include` function for rendering external templates like `myinclude.html`. It parses and executes the specified template, inserting its output into the current stream or capturing it into a variable. Requires a configured `ITemplateLoader`.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/language.md#_snippet_104

LANGUAGE: Scriban
CODE:

```
include 'myinclude.html'
x = include 'myinclude.html'
x + " modified"
```

---

TITLE: Replacing First Substring Occurrence with string.replace_first in Scriban
DESCRIPTION: Illustrates the usage of the `string.replace_first` function to replace only the first occurrence of a specified substring in an input string. An optional parameter allows starting the search from the end.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/builtins.md#_snippet_85

LANGUAGE: scriban-html
CODE:

```
{{ "Hello, world. Goodbye, world." | string.replace_first "world" "buddy" }}
```

LANGUAGE: html
CODE:

```
Hello, buddy. Goodbye, world.
```

---

TITLE: Calling Function with Named Arguments - Scriban
DESCRIPTION: Demonstrates calling a function (like a .NET method) using named arguments. Once a named argument is used, all subsequent arguments must also be named.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/language.md#_snippet_91

LANGUAGE: Scriban
CODE:

```
{{ my_processor "Hello" "World" count: 15 options: "optimized" }}
```

---

TITLE: Mapping Array Elements in Scriban
DESCRIPTION: Uses the `array.map` function to extract a specific member's value from each element in a list. The example demonstrates mapping the 'type' member from a list of product objects, then applying `array.uniq` and `array.sort`.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/builtins.md#_snippet_13

LANGUAGE: Scriban
CODE:

```
{{
products = [{title: "orange", type: "fruit"}, {title: "computer", type: "electronics"}, {title: "sofa", type: "furniture"}]
products | array.map "type" | array.uniq | array.sort }}
```

LANGUAGE: HTML
CODE:

```
["electronics", "fruit", "furniture"]
```

---

TITLE: Cycling Through Array Elements using array.cycle (Scriban)
DESCRIPTION: Loops through a list of values (strings in the example) and outputs them sequentially on each call. It cycles back to the beginning after exhausting the list. Can optionally use a group name for multiple independent cycles.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/builtins.md#_snippet_4

LANGUAGE: scriban-html
CODE:

```
{{ array.cycle ['one', 'two', 'three'] }}
{{ array.cycle ['one', 'two', 'three'] }}
{{ array.cycle ['one', 'two', 'three'] }}
{{ array.cycle ['one', 'two', 'three'] }}
```

---

TITLE: Using Simple Function in Scriban
DESCRIPTION: Demonstrates calling a simple function 'sub' with direct arguments and using it as a pipe receiver. When used with a pipe, the piped value becomes the first argument ($0).

SOURCE: https://github.com/scriban/scriban/blob/master/doc/language.md#_snippet_57

LANGUAGE: Scriban
CODE:

```
{{sub 5 1}}
{{5 | sub 1}}
```

---

TITLE: Importing a .NET Object Instance into a Scriban ScriptObject (C#)
DESCRIPTION: Shows how to create an instance of a .NET object (`MyObject`) and import its public properties into a `ScriptObject` using `ScriptObject.Import(object)`. Demonstrates accessing the imported property in a Scriban template.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/runtime.md#_snippet_19

LANGUAGE: C#
CODE:

```
var scriptObject1 = new ScriptObject();
scriptObject1.Import(new MyObject());

var context = new TemplateContext();
context.PushGlobal(scriptObject1);

var template = Template.Parse("This is Hello: `{{hello}}`");
var result = template.Render(context);

// Prints This is MyFunctions.Hello: `hello from method!`
Console.WriteLine(result);
```

---

TITLE: Create Timespan from Days in Scriban
DESCRIPTION: Uses the `timespan.from_days` function to create a timespan object representing 5 days. The example then accesses the `.days` property of the resulting timespan to retrieve the day component. This snippet demonstrates the basic usage and property access.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/builtins.md#_snippet_112

LANGUAGE: scriban-html
CODE:

```
{{ (timespan.from_days 5).days }}
```

---

TITLE: Evaluating a Scriban Expression (C#)
DESCRIPTION: Demonstrates how to use the static Template.Evaluate method to evaluate a Scriban expression against a given TemplateContext and retrieve the result without rendering output. It shows setting up a context with a variable and evaluating an arithmetic expression.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/runtime.md#_snippet_42

LANGUAGE: C#
CODE:

```
var scriptObject1 = new ScriptObject();
scriptObject1.Add("var1", 5);

var context = new TemplateContext();
context.PushGlobal(scriptObject1);

var result = Template.Evaluate("var1 * 5 + 2", context);
// Prints `27`
Console.WriteLine(result);
```

---

TITLE: String Repetition in Scriban
DESCRIPTION: Illustrates how to repeat a string multiple times using the '\*' operator with a string and an integer. The string and number operands can be swapped.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/language.md#_snippet_77

LANGUAGE: Scriban
CODE:

```
'a' * 5
```

---

TITLE: Removing Element by Index in Scriban
DESCRIPTION: Uses the `array.remove_at` function to create a new list with the element at the given index removed. Supports negative indices to remove from the end. The examples show removing an element by positive and negative indices.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/builtins.md#_snippet_15

LANGUAGE: Scriban
CODE:

```
{{ [4, 5, 6, 7, 8] | array.remove_at 2 }}
```

LANGUAGE: HTML
CODE:

```
[4, 5, 7, 8]
```

LANGUAGE: Scriban
CODE:

```
{{ [4, 5, 6, 7, 8] | array.remove_at (-1) }}
```

LANGUAGE: HTML
CODE:

```
[4, 5, 6, 7]
```

---

TITLE: Parsing Scriban Template with Scientific Language and ScriptOnly Mode (C#)
DESCRIPTION: Demonstrates parsing a template using both `ScriptMode.ScriptOnly` and `ScriptLang.Scientific` via `LexerOptions`, showing how scientific notation rules apply during evaluation.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/runtime.md#_snippet_3

LANGUAGE: C#
CODE:

```
// Create a template in ScriptOnly mode
var lexerOptions = new LexerOptions() { Lang = ScriptLang.Scientific, Mode = ScriptMode.ScriptOnly };
// Notice that code is not enclosed by `{{` and `}}`
var template = Template.Parse("y = x + 1; 2y;", lexerOptions: lexerOptions);
// Renders it with the specified parameter
var result = template.Evaluate(new {x = 10});
// Prints 22
Console.WriteLine(result);
```

---

TITLE: Using Parametric Function with Optional Args (Default) in Scriban
DESCRIPTION: Shows calling a parametric function 'sub_opt' providing only the required arguments. The optional parameters 'z' and 'w' will use their default values.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/language.md#_snippet_64

LANGUAGE: Scriban
CODE:

```
{{sub_opt 5 1}}
{{5 | sub_opt 1}}
```

---

TITLE: Sorting an Array in Scriban
DESCRIPTION: Sorts the elements of a list. Can sort based on element value or a specific member's value. The example shows sorting the unique 'type' values extracted from a list of products, demonstrating the function's use in a pipeline.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/builtins.md#_snippet_18

LANGUAGE: Scriban
CODE:

```
products | array.map "type" | array.uniq | array.sort
```

LANGUAGE: HTML
CODE:

```
["electronics", "fruit", "furniture"]
```

---

TITLE: Comparing with Scriban empty Variable
DESCRIPTION: Demonstrates how to use the special `empty` variable to check if an object or array is empty, primarily for compatibility with Liquid templates.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/language.md#_snippet_38

LANGUAGE: Scriban
CODE:

```
{{
a = {}
b = [1, 2]~}}
{{a == empty}}
{{b == empty}}
```

LANGUAGE: HTML
CODE:

```
true
false
```

---

TITLE: Scriban Input: Null Literal
DESCRIPTION: Demonstrates the `null` literal in Scriban.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/language.md#_snippet_32

LANGUAGE: scriban-html
CODE:

```
{{ null }}
```

---

TITLE: Accessing Scriban Array Items (Index Notation)
DESCRIPTION: Demonstrates accessing an array item using zero-based index notation.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/language.md#_snippet_51

LANGUAGE: Scriban
CODE:

```
{{ myarray[0] }}
```

---

TITLE: Adding Members to Scriban Object Dynamically
DESCRIPTION: Shows how to add new members to a pure Scriban object (created with `{}`) using simple assignment after initialization.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/language.md#_snippet_46

LANGUAGE: Scriban
CODE:

```
{{
  myobject = {}
  myobject.member3 = "may be"
  myobject.member3
}}
```

LANGUAGE: HTML
CODE:

```
may be
```

---

TITLE: Rendering a List with Scriban Template in C#
DESCRIPTION: This snippet illustrates a more complex templating scenario involving iterating over a list (`products`) and applying a filter (`string.truncate`) within the template, parsed with `Template.Parse` and rendered with a data object containing the list.

SOURCE: https://github.com/scriban/scriban/blob/master/readme.md#_snippet_2

LANGUAGE: C#
CODE:

```
var template = Template.Parse(@"
<ul id='products'>
  {{ for product in products }}
    <li>
      <h2>{{ product.name }}</h2>
           Price: {{ product.price }}
           {{ product.description | string.truncate 15 }}
    </li>
  {{ end }}
</ul>
");
var result = template.Render(new { Products = this.ProductList });
```

---

TITLE: Importing Properties Between ScriptObjects in Scriban (C#)
DESCRIPTION: Demonstrates how to use the `Import` method on a `ScriptObject` to copy properties and functions from another `ScriptObject` instance. Notes that this is a copy operation, not a reference.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/runtime.md#_snippet_17

LANGUAGE: C#
CODE:

```
var scriptObject1 = new ScriptObject();
scriptObject1.Add("var1", "Variable 1");

var scriptObject2 = new ScriptObject();
scriptObject2.Add("var2", "Variable 2");

// After this command, scriptObject2 contains var1 and var2
// But modifying var2 on scriptObject2 will not modify var2 on scriptObject1!
scriptObject2.Import(scriptObject1);
```

---

TITLE: Parsing a Scriban Template from File with Source Path (C#)
DESCRIPTION: Shows how to parse a Scriban template read from a file, passing the file path as the `sourceFilePath` argument to `Template.Parse` for improved error reporting.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/runtime.md#_snippet_1

LANGUAGE: C#
CODE:

```
// Parse the template
var template = Template.Parse(File.ReadAllText(filePath), filePath);
```

---

TITLE: Scriban Code Block Output Behavior (Input)
DESCRIPTION: Demonstrates how expressions within a Scriban code block are outputted, while assignment statements are not. Shows multiple expressions outputting results consecutively without intervening newlines.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/language.md#_snippet_3

LANGUAGE: scriban-html
CODE:

```
{{
  x = 5     # This assignment will not output anything
  x         # This expression will print 5
  x + 1     # This expression will print 6
}}
```

---

TITLE: Parsing and Rendering a Scriban Template in C#
DESCRIPTION: This snippet demonstrates the basic usage of Scriban to parse a template string using `Template.Parse` and then render it by providing an anonymous object with data to the `Render` method.

SOURCE: https://github.com/scriban/scriban/blob/master/readme.md#_snippet_0

LANGUAGE: C#
CODE:

```
// Parse a scriban template
var template = Template.Parse("Hello {{name}}!");
var result = template.Render(new { Name = "World" }); // => "Hello World!"
```

---

TITLE: Scriban Input: Boolean Literals
DESCRIPTION: Demonstrates the boolean literals `true` and `false` in Scriban.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/language.md#_snippet_30

LANGUAGE: scriban-html
CODE:

```
{{ true }}\n{{ false }}
```

---

TITLE: Scriban Mixed Blocks Output Behavior (Input)
DESCRIPTION: Shows how separating Scriban statements into multiple code blocks allows text (including newlines) to appear between the outputs of each block.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/language.md#_snippet_5

LANGUAGE: scriban-html
CODE:

```
{{ x = 5 }}
{{ x }}
{{ x + 1 }}
```

---

TITLE: Calling Function with Positional Arguments - Scriban
DESCRIPTION: Demonstrates a basic function call in Scriban passing multiple arguments separated by whitespace. Arguments can be literals, strings (quoted), or expressions.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/language.md#_snippet_87

LANGUAGE: Scriban
CODE:

```
{{ myfunction arg1 "arg2" (1+5) }}
```

---

TITLE: Translating Liquid for Tag to Scriban
DESCRIPTION: Demonstrates the translation of the Liquid `for`/`endfor` loop for iterating over collections to the identical `for`/`end` structure in Scriban.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/liquid-support.md#_snippet_5

LANGUAGE: liquid
CODE:

```
{%- for variable in (1..5) -%}
    This is variable {{variable}}
{% endfor -%}
```

LANGUAGE: scriban
CODE:

```
{{ for variable in (1..5) -}}
    This is variable {{variable}}
{{ end }}
```

---

TITLE: Defining a Simple .NET Object for Scriban Import (C#)
DESCRIPTION: Defines a basic C# class (`MyObject`) with a public property (`Hello`) that can be imported into a Scriban `ScriptObject`.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/runtime.md#_snippet_18

LANGUAGE: C#
CODE:

```
public class MyObject
{
    public MyObject()
    {
        Hello = "hello from property!";
    }

    public string Hello { get; set; }
}
```

---

TITLE: Using Pipe Operator - Scriban
DESCRIPTION: Shows how to use the pipe operator `|` to pass the result of the left-hand expression as the first argument to the function on the right-hand side.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/language.md#_snippet_88

LANGUAGE: Scriban
CODE:

```
{{ date.parse '2016/01/05' | date.to_string '%g' }}
```

---

TITLE: Checking String Prefix with string.starts_with in Scriban
DESCRIPTION: Illustrates the usage of the `string.starts_with` function to check if an input string begins with a specific substring. It returns a boolean value indicating the result of the check.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/builtins.md#_snippet_91

LANGUAGE: Scriban
CODE:

```
{{ "This is easy" | string.starts_with "This" }}
```

LANGUAGE: HTML
CODE:

```
true
```

---

TITLE: Adding Items to Scriban Array by Index
DESCRIPTION: Shows how to add items to a pure Scriban array (created with `[]`) by assigning values to specific indices, automatically expanding the array.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/language.md#_snippet_52

LANGUAGE: Scriban
CODE:

```
{{
  myarray = []
  myarray[0] = 1
  myarray[1] = 2
  myarray[2] = 3
  myarray[3] = 4
}}
```

---

TITLE: Scriban: Importing Object Members
DESCRIPTION: Demonstrates the `import` statement. It makes members of `myobject` (like `member1`) accessible directly as variables in the current scope, allowing `member1` to be used without the `myobject.` prefix.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/language.md#_snippet_101

LANGUAGE: Scriban
CODE:

```
{{
  myobject = { member1: "yes" }
  import myobject
  member1  # will print the "yes" string to the output
}}
```

---

TITLE: Chaining Pipes Across Lines - Scriban
DESCRIPTION: Illustrates how the pipe operator's greedy nature allows chaining multiple function calls across several lines for readability. The `-}}` syntax is used to trim leading/trailing whitespace.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/language.md#_snippet_89

LANGUAGE: Scriban
CODE:

```
{{-
"text"                        |
      string.append "END"     |
      string.prepend "START"
-}}
```

---

TITLE: Adding Properties to Scriban Array
DESCRIPTION: Demonstrates that Scriban arrays can also have properties attached to them, in addition to their indexed items.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/language.md#_snippet_54

LANGUAGE: Scriban
CODE:

```
{{
a = [5, 6, 7]
a.x = "yes"
a.x + a[0]
}}
```

LANGUAGE: HTML
CODE:

```
yes5
```

---

TITLE: Adding Property to Scriban Builtin Object (Scriban)
DESCRIPTION: Shows how to add a new property directly to an existing builtin object, such as the 'string' object, using Scriban's assignment syntax within a template.

SOURCE: https://github.com/scriban/scriban/blob/master/doc/runtime.md#_snippet_24

LANGUAGE: Scriban
CODE:

```
{{
   string.myprop = "Yoyo"
}}
```

---

TITLE: Disambiguating Array Indexer vs Initializer
DESCRIPTION: Explains how whitespace before `[` can change whether `[...]` is interpreted as an array indexer (no whitespace) or an array initializer passed as an argument (whitespace).

SOURCE: https://github.com/scriban/scriban/blob/master/doc/language.md#_snippet_53

LANGUAGE: Scriban
CODE:

```
{{
myfunction [1]  # There is a whitespace after myfunction.
                # It will result in a call to myfunction passing an array as an argument

myvariable[1]   # Without a whitespace, this is accessing
                # an element in the array provided by myvariable
}}
```
