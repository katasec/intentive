package tool

import (
	"context"

	"github.com/katasec/intentive/pkg/router"
)

// SimpleTool is a minimal Tool implementation.
type SimpleTool struct {
	IDStr     string
	SchemaAny any
	ExecFn    func(ctx context.Context, in string) (string, error)
}

func (t *SimpleTool) ID() string                                          { return t.IDStr }
func (t *SimpleTool) Schema() any                                         { return t.SchemaAny }
func (t *SimpleTool) Exec(ctx context.Context, in string) (string, error) { return t.ExecFn(ctx, in) }

// Registry stores tools and implements router.ToolResolver.
type Registry struct {
	m map[string]router.Tool
}

func NewRegistry() *Registry {
	return &Registry{m: make(map[string]router.Tool)}
}

func (r *Registry) Register(t router.Tool) {
	if t == nil {
		return
	}
	r.m[t.ID()] = t
}

func (r *Registry) Resolve(name string) (router.Tool, bool) {
	t, ok := r.m[name]
	return t, ok
}

// EchoTool provides a trivial echo tool useful for wiring/tests.
// SchemaAny must be non-nil to pass the router's simple validation check.
func EchoTool() router.Tool {
	return &SimpleTool{
		IDStr:     "echo",
		SchemaAny: struct{}{}, // non-nil placeholder schema
		ExecFn: func(ctx context.Context, in string) (string, error) {
			return "(echo) " + in, nil
		},
	}
}
