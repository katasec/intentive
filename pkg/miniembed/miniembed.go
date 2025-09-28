package miniembed

// Embedder provides vector embeddings.
type Embedder interface {
	Embed(text string) ([]float32, error)
}

// StubEmbedder is a no-op implementation for wiring.
type StubEmbedder struct{}

// NewStubEmbedder returns a new stub embedder.
func NewStubEmbedder() *StubEmbedder {
	return &StubEmbedder{}
}

// Embed returns a fixed vector for deterministic behavior.
func (s *StubEmbedder) Embed(text string) ([]float32, error) {
	return []float32{0.5, 0.5}, nil
}
