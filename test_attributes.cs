using Xunit;

namespace TestNamespace
{
    public class TestClass
    {
        [Fact]
        public void TestMethod1()
        {
            // This is a test with [Fact] attribute
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void TestMethod2(int value)
        {
            // This is a test with [Theory] attribute
        }

        [HttpGet("/api/users")]
        public void GetUsers()
        {
            // This is a method with [HttpGet] attribute
        }
    }
}