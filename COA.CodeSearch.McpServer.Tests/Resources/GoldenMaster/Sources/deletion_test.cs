using System;

namespace DeletionTest
{
    public class DebugClass
    {
        public void ProcessValue(string value)
        {
            // TODO: Remove this debug code
            Console.WriteLine("Debug: " + value);
            
            var result = value.ToUpperInvariant();
            
            // TODO: Remove this debug code
            Console.WriteLine("Debug: " + result);
            
            // Actual processing
            ProcessInternal(result);
            
            // TODO: Remove this debug code
            Console.WriteLine("Debug: " + "Processing complete");
        }
        
        private void ProcessInternal(string value)
        {
            // Implementation details
            Console.WriteLine($"Processing: {value}");
        }
    }
}