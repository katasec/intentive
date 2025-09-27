using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.ML;
using Microsoft.ML.Data;
using Intentive.Core.Models;

namespace Intentive.Core.Services;

/// <summary>
/// Auto-discovery and training system for tool-driven intent classification
/// </summary>
public class IntentGenerator
{
    private readonly ILogger _logger;
    private readonly ToolRegistry _toolRegistry;

    public IntentGenerator(ILogger logger, ToolRegistry toolRegistry)
    {
        _logger = logger;
        _toolRegistry = toolRegistry;
    }

    /// <summary>
    /// Full discovery and training pipeline - main entry point
    /// </summary>
    public async Task<string> DiscoverAndTrainAsync(int examplesPerTool = 200, string? outputModelPath = null)
    {
        _logger.LogInformation("🔧 Starting tool discovery and training pipeline...");
        
        outputModelPath ??= "models/trained-intentive.onnx";
        
        // 1. Discover all tools from configured MCP servers
        var discoveredTools = await DiscoverAllToolsAsync();
        _logger.LogInformation("✅ Discovered {ToolCount} tools from servers", discoveredTools.Count);

        // 2. Generate training examples from discovered tools
        var examples = await GenerateTrainingExamplesAsync(discoveredTools, examplesPerTool);
        _logger.LogInformation("✅ Generated {ExampleCount} training examples", examples.Count);

        // 3. Train ONNX model with generated data
        var modelPath = await TrainOnnxModelAsync(examples, outputModelPath);
        _logger.LogInformation("✅ Model trained and saved to {ModelPath}", modelPath);

        return modelPath;
    }

    /// <summary>
    /// Discover all available tools from MCP servers and local tools
    /// </summary>
    public async Task<Dictionary<string, List<DiscoveredTool>>> DiscoverAllToolsAsync()
    {
        var discoveredTools = new Dictionary<string, List<DiscoveredTool>>();

        // Get all tools from the registry
        var allTools = _toolRegistry.GetAllTools();

        foreach (var (toolName, toolDescriptor) in allTools)
        {
            try
            {
                if (toolDescriptor.Type == ToolType.MCP && toolDescriptor.McpClient != null)
                {
                    // Discover actual MCP tools
                    var mcpTools = await DiscoverMcpToolsAsync(toolName, toolDescriptor.McpClient);
                    discoveredTools[toolName] = mcpTools;
                }
                else if (toolDescriptor.Type == ToolType.Local)
                {
                    // Use local tool info
                    var localTool = CreateDiscoveredToolFromLocal(toolDescriptor);
                    discoveredTools[toolName] = new List<DiscoveredTool> { localTool };
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Failed to discover tools from {ToolName}", toolName);
            }
        }

        return discoveredTools;
    }

    /// <summary>
    /// Discover tools from an MCP server via list_tools protocol
    /// </summary>
    private async Task<List<DiscoveredTool>> DiscoverMcpToolsAsync(string serverName, McpClient mcpClient)
    {
        try
        {
            // Create MCP list_tools request
            var listToolsRequest = new
            {
                jsonrpc = "2.0",
                id = Guid.NewGuid().ToString(),
                method = "tools/list"
            };

            var requestJson = JsonSerializer.Serialize(listToolsRequest);
            _logger.LogDebug("📤 Sending MCP list_tools request to {ServerName}", serverName);

            // This is a bit of a hack - we need to extend McpClient to handle list_tools
            // For now, we'll use the capabilities from the config and enhance later
            return CreateToolsFromCapabilities(serverName, _toolRegistry.GetAllTools()[serverName].Capabilities);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to discover MCP tools from {ServerName}", serverName);
            return new List<DiscoveredTool>();
        }
    }

    /// <summary>
    /// Create discovered tools from configured capabilities (fallback approach)
    /// </summary>
    private List<DiscoveredTool> CreateToolsFromCapabilities(string serverName, List<string> capabilities)
    {
        var tools = new List<DiscoveredTool>();

        foreach (var capability in capabilities)
        {
            var tool = new DiscoveredTool
            {
                ServerName = serverName,
                ToolName = capability,
                Description = $"Execute {capability} operations",
                Parameters = InferParametersFromCapability(capability),
                Capabilities = new List<string> { capability }
            };

            tools.Add(tool);
        }

        return tools;
    }

    /// <summary>
    /// Create discovered tool from local tool descriptor
    /// </summary>
    private DiscoveredTool CreateDiscoveredToolFromLocal(ToolDescriptor toolDescriptor)
    {
        return new DiscoveredTool
        {
            ServerName = "local",
            ToolName = toolDescriptor.Name,
            Description = toolDescriptor.Description,
            Parameters = toolDescriptor.Parameters?.ToDictionary(
                kv => kv.Key, 
                kv => new ToolParameter { Type = kv.Value.Type, Required = kv.Value.Required, Description = kv.Value.Description }
            ) ?? new(),
            Capabilities = toolDescriptor.Capabilities
        };
    }

    /// <summary>
    /// Generate training examples from all discovered tools
    /// </summary>
    public async Task<List<IntentTrainingExample>> GenerateTrainingExamplesAsync(
        Dictionary<string, List<DiscoveredTool>> discoveredTools, 
        int examplesPerTool = 200)
    {
        var allExamples = new List<IntentTrainingExample>();

        foreach (var (serverName, tools) in discoveredTools)
        {
            foreach (var tool in tools)
            {
                var examples = GenerateExamplesForTool(tool, examplesPerTool / tools.Count);
                allExamples.AddRange(examples);
                
                _logger.LogDebug("📝 Generated {Count} examples for {ToolName}", examples.Count, tool.ToolName);
            }
        }

        return allExamples;
    }

    /// <summary>
    /// Generate diverse training examples for a specific tool
    /// </summary>
    private List<IntentTrainingExample> GenerateExamplesForTool(DiscoveredTool tool, int count)
    {
        var examples = new List<IntentTrainingExample>();
        var templates = GetTemplatesForTool(tool);

        for (int i = 0; i < count; i++)
        {
            var template = templates[i % templates.Count];
            var example = PopulateTemplate(template, tool);
            
            examples.Add(new IntentTrainingExample
            {
                Text = example,
                ToolName = tool.ToolName,
                Capability = tool.ToolName
            });
        }

        return examples;
    }

    /// <summary>
    /// Get training example templates for a tool based on its capabilities
    /// </summary>
    private List<string> GetTemplatesForTool(DiscoveredTool tool)
    {
        // Get capability-specific templates
        var templates = new List<string>();

        foreach (var capability in tool.Capabilities)
        {
            templates.AddRange(GetTemplatesForCapability(capability));
        }

        // Add generic templates if no specific ones found
        if (templates.Count == 0)
        {
            templates.AddRange(new[]
            {
                $"use {tool.ToolName}",
                $"run {tool.ToolName}",
                $"execute {tool.ToolName}",
                $"perform {tool.ToolName}",
                $"do {tool.ToolName}"
            });
        }

        return templates;
    }

    /// <summary>
    /// Get templates for specific capabilities
    /// </summary>
    private List<string> GetTemplatesForCapability(string capability)
    {
        return capability.ToLower() switch
        {
            "weather" or "forecast" or "temperature" => new[]
            {
                "what's the weather in {location}?",
                "weather forecast for {location}",
                "temperature in {location}",
                "is it raining in {location}?",
                "how's the weather in {location} today?",
                "weather report for {location}",
                "what's it like outside in {location}?",
                "check weather {location}",
                "forecast {location}",
                "weather {location}"
            }.ToList(),

            "current_time" or "date" or "time" => new[]
            {
                "what time is it?",
                "current time",
                "what's today's date?",
                "today's date",
                "what day is it?",
                "show me the time",
                "tell me the date",
                "current date and time",
                "what's the current time?",
                "date today"
            }.ToList(),

            "calculate" or "math" or "arithmetic" => new[]
            {
                "calculate {expression}",
                "what's {number} plus {number}?",
                "solve {expression}",
                "math problem: {expression}",
                "{number} * {number}",
                "compute {expression}",
                "what is {expression}?",
                "do the math: {expression}",
                "{number} divided by {number}",
                "add {number} and {number}"
            }.ToList(),

            "order" or "status" or "track" => new[]
            {
                "order status {orderId}",
                "track order {orderId}",
                "check my order {orderId}",
                "where is order {orderId}?",
                "status of order {orderId}",
                "find order {orderId}",
                "lookup order {orderId}",
                "order {orderId} status",
                "tracking {orderId}",
                "my order {orderId}"
            }.ToList(),

            _ => new[]
            {
                $"use {capability}",
                $"run {capability}",
                $"execute {capability}",
                $"do {capability}",
                $"perform {capability}"
            }.ToList()
        };
    }

    /// <summary>
    /// Populate template with sample values
    /// </summary>
    private string PopulateTemplate(string template, DiscoveredTool tool)
    {
        var result = template;

        // Replace common placeholders
        result = result.Replace("{location}", GetRandomValue(LocationSamples));
        result = result.Replace("{number}", GetRandomValue(NumberSamples));
        result = result.Replace("{expression}", GetRandomValue(ExpressionSamples));
        result = result.Replace("{orderId}", GetRandomValue(OrderIdSamples));

        return result;
    }

    /// <summary>
    /// Train ONNX model from training examples using ML.NET
    /// </summary>
    private async Task<string> TrainOnnxModelAsync(List<IntentTrainingExample> examples, string outputPath)
    {
        _logger.LogInformation("🧠 Training ONNX model with {ExampleCount} examples...", examples.Count);

        try
        {
            // Create ML.NET context
            var mlContext = new MLContext(seed: 0);

        // Convert examples to ML.NET format
        var trainingData = examples.Select(e => new ModelInput
        {
            Text = e.Text,
            Label = e.ToolName
        }).ToList();
        
        // ML.NET requires at least 2 classes - add 'other' class if we only have one tool
        var distinctLabels = trainingData.Select(d => d.Label).Distinct().Count();
        if (distinctLabels < 2)
        {
            _logger.LogInformation("🔧 Adding 'other' class examples for ML.NET multiclass requirement");
            var otherExamples = new[]
            {
                "what's the weather like?",
                "tell me a joke",
                "how do I cook pasta?",
                "what time is it?",
                "help me with something",
                "calculate 2 + 2",
                "show me the news",
                "translate this text",
                "find a restaurant",
                "book a meeting"
            };
            
            foreach (var example in otherExamples)
            {
                trainingData.Add(new ModelInput { Text = example, Label = "other" });
            }
        }

            var dataView = mlContext.Data.LoadFromEnumerable(trainingData);

            // Create training pipeline with proper label conversion
            var pipeline = mlContext.Transforms.Conversion.MapValueToKey("Label", nameof(ModelInput.Label))
                .Append(mlContext.Transforms.Text.FeaturizeText("Features", nameof(ModelInput.Text)))
                .Append(mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy(
                    labelColumnName: "Label",
                    featureColumnName: "Features"))
                .Append(mlContext.Transforms.Conversion.MapKeyToValue(nameof(ModelOutput.PredictedLabel)));

            // Train the model
            _logger.LogInformation("⏳ Training in progress...");
            var model = pipeline.Fit(dataView);

            // Ensure output directory exists
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? "models");

            // Save model (ML.NET format first, then we'd convert to ONNX in production)
            mlContext.Model.Save(model, dataView.Schema, outputPath.Replace(".onnx", ".zip"));
            
            _logger.LogInformation("✅ Model training completed");
            return outputPath.Replace(".onnx", ".zip"); // Return actual path for now
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Model training failed");
            throw;
        }
    }

    /// <summary>
    /// Infer parameters for a capability (heuristic-based)
    /// </summary>
    private Dictionary<string, ToolParameter> InferParametersFromCapability(string capability)
    {
        return capability.ToLower() switch
        {
            "weather" or "forecast" => new Dictionary<string, ToolParameter>
            {
                ["location"] = new() { Type = "string", Required = true, Description = "Location to get weather for" }
            },
            "calculate" => new Dictionary<string, ToolParameter>
            {
                ["expression"] = new() { Type = "string", Required = true, Description = "Mathematical expression to calculate" }
            },
            "order" => new Dictionary<string, ToolParameter>
            {
                ["orderId"] = new() { Type = "string", Required = true, Description = "Order ID to look up" }
            },
            _ => new Dictionary<string, ToolParameter>()
        };
    }

    /// <summary>
    /// Get random sample value
    /// </summary>
    private string GetRandomValue(string[] samples)
    {
        return samples[Random.Shared.Next(samples.Length)];
    }

    // Sample data for template population
    private static readonly string[] LocationSamples = 
    {
        "Paris", "London", "New York", "Tokyo", "San Francisco", "Los Angeles", 
        "Berlin", "Sydney", "Toronto", "Chicago", "Miami", "Seattle"
    };

    private static readonly string[] NumberSamples = 
    {
        "15", "23", "100", "50", "7", "42", "99", "12", "88", "33"
    };

    private static readonly string[] ExpressionSamples = 
    {
        "15 + 23", "100 - 50", "7 * 8", "84 / 12", "25 + 25", "200 - 150"
    };

    private static readonly string[] OrderIdSamples = 
    {
        "12345", "67890", "ABC123", "ORD456", "12456", "98765"
    };
}

/// <summary>
/// Discovered tool information from MCP servers or local tools
/// </summary>
public class DiscoveredTool
{
    public string ServerName { get; set; } = "";
    public string ToolName { get; set; } = "";
    public string Description { get; set; } = "";
    public Dictionary<string, ToolParameter> Parameters { get; set; } = new();
    public List<string> Capabilities { get; set; } = new();
}

/// <summary>
/// Tool parameter information
/// </summary>
public class ToolParameter
{
    public string Type { get; set; } = "";
    public bool Required { get; set; } = false;
    public string Description { get; set; } = "";
}

