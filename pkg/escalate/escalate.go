package escalate

import (
	"context"

	"github.com/katasec/intentive/pkg/router"
)

// StubEscalator is a no-op LLM fallback used in v1.
type StubEscalator struct{}

// NewStubEscalator returns a stub escalator.
func NewStubEscalator() *StubEscalator { return &StubEscalator{} }

// Ensure StubEscalator implements router.Escalator.
var _ router.Escalator = (*StubEscalator)(nil)

func (s *StubEscalator) Fallback(ctx context.Context, in string) (string, error) {
	return "(stub-llm) " + in, nil
}
