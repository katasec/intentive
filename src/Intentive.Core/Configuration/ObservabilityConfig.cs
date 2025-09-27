using System.Diagnostics;
using System.Diagnostics.Metrics;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Intentive.Core.Configuration;

/// <summary>
/// Lightweight vendor-agnostic observability configuration
/// Supports any OTLP-compatible backend (SigNoz, Jaeger, Grafana, New Relic, etc.)
/// </summary>
public class ObservabilityOptions
{
    public const string SectionName = "Observability";

    /// <summary>
    /// Enable OpenTelemetry instrumentation (default: OFF)
    /// </summary>
    public bool EnableOtel { get; set; } = false;

    /// <summary>
    /// OTLP endpoint for traces and metrics
    /// Examples:
    /// - Local: http://localhost:4317
    /// - SigNoz Cloud: https://ingest.signoz.cloud:4317
    /// - Jaeger: https://jaeger.company.com:4317
    /// - Grafana Cloud: https://traces-prod-us-central1.grafana.net:4317
    /// </summary>
    public string OtlpEndpoint { get; set; } = "http://localhost:4317";

    /// <summary>
    /// OTLP headers for authentication (vendor-agnostic)
    /// Examples:
    /// - SigNoz: { "signoz-access-token": "your-key" }
    /// - Bearer auth: { "Authorization": "Bearer your-token" }
    /// - API Key: { "X-API-Key": "your-key" }
    /// </summary>
    public Dictionary<string, string> OtlpHeaders { get; set; } = new();

    /// <summary>
    /// Service name for telemetry
    /// </summary>
    public string ServiceName { get; set; } = "intentive";

    /// <summary>
    /// Service version
    /// </summary>
    public string ServiceVersion { get; set; } = "1.0.0";

    /// <summary>
    /// Global attributes to add to all spans and metrics
    /// </summary>
    public Dictionary<string, object> GlobalAttributes { get; set; } = new();
}

/// <summary>
/// Intentive telemetry sources and metrics
/// </summary>
public static class IntentiveTelemetry
{
    /// <summary>
    /// Activity source for orchestration spans
    /// </summary>
    public static readonly ActivitySource ActivitySource = new("Intentive", "1.0.0");

    /// <summary>
    /// Meter for business metrics
    /// </summary>
    public static readonly Meter Meter = new("Intentive", "1.0.0");

    // === REQUIRED COUNTERS ===
    
    /// <summary>
    /// Total LLM calls (both Intentive escalations and LLM-first)
    /// </summary>
    public static readonly Counter<long> LlmCallsTotal = Meter.CreateCounter<long>(
        "llm.calls.total",
        description: "Total number of LLM calls across all orchestration modes");

    /// <summary>
    /// Total escalations from Intentive to larger LLM
    /// </summary>
    public static readonly Counter<long> EscalationsTotal = Meter.CreateCounter<long>(
        "escalations.total",
        description: "Total number of escalations to larger LLM models");

    /// <summary>
    /// Intent-level cache hits
    /// </summary>
    public static readonly Counter<long> CacheHitsIntent = Meter.CreateCounter<long>(
        "cache.hits.intent",
        description: "Intent cache hit rate");

    /// <summary>
    /// Tool results cache hits
    /// </summary>
    public static readonly Counter<long> CacheHitsTool = Meter.CreateCounter<long>(
        "cache.hits.tool",
        description: "Tool results cache hit rate");

    /// <summary>
    /// Final answer cache hits
    /// </summary>
    public static readonly Counter<long> CacheHitsAnswer = Meter.CreateCounter<long>(
        "cache.hits.answer",
        description: "Final answer cache hit rate");

    // === REQUIRED HISTOGRAMS ===

    /// <summary>
    /// Total orchestration latency per request
    /// </summary>
    public static readonly Histogram<double> OrchestratorLatencyMs = Meter.CreateHistogram<double>(
        "orchestrator.latency.ms",
        unit: "ms",
        description: "Total time per orchestration request");

    /// <summary>
    /// MiniLM ONNX inference time
    /// </summary>
    public static readonly Histogram<double> EmbedLatencyMs = Meter.CreateHistogram<double>(
        "embed.latency.ms",
        unit: "ms",
        description: "MiniLM ONNX inference time");

    /// <summary>
    /// LLM response time per call
    /// </summary>
    public static readonly Histogram<double> LlmLatencyMs = Meter.CreateHistogram<double>(
        "llm.latency.ms",
        unit: "ms",
        description: "Response time per LLM call");

    /// <summary>
    /// Tool execution time per call
    /// </summary>
    public static readonly Histogram<double> ToolExecLatencyMs = Meter.CreateHistogram<double>(
        "tool.exec.latency.ms",
        unit: "ms",
        description: "Execution time per tool call");

    // === UTILITY METHODS ===

    /// <summary>
    /// Create common attributes for spans and metrics
    /// </summary>
    public static Dictionary<string, object?> CreateAttributes(
        string mode,
        string requestId,
        string? sessionId = null,
        string? modelName = null,
        bool? onnxInt8 = null,
        bool? escalated = null,
        bool? cacheHit = null,
        string? domain = null)
    {
        var attributes = new Dictionary<string, object?>
        {
            ["mode"] = mode,
            ["request.id"] = requestId
        };

        if (sessionId != null) attributes["session.id"] = sessionId;
        if (modelName != null) attributes["model.name"] = modelName;
        if (onnxInt8.HasValue) attributes["onnx.int8"] = onnxInt8.Value;
        if (escalated.HasValue) attributes["escalated"] = escalated.Value;
        if (cacheHit.HasValue) attributes["cache.hit"] = cacheHit.Value;
        if (domain != null) attributes["domain"] = domain;

        return attributes;
    }

    /// <summary>
    /// Start orchestration span with standard attributes
    /// </summary>
    public static Activity? StartOrchestrationSpan(
        string operationName,
        string mode,
        string requestId,
        string? sessionId = null,
        Dictionary<string, object?>? additionalAttributes = null)
    {
        var activity = ActivitySource.StartActivity($"orchestrator.{operationName}");
        
        if (activity != null)
        {
            var attributes = CreateAttributes(mode, requestId, sessionId);
            
            if (additionalAttributes != null)
            {
                foreach (var (key, value) in additionalAttributes)
                {
                    attributes[key] = value;
                }
            }

            foreach (var (key, value) in attributes)
            {
                activity.SetTag(key, value?.ToString());
            }
        }

        return activity;
    }

    /// <summary>
    /// Dispose all telemetry resources
    /// </summary>
    public static void Dispose()
    {
        ActivitySource?.Dispose();
        Meter?.Dispose();
    }
}

/// <summary>
/// OpenTelemetry configuration extensions for Intentive
/// </summary>
public static class ObservabilityExtensions
{
    /// <summary>
    /// Add Intentive tracing with SigNoz or local OTLP support
    /// </summary>
    public static TracerProviderBuilder AddIntentiveTracing(
        this TracerProviderBuilder builder,
        ObservabilityOptions options)
    {
        if (!options.EnableOtel)
            return builder;

        return builder
            .SetSampler(new AlwaysOnSampler())
            .AddSource(IntentiveTelemetry.ActivitySource.Name)
            .AddSource("Microsoft.SemanticKernel*") // SK's built-in instrumentation
            .SetResourceBuilder(
                OpenTelemetry.Resources.ResourceBuilder.CreateDefault()
                    .AddService(options.ServiceName, serviceVersion: options.ServiceVersion)
                    .AddAttributes(options.GlobalAttributes))
            .AddOtlpExporter(otlpOptions =>
            {
                otlpOptions.Endpoint = new Uri(options.OtlpEndpoint);
                otlpOptions.Protocol = OtlpExportProtocol.Grpc;
                
                // Add vendor-agnostic headers (works with any OTLP backend)
                if (options.OtlpHeaders.Any())
                {
                    otlpOptions.Headers = string.Join(",", 
                        options.OtlpHeaders.Select(h => $"{h.Key}={h.Value}"));
                }
            });
    }

    /// <summary>
    /// Add Intentive metrics with SigNoz or local OTLP support
    /// </summary>
    public static MeterProviderBuilder AddIntentiveMetrics(
        this MeterProviderBuilder builder,
        ObservabilityOptions options)
    {
        if (!options.EnableOtel)
            return builder;

        return builder
            .AddMeter(IntentiveTelemetry.Meter.Name)
            .AddMeter("Microsoft.SemanticKernel*") // SK's built-in metrics
            .SetResourceBuilder(
                OpenTelemetry.Resources.ResourceBuilder.CreateDefault()
                    .AddService(options.ServiceName, serviceVersion: options.ServiceVersion)
                    .AddAttributes(options.GlobalAttributes))
            .AddOtlpExporter(otlpOptions =>
            {
                otlpOptions.Endpoint = new Uri(options.OtlpEndpoint);
                otlpOptions.Protocol = OtlpExportProtocol.Grpc;
                
                // Add vendor-agnostic headers (works with any OTLP backend)
                if (options.OtlpHeaders.Any())
                {
                    otlpOptions.Headers = string.Join(",", 
                        options.OtlpHeaders.Select(h => $"{h.Key}={h.Value}"));
                }
            });
    }

    /// <summary>
    /// Update observability options from orchestration config
    /// </summary>
    public static ObservabilityOptions UpdateFromOrchestrationConfig(
        this ObservabilityOptions observabilityOptions,
        OrchestrationConfig orchestrationConfig)
    {
        // Add orchestration context as global attributes
        observabilityOptions.GlobalAttributes["orchestration.mode"] = 
            orchestrationConfig.Mode.ToString().ToLowerInvariant();
        observabilityOptions.GlobalAttributes["ai.model.cheap"] = 
            orchestrationConfig.OpenAI.CheapModel;
        observabilityOptions.GlobalAttributes["ai.model.escalation"] = 
            orchestrationConfig.OpenAI.EscalationModel;

        return observabilityOptions;
    }
}