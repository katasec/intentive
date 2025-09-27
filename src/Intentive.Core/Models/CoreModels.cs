using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Intentive.Core.Models;

/// <summary>
/// Represents a structured plan with steps to execute
/// </summary>
public record Plan(
    [Description("The user's intent or goal")]
    string Intent,
    [Description("List of steps to execute the plan")]
    List<PlanStep> Steps,
    [Description("Confidence score for the plan (0-1)")]
    double Confidence,
    [Description("Execution path taken to generate the plan")]
    string? ExecutionPath = null
)
{
    public Plan() : this(string.Empty, new List<PlanStep>(), 0.0) { }
}

/// <summary>
/// A single step in a plan with tool name and parameters
/// </summary>
public record PlanStep(
    [Description("Name of the tool to execute")]
    string Tool,
    [Description("Parameters to pass to the tool")]
    Dictionary<string, object> Parameters
)
{
    public PlanStep() : this(string.Empty, new Dictionary<string, object>()) { }
}

/// <summary>
/// Result of executing a plan step or tool
/// </summary>
public record ExecutionResult(
    object? Data,
    bool Success,
    string? Error = null,
    double ExecutionTimeMs = 0,
    Dictionary<string, object>? Metadata = null
)
{
    public ExecutionResult() : this(null, false) { }
}

/// <summary>
/// Result of intent classification and ambiguity detection
/// </summary>
public record ClassificationResult(
    string TaskType,
    double AmbiguityScore,
    double RiskScore,
    string? Domain = null,
    Dictionary<string, double>? IntentScores = null
)
{
    public ClassificationResult() : this(string.Empty, 0.0, 0.0) { }
}

/// <summary>
/// Result of rule-based filtering and fast-path routing
/// </summary>
public record RuleGateResult(
    bool ShouldContinue,
    string? FastPathResponse = null,
    string? RejectReason = null,
    Dictionary<string, object>? ProcessedInput = null
)
{
    public RuleGateResult() : this(true) { }
}

/// <summary>
/// Result of plan validation
/// </summary>
public record ValidationResult(
    bool IsValid,
    List<string> Errors,
    double ConfidenceScore = 0.0,
    string? SuggestedFix = null
)
{
    public ValidationResult() : this(false, new List<string>()) { }
}

/// <summary>
/// Quality indicators for response evaluation and escalation triggers
/// </summary>
public record QualityIndicators(
    bool RequiresEscalation,
    List<string> Reasons
)
{
    public QualityIndicators() : this(false, new List<string>()) { }
}

/// <summary>
/// Complete orchestration result with cost tracking
/// </summary>
public record OrchestrationResult(
    string Response,
    string ExecutionPath,
    double TokenCost,
    int TokensUsed,
    TimeSpan TotalExecutionTime,
    bool RequiredEscalation = false,
    Dictionary<string, object>? DebugInfo = null
)
{
    public OrchestrationResult() : this(string.Empty, string.Empty, 0.0, 0, TimeSpan.Zero) { }
}