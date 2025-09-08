using System;
using System.Threading.Tasks;

namespace TestPatterns
{
    public class PatternTestFile
    {
        // Test empty catch block detection
        public void TestEmptyCatch()
        {
            try
            {
                throw new Exception("Test");
            }
            catch
            {
                // Empty catch block - should be detected
            }
        }

        // Test async without ConfigureAwait
        public async Task TestAsyncPattern()
        {
            await Task.Delay(1000); // Missing ConfigureAwait(false)
        }

        // Test magic numbers
        public void TestMagicNumbers()
        {
            var timeout = 5000; // Magic number
            var buffer = new byte[1024]; // Another magic number
            if (timeout > 3600) // Yet another magic number
            {
                Console.WriteLine("Too long");
            }
        }

        // Test large method (simulate with comments to reach 50+ lines)
        public void TestLargeMethod()
        {
            // Line 1
            // Line 2
            // Line 3
            // Line 4
            // Line 5
            // Line 6
            // Line 7
            // Line 8
            // Line 9
            // Line 10
            // Line 11
            // Line 12
            // Line 13
            // Line 14
            // Line 15
            // Line 16
            // Line 17
            // Line 18
            // Line 19
            // Line 20
            // Line 21
            // Line 22
            // Line 23
            // Line 24
            // Line 25
            // Line 26
            // Line 27
            // Line 28
            // Line 29
            // Line 30
            // Line 31
            // Line 32
            // Line 33
            // Line 34
            // Line 35
            // Line 36
            // Line 37
            // Line 38
            // Line 39
            // Line 40
            // Line 41
            // Line 42
            // Line 43
            // Line 44
            // Line 45
            // Line 46
            // Line 47
            // Line 48
            // Line 49
            // Line 50
            Console.WriteLine("Large method completed");
        }
    }
}