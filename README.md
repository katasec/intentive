# Intentive - Fit-for-Purpose AI Orchestration

Experimental implementation exploring a deterministic-first approach to AI orchestration. The core hypothesis is that most AI systems over-rely on large language models where simpler approaches would suffice.

## Design Hypothesis

Current AI applications often default to LLMs for every task, creating unnecessary latency, cost, and unpredictability. This implementation tests an alternative approach:

1. Use deterministic code for orchestration and business rules
2. Use lightweight models (ONNX) for intent classification
3. Escalate to LLMs only when ambiguity requires it

The goal is to understand the trade-offs between system complexity and operational characteristics like latency, cost, and reliability.

## Implementation Architecture

The system implements a multi-stage pipeline with escalation points:

```
User Request
    ↓
Rule Gate (heuristic filtering)
    ↓
ONNX Intent Classifier (MiniLM-L6-v2)
    ├─→ [high confidence] → Plan Generation → Tool Execution
    └─→ [low confidence/high risk] → LLM Escalation
        ├─→ Structured plan generation
        ├─→ Quality evaluation
        └─→ Response refinement if needed
```

### Components

**Rule Gate**: Pattern matching for common cases (greetings, simple queries)
**ONNX Classifier**: 86MB MiniLM model doing embedding-based intent classification
**Plan Validator**: Schema validation and business rule checking
**Quality Indicators**: Multi-layer evaluation of response adequacy
**Tool Execution**: Deterministic business logic (order lookups, etc.)

### Observed Execution Paths

- `RuleGate → FastPath` - Pattern-matched responses
- `RuleGate → OnnxClassifier → ToolExecution` - High-confidence classification
- `RuleGate → OnnxClassifier → LLMEscalation → QualityEvaluation` - Complex requests
- `RuleGate → OnnxClassifier → LLMEscalation → ResponseRefinement` - Quality-driven retry

## Running the Implementation

**Prerequisites**: .NET 9.0 SDK, OpenAI-compatible API key

```bash
git clone https://github.com/katasec/intentive.git
cd intentive
make build

# Set API credentials
export OPENAI_API_KEY="your-key"
export OPENAI_BASE_URL="https://api.groq.com/openai/v1"  # Optional

make run
```

**Test cases to observe different execution paths:**
```
> what is the status of order 12345?    # Tool execution path
> what's today's date?                  # LLM escalation path
> hello                                 # Rule gate fast path
> help me with something complex        # Quality refinement path
```

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

```bash
make build      # Build solution
make test       # Run tests
make run        # Run with clean console
make confidence # Full connectivity test
```

## Performance Observations

- ONNX Classification: ~50ms (local inference)
- Rule Gate: <5ms (pattern matching)  
- LLM Escalation: 200-800ms (network dependent)
- Memory Usage: ~186MB (base + ONNX model)

## Notes

This is an experimental exploration of alternatives to LLM-first architectures. The implementation uses Microsoft Semantic Kernel for LLM integration and Microsoft.ML.OnnxRuntime for local model inference.
