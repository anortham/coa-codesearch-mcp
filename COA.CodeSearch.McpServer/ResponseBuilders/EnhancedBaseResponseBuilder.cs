using COA.Mcp.Framework.TokenOptimization.ResponseBuilders;
using COA.Mcp.Framework.TokenOptimization.Models;
using COA.Mcp.Framework.TokenOptimization.Reduction;
using COA.Mcp.Framework.TokenOptimization;
using COA.Mcp.Framework;
using COA.CodeSearch.McpServer.Models;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.ResponseBuilders;

/// <summary>
/// Token coordination strategies for different scenarios.
/// </summary>
public enum CoordinationStrategy
    {
        /// <summary>
        /// Trust tool estimates completely and allocate budget accordingly.
        /// </summary>
        TrustTool,

        /// <summary>
        /// Blend tool estimates with response builder heuristics.
        /// </summary>
        Blended,

        /// <summary>
        /// Use conservative approach when tool accuracy is unknown.
        /// </summary>
        Conservative,

        /// <summary>
        /// Adapt based on historical tool accuracy.
        /// </summary>
        Adaptive
    }

/// <summary>
/// Enhanced base response builder with sophisticated tool-response builder token coordination.
/// Provides dynamic budget allocation based on tool estimates and historical accuracy.
/// </summary>
/// <typeparam name="TInput">The type of input data being processed.</typeparam>
/// <typeparam name="TResult">The type of result being returned.</typeparam>
public abstract class EnhancedBaseResponseBuilder<TInput, TResult> : BaseResponseBuilder<TInput, TResult>
    where TResult : new()
{

    protected EnhancedBaseResponseBuilder(
        ILogger? logger = null,
        ProgressiveReductionEngine? reductionEngine = null)
        : base(logger, reductionEngine)
    {
    }

    /// <summary>
    /// Calculates coordinated token budget using tool estimates and historical accuracy.
    /// This method bridges tool estimation with response building for optimal token usage.
    /// </summary>
    /// <param name="context">Standard response context.</param>
    /// <param name="enhancedContext">Enhanced context with tool coordination data.</param>
    /// <returns>Coordinated token budget optimized for both tool and response needs.</returns>
    protected virtual CoordinatedTokenBudget CalculateCoordinatedTokenBudget(
        ResponseContext context,
        EnhancedResponseContext? enhancedContext = null)
    {
        var baseBudget = base.CalculateTokenBudget(context);

        if (enhancedContext?.ToolTokenEstimate == null)
        {
            // No tool coordination data available - use standard budget
            return new CoordinatedTokenBudget
            {
                TotalBudget = baseBudget,
                DataBudget = (int)(baseBudget * 0.70),
                InsightsBudget = (int)(baseBudget * 0.15),
                ActionsBudget = (int)(baseBudget * 0.15),
                Strategy = CoordinationStrategy.Conservative,
                ConfidenceLevel = 0.5,
                AdjustmentReason = "No tool coordination data available"
            };
        }

        var toolEstimate = enhancedContext.ToolTokenEstimate.Value;
        var toolAccuracy = enhancedContext.ToolEstimationAccuracy ?? 0.8; // Default to 80% if unknown
        var strategy = DetermineCoordinationStrategy(enhancedContext, toolAccuracy);

        _logger?.LogDebug(
            "Coordinating token budget - Base: {BaseBudget}, Tool Estimate: {ToolEstimate}, " +
            "Tool Accuracy: {ToolAccuracy:P1}, Strategy: {Strategy}",
            baseBudget, toolEstimate, toolAccuracy, strategy);

        var coordinatedBudget = strategy switch
        {
            CoordinationStrategy.TrustTool => CalculateTrustToolBudget(baseBudget, toolEstimate, enhancedContext),
            CoordinationStrategy.Blended => CalculateBlendedBudget(baseBudget, toolEstimate, toolAccuracy, enhancedContext),
            CoordinationStrategy.Conservative => CalculateConservativeBudget(baseBudget, toolEstimate, enhancedContext),
            CoordinationStrategy.Adaptive => CalculateAdaptiveBudget(baseBudget, toolEstimate, toolAccuracy, enhancedContext),
            _ => throw new ArgumentOutOfRangeException(nameof(strategy))
        };

        coordinatedBudget.Strategy = strategy;
        coordinatedBudget.ConfidenceLevel = CalculateConfidenceLevel(enhancedContext, toolAccuracy);

        _logger?.LogDebug(
            "Token budget coordination complete - Total: {TotalBudget}, Data: {DataBudget}, " +
            "Insights: {InsightsBudget}, Actions: {ActionsBudget}, Confidence: {ConfidenceLevel:P1}",
            coordinatedBudget.TotalBudget, coordinatedBudget.DataBudget,
            coordinatedBudget.InsightsBudget, coordinatedBudget.ActionsBudget, coordinatedBudget.ConfidenceLevel);

        return coordinatedBudget;
    }

    /// <summary>
    /// Determines the best coordination strategy based on tool characteristics and accuracy.
    /// </summary>
    private CoordinationStrategy DetermineCoordinationStrategy(
        EnhancedResponseContext enhancedContext,
        double toolAccuracy)
    {
        // High accuracy tools with high confidence estimates
        if (toolAccuracy > 0.9 && enhancedContext.ToolHighConfidenceEstimate == true)
        {
            return CoordinationStrategy.TrustTool;
        }

        // Good accuracy tools
        if (toolAccuracy > 0.8)
        {
            return CoordinationStrategy.Blended;
        }

        // Unknown or poor accuracy
        if (toolAccuracy < 0.7 || !enhancedContext.ToolEstimationAccuracy.HasValue)
        {
            return CoordinationStrategy.Conservative;
        }

        // Default to adaptive for moderate accuracy
        return CoordinationStrategy.Adaptive;
    }

    /// <summary>
    /// Calculates budget when trusting tool estimates completely.
    /// </summary>
    private CoordinatedTokenBudget CalculateTrustToolBudget(
        int baseBudget,
        int toolEstimate,
        EnhancedResponseContext enhancedContext)
    {
        // Use tool estimate as primary guide, but ensure minimum budget for response structure
        var totalBudget = Math.Max(toolEstimate, baseBudget / 2);
        var responseOverhead = EstimateResponseOverhead(enhancedContext);
        var dataBudget = Math.Max(totalBudget - responseOverhead, totalBudget * 0.6);

        return new CoordinatedTokenBudget
        {
            TotalBudget = totalBudget,
            DataBudget = (int)dataBudget,
            InsightsBudget = (int)((totalBudget - dataBudget) * 0.6),
            ActionsBudget = (int)((totalBudget - dataBudget) * 0.4),
            AdjustmentReason = "Trusted tool estimate with high confidence"
        };
    }

    /// <summary>
    /// Calculates budget by blending tool estimates with response builder heuristics.
    /// </summary>
    private CoordinatedTokenBudget CalculateBlendedBudget(
        int baseBudget,
        int toolEstimate,
        double toolAccuracy,
        EnhancedResponseContext enhancedContext)
    {
        // Weight tool estimate by accuracy, response builder estimate by (1 - accuracy)
        var blendedEstimate = (int)(toolEstimate * toolAccuracy + baseBudget * (1 - toolAccuracy));

        // Adjust based on expected complexity
        var complexityMultiplier = enhancedContext.ExpectedComplexity switch
        {
            ResultComplexity.Simple => 0.9,
            ResultComplexity.Moderate => 1.0,
            ResultComplexity.High => 1.2,
            ResultComplexity.VeryHigh => 1.5,
            _ => 1.0
        };

        var totalBudget = (int)(blendedEstimate * complexityMultiplier);
        var responseOverhead = EstimateResponseOverhead(enhancedContext);
        var dataBudget = Math.Max(totalBudget - responseOverhead, (int)(totalBudget * 0.65));

        return new CoordinatedTokenBudget
        {
            TotalBudget = totalBudget,
            DataBudget = dataBudget,
            InsightsBudget = (int)((totalBudget - dataBudget) * 0.6),
            ActionsBudget = (int)((totalBudget - dataBudget) * 0.4),
            AdjustmentReason = $"Blended estimate (tool: {toolAccuracy:P1} weight, complexity: {enhancedContext.ExpectedComplexity})"
        };
    }

    /// <summary>
    /// Calculates conservative budget when tool estimates are uncertain.
    /// </summary>
    private CoordinatedTokenBudget CalculateConservativeBudget(
        int baseBudget,
        int toolEstimate,
        EnhancedResponseContext enhancedContext)
    {
        // Use the smaller of base budget or tool estimate, with safety margin
        var conservativeEstimate = (int)(Math.Min(baseBudget, toolEstimate) * 0.8);
        var totalBudget = Math.Max(conservativeEstimate, 1000); // Minimum viable budget

        return new CoordinatedTokenBudget
        {
            TotalBudget = totalBudget,
            DataBudget = (int)(totalBudget * 0.65), // Slightly less data, more overhead
            InsightsBudget = (int)(totalBudget * 0.20),
            ActionsBudget = (int)(totalBudget * 0.15),
            AdjustmentReason = "Conservative approach due to uncertain tool accuracy"
        };
    }

    /// <summary>
    /// Calculates adaptive budget based on historical tool performance.
    /// </summary>
    private CoordinatedTokenBudget CalculateAdaptiveBudget(
        int baseBudget,
        int toolEstimate,
        double toolAccuracy,
        EnhancedResponseContext enhancedContext)
    {
        // Adapt the blend ratio based on accuracy trends and tool category
        var adaptiveWeight = CalculateAdaptiveWeight(toolAccuracy, enhancedContext.ToolCategory);
        var adaptedEstimate = (int)(toolEstimate * adaptiveWeight + baseBudget * (1 - adaptiveWeight));

        // Apply category-specific adjustments
        var categoryMultiplier = enhancedContext.ToolCategory switch
        {
            ToolCategory.Query => 1.1,      // Query tools often return more data than expected
            ToolCategory.Analysis => 1.0,   // Analysis tools are usually well-estimated
            ToolCategory.Resources => 0.9,  // Resource tools are typically smaller
            ToolCategory.Utility => 0.8,    // Utility tools have minimal output
            _ => 1.0
        };

        var totalBudget = (int)(adaptedEstimate * categoryMultiplier);

        return new CoordinatedTokenBudget
        {
            TotalBudget = totalBudget,
            DataBudget = (int)(totalBudget * 0.7),
            InsightsBudget = (int)(totalBudget * 0.15),
            ActionsBudget = (int)(totalBudget * 0.15),
            AdjustmentReason = $"Adaptive strategy (weight: {adaptiveWeight:P1}, category: {enhancedContext.ToolCategory})"
        };
    }

    /// <summary>
    /// Calculates adaptive weight for blending tool and response builder estimates.
    /// </summary>
    private double CalculateAdaptiveWeight(double toolAccuracy, ToolCategory? toolCategory)
    {
        var baseWeight = toolAccuracy;

        // Adjust weight based on tool category reliability
        var categoryAdjustment = toolCategory switch
        {
            ToolCategory.Analysis => 0.1,    // Analysis tools tend to be more accurate
            ToolCategory.Query => -0.05,     // Query tools can be less predictable
            ToolCategory.Resources => 0.05,  // Resource tools are usually reliable
            ToolCategory.Utility => 0.05,    // Utility tools are simple and predictable
            _ => 0.0
        };

        return Math.Max(0.1, Math.Min(0.9, baseWeight + categoryAdjustment));
    }

    /// <summary>
    /// Estimates the token overhead for response structure (insights, actions, metadata).
    /// </summary>
    private int EstimateResponseOverhead(EnhancedResponseContext? enhancedContext)
    {
        var baseOverhead = 300; // Basic response structure

        // Adjust based on expected insights and actions
        if (enhancedContext?.ExpectedComplexity >= ResultComplexity.High)
        {
            baseOverhead += 200; // More complex responses need more insights/actions
        }

        return baseOverhead;
    }

    /// <summary>
    /// Calculates confidence level in the coordinated budget.
    /// </summary>
    private double CalculateConfidenceLevel(EnhancedResponseContext enhancedContext, double toolAccuracy)
    {
        var baseConfidence = toolAccuracy;

        // Adjust based on tool confidence and complexity
        if (enhancedContext.ToolHighConfidenceEstimate == true)
        {
            baseConfidence += 0.1;
        }

        if (enhancedContext.ExpectedComplexity >= ResultComplexity.High)
        {
            baseConfidence -= 0.1; // Lower confidence for complex scenarios
        }

        return Math.Max(0.1, Math.Min(1.0, baseConfidence));
    }

    /// <summary>
    /// Records token coordination telemetry for future optimization.
    /// </summary>
    protected void RecordCoordinationTelemetry(
        CoordinatedTokenBudget budget,
        int actualTokensUsed,
        EnhancedResponseContext enhancedContext)
    {
        var budgetAccuracy = actualTokensUsed > 0
            ? (double)Math.Min(budget.TotalBudget, actualTokensUsed) / Math.Max(budget.TotalBudget, actualTokensUsed)
            : 1.0;

        _logger?.LogInformation(
            "Token Coordination Telemetry - Strategy: {Strategy}, Budget: {Budget}, " +
            "Actual: {Actual}, Accuracy: {Accuracy:P1}, Tool Estimate: {ToolEstimate}, " +
            "Confidence: {Confidence:P1}, Reason: {Reason}",
            budget.Strategy, budget.TotalBudget, actualTokensUsed, budgetAccuracy,
            enhancedContext.ToolTokenEstimate, budget.ConfidenceLevel, budget.AdjustmentReason);
    }
}

/// <summary>
/// Represents a coordinated token budget with strategy and confidence information.
/// </summary>
public class CoordinatedTokenBudget
{
    /// <summary>
    /// Total token budget allocated.
    /// </summary>
    public int TotalBudget { get; set; }

    /// <summary>
    /// Budget allocated for primary data content.
    /// </summary>
    public int DataBudget { get; set; }

    /// <summary>
    /// Budget allocated for insights generation.
    /// </summary>
    public int InsightsBudget { get; set; }

    /// <summary>
    /// Budget allocated for action suggestions.
    /// </summary>
    public int ActionsBudget { get; set; }

    /// <summary>
    /// Strategy used for budget coordination.
    /// </summary>
    public CoordinationStrategy Strategy { get; set; }

    /// <summary>
    /// Confidence level in this budget allocation (0.0 to 1.0).
    /// </summary>
    public double ConfidenceLevel { get; set; }

    /// <summary>
    /// Explanation of why this budget was chosen.
    /// </summary>
    public string AdjustmentReason { get; set; } = string.Empty;
}