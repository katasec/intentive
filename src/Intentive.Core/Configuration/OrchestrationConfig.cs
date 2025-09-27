using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.CommandLine;
using Microsoft.Extensions.Configuration.EnvironmentVariables;

namespace Intentive.Core.Configuration;

/// <summary>
/// Orchestration strategy selection
/// </summary>
public enum OrchestrationMode
{
    /// <summary>
    /// Deterministic-first: RuleGate → ONNX Classification → Validator → Tools → LLM escalation
    /// </summary>
    Intentive,
    
    /// <summary>
    /// Direct Semantic Kernel orchestration with LLM-driven planning
    /// </summary>
    LLMFirst
}

/// <summary>
/// Main configuration for the orchestration system
/// </summary>
public class OrchestrationConfig
{
    public const string SectionName = "Orchestration";

    /// <summary>
    /// Selected orchestration mode
    /// </summary>
    public OrchestrationMode Mode { get; set; } = OrchestrationMode.Intentive;

    /// <summary>
    /// OpenAI configuration
    /// </summary>
    public OpenAIConfig OpenAI { get; set; } = new();

    /// <summary>
    /// Intent classification configuration
    /// </summary>
    public IntentConfig Intent { get; set; } = new();

    /// <summary>
    /// Telemetry and metrics configuration
    /// </summary>
    public TelemetryConfig Telemetry { get; set; } = new();

    /// <summary>
    /// Observability configuration (OpenTelemetry with SigNoz Cloud support)
    /// </summary>
    public ObservabilityOptions Observability { get; set; } = new();
}

/// <summary>
/// OpenAI API configuration
/// </summary>
public class OpenAIConfig
{
    /// <summary>
    /// OpenAI API key (can be set via env var OPENAI_API_KEY)
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Base URL for OpenAI-compatible API (can be set via env var OPENAI_BASE_URL)
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Model for cheap LM probe (gpt-4o-mini)
    /// </summary>
    public string CheapModel { get; set; } = "gpt-4o-mini";

    /// <summary>
    /// Model for escalation (gpt-4)
    /// </summary>
    public string EscalationModel { get; set; } = "gpt-4";

    /// <summary>
    /// Maximum tokens for responses
    /// </summary>
    public int MaxTokens { get; set; } = 1000;

    /// <summary>
    /// Temperature for generation
    /// </summary>
    public double Temperature { get; set; } = 0.1;
}

/// <summary>
/// Intent classification configuration
/// </summary>
public class IntentConfig
{
    /// <summary>
    /// Path to ONNX model file
    /// </summary>
    public string ModelPath { get; set; } = "./models/all-MiniLM-L6-v2.onnx";

    /// <summary>
    /// Confidence threshold for classification
    /// </summary>
    public double ConfidenceThreshold { get; set; } = 0.7;

    /// <summary>
    /// Ambiguity threshold for escalation
    /// </summary>
    public double AmbiguityThreshold { get; set; } = 0.5;

    /// <summary>
    /// Risk threshold for escalation
    /// </summary>
    public double RiskThreshold { get; set; } = 0.8;
}

/// <summary>
/// Telemetry and metrics configuration
/// </summary>
public class TelemetryConfig
{
    /// <summary>
    /// Enable detailed logging
    /// </summary>
    public bool EnableDetailedLogging { get; set; } = true;

    /// <summary>
    /// Enable token cost tracking
    /// </summary>
    public bool EnableCostTracking { get; set; } = true;

    /// <summary>
    /// Enable execution path tracing
    /// </summary>
    public bool EnablePathTracing { get; set; } = true;
}

/// <summary>
/// Configuration builder extensions
/// </summary>
public static class ConfigurationExtensions
{
    /// <summary>
    /// Builds orchestration configuration from multiple sources
    /// </summary>
    public static OrchestrationConfig BuildOrchestrationConfig(string[] args)
    {
        var builder = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .AddCommandLine(args, new Dictionary<string, string>
            {
                ["--mode"] = "Mode",
                ["--openai-key"] = "OpenAI:ApiKey",
                ["--cheap-model"] = "OpenAI:CheapModel",
                ["--escalation-model"] = "OpenAI:EscalationModel",
                ["--enable-logging"] = "Telemetry:EnableDetailedLogging"
            });

        var configuration = builder.Build();
        var config = new OrchestrationConfig();
        
        // Bind configuration directly (CLI args are mapped to root level)
        configuration.Bind(config);
        
        // Explicitly handle Mode from CLI args (configuration binding might not work with enums)
        var modeValue = configuration["Mode"];
        if (!string.IsNullOrEmpty(modeValue))
        {
            if (Enum.TryParse<OrchestrationMode>(modeValue, true, out var parsedMode))
            {
                config.Mode = parsedMode;
            }
        }

        // Override with environment variables if present
        var envMode = Environment.GetEnvironmentVariable("ORCHESTRATION_MODE");
        if (!string.IsNullOrEmpty(envMode))
        {
            if (Enum.TryParse<OrchestrationMode>(envMode, true, out var mode))
            {
                config.Mode = mode;
            }
        }

        // Override OpenAI key from environment if present and not already set
        if (string.IsNullOrEmpty(config.OpenAI.ApiKey))
        {
            config.OpenAI.ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty;
        }

        // Override OpenAI base URL from environment if present and not already set
        if (string.IsNullOrEmpty(config.OpenAI.BaseUrl))
        {
            config.OpenAI.BaseUrl = Environment.GetEnvironmentVariable("OPENAI_BASE_URL") ?? string.Empty;
        }

        return config;
    }
}