# Tool-Driven ONNX Training Approach

## 🎯 Self-Discovering, Self-Training System

We'll implement a **self-configuring training pipeline** that automatically adapts to new MCP servers without code changes.

## 📋 How It Works

### 1. Training Command

```bash
# Run training process when adding new tools
./intentive --train-tools

# Alternatively, with options
./intentive --train-tools --examples=500 --model=models/custom.onnx
```

### 2. Training Pipeline Process

1. **Read tools.json configuration**
   - No hardcoded capabilities - just reads what servers are listed

2. **Connect to all MCP servers listed in config**
   - Starts Docker containers or connects to existing servers
   - Establishes MCP protocol handshake

3. **Auto-discover actual capabilities via MCP protocol**
   - Calls MCP `list_tools` endpoint to get actual capabilities
   - Discovers real tool names and arguments available
   - Example: Weather server exposes `get_weather`, `get_forecast` with `location` parameter

4. **Generate training examples for each discovered tool**
   - 200-500 examples per tool/capability
   - Uses tool descriptions, parameter names, and templates
   - Examples: "weather in Paris", "forecast for London", "calculate 15 * 23"

5. **Train ONNX model with all generated data**
   - Uses ML.NET to train a lightweight model (2-5 minutes)
   - Trains to map user text → specific tool names
   - Cross-validation for accuracy verification

6. **Replace existing model with trained version**
   - Saves to models/trained-intentive.onnx
   - Backup of previous model for safety

7. **Ready for use!**
   - No code changes needed
   - Model accurately maps user requests to actual tools

### 3. Enhanced Classification Flow

```
User input: "What's the weather in Paris?"
↓
ONNX Classification → Tool: "weather-server", Confidence: 0.93
↓
Parameter Extraction → { "location": "Paris" }
↓
Execute MCP Tool → weather-server.get_weather(location="Paris")
↓
Result: "🌤️ Paris: 18°C, partly cloudy"
```

## 🔧 Implementation Components

### 1. Extended MCP Client
```csharp
public class McpClient 
{
    // New discovery method
    public async Task<List<ToolInfo>> DiscoverToolsAsync()
    {
        // Call MCP 'list_tools' endpoint
        // Return actual available tools and parameters
    }
}
```

### 2. Training Data Generator
```csharp
public class TrainingDataGenerator
{
    // Generate diverse training examples from discovered tools
    public async Task<List<TrainingExample>> GenerateFromDiscoveredToolsAsync(
        Dictionary<string, List<ToolInfo>> discoveredTools, 
        int examplesPerTool = 200)
    {
        var examples = new List<TrainingExample>();
        
        foreach (var (serverName, tools) in discoveredTools)
        {
            foreach (var tool in tools)
            {
                // Generate varied examples for this tool
                var toolExamples = GenerateExamplesForTool(serverName, tool);
                examples.AddRange(toolExamples);
            }
        }
        
        return examples;
    }
}
```

### 3. ONNX Model Trainer
```csharp
public class OnnxModelTrainer
{
    // Train model from examples
    public async Task<string> TrainModelAsync(
        List<TrainingExample> examples, 
        string outputPath = "models/trained-intentive.onnx")
    {
        // ML.NET model training
        // Save trained model
        return outputPath;
    }
}
```

### 4. Command Line Interface
```csharp
// In Program.cs
if (args.Contains("--train-tools"))
{
    Console.WriteLine("🔧 Starting tool discovery and training process...");
    await TrainToolsAsync(args);
    return;
}

async Task TrainToolsAsync(string[] args)
{
    // Parse training options
    int exampleCount = GetOptionValue(args, "--examples", 200);
    string modelPath = GetOptionValue(args, "--model", "models/trained-intentive.onnx");
    
    // 1. Initialize tool registry
    var toolRegistry = new ToolRegistry(_logger);
    await toolRegistry.InitializeAsync();
    
    // 2. Discover all tools from MCP servers
    var discoveredTools = await toolRegistry.DiscoverAllToolsAsync();
    Console.WriteLine($"✅ Discovered {discoveredTools.Count} tools from servers");
    
    // 3. Generate training examples
    var generator = new TrainingDataGenerator();
    var examples = await generator.GenerateFromDiscoveredToolsAsync(discoveredTools, exampleCount);
    Console.WriteLine($"✅ Generated {examples.Count} training examples");
    
    // 4. Train ONNX model
    var trainer = new OnnxModelTrainer();
    var trainedModelPath = await trainer.TrainModelAsync(examples, modelPath);
    Console.WriteLine($"✅ Model trained and saved to {trainedModelPath}");
}
```

## 📊 Benefits

### 1. Zero Code Changes
Add new MCP servers to tools.json without touching code

### 2. Self-Discovery
System learns actual capabilities from MCP servers

### 3. Accurate Classification
Trained model provides higher accuracy than pattern matching

### 4. Simple User Workflow
```bash
# Add new server
vim tools.json
# Train system
./intentive --train-tools
# Use system
./intentive
```

## 🚀 Real-World Example

```
# User adds database-server to tools.json
{
  "mcpServers": [
    {
      "name": "database-server",
      "enabled": true,
      "transport": { 
        "type": "stdio", 
        "command": "docker", 
        "args": ["run", "--rm", "-i", "mcp/database-server"]
      }
    }
  ]
}

# Training discovers its capabilities:
$ ./intentive --train-tools
🔧 Starting tool discovery...
✅ Connected to weather-server: get_weather, get_forecast
✅ Connected to time-server: current_time, get_date
✅ Connected to calculator-server: calculate, solve
✅ Connected to database-server: query_table, get_schema, count_rows
✅ Discovered 8 tools from servers
✅ Generated 1,600 training examples
🧠 Training model... (this will take 3-5 minutes)
✅ Model trained and saved to models/trained-intentive.onnx

# User can now immediately query databases:
$ ./intentive
> show schema for users table
⚡ Executing database-server.get_schema(table="users")
...
```

## 📝 Next Steps

1. **Create IntentGenerator** with discovery system
2. **Build ONNX training pipeline**
3. **Add command-line interface** for training
4. **Test with multiple MCP servers**

**Status: Ready for implementation** ⚡