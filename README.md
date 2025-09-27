# Intentive - Tool-First AI Orchestration

A **tool-first architecture** that uses AI as a **translator** rather than an orchestrator. Instead of using LLMs to drive workflow logic, Intentive uses lightweight ONNX models to map human intent to specific tools, with LLM escalation only for unsupported requests.

| Source | Key Point | Quote (Workflow-Orchestration Emphasis) |
|--------|-----------|------------------------------------------|
| [Retool - State of AI (H1 2024)](https://retool.com/blog/state-of-ai-h1-2024) | Workflow automation jumped YoY from 13% → 18% | "We saw a big jump in AI used for workflow automation... the fastest-growing category of adoption this year." |
| [LangChain - Is LangGraph Used in Production?](https://blog.langchain.com/is-langgraph-used-in-production/) | Enterprises use LangGraph for reliable/observable workflows | "The key driver for LangGraph adoption is making agents reliable, observable, and controlabile in production workflows." |

## 🎯 Core Philosophy

**AI as Translator, Not Orchestrator**: Convert human intent into deterministic tool execution paths instead of using LLMs for business logic.

## ⚡ Tool-First Architecture

**Self-Configuring System**: Automatically discovers tools from `tools.json` configuration, trains ONNX models on discovered capabilities, and provides fast deterministic execution.

1. **Tool Discovery**: Automatically connects to MCP servers and local tools
2. **Intent Training**: Generates training data from discovered tool capabilities  
3. **ONNX Classification**: Lightweight models (86MB) classify requests to specific tools
4. **Direct Execution**: Fast tool execution (~10-50ms) without LLM overhead
5. **LLM Escalation**: Only for unsupported or complex requests

## 🚀 Quick Start with Docker

Try the implementation immediately without any setup using Docker:

```bash
# Run with Groq (fast, free API)
docker run -e OPENAI_API_KEY=your-groq-key -e OPENAI_BASE_URL=https://api.groq.com/openai/v1 ghcr.io/katasec/intentive:latest

# Run with OpenAI
docker run -e OPENAI_API_KEY=your-openai-key ghcr.io/katasec/intentive:latest

# Without API key (shows usage)
docker run ghcr.io/katasec/intentive:latest
```

**Test different execution paths:**
```
> what is the status of order 12345?    # Deterministic tool execution
> hello                                 # Fast rule-based response 
> what's today's date?                  # LLM escalation
> help me with something complex        # Quality-driven refinement
```

**Get a Groq API key** (free, fast):
1. Visit [console.groq.com](https://console.groq.com)
2. Sign up and create an API key
3. Use with the Docker command above

## Current Architecture

Simple 3-stage pipeline optimized for speed and cost-efficiency:

```
1. USER REQUEST
   ↓

2. RULE GATE (~5ms)
   • Pattern matching (hi/hello)
   • Input validation (length) 
   • Fast path responses
   ↓ [if no direct match, continue]

3. ONNX INTENT CLASSIFIER (~50ms)
   • MiniLM-L6-v2 (86MB local model)
   • Embedding-based classification
   • Confidence + Risk scoring
   ↓
   
   HIGH CONFIDENCE              LOW CONFIDENCE/RISKY
   ↓                            ↓
   
   DETERMINISTIC EXECUTOR       LLM ESCALATION (~200-800ms)
   (~10ms)                      • GPT-4o-mini/Groq
   • GetOrder lookup            • Plan generation  
   • Data validation            • Tool orchestration
   • Fast business logic        • Response composition
   ↓                            ↓
                 ↓
                 
4. RESPONSE TO USER
```

### Components

**Rule Gate** (0-5ms): Pattern matching for common cases like greetings, input validation
**ONNX Classifier** (~50ms): 86MB MiniLM model for local intent classification with confidence scoring  
**Tool Executor** (~10ms): Deterministic business logic - order lookups, data queries, calculations
**LLM Escalation** (200-800ms): GPT-4o-mini or Groq models for complex reasoning and plan generation

### Execution Paths

1. **Fast Path**: `Rule Gate → Response` (greetings, simple queries)
2. **Deterministic Path**: `Rule Gate → ONNX → Tool Executor` (high-confidence classifications) 
3. **LLM Path**: `Rule Gate → ONNX → LLM Escalation → Tools` (low-confidence or high-risk requests)

## 🔧 Tool Configuration & Training

### 1. Configure Your Tools

Edit `tools.json` to define MCP servers and local tools:

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

### 2. Train Intent Classifier

**Auto-discover and train** from your configured tools:

```bash
# Train ONNX model from discovered tools
./intentive --train-tools

# With custom parameters
./intentive --train-tools --examples 200 --model models/custom.onnx
```

**Training Process**:
1. 🔍 Discovers tools from `tools.json` (local + MCP servers)
2. 🎨 Generates training examples for each discovered capability
3. 🧠 Trains lightweight ONNX model (2-5 minutes)
4. ✅ System ready - now accurately classifies user input to tools

### 3. Zero-Code Tool Addition

**Add new capabilities** without changing code:

```bash
# 1. Add new MCP server to tools.json
vim tools.json

# 2. Retrain system
./intentive --train-tools

# 3. Use new capabilities immediately
./intentive
```

## Development Setup

**For development** (requires .NET 9.0 SDK):

```bash
git clone https://github.com/katasec/intentive.git
cd intentive
make build

# Set API credentials
export OPENAI_API_KEY="your-key"
export OPENAI_BASE_URL="https://api.groq.com/openai/v1"  # Optional

# First-time setup: train your model
./intentive --train-tools

# Run the system
make run
```

**Docker is recommended** for trying the implementation - see the Quick Start section above.

## Performance Observations

- ONNX Classification: ~50ms (local inference)
- Rule Gate: <5ms (pattern matching)  
- LLM Escalation: 200-800ms (network dependent)
- Memory Usage: ~186MB (base + ONNX model)

## Notes

This is an experimental exploration of alternatives to LLM-first architectures. The implementation uses Microsoft Semantic Kernel for LLM integration and Microsoft.ML.OnnxRuntime for local model inference.
