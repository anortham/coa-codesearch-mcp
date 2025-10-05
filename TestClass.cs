namespace Test;

public class TestClass
{
    public void TestMethod()
    {
        Console.WriteLine("Hello");
    }

    public int TestProperty { get; set; }

    public string GetMessage()
    {
        return "Testing incremental embedding updates";
    }

    public bool IsValid()
    {
        return true;
    }

    public int Calculate(int x, int y)
    {
        return x + y;
    }

    public string FormatName(string firstName, string lastName)
    {
        return $"{firstName} {lastName}";
    }

    public double Average(int a, int b)
    {
        return (a + b) / 2.0;
    }
}
// test change
// test change again
