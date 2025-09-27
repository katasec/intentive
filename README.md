# Intentive - Fit-for-Purpose AI Orchestration

Most agentic AI solutions use LLMs for workflow orchestration - essentially deploying something that knows the history of humanity to execute business logic. This is like using a cannonball to kill a mosquito.

**The industry is recognizing this problem.** Recent surveys show workflow automation is the fastest-growing AI adoption category, jumping from 13% to 18% year-over-year ([Retool State of AI H1 2024](https://retool.com/blog/state-of-ai-h1-2024)). Companies like [Remote.com automated 11M tasks and resolved 27.5% of IT tickets via AI](https://zapier.com/blog/remote-ai-automation-case-study/), while enterprises choose [LangGraph specifically for "reliable, observable, and controllable" production workflows](https://blog.langchain.com/is-langgraph-used-in-production/).

The pattern is clear: **ROI comes from embedding AI into existing business processes, not replacing them with chat interfaces.**

Workflow automation worked efficiently for decades before LLMs existed. The key insight is that AI's primary value should be as a **translator** - converting human intent into deterministic code paths, not replacing the execution engine itself.

## Implementation Approach

This implementation separates intent translation from workflow execution:

1. **Intent Translation**: Lightweight ONNX models classify user requests into actionable intents
2. **Workflow Execution**: Traditional deterministic code handles business logic
3. **LLM Escalation**: Only when human intent cannot be reliably mapped to existing workflows

The hypothesis is that this separation yields better latency, cost, and reliability characteristics than LLM-driven orchestration while maintaining the human-friendly interface that makes AI valuable.

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
┌─────────────────┐
│   User Request  │
└─────────┬───────┘
          ↓
   ┌─────────────────────────────────────┐
   │         Rule Gate                   │  ←── <5ms
   │  • Pattern matching (hi/hello)     │  
   │  • Input validation (length)       │  
   │  • Fast path responses             │  
   └─────────┬───────────────────────────┘
             ↓ [continue]
   ┌─────────────────────────────────────┐
   │    ONNX Intent Classifier          │  ←── ~50ms
   │  • MiniLM-L6-v2 (86MB local)      │  
   │  • Embedding-based classification  │  
   │  • Confidence + Risk scoring       │  
   └─────┬─────────────┬─────────────────┘
         ↓             ↓
   [confident]    [uncertain/risky]
         ↓             ↓
 ┌──────────────┐ ┌────────────────────────┐
 │ Deterministic│ │    LLM Escalation      │  ←── 200-800ms
 │ Tool Executor│ │ • GPT-4o-mini/Groq     │  
 │ • GetOrder   │ │ • Plan generation      │  
 │ • Validation │ │ • Tool orchestration   │  
 │ • Fast paths │ │ • Response composition │  
 └──────┬───────┘ └─────────┬──────────────┘
        ↓                   ↓
        └─────┬─────────────┘
              ↓
    ┌─────────────────┐
    │ Response to User│
    └─────────────────┘
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

## Development Setup

**For development** (requires .NET 9.0 SDK):

```bash
git clone https://github.com/katasec/intentive.git
cd intentive
make build

# Set API credentials
export OPENAI_API_KEY="your-key"
export OPENAI_BASE_URL="https://api.groq.com/openai/v1"  # Optional

make run
```

**Docker is recommended** for trying the implementation - see the Quick Start section above.

## Structure

```
src/Intentive.Core/     # Core orchestration logic
src/Intentive.Console/  # CLI interface
models/                 # ONNX models (MiniLM-L6-v2)
tests/                  # Tests
Makefile                # Build targets
```

## Configuration

Key parameters (environment variables or command line):

- `OPENAI_API_KEY` - API key for LLM escalation
- `OPENAI_BASE_URL` - Custom endpoint (Groq, Azure, etc.)
- Intent thresholds in `OrchestrationConfig.cs`:
  - `ConfidenceThreshold`: 0.7 (ONNX classification confidence)
  - `AmbiguityThreshold`: 0.5 (triggers LLM escalation)
  - `RiskThreshold`: 0.8 (high-risk escalation)

## Build Targets

**Local Development:**
```bash
make build      # Build solution
make test       # Run tests  
make run        # Run with clean console
make confidence # Full connectivity test
```

**Docker:**
```bash
make docker-build    # Build Docker image
make docker-run      # Run container (uses your env vars)
make docker-push     # Push to GitHub Container Registry
make docker-pull     # Pull published image
```

## Performance Observations

- ONNX Classification: ~50ms (local inference)
- Rule Gate: <5ms (pattern matching)  
- LLM Escalation: 200-800ms (network dependent)
- Memory Usage: ~186MB (base + ONNX model)

## Notes

This is an experimental exploration of alternatives to LLM-first architectures. The implementation uses Microsoft Semantic Kernel for LLM integration and Microsoft.ML.OnnxRuntime for local model inference.
