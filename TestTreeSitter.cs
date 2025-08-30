using TreeSitter;
using System;

class Test
{
    static void Main()
    {
        // Test what's available in TreeSitter namespace
        var language = new Language("CSharp");
        var parser = new Parser(language);
        var tree = parser.Parse("class Test { }");
        
        // Try to access tree properties
        Console.WriteLine(tree.GetType().GetProperties());
    }
}