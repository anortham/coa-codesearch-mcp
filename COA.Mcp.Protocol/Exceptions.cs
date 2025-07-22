namespace COA.Mcp.Protocol;

/// <summary>
/// Exception thrown when tool parameters are invalid.
/// </summary>
public class InvalidParametersException : Exception
{
    /// <summary>
    /// Gets the parameter name that was invalid.
    /// </summary>
    public string? ParameterName { get; }

    /// <summary>
    /// Gets the expected parameter type or format.
    /// </summary>
    public string? ExpectedFormat { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidParametersException"/> class.
    /// </summary>
    public InvalidParametersException() : base("Invalid parameters provided")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidParametersException"/> class with a message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public InvalidParametersException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidParametersException"/> class with parameter details.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="parameterName">The name of the invalid parameter.</param>
    public InvalidParametersException(string message, string parameterName) : base(message)
    {
        ParameterName = parameterName;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidParametersException"/> class with full details.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="parameterName">The name of the invalid parameter.</param>
    /// <param name="expectedFormat">The expected format or type.</param>
    public InvalidParametersException(string message, string parameterName, string expectedFormat) : base(message)
    {
        ParameterName = parameterName;
        ExpectedFormat = expectedFormat;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidParametersException"/> class with an inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public InvalidParametersException(string message, Exception innerException) : base(message, innerException)
    {
    }
}