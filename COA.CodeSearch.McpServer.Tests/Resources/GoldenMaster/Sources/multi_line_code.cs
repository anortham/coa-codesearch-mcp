using System;
using System.Collections.Generic;

namespace TestNamespace
{
    public class MultiLineProcessor
    {
        public void ProcessItems(
            List<string> items
        )
        {
            foreach (var item in items)
            {
                Console.WriteLine(item);
            }
        }

        public void AnotherMethod()
        {
            var items = new List<string> { "item1", "item2", "item3" };
            ProcessItems(items);
        }
    }
}