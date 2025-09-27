# Intentive - Tool-First AI Orchestration  
# Self-documenting Makefile for build, test, and training workflow

# Colors for output
CYAN := \033[36m
GREEN := \033[32m
YELLOW := \033[33m
RED := \033[31m
RESET := \033[0m

# .NET paths
DOTNET := /usr/local/share/dotnet/dotnet

# Default target - show help
.DEFAULT_GOAL := help

.PHONY: help
help: ## Show available make targets with descriptions
	@echo "$(CYAN)🚀 Intentive - Tool-First AI Orchestration$(RESET)"
	@echo ""
	@echo "$(GREEN)Available targets:$(RESET)"
	@awk 'BEGIN {FS = ":.*##"; printf ""} /^[a-zA-Z_-]+:.*?##/ { printf "  $(CYAN)%-20s$(RESET) %s\n", $$1, $$2 } /^##@/ { printf "\n$(YELLOW)%s$(RESET)\n", substr($$0, 5) }' $(MAKEFILE_LIST)
	@echo ""

##@ Build Commands
.PHONY: restore
restore: ## Restore NuGet packages for all projects
	@echo "$(CYAN)📦 Restoring NuGet packages...$(RESET)"
	$(DOTNET) restore Intentive.sln

.PHONY: build
build: restore ## Build the entire solution
	@echo "$(CYAN)🔨 Building solution...$(RESET)"
	$(DOTNET) build Intentive.sln

.PHONY: clean
clean: ## Remove all build artifacts
	@echo "$(CYAN)🧹 Cleaning build artifacts...$(RESET)"
	$(DOTNET) clean Intentive.sln
	@find . -name "bin" -type d -exec rm -rf {} + 2>/dev/null || true
	@find . -name "obj" -type d -exec rm -rf {} + 2>/dev/null || true

.PHONY: rebuild
rebuild: clean build ## Clean and build from scratch

##@ Testing Commands
.PHONY: test
test: check-llm-env build ## Run all tests with detailed output showing LLM responses
	@echo "$(CYAN)🧪 Running tests with LLM connectivity validation...$(RESET)"
	@echo ""
	$(DOTNET) test --logger "console;verbosity=detailed" --verbosity normal
	@echo ""
	@echo "$(GREEN)✅ Tests completed! LLM responses shown above confirm connectivity.$(RESET)"

.PHONY: test-fast
test-fast: build ## Run tests with minimal output (fast)
	@echo "$(CYAN)🧪 Running tests (fast mode)...$(RESET)"
	$(DOTNET) test --verbosity minimal

.PHONY: test-llm
test-llm: check-llm-env build ## Run only LLM integration tests with full output
	@echo "$(CYAN)🤖 Testing LLM connectivity...$(RESET)"
	@echo ""
	$(DOTNET) test --filter "FullyQualifiedName~LLM" --logger "console;verbosity=detailed" --verbosity normal
	@echo ""
	@echo "$(GREEN)✅ LLM connectivity tests completed!$(RESET)"

##@ Development Commands
.PHONY: run
run: build ## Run console app with clean output (logs to intentive.log)
	@rm -f intentive.log
	@echo "$(CYAN)🚀 Starting Intentive - Tool-First AI Orchestration...$(RESET)"
	$(DOTNET) run --project src/Intentive.Console

.PHONY: run-quiet
run-quiet: build ## Run console app with minimal logging (clean output)
	@echo "$(CYAN)🚀 Starting Intentive (quiet mode)...$(RESET)"
	$(DOTNET) run --project src/Intentive.Console -- --mode intentive --enable-logging false

.PHONY: run-log
run-log: run ## Alias: run with logging to file (default)

.PHONY: run-verbose
run-verbose: build ## Run console app with verbose logs in console + file
	@echo "$(CYAN)🚀 Starting Intentive (verbose console logs)...$(RESET)"
	$(DOTNET) run --project src/Intentive.Console -- --mode intentive --console-logs

.PHONY: train-tools
train-tools: build ## Train ONNX model from discovered tools (MCP + local)
	@echo "$(CYAN)🎯 Training tool classification model...$(RESET)"
	$(DOTNET) run --project src/Intentive.Console -- --train-tools

.PHONY: train-tools-custom
train-tools-custom: build ## Train with custom parameters (examples=300, model=custom.onnx)
	@echo "$(CYAN)🎯 Training with custom parameters...$(RESET)"
	$(DOTNET) run --project src/Intentive.Console -- --train-tools --examples 300 --model models/custom-intentive.onnx

##@ Environment Validation
.PHONY: check-env
check-env: ## Check development environment setup
	@echo "$(CYAN)🔍 Checking development environment...$(RESET)"
	@echo "$(YELLOW)• .NET SDK:$(RESET) $$($(DOTNET) --version)"
	@echo "$(YELLOW)• PowerShell:$(RESET) $$(pwsh --version 2>/dev/null || echo 'Not installed')"
	@echo "$(YELLOW)• Operating System:$(RESET) $$(uname -s) $$(uname -r)"
	@echo ""

.PHONY: check-llm-env
check-llm-env: ## Validate LLM API configuration
	@echo "$(CYAN)🔍 Validating LLM API configuration...$(RESET)"
	@if [ -z "$$OPENAI_API_KEY" ]; then \
		echo "$(RED)❌ OPENAI_API_KEY not set$(RESET)"; \
		echo "$(YELLOW)💡 Tip: Run 'Initialize-OpenApiKey' in PowerShell or export manually$(RESET)"; \
		exit 1; \
	else \
		echo "$(GREEN)✅ OPENAI_API_KEY is set$(RESET) ($(shell echo $$OPENAI_API_KEY | cut -c1-8)...)"; \
	fi
	@if [ -n "$$OPENAI_BASE_URL" ]; then \
		echo "$(GREEN)✅ OPENAI_BASE_URL:$(RESET) $$OPENAI_BASE_URL"; \
		if echo "$$OPENAI_BASE_URL" | grep -q "groq.com"; then \
			echo "$(YELLOW)⚡ Using Groq for fast inference$(RESET)"; \
		fi; \
	else \
		echo "$(YELLOW)ℹ️  OPENAI_BASE_URL not set (will use OpenAI default)$(RESET)"; \
	fi
	@echo ""

.PHONY: init-groq
init-groq: ## Initialize environment for Groq API (PowerShell function)
	@echo "$(CYAN)🔧 Groq API Setup Instructions:$(RESET)"
	@echo "$(YELLOW)Run this in PowerShell:$(RESET)"
	@echo "  Initialize-OpenApiKey"
	@echo ""
	@echo "$(YELLOW)Or set manually:$(RESET)"
	@echo "  $$env:OPENAI_API_KEY=\"your-groq-key\""
	@echo "  $$env:OPENAI_BASE_URL=\"https://api.groq.com/openai/v1\""
	@echo ""

##@ Confidence Testing
.PHONY: confidence
confidence: check-llm-env test ## Full confidence check - environment + LLM connectivity
	@echo ""
	@echo "$(GREEN)🎉 CONFIDENCE CHECK COMPLETE! 🎉$(RESET)"
	@echo "$(GREEN)✅ Environment configured correctly$(RESET)"
	@echo "$(GREEN)✅ LLM API connectivity working$(RESET)"
	@echo "$(GREEN)✅ All tests passing$(RESET)"
	@echo ""
	@echo "$(CYAN)Ready for development! 🚀$(RESET)"

.PHONY: smoke
smoke: check-llm-env ## Quick smoke test - verify basic LLM connectivity
	@echo "$(CYAN)💨 Running smoke test...$(RESET)"
	@echo ""
	$(DOTNET) test --filter "FullyQualifiedName~BasicConfiguration" --verbosity minimal
	@echo ""
	@echo "$(GREEN)✅ Smoke test passed - basic connectivity OK$(RESET)"

##@ Utility Commands
.PHONY: watch
watch: ## Run tests in watch mode for development
	@echo "$(CYAN)👀 Running tests in watch mode...$(RESET)"
	$(DOTNET) watch test --project tests/Intentive.Tests

.PHONY: info
info: check-env check-llm-env ## Show complete project and environment information
	@echo "$(CYAN)📋 Project Information:$(RESET)"
	@echo "$(YELLOW)• Project:$(RESET) Intentive - Tool-First AI Orchestration"
	@echo "$(YELLOW)• Architecture:$(RESET) Rule Gate → ONNX Tool Classification → Direct Execution OR LLM Escalation"
	@echo "$(YELLOW)• Solution:$(RESET) $$(find . -name "*.sln" | head -1)"
	@echo "$(YELLOW)• Test Projects:$(RESET) $$(find . -name "*Tests.csproj" | wc -l | tr -d ' ')"
	@echo ""

##@ Docker Commands
# Docker image configuration
DOCKER := /usr/local/bin/docker
DOCKER_REGISTRY := ghcr.io
DOCKER_NAMESPACE := katasec
DOCKER_IMAGE := intentive
DOCKER_TAG ?= latest
DOCKER_FULL_NAME := $(DOCKER_REGISTRY)/$(DOCKER_NAMESPACE)/$(DOCKER_IMAGE):$(DOCKER_TAG)

.PHONY: docker-build
docker-build: ## Build Docker image for GitHub Container Registry
	@echo "$(CYAN)🐳 Building Docker image: $(DOCKER_FULL_NAME)$(RESET)"
	$(DOCKER) build -t $(DOCKER_FULL_NAME) -t $(DOCKER_IMAGE):latest .

.PHONY: docker-run
docker-run: ## Run Docker container with environment variables
	@echo "$(CYAN)🚀 Running Docker container: $(DOCKER_FULL_NAME)$(RESET)"
	$(DOCKER) run -it --rm \
		-e OPENAI_API_KEY="$$OPENAI_API_KEY" \
		-e OPENAI_BASE_URL="$$OPENAI_BASE_URL" \
		$(DOCKER_FULL_NAME) --mode intentive

.PHONY: docker-push
docker-push: docker-build ## Push Docker image to GitHub Container Registry
	@echo "$(CYAN)📤 Pushing Docker image to GHCR...$(RESET)"
	@echo "$(YELLOW)Make sure you're logged in: $(DOCKER) login ghcr.io$(RESET)"
	$(DOCKER) push $(DOCKER_FULL_NAME)

.PHONY: docker-pull
docker-pull: ## Pull Docker image from GitHub Container Registry
	@echo "$(CYAN)📥 Pulling Docker image from GHCR...$(RESET)"
	$(DOCKER) pull $(DOCKER_FULL_NAME)

.PHONY: docker-size
docker-size: ## Show Docker image size
	@echo "$(CYAN)📊 Docker image sizes:$(RESET)"
	@$(DOCKER) images $(DOCKER_FULL_NAME) --format "table {{.Repository}}\t{{.Tag}}\t{{.Size}}" 2>/dev/null || echo "$(YELLOW)Image not found locally. Run 'make docker-build' first.$(RESET)"
	@$(DOCKER) images $(DOCKER_IMAGE):latest --format "table {{.Repository}}\t{{.Tag}}\t{{.Size}}" 2>/dev/null || true

.PHONY: docker-login
docker-login: ## Login to GitHub Container Registry
	@echo "$(CYAN)🔑 Logging into GitHub Container Registry...$(RESET)"
	@echo "$(YELLOW)Enter your GitHub Personal Access Token when prompted$(RESET)"
	$(DOCKER) login ghcr.io

.PHONY: docker-info
docker-info: ## Show Docker configuration
	@echo "$(CYAN)🐳 Docker Configuration:$(RESET)"
	@echo "$(YELLOW)• Full Image Name:$(RESET) $(DOCKER_FULL_NAME)"
	@echo "$(YELLOW)• Registry:$(RESET) $(DOCKER_REGISTRY)"
	@echo "$(YELLOW)• Namespace:$(RESET) $(DOCKER_NAMESPACE)"
	@echo "$(YELLOW)• Image:$(RESET) $(DOCKER_IMAGE)"
	@echo "$(YELLOW)• Tag:$(RESET) $(DOCKER_TAG)"
	@echo ""
	@echo "$(CYAN)📋 Available Docker Commands:$(RESET)"
	@echo "  make docker-build    # Build image locally"
	@echo "  make docker-push     # Push to GHCR"
	@echo "  make docker-pull     # Pull from GHCR"
	@echo "  make docker-run      # Run container"
	@echo "  make docker-login    # Login to GHCR"
