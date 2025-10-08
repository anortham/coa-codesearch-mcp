using System;

namespace RefactorDemo
{
    public class TestClass
    {
        public void OldMethodName()
        {
            Console.WriteLine("Testing");
        }

        public void CallerMethod()
        {
            OldMethodName();
            OldMethodName();
        }

        public void AnotherCaller()
        {
            OldMethodName();
        }
    }
}
