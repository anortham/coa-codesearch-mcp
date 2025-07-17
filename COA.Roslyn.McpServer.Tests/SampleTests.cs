namespace COA.Roslyn.McpServer.Tests;

public class SampleTests
{
    [Fact]
    public void Sample_Test_Should_Pass()
    {
        // Arrange
        var expected = 4;
        
        // Act
        var result = 2 + 2;
        
        // Assert
        result.Should().Be(expected);
    }
    
    [Theory]
    [InlineData(1, 2, 3)]
    [InlineData(10, 20, 30)]
    [InlineData(-5, 5, 0)]
    public void Addition_Should_Return_Correct_Sum(int a, int b, int expected)
    {
        // Act
        var result = a + b;
        
        // Assert
        result.Should().Be(expected);
    }
}