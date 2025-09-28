package router

import (
	"context"
	"errors"
	"strings"
)

// RouteMeta carries side-channel routing hints.
type RouteMeta struct {
	Path   string  // local | mcp | llm
	ToolID string  // tool identifier
	Score  float64 // router confidence [0..1]
	Reason string  // explanation / reason code
}

// Router defines the routing interface.
type Router interface {
	Route(ctx context.Context, in string) (out string, meta RouteMeta, err error)
}

// Tool represents an executable capability.
type Tool interface {
	ID() string
	Schema() any
	Exec(ctx context.Context, in string) (string, error)
}

// ToolResolver resolves tools by name or intent.
type ToolResolver interface {
	Resolve(name string) (Tool, bool)
}

// Embedder provides vector embeddings.
type Embedder interface {
	Embed(text string) ([]float32, error)
}

// Escalator provides LLM fallback.
type Escalator interface {
	Fallback(ctx context.Context, in string) (string, error)
}

// Config holds routing thresholds.
type Config struct {
	// τ thresholds and ε tie-break (kept simple for v1 CLI)
	TauLocalHigh float64 // prefer local if score >= this
	TauMcpHigh   float64 // prefer MCP if score >= this (reserved for later)
	TauLow       float64 // below this, MAY influence reasons; we still prefer local if a tool is available
	Epsilon      float64 // tie-break tolerance
}

// simpleRouter is the v1 implementation with deterministic scoring/tie-break.
type simpleRouter struct {
	resolver  ToolResolver
	embedder  Embedder
	escalator Escalator
	cfg       Config
}

// New constructs a Router with the given dependencies.
func New(resolver ToolResolver, embed Embedder, esc Escalator, cfg Config) Router {
	// Apply conservative defaults if caller leaves zero-values.
	if cfg.TauLocalHigh == 0 && cfg.TauMcpHigh == 0 && cfg.TauLow == 0 && cfg.Epsilon == 0 {
		cfg = Config{
			TauLocalHigh: 0.60,
			TauMcpHigh:   0.60,
			TauLow:       0.20,
			Epsilon:      0.05,
		}
	}
	return &simpleRouter{
		resolver:  resolver,
		embedder:  embed,
		escalator: esc,
		cfg:       cfg,
	}
}

func (r *simpleRouter) Route(ctx context.Context, in string) (string, RouteMeta, error) {
	// 1) Slot extraction (placeholder): first token as a potential tool hint.
	toolHint := strings.ToLower(strings.Fields(in + " echo")[0]) // default "echo" if empty
	if toolHint == "" {
		toolHint = "echo"
	}

	// 2) Embed and compute a simple deterministic score.
	score := r.score(in)

	// 3) Resolve tool: try hint, else fall back to "echo" if available.
	var (
		t         Tool
		ok        bool
		toolID    string
		reasonTag string
	)

	if t, ok = r.resolver.Resolve(toolHint); ok {
		toolID = t.ID()
	} else if t, ok = r.resolver.Resolve("echo"); ok {
		toolID = t.ID()
		reasonTag = "default-echo"
	} else {
		// No tool resolved at all → escalate if possible.
		if out, meta, ok2 := r.tryEscalate(ctx, in, "no-tool"); ok2 {
			return out, meta, nil
		}
		// Last resort: explain no tool matched.
		return "(router) no tool matched", RouteMeta{
			Path:   "local",
			ToolID: "",
			Score:  score,
			Reason: "no-tool",
		}, nil
	}

	// 4) Schema "validation" placeholder: require non-nil schema to simulate validation pass.
	if t.Schema() == nil {
		// treat as validation fail → escalate if possible
		if out, meta, ok2 := r.tryEscalate(ctx, in, "validation-fail"); ok2 {
			return out, meta, nil
		}
		return "", RouteMeta{Path: "local", ToolID: toolID, Score: score, Reason: "validation-fail"}, errors.New("schema validation failed")
	}

	// 5) Execute LOCAL tool even on low confidence.
	// Philosophy: escalate only when unavoidable (no tool / validation fail).
	out, err := t.Exec(ctx, in)
	meta := RouteMeta{
		Path:   "local",
		ToolID: toolID,
		Score:  score,
		Reason: r.composeReason(score, reasonTag),
	}
	return out, meta, err
}

// score maps embedding length to a stable [0..1] confidence (stub logic).
func (r *simpleRouter) score(text string) float64 {
	if r.embedder == nil {
		return 0.0
	}
	v, err := r.embedder.Embed(text)
	if err != nil || len(v) == 0 {
		return 0.0
	}
	// Deterministic: scale by vector length (cap at 1.0).
	// With StubEmbedder len=2 → score=0.2.
	n := float64(len(v))
	if n >= 10 {
		return 1.0
	}
	return n / 10.0
}

func (r *simpleRouter) reasonForScore(s float64) string {
	switch {
	case s >= r.cfg.TauLocalHigh:
		return "local-high"
	case s >= r.cfg.TauLow:
		return "local-mid"
	default:
		return "low-confidence"
	}
}

func (r *simpleRouter) composeReason(s float64, tag string) string {
	base := r.reasonForScore(s)
	if tag == "" {
		return base
	}
	return base + "|" + tag
}

func (r *simpleRouter) tryEscalate(ctx context.Context, in string, reason string) (string, RouteMeta, bool) {
	if r.escalator == nil {
		return "", RouteMeta{}, false
	}
	out, _ := r.escalator.Fallback(ctx, in)
	return out, RouteMeta{
		Path:   "llm",
		ToolID: "llm",
		Score:  0.0,
		Reason: reason,
	}, true
}
