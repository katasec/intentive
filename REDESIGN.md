# Intentive Tool-First Architecture Redesign

## 🔥 SLASH AND BURN APPROACH

This is a **COMPLETE REWRITE** - not a refactor. We're gutting the over-engineered current system and replacing it with a clean, tool-first architecture.

## 🗑️ FILES TO DELETE COMPLETELY

```bash
# Complex orchestration (616 lines → DELETE)
rm src/Intentive.Core/Plugins/IntentiveOrchestrationPlugin.cs

# Unused LLM-first (223 lines → DELETE) 
rm src/Intentive.Core/Plugins/LLMFirstOrchestrationPlugin.cs

# Complex model classes → DELETE ALL
rm src/Intentive.Core/Models/Plan.cs
rm src/Intentive.Core/Models/PlanStep.cs
rm src/Intentive.Core/Models/ValidationResult.cs
rm src/Intentive.Core/Models/QualityIndicators.cs
# Keep OrchestrationResult.cs but simplify it
```

## 🔪 LOGIC TO REMOVE

- ❌ Multi-step plan generation and validation
- ❌ Quality indicators and response refinement  
- ❌ Complex execution path tracking
- ❌ Plan repair and LLM escalation loops
- ❌ JSON parsing for plan steps
- ❌ Generic/vague intent detection
- ❌ The entire "CheapLMProbe → PlanValidator → ToolExecution → QualityEscalation" chain

## ✅ WHAT SURVIVES (Minimal)

- ✅ `GetOrderTool.cs` (example local tool)
- ✅ `OrchestrationConfig.cs` (extend for tools)
- ✅ `SimpleIntentClassifier.cs` (gut and rebuild)
- ✅ Rule Gate concept (simplify to 5 lines)

## 🆕 NEW ARCHITECTURE

### Core Flow:
```
STARTUP:
1. Load tools.json config
2. Discover local tools ([KernelFunction] methods)  
3. Connect to MCP Docker servers
4. Generate intent training examples from ALL tools
5. Train/load ONNX classifier
6. Display: "🤖 Here's what I can do: weather, time, math, orders"

RUNTIME:
User Input → Rule Gate → ONNX Classification → {
    HIGH CONFIDENCE + Tool Available: Execute Tool Directly (5-10ms)
    LOW CONFIDENCE: "I don't have a tool for that, escalating..." → LLM
}
```

### New Simplified Orchestration (~50 lines total):
```csharp
public class SimpleOrchestrationEngine
{
    public async Task<string> HandleAsync(string input)
    {
        // Rule gate (5 lines)
        if (input.ToLower() is "hi" or "hello") 
            return "Hello! Ask me about weather, time, math, or orders.";
            
        // ONNX classification (1 line)  
        var tool = await _classifier.FindToolForAsync(input);
        
        // Execute or escalate (3 lines)
        return tool != null 
            ? await _toolRegistry.ExecuteAsync(tool, input)
            : await _llm.RespondAsync($"I don't have a tool for: {input}");
    }
}
```

## 📋 IMPLEMENTATION TASKS

### Task 1: Create MCP/Tool Configuration
**File:** `tools.json`
```json
{
  "mcpServers": [
    {
      "name": "weather-server", 
      "enabled": true,
      "transport": {
        "type": "stdio",
        "command": "docker",
        "args": ["run", "--rm", "-i", "mcp/weather-server"]
      },
      "capabilities": ["weather", "forecast", "temperature"]
    },
    {
      "name": "time-server",
      "enabled": true, 
      "transport": {
        "type": "stdio",
        "command": "docker", 
        "args": ["run", "--rm", "-i", "mcp/time-server"]
      },
      "capabilities": ["current_time", "date", "timezone"]
    },
    {
      "name": "calculator-server",
      "enabled": true,
      "transport": {
        "type": "stdio",
        "command": "docker",
        "args": ["run", "--rm", "-i", "mcp/calculator-server"]  
      },
      "capabilities": ["calculate", "math", "arithmetic"]
    }
  ],
  "localTools": [
    {
      "name": "GetOrder",
      "enabled": true,
      "class": "Intentive.Core.Tools.GetOrderTool",
      "capabilities": ["order", "status", "track"]
    }
  ]
}
```

### Task 2: Build Tool Registry with Discovery
**File:** `src/Intentive.Core/Services/ToolRegistry.cs`
- Discover local tools via reflection ([KernelFunction] attributes)
- Connect to MCP Docker servers via stdio
- Maintain unified tool catalog with capabilities
- Health check MCP servers at startup

### Task 3: Auto-Discovery and ONNX Training System
**File:** `src/Intentive.Core/Services/IntentGenerator.cs`
**Binary Command:** `./intentive --train-tools`

**Self-Configuring Training Pipeline:**
1. **Read tools.json** (no hardcoded capabilities)
2. **Connect to ALL MCP servers** listed in config
3. **Auto-discover actual capabilities** via MCP `list_tools` protocol
4. **Generate training examples** (200-500 per discovered tool):
   - Weather server discovers: ["get_weather", "get_forecast"] → generates "weather in NYC", "forecast for tomorrow"
   - Calculator discovers: ["calculate", "solve"] → generates "calculate 15 * 23", "what's 100 + 50"
   - Time server discovers: ["current_time", "get_date"] → generates "what time is it?", "today's date"
   - Local GetOrder: ["GetOrderAsync"] → generates "order status 12345", "track order"
5. **Train ONNX model** with generated data (2-5 minutes)
6. **Replace existing model** with trained version
7. **System ready** - now accurately classifies user input to discovered tools

**Key Benefits:**
- **Zero code changes** when adding new MCP servers - just edit tools.json
- **Self-discovery** - system learns actual capabilities from MCP servers
- **Accurate classification** - trained model vs pattern matching
- **Simple workflow** - one command retrains everything

### Task 4: Simplify ONNX Classifier for Tool-Based Intents
**File:** `src/Intentive.Core/Services/SimpleIntentClassifier.cs` (REWRITE)
- Remove hardcoded patterns
- Work with tool-generated training data
- Return tool name + confidence (not complex classification result)
- Simple interface: `Task<(string? ToolName, double Confidence)> FindToolForAsync(string input)`

### Task 5: Create Simplified Orchestration Engine
**File:** `src/Intentive.Core/Services/SimpleOrchestrationEngine.cs` (NEW)
- Replace IntentiveOrchestrationPlugin entirely
- Clean 3-step process: Rule Gate → Classification → Execute/Escalate
- ~50 lines total vs 616 lines current

### Task 6: Add MCP Client Implementation  
**File:** `src/Intentive.Core/Services/McpClient.cs`
- Communicate with Docker MCP servers via stdio
- Handle tool discovery and execution
- Manage process lifecycle (docker run --rm -i)

### Task 7: Update Startup Sequence
**File:** `src/Intentive.Console/Program.cs`
- Initialize ToolRegistry on startup
- Display available capabilities to user
- Show "Here's what I can do" message

## 🎯 EXPECTED OUTCOME

### Before (Complex):
- 616 lines of orchestration complexity
- Plan generation → validation → execution → quality checking → refinement
- Multiple LLM calls for simple requests
- Unclear what the system can actually do

### After (Simple):
- ~50 lines of orchestration
- Direct tool execution for supported requests
- Single LLM call only for unsupported requests  
- Clear "I can do X, Y, Z" startup message

### User Experience:
```
🚀 Starting Intentive...
✅ Local tools: GetOrder  
✅ MCP weather-server: weather, forecast
✅ MCP time-server: current_time, date
✅ MCP calculator: math, calculate
🤖 Ready! I can help with: weather, time, calculations, orders

> What's the weather in Paris?
🌤️ Paris: 18°C, partly cloudy, humidity 65%
⚡ Executed: weather-server (45ms)

> What's today's date?
📅 Today is Friday, January 27, 2025  
⚡ Executed: time-server (32ms)

> How do I cook pasta?
🤔 I don't have a tool for cooking, escalating to AI...
🤖 Here's how to cook pasta... (LLM response)
⚡ Executed: LLM escalation (850ms)
```

## 🚀 IMPLEMENTATION ORDER

1. **Task 1** - Create tools.json config (foundation)
2. **Task 2** - Build ToolRegistry (core infrastructure)  
3. **Task 6** - Add MCP client (enables MCP servers)
4. **Task 3** - Generate training data (enables ONNX)
5. **Task 4** - Rewrite ONNX classifier (enables classification)
6. **Task 5** - Create simple orchestration (ties it together)
7. **Task 7** - Update startup (user experience)

**Status: Ready for implementation** ⚡

## 📊 METRICS

- **Code reduction:** ~90% less orchestration code
- **Performance:** 10x faster for supported tools (5-10ms vs 200-800ms)
- **Clarity:** Users know exactly what system can do
- **Maintainability:** Simple, understandable architecture
- **Extensibility:** Just add tools to JSON config