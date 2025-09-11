using System;
using System.Threading.Tasks;

namespace ConcurrencyTest
{
    public class ConcurrentProcessor
    {
        // CONCURRENT_MARKER: Test case 1
        public async Task ProcessAsync()
        {
            // CONCURRENT_MARKER: Test case 2
            await Task.Delay(100);
            // CONCURRENT_MARKER: Test case 3
        }

        public void ProcessSync()
        {
            // CONCURRENT_MARKER: Test case 4
            System.Threading.Thread.Sleep(100);
            // CONCURRENT_MARKER: Test case 5
        }
    }
}