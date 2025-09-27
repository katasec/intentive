# Intentive - Fit-for-Purpose AI Orchestration

A production-ready AI orchestration system that uses the right AI tool for each job, implementing a deterministic-first approach with intelligent escalation.

## Design Philosophy: Fit-for-Purpose AI

The most effective way to build with AI is to avoid the "one-tool-to-rule-them-all" trap. Instead, use **deterministic code** for orchestration and rules, **lightweight models** for intent classification, and only escalate to **larger LLMs** when ambiguity truly demands it.

### Why This Approach Works
- ⚡ **Cuts costs** - Lightweight models handle 80%+ of requests  
- 🚀 **Reduces latency** - Fast local ONNX inference (50ms vs 500ms)
- 🎯 **Lowers hallucinations** - Deterministic paths reduce probabilistic hops
- 📋 **Keeps workflows reliable** - Auditable execution paths with quality indicators
- 🔍 **Maintains efficiency** - Resources used proportionally to complexity
- 🛡️ **Enhances control** - Multi-layer quality assurance with automatic refinement

## Architecture & Flow

```
User Request
    ↓
🚪 Rule Gate (heuristics, fast paths)
    ↓
🧠 ONNX Intent Classifier (MiniLM-L6-v2, ~50ms)
    ├─→ [high confidence] → Plan Generator → Validator → Tools
    └─→ [low confidence/high risk] → LLM Escalation
        ├─→ Plan Generation (JSON structured)
        ├─→ Quality Indicators (confidence, relevance, specificity)  
        ├─→ Response Refinement (if insufficient)
        └─→ Direct LLM Response (when no tools match)
```

### Orchestration Layers

1. **Rule Gate** - Fast heuristic filtering and simple pattern matching
2. **ONNX Classification** - 86MB MiniLM model for intent decoding via embeddings + cosine similarity
3. **Plan Validation** - Schema validation and constraint checking  
4. **Tool Execution** - Deterministic business logic (e.g., order lookups)
5. **Quality Indicators** - Multi-stage response evaluation:
   - Plan quality (confidence scores, intent-tool mismatches)
   - Response quality (relevance, specificity, deflection detection)
   - Automatic refinement when quality thresholds not met

### Execution Paths

The system provides **full visibility** into decision-making:

- `RuleGate → FastPath` - Simple greetings, cached responses
- `RuleGate → MiniLMClassifier → CheapLMProbe → ToolExecution` - Standard flow  
- `RuleGate → MiniLMClassifier → LLMEscalation → QualityEscalation` - Complex requests
- `RuleGate → MiniLMClassifier → LLMEscalation → QualityEscalation → ResponseRefinement` - Quality-driven refinement

## Getting Started

### Prerequisites
- .NET 9.0 SDK
- PowerShell 7+ (for environment setup)
- OpenAI-compatible API key (OpenAI, Groq, etc.)

### Quick Start

1. **Clone and build:**
```bash
git clone <repo-url>
cd intentive
make build
```

2. **Set up API credentials:**
```powershell
# PowerShell (recommended)
$env:OPENAI_API_KEY = "your-api-key-here"
$env:OPENAI_BASE_URL = "https://api.groq.com/openai/v1"  # Optional: for Groq

# Or Bash  
export OPENAI_API_KEY="your-api-key-here"
export OPENAI_BASE_URL="https://api.groq.com/openai/v1"
```

3. **Run the system:**
```bash
make run
```

4. **Test different execution paths:**
```
> what is the status of order 12345?    # → Tool execution
> what's today's date?                  # → LLM escalation  
> hello                                 # → Rule gate fast path
> help me with something complex        # → Quality refinement
```

## Project Structure

```
intentive/
├── src/
│   ├── Intentive.Core/           # Core orchestration logic
│   │   ├── Configuration/        # Config models and binding
│   │   ├── Models/               # Domain models and records
│   │   ├── Plugins/              # Orchestration plugins
│   │   ├── Services/             # ONNX intent classifier
│   │   └── Tools/                # Business logic tools
│   └── Intentive.Console/        # CLI application
├── tests/
│   └── Intentive.Tests/          # Unit and integration tests
├── models/                       # ONNX models (86MB MiniLM)
│   ├── all-MiniLM-L6-v2.onnx    # Main embedding model
│   ├── tokenizer.json           # Tokenizer configuration
│   └── vocab.txt                # Vocabulary file
├── Makefile                      # Build, test, and run targets
└── README.md                     # This file
```

## Configuration

The system uses a layered configuration approach:

1. **Environment variables** (highest priority)
2. **Command line arguments**  
3. **Default values**

### Key Settings

```json
{
  "Mode": "Intentive",                    # Orchestration strategy
  "OpenAI": {
    "ApiKey": "env:OPENAI_API_KEY",      # API key from environment
    "BaseUrl": "env:OPENAI_BASE_URL",    # Optional: custom endpoint
    "CheapModel": "gpt-4o-mini",         # For planning/classification
    "EscalationModel": "gpt-4"           # For complex requests
  },
  "Intent": {
    "ModelPath": "./models/all-MiniLM-L6-v2.onnx",  # ONNX model
    "ConfidenceThreshold": 0.7,          # Classification confidence
    "AmbiguityThreshold": 0.5,           # Escalation trigger
    "RiskThreshold": 0.8                 # High-risk escalation
  }
}
```

## Development

### Available Make Targets

```bash
make build          # Build solution
make test           # Run tests with LLM output
make run            # Run with clean console (logs to file)
make run-verbose    # Run with console logs  
make confidence     # Full environment + LLM connectivity test
make check-env      # Validate development setup
```

### Adding New Tools

1. Create a tool class implementing semantic kernel patterns:
```csharp
public class MyTool
{
    [KernelFunction("my_function")]
    public async Task<string> ExecuteAsync(string parameter) 
    {
        // Business logic here
        return result;
    }
}
```

2. Register in `Program.cs`:
```csharp
kernelBuilder.Plugins.AddFromType<MyTool>();
```

3. Update intent patterns in `SimpleIntentClassifier.cs`

### Quality Indicators

The system includes sophisticated quality evaluation:

**Plan Quality Indicators:**
- Confidence scores below thresholds
- Intent-tool mismatches (e.g., cooking question → GetOrder tool)
- Generic/vague intents
- Missing actionable steps

**Response Quality Indicators:**  
- Response relevance to user question
- Generic/deflecting responses
- Insufficient specificity
- Multiple quality issues

**Automatic Refinement:**
When quality indicators trigger, the system automatically:
1. Re-prompts with specific guidance
2. Provides context about quality issues  
3. Requests more direct, helpful responses

## Performance Characteristics

- **ONNX Classification**: ~50ms (local inference)
- **Rule Gate**: <5ms (pattern matching)
- **LLM Escalation**: 200-800ms (depending on endpoint)
- **Tool Execution**: 10-100ms (business logic dependent)
- **Memory Usage**: ~100MB base + 86MB ONNX model

## API Compatibility

Works with any OpenAI-compatible API:
- **OpenAI GPT models** (gpt-4, gpt-4o-mini, etc.)
- **Groq** (llama-3.1-8b-instant, etc.)
- **Azure OpenAI**
- **Local LLMs** via OpenAI-compatible servers

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make changes with tests
4. Run `make confidence` to validate
5. Submit a pull request

## License

MIT License - see LICENSE file for details

## Design Credits

This implementation is based on fit-for-purpose AI principles:
- Deterministic code handles orchestration and rules
- Lightweight models decode intent  
- LLMs used only when ambiguity demands it
- Multiple quality checkpoints ensure reliable results

The architecture avoids the "one-tool-to-rule-them-all" trap while maintaining the flexibility to handle complex requests when needed.