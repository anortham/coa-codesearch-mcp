using System;
using System.Threading.Tasks;

namespace ConcurrencyTest
{
    public class ConcurrentProcessor
    {
        // SAFELY_REPLACED: Test case 1
        public async Task ProcessAsync()
        {
            // SAFELY_REPLACED: Test case 2
            await Task.Delay(100);
            // SAFELY_REPLACED: Test case 3
        }

        public void ProcessSync()
        {
            // SAFELY_REPLACED: Test case 4
            System.Threading.Thread.Sleep(100);
            // SAFELY_REPLACED: Test case 5
        }
    }
}