namespace COA.CodeSearch.McpServer.Scoring;

/// <summary>
/// Types of temporal decay functions for scoring
/// </summary>
public enum DecayType
{
    /// <summary>
    /// Exponential decay - aggressive aging
    /// </summary>
    Exponential,
    
    /// <summary>
    /// Linear decay - gradual aging
    /// </summary>
    Linear,
    
    /// <summary>
    /// Gaussian decay - bell curve aging
    /// </summary>
    Gaussian
}

/// <summary>
/// Temporal decay function for scoring memory relevance based on age
/// </summary>
public class TemporalDecayFunction
{
    /// <summary>
    /// Rate of decay (0.0 to 1.0, where 1.0 = no decay)
    /// </summary>
    public float DecayRate { get; set; } = 0.95f;
    
    /// <summary>
    /// Half-life in days (time for score to decay to 50%)
    /// </summary>
    public float HalfLife { get; set; } = 30;
    
    /// <summary>
    /// Type of decay function to apply
    /// </summary>
    public DecayType Type { get; set; } = DecayType.Exponential;
    
    /// <summary>
    /// Default decay function - moderate exponential decay
    /// </summary>
    public static TemporalDecayFunction Default => new TemporalDecayFunction();
    
    /// <summary>
    /// Aggressive decay - strongly favors recent memories
    /// </summary>
    public static TemporalDecayFunction Aggressive => new TemporalDecayFunction
    {
        DecayRate = 0.9f,
        HalfLife = 7,
        Type = DecayType.Exponential
    };
    
    /// <summary>
    /// Gentle decay - slowly ages memories over long periods
    /// </summary>
    public static TemporalDecayFunction Gentle => new TemporalDecayFunction
    {
        DecayRate = 0.98f,
        HalfLife = 90,
        Type = DecayType.Linear
    };
    
    /// <summary>
    /// Calculate the temporal boost factor for a given age
    /// </summary>
    /// <param name="ageInDays">Age of the memory in days</param>
    /// <returns>Boost factor (0.0 to 1.0+)</returns>
    public float Calculate(double ageInDays)
    {
        if (ageInDays < 0) return 1.0f; // Future dates get no penalty
        
        return Type switch
        {
            DecayType.Exponential => (float)Math.Pow(DecayRate, ageInDays / HalfLife),
            DecayType.Linear => Math.Max(0.1f, 1 - (float)(ageInDays / (HalfLife * 10))),
            DecayType.Gaussian => (float)Math.Exp(-Math.Pow(ageInDays / HalfLife, 2)),
            _ => 1.0f
        };
    }
    
    /// <summary>
    /// Get a descriptive name for this decay function
    /// </summary>
    public string GetDescription()
    {
        var typeName = Type.ToString().ToLowerInvariant();
        return $"{typeName} decay (rate: {DecayRate:F2}, half-life: {HalfLife:F0} days)";
    }
}