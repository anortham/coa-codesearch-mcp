using System;
using System.Collections.Generic;

namespace TestNamespace
{
    public class MultiLineProcessor
    {
        public async Task ProcessItemsAsync(List<string> items)
        {
            await foreach (var item in items.ToAsyncEnumerable())
            {
                await Console.Out.WriteLineAsync(item);
            }
        }

        public void AnotherMethod()
        {
            var items = new List<string> { "item1", "item2", "item3" };
            ProcessItems(items);
        }
    }
}