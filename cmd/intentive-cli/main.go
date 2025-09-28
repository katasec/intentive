package main

import (
	"bufio"
	"context"
	"flag"
	"fmt"
	"os"
	"os/signal"
	"strings"
	"syscall"

	"github.com/katasec/intentive/pkg/escalate"
	"github.com/katasec/intentive/pkg/miniembed"
	"github.com/katasec/intentive/pkg/router"
	"github.com/katasec/intentive/pkg/tool"
)

func main() {
	var verbose bool
	flag.BoolVar(&verbose, "v", false, "verbose: show routing metadata")
	flag.Parse()

	// Wire up dependencies for the router.
	reg := tool.NewRegistry()
	reg.Register(tool.EchoTool()) // simple local tool
	emb := miniembed.NewStubEmbedder()
	esc := escalate.NewStubEscalator() // LLM fallback

	// Tune thresholds so our stub embedder (score=0.20) triggers escalation for unknown tools.
	cfg := router.Config{
		TauLocalHigh: 0.60,
		TauMcpHigh:   0.60,
		TauLow:       0.30, // 0.20 < 0.30 → escalate when no tool matches the hint
		Epsilon:      0.05,
	}

	r := router.New(reg, emb, esc, cfg)

	// Handle Ctrl+C and SIGTERM cleanly.
	sigc := make(chan os.Signal, 1)
	signal.Notify(sigc, os.Interrupt, syscall.SIGTERM)
	defer signal.Stop(sigc)

	go func() {
		<-sigc
		fmt.Println()
		os.Exit(0)
	}()

	sc := bufio.NewScanner(os.Stdin)
	fmt.Println("Intentive CLI — type your query. Press Ctrl+C to exit.")

	for {
		fmt.Print("> ")
		if !sc.Scan() {
			if err := sc.Err(); err != nil {
				fmt.Fprintln(os.Stderr, "read error:", err)
			}
			return
		}
		line := strings.TrimSpace(sc.Text())
		if line == "" {
			continue
		}

		ctx := context.Background()
		out, meta, err := r.Route(ctx, line)
		if err != nil {
			fmt.Fprintln(os.Stderr, "route error:", err)
			continue
		}

		fmt.Println(out)
		if verbose {
			fmt.Printf("path=%s, tool=%s, score=%.2f, reason=%s\n", meta.Path, meta.ToolID, meta.Score, meta.Reason)
		}
	}
}
