# Fit-for-Purpose AI Orchestration (Design Approach)

From a systems design perspective, the most effective way to build with AI is to use the right tool for the right job. Deterministic code handles orchestration and rules; lightweight models decode intent; and only when ambiguity truly demands it do we escalate to larger LLMs. This fit-for-purpose approach avoids the 'one-tool-to-rule-them-all' trap, cutting costs, reducing latency, and lowering hallucinations — while keeping workflows reliable, auditable, and efficient.

## Intent Decoding with MiniLM ONNX

For lightweight intent decoding, we will use the **MiniLM ONNX model** (e.g., [onnx-models/all-MiniLM-L6-v2-onnx](https://huggingface.co/onnx-models/all-MiniLM-L6-v2-onnx)).

**Why MiniLM ONNX?**
- Small disk size (~80–100 MB fp32, ~40 MB quantized).
- Fast inference on CPU with Microsoft.ML.OnnxRuntime.
- Widely used in production for text embeddings and intent classification.
- Easy to distribute with no Python runtime required.

**Purpose in this project**
- Decode human text into **embeddings**.
- Compare embeddings against pre-defined intent exemplars (cosine similarity).
- Select the most likely intent quickly and cheaply.
- Provide a reliable, auditable first step before any escalation to larger LLMs.

## Systems Designer View (Adjusted)
- LLMs ≠ default OS (use surgically to translate ambiguous human intent into structured signals)
- Most orchestration doesn't need LLMs (routing, schema validation, retries, workflow logic → deterministic code)
- Fit-for-purpose layering (Front: heuristics/normalization/light classifiers; Core: deterministic pipelines; LLM assist only when ambiguity is unavoidable)
- Reduce unnecessary probabilistic hops (fewer LLM calls ⇒ fewer hallucinations)

## Reference Flow (text diagram)
```
User Request
↓
Normalizer (trim, lang detect, PII mask)
↓
Rule Gate (heuristics: length / domain / safety)
├─> [hit] Semantic Intent Cache
│       └─> [plan found] Validator & Oracles (JSON schema, dry-runs, constraints)
│
└─> [miss/continue] Tiny Classifier (task type, ambiguity, risk)
   ├─> [high risk / unclear] Escalate?
   │        ├─ yes → Mid/Big Model (plan repair or compose)
   │        └─ no  → Cheap LM Probe (optional draft plan)
   │
   └─> [low risk & clear] Cheap LM Probe (optional draft plan)
↓
Validator & Oracles (JSON schema, dry-runs, constraints)
├─> [valid plan] Tool Executor
│         ↓
│   Tool Result Cache
│         ↓
│   Small Composer LM
│         ↓
│   Answer Cache
│         ↓
│   Response to User
│
└─> [invalid / low confidence] Escalate?
    ├─ yes → Mid/Big Model (plan repair or compose)
    └─ no  → Cheap LM Probe (retry)
```

## Additional Caches:
• Tool Plan Cache (after Cheap LM Probe, before Tool Executor)
• Tool Result Cache (after Tool Executor, before Small Composer LM)
• Answer Cache (after Small Composer LM, before Response to User)