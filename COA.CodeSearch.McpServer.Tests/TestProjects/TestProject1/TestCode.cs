using System;
using System.Collections.Generic;

namespace TestNamespace
{
    /// <summary>
    /// Test class for unit tests
    /// </summary>
    public class TestClass
    {
        private int _field;
        
        public string Property { get; set; } = string.Empty;
        
        public event EventHandler? TestEvent;
        
        public TestClass()
        {
            _field = 0;
        }
        
        public void TestMethod()
        {
            var instance = new TestClass();
            instance.CallMethod();
        }
        
        public void CallMethod()
        {
            Console.WriteLine("Called");
        }
        
        public virtual void VirtualMethod()
        {
        }
    }
    
    public interface IService
    {
        void DoWork();
    }
    
    public class ServiceA : IService
    {
        public void DoWork()
        {
            Console.WriteLine("ServiceA doing work");
        }
    }
    
    public class ServiceB : IService
    {
        public void DoWork()
        {
            Console.WriteLine("ServiceB doing work");
        }
    }
    
    public abstract class BaseService
    {
        public abstract void Process();
    }
    
    public class ConcreteService : BaseService
    {
        public override void Process()
        {
            Console.WriteLine("Processing");
        }
    }
    
    public class DerivedClass : TestClass
    {
        public override void VirtualMethod()
        {
            base.VirtualMethod();
        }
    }
    
    public delegate void TestDelegate(string message);
    
    public enum TestEnum
    {
        Value1,
        Value2,
        Value3
    }
    
    public class OuterClass
    {
        public class NestedClass
        {
            public void NestedMethod() { }
        }
        
        public struct NestedStruct
        {
            public int Field;
        }
    }
}

namespace TestNamespace.SubNamespace
{
    public class AnotherTestClass
    {
        public void Method1() { }
        public void Method2() { }
    }
}