using System;
using System.Threading.Tasks;

namespace RegexCaptureTest
{
    public class ServiceMethods
    {
        public string ProcessDataAsync(string input)
        {
            return input.ToUpperInvariant();
        }
        
        public int CalculateValueAsync(int number)
        {
            return number * 2;
        }
        
        public void ExecuteOperationAsync(string operation)
        {
            Console.WriteLine($"Executing: {operation}");
        }
        
        public bool ValidateInputAsync(string data)
        {
            return !string.IsNullOrWhiteSpace(data);
        }
    }
}