using System;

namespace DeletionTest
{
    public class DebugClass
    {
        public void ProcessValue(string value)
        {
            
            var result = value.ToUpperInvariant();
            
            
            // Actual processing
            ProcessInternal(result);
            
        }
        
        private void ProcessInternal(string value)
        {
            // Implementation details
            Console.WriteLine($"Processing: {value}");
        }
    }
}