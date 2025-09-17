
using NUnit.Framework;
using FluentAssertions;
using COA.CodeSearch.McpServer.Services.TypeExtraction;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Tests.Services.TypeExtraction
{
    [TestFixture]
    public class CSharpTypeExtractionTests
    {
        private TypeExtractionService _service;
        private LanguageRegistry _languageRegistry;

        [SetUp]
        public void SetUp()
        {
            var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<TypeExtractionService>.Instance;
            var languageRegistryLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<LanguageRegistry>.Instance;
            _languageRegistry = new LanguageRegistry(languageRegistryLogger);
            _service = new TypeExtractionService(logger, _languageRegistry);
        }

        [TearDown]
        public void TearDown()
        {
            _languageRegistry?.Dispose();
        }

        [Test]
        public async Task Should_Extract_Class_And_Method_From_CSharp_Code()
        {
            // Arrange
            var code = @"
namespace MyNamespace
{
    public class MyClass
    {
        public void MyMethod()
        {
        }
    }
}";

            // Act
            var result = await _service.ExtractTypes(code, "test.cs");

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Types.Should().HaveCount(1);
            result.Types[0].Name.Should().Be("MyClass");
            result.Types[0].Kind.Should().Be("class");
            result.Methods.Should().HaveCount(1);
            result.Methods[0].Name.Should().Be("MyMethod");
            result.Methods[0].ReturnType.Should().Be("void");
        }

        [Test]
        public async Task Should_Extract_Correct_Return_Types_For_Various_Method_Signatures()
        {
            // Arrange
            var code = @"
using System;
using System.Threading.Tasks;
using System.Collections.Generic;

public class TestClass
{
    public void VoidMethod() { }
    
    public string StringMethod() { return ""test""; }
    
    public int IntMethod() { return 42; }
    
    public Task AsyncVoidMethod() { return Task.CompletedTask; }
    
    public Task<string> AsyncStringMethod() { return Task.FromResult(""test""); }
    
    public List<int> GenericMethod() { return new List<int>(); }
    
    public TestClass ChainableMethod() { return this; }
    
    public static bool StaticMethod() { return true; }
    
    private async Task<bool> PrivateAsyncMethod() { return await Task.FromResult(true); }
}";

            // Act
            var result = await _service.ExtractTypes(code, "test.cs");

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Methods.Should().NotBeEmpty();

            // Find specific methods and verify their return types
            var voidMethod = result.Methods.FirstOrDefault(m => m.Name == "VoidMethod");
            voidMethod.Should().NotBeNull();
            voidMethod.ReturnType.Should().Be("void");

            var stringMethod = result.Methods.FirstOrDefault(m => m.Name == "StringMethod");
            stringMethod.Should().NotBeNull();
            stringMethod.ReturnType.Should().Be("string");

            var intMethod = result.Methods.FirstOrDefault(m => m.Name == "IntMethod");
            intMethod.Should().NotBeNull();
            intMethod.ReturnType.Should().Be("int");

            var asyncVoidMethod = result.Methods.FirstOrDefault(m => m.Name == "AsyncVoidMethod");
            asyncVoidMethod.Should().NotBeNull();
            asyncVoidMethod.ReturnType.Should().Be("Task");

            var asyncStringMethod = result.Methods.FirstOrDefault(m => m.Name == "AsyncStringMethod");
            asyncStringMethod.Should().NotBeNull();
            asyncStringMethod.ReturnType.Should().Be("Task<string>");

            var staticMethod = result.Methods.FirstOrDefault(m => m.Name == "StaticMethod");
            staticMethod.Should().NotBeNull();
            staticMethod.ReturnType.Should().Be("bool");

            var privateAsyncMethod = result.Methods.FirstOrDefault(m => m.Name == "PrivateAsyncMethod");
            privateAsyncMethod.Should().NotBeNull();
            privateAsyncMethod.ReturnType.Should().Be("Task<bool>");
        }

        [Test]
        public async Task Should_Extract_Method_Modifiers()
        {
            // Arrange
            var code = @"
public class TestClass
{
    public void PublicMethod() { }
    
    private void PrivateMethod() { }
    
    protected void ProtectedMethod() { }
    
    public static void StaticMethod() { }
    
    public async Task AsyncMethod() { }
    
    private static async Task<string> ComplexMethod() { return await Task.FromResult(""test""); }
}";

            // Act
            var result = await _service.ExtractTypes(code, "test.cs");

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            
            var staticMethod = result.Methods.FirstOrDefault(m => m.Name == "StaticMethod");
            staticMethod.Should().NotBeNull();
            staticMethod.Modifiers.Should().Contain("static");

            var complexMethod = result.Methods.FirstOrDefault(m => m.Name == "ComplexMethod");
            complexMethod.Should().NotBeNull();
            complexMethod.Modifiers.Should().Contain("private");
            complexMethod.Modifiers.Should().Contain("static");
            complexMethod.Modifiers.Should().Contain("async");
            complexMethod.ReturnType.Should().Be("Task<string>");
        }

        [Test]
        public async Task Should_Handle_Interface_Methods()
        {
            // Arrange
            var code = @"
public interface ITestService
{
    void VoidMethod();
    
    string GetString();
    
    Task<bool> ProcessAsync();
}";

            // Act
            var result = await _service.ExtractTypes(code, "test.cs");

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Types.Should().NotBeEmpty();
            result.Methods.Should().NotBeEmpty();

            var interfaceType = result.Types.FirstOrDefault(t => t.Name == "ITestService");
            interfaceType.Should().NotBeNull();
            interfaceType.Kind.Should().Be("interface");

            // Verify interface methods have correct return types
            var voidMethod = result.Methods.FirstOrDefault(m => m.Name == "VoidMethod");
            voidMethod.Should().NotBeNull();
            voidMethod.ReturnType.Should().Be("void");

            var getString = result.Methods.FirstOrDefault(m => m.Name == "GetString");
            getString.Should().NotBeNull();
            getString.ReturnType.Should().Be("string");

            var processAsync = result.Methods.FirstOrDefault(m => m.Name == "ProcessAsync");
            processAsync.Should().NotBeNull();
            processAsync.ReturnType.Should().Be("Task<bool>");
        }

        [Test]
        public async Task Debug_Simple_String_Return_Method()
        {
            // Arrange - The simplest possible case with a string return type
            var code = @"
public class TestClass
{
    public string GetName() { return ""test""; }
}";

            // Act
            var result = await _service.ExtractTypes(code, "test.cs");

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Methods.Should().HaveCount(1);

            var method = result.Methods.FirstOrDefault(m => m.Name == "GetName");
            method.Should().NotBeNull();
            
            // Debug: Print all extracted info
            Console.WriteLine($"Method Name: {method.Name}");
            Console.WriteLine($"Return Type: '{method.ReturnType}'");
            Console.WriteLine($"Signature: {method.Signature}");
            Console.WriteLine($"Modifiers: {string.Join(", ", method.Modifiers)}");
            
            method.ReturnType.Should().Be("string");
        }

        [Test]
        public async Task Debug_Task_Return_Method()
        {
            // Arrange - Test the specific failing case
            var code = @"
public class TestClass
{
    public Task AsyncVoidMethod() { return Task.CompletedTask; }
}";

            // Act
            var result = await _service.ExtractTypes(code, "test.cs");

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Methods.Should().HaveCount(1);

            var method = result.Methods.FirstOrDefault(m => m.Name == "AsyncVoidMethod");
            method.Should().NotBeNull();
            
            // Debug: Print all extracted info
            Console.WriteLine($"Method Name: {method.Name}");
            Console.WriteLine($"Return Type: '{method.ReturnType}'");
            Console.WriteLine($"Signature: {method.Signature}");
            Console.WriteLine($"Modifiers: {string.Join(", ", method.Modifiers)}");
            
            method.ReturnType.Should().Be("Task");
        }
    }
}
