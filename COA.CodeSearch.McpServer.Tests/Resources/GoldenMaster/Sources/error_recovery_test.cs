using System;

namespace ErrorRecoveryTest
{
    public class ErrorHandlingService
    {
        public void TestMethod()
        {
            Console.WriteLine("This file should remain unchanged when invalid regex is used");
            // This content should not be modified if the regex pattern is invalid
        }
    }
}