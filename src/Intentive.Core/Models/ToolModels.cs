using System.Reflection;
using System.Text.Json.Serialization;
using Intentive.Core.Services;

namespace Intentive.Core.Models;

/// <summary>
/// Configuration loaded from tools.json
/// </summary>
public class ToolsConfiguration
{
    public List<McpServerConfig> McpServers { get; set; } = new();
    public List<LocalToolConfig> LocalTools { get; set; } = new();
    public IntentGenerationConfig IntentGeneration { get; set; } = new();
}

/// <summary>
/// MCP server configuration
/// </summary>
public class McpServerConfig
{
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public TransportConfig Transport { get; set; } = new();
    public List<string> Capabilities { get; set; } = new();
    public string Description { get; set; } = "";
    public HealthCheckConfig HealthCheck { get; set; } = new();
}

/// <summary>
/// Transport configuration for MCP servers
/// </summary>
public class TransportConfig
{
    public string Type { get; set; } = "stdio";
    public string Command { get; set; } = "";
    public List<string> Args { get; set; } = new();
}

/// <summary>
/// Health check configuration
/// </summary>
public class HealthCheckConfig
{
    public int Timeout { get; set; } = 5000;
    public int Retries { get; set; } = 2;
}

/// <summary>
/// Local tool configuration
/// </summary>
public class LocalToolConfig
{
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string Class { get; set; } = "";
    public string Method { get; set; } = "";
    public List<string> Capabilities { get; set; } = new();
    public string Description { get; set; } = "";
    public Dictionary<string, ParameterConfig> Parameters { get; set; } = new();
}

/// <summary>
/// Parameter configuration for local tools
/// </summary>
public class ParameterConfig
{
    public string Type { get; set; } = "";
    public bool Required { get; set; } = false;
    public string Description { get; set; } = "";
    public object? DefaultValue { get; set; }
}

/// <summary>
/// Intent generation configuration
/// </summary>
public class IntentGenerationConfig
{
    public bool Enabled { get; set; } = true;
    public int ExamplesPerCapability { get; set; } = 5;
    public Dictionary<string, List<string>> CustomExamples { get; set; } = new();
}

/// <summary>
/// Tool descriptor - unified representation of local tools and MCP servers
/// </summary>
public class ToolDescriptor
{
    public string Name { get; set; } = "";
    public ToolType Type { get; set; }
    public string Description { get; set; } = "";
    public List<string> Capabilities { get; set; } = new();
    
    // For local tools
    public Type? LocalType { get; set; }
    public MethodInfo? LocalMethod { get; set; }
    public Dictionary<string, ParameterConfig>? Parameters { get; set; }
    
    // For MCP tools
    public McpClient? McpClient { get; set; }
}

/// <summary>
/// Tool type enumeration
/// </summary>
public enum ToolType
{
    Local,
    MCP
}

/// <summary>
/// Tool classification result from ONNX
/// </summary>
public class ToolClassificationResult
{
    public string? ToolName { get; set; }
    public double Confidence { get; set; }
    public string? BestCapability { get; set; }
    public Dictionary<string, double> AllScores { get; set; } = new();
}

/// <summary>
/// Intent training example for ONNX model
/// </summary>
public class IntentTrainingExample
{
    public string Text { get; set; } = "";
    public string ToolName { get; set; } = "";
    public string Capability { get; set; } = "";
}

/// <summary>
/// Simplified orchestration result
/// </summary>
public class SimpleOrchestrationResult
{
    public string Response { get; set; } = "";
    public string ExecutionPath { get; set; } = "";
    public double ExecutionTimeMs { get; set; }
    public bool UsedLLM { get; set; }
    public string? ToolUsed { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}