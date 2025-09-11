using System;
using System.Threading.Tasks;

namespace RegexCaptureTest
{
    public class ServiceMethods
    {
        public string ProcessData(string input)
        {
            return input.ToUpperInvariant();
        }
        
        public int CalculateValue(int number)
        {
            return number * 2;
        }
        
        public void ExecuteOperation(string operation)
        {
            Console.WriteLine($"Executing: {operation}");
        }
        
        public bool ValidateInput(string data)
        {
            return !string.IsNullOrWhiteSpace(data);
        }
    }
}