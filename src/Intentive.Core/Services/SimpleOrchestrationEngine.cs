using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Intentive.Core.Configuration;
using Intentive.Core.Models;

namespace Intentive.Core.Services;

/// <summary>
/// Simple orchestration engine with clean 3-step process:
/// 1. Rule Gate (fast patterns)
/// 2. ONNX Tool Classification 
/// 3. Direct Tool Execution OR LLM Escalation
/// 
/// Replaces the complex 616-line IntentiveOrchestrationPlugin
/// </summary>
public class SimpleOrchestrationEngine
{
    private readonly ILogger _logger;
    private readonly OrchestrationConfig _config;
    private readonly ToolRegistry _toolRegistry;
    private readonly SimpleIntentClassifier _classifier;
    private readonly Kernel _kernel;
    private readonly Stopwatch _stopwatch;

    public SimpleOrchestrationEngine(
        ILogger logger,
        OrchestrationConfig config,
        ToolRegistry toolRegistry,
        SimpleIntentClassifier classifier,
        Kernel kernel)
    {
        _logger = logger;
        _config = config;
        _toolRegistry = toolRegistry;
        _classifier = classifier;
        _kernel = kernel;
        _stopwatch = new Stopwatch();
    }

    /// <summary>
    /// Main orchestration method - simple 3-step process
    /// </summary>
    public async Task<SimpleOrchestrationResult> HandleAsync(string userInput)
    {
        _stopwatch.Restart();
        var executionPath = new List<string>();

        try
        {
            _logger.LogInformation("🔍 Processing: {Input}", userInput);
            
            // STEP 1: Rule Gate - Fast path for simple patterns
            var ruleGateResult = ProcessRuleGate(userInput);
            executionPath.Add("RuleGate");

            if (!string.IsNullOrEmpty(ruleGateResult))
            {
                _stopwatch.Stop();
                _logger.LogInformation("⚡ Rule gate handled request ({ElapsedMs}ms)", _stopwatch.ElapsedMilliseconds);
                
                return new SimpleOrchestrationResult
                {
                    Response = ruleGateResult,
                    ExecutionPath = string.Join(" → ", executionPath),
                    ExecutionTimeMs = _stopwatch.Elapsed.TotalMilliseconds,
                    UsedLLM = false,
                    ToolUsed = null
                };
            }

            // STEP 2: ONNX Classification - Find best tool
            var toolClassification = await _classifier.FindToolForAsync(userInput);
            executionPath.Add("ToolClassification");
            
            _logger.LogInformation("🎯 Classification: {Tool} (confidence: {Confidence})", 
                toolClassification.ToolName, toolClassification.Confidence);

            // STEP 3A: Direct Tool Execution (high confidence)
            if (toolClassification.Confidence > 0.7 && !string.IsNullOrEmpty(toolClassification.ToolName))
            {
                executionPath.Add("DirectToolExecution");
                
                var toolResult = await ExecuteToolDirectlyAsync(toolClassification.ToolName, userInput);
                _stopwatch.Stop();
                
                _logger.LogInformation("✅ Direct tool execution ({ElapsedMs}ms): {Tool}", 
                    _stopwatch.ElapsedMilliseconds, toolClassification.ToolName);

                return new SimpleOrchestrationResult
                {
                    Response = toolResult,
                    ExecutionPath = string.Join(" → ", executionPath),
                    ExecutionTimeMs = _stopwatch.Elapsed.TotalMilliseconds,
                    UsedLLM = false,
                    ToolUsed = toolClassification.ToolName
                };
            }

            // STEP 3B: LLM Escalation (low confidence or unknown)
            executionPath.Add("LLMEscalation");
            
            var llmResult = await EscalateToLLMAsync(userInput, toolClassification);
            _stopwatch.Stop();
            
            _logger.LogInformation("🤖 LLM escalation completed ({ElapsedMs}ms)", _stopwatch.ElapsedMilliseconds);

            return new SimpleOrchestrationResult
            {
                Response = llmResult,
                ExecutionPath = string.Join(" → ", executionPath),
                ExecutionTimeMs = _stopwatch.Elapsed.TotalMilliseconds,
                UsedLLM = true,
                ToolUsed = null
            };
        }
        catch (Exception ex)
        {
            _stopwatch.Stop();
            _logger.LogError(ex, "❌ Orchestration failed for input: {Input}", userInput);
            
            return new SimpleOrchestrationResult
            {
                Response = $"❌ Sorry, I encountered an error processing your request: {ex.Message}",
                ExecutionPath = string.Join(" → ", executionPath) + " → Error",
                ExecutionTimeMs = _stopwatch.Elapsed.TotalMilliseconds,
                UsedLLM = false,
                ToolUsed = null
            };
        }
    }

    /// <summary>
    /// STEP 1: Rule Gate - Handle simple patterns immediately
    /// </summary>
    private string? ProcessRuleGate(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "Please provide a request.";

        if (input.Length > 2000)
            return "Request too long. Please keep it under 2000 characters.";

        var lowerInput = input.ToLower().Trim();

        // Fast path for greetings
        if (lowerInput is "hi" or "hello" or "hey" or "greetings")
        {
            var capabilities = _toolRegistry.GetAvailableCapabilities();
            return $"👋 Hello! I can help with: {string.Join(", ", capabilities)}.\n\nTry asking me about weather, time, calculations, or orders!";
        }

        // Fast path for help
        if (lowerInput is "help" or "h" or "?" or "what can you do")
        {
            var capabilities = _toolRegistry.GetAvailableCapabilities();
            return $"🤖 Here's what I can do:\n\n" +
                   $"📋 Available capabilities: {string.Join(", ", capabilities)}\n\n" +
                   $"💡 Try these examples:\n" +
                   $"   • \"What's the weather in Paris?\"\n" +
                   $"   • \"What time is it?\"\n" +
                   $"   • \"Calculate 15 * 23\"\n" +
                   $"   • \"Order status 12345\"\n\n" +
                   $"⚡ For simple requests, I execute tools directly (~10ms)\n" +
                   $"🤖 For complex requests, I escalate to AI (~500ms)";
        }

        // No fast path match
        return null;
    }

    /// <summary>
    /// STEP 3A: Execute tool directly with high confidence
    /// </summary>
    private async Task<string> ExecuteToolDirectlyAsync(string toolName, string userInput)
    {
        try
        {
            // Extract parameters from user input for the tool
            var parameters = ExtractParametersForTool(toolName, userInput);
            
            // Execute the tool via registry
            var result = await _toolRegistry.ExecuteToolAsync(toolName, userInput, parameters);
            
            return FormatToolResult(result, toolName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Direct tool execution failed: {ToolName}", toolName);
            return $"❌ Error executing {toolName}: {ex.Message}";
        }
    }

    /// <summary>
    /// STEP 3B: Escalate to LLM for complex/unknown requests
    /// </summary>
    private async Task<string> EscalateToLLMAsync(string userInput, ToolClassificationResult classification)
    {
        try
        {
            var availableCapabilities = _toolRegistry.GetAvailableCapabilities();
            var escalationReason = classification.Confidence < 0.7 ? 
                "low confidence in tool classification" : 
                "no suitable tool found";
            
            var prompt = $@"User question: '{userInput}'
Available tools: {string.Join(", ", availableCapabilities)}

RULE: Keep your response under 50 words maximum.

For math questions: Just give the answer (e.g., '4' for '2+2')
For requests I can't handle: Briefly explain I don't have that capability
For general questions: Answer concisely

Respond now in under 50 words:";

            var result = await _kernel.InvokePromptAsync(prompt);
            var response = result.ToString().Trim();
            
            _logger.LogInformation("🤖 LLM response received: '{Response}' (length: {Length})", 
                response.Length > 100 ? response.Substring(0, 100) + "..." : response, response.Length);
            
            if (string.IsNullOrEmpty(response))
            {
                _logger.LogWarning("⚠️ LLM returned empty response");
                return "I received your question but couldn't generate a proper response. Please try rephrasing.";
            }
            
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ LLM escalation failed");
            return "❌ I'm having trouble processing your request right now. Please try rephrasing or ask for help to see what I can do.";
        }
    }

    /// <summary>
    /// Extract parameters from user input for specific tools
    /// </summary>
    private Dictionary<string, object> ExtractParametersForTool(string toolName, string userInput)
    {
        var parameters = new Dictionary<string, object>();
        var lowerInput = userInput.ToLower();

        // Parameter extraction based on tool type
        // This is heuristic-based - could be enhanced with NLP
        
        if (toolName.Contains("weather"))
        {
            var location = ExtractLocation(userInput);
            if (!string.IsNullOrEmpty(location))
                parameters["location"] = location;
        }
        else if (toolName.Contains("calculator"))
        {
            var expression = ExtractMathExpression(userInput);
            if (!string.IsNullOrEmpty(expression))
                parameters["expression"] = expression;
        }
        else if (toolName.Contains("GetOrder") || toolName.Contains("order"))
        {
            var orderId = ExtractOrderId(userInput);
            if (!string.IsNullOrEmpty(orderId))
                parameters["orderId"] = orderId;
        }

        return parameters;
    }

    /// <summary>
    /// Extract location from user input
    /// </summary>
    private string ExtractLocation(string input)
    {
        // Simple location extraction - could be enhanced
        var words = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var inIndex = Array.FindIndex(words, w => w.Equals("in", StringComparison.OrdinalIgnoreCase));
        
        if (inIndex >= 0 && inIndex < words.Length - 1)
        {
            return string.Join(" ", words.Skip(inIndex + 1)).Trim('?', '.', ',');
        }

        // Look for common cities
        var commonCities = new[] { "paris", "london", "tokyo", "new york", "san francisco", "los angeles", "chicago", "seattle" };
        foreach (var city in commonCities)
        {
            if (input.Contains(city, StringComparison.OrdinalIgnoreCase))
                return city;
        }

        return "current location";
    }

    /// <summary>
    /// Extract mathematical expression from user input
    /// </summary>
    private string ExtractMathExpression(string input)
    {
        // Look for mathematical patterns
        var mathPattern = @"(\d+(?:\.\d+)?)\s*([+\-*/])\s*(\d+(?:\.\d+)?)";
        var match = System.Text.RegularExpressions.Regex.Match(input, mathPattern);
        
        if (match.Success)
        {
            return $"{match.Groups[1].Value} {match.Groups[2].Value} {match.Groups[3].Value}";
        }

        // Look for word-based math
        var words = input.ToLower();
        if (words.Contains("plus") || words.Contains("add"))
        {
            var numbers = ExtractNumbers(input);
            if (numbers.Count >= 2)
                return $"{numbers[0]} + {numbers[1]}";
        }
        else if (words.Contains("minus") || words.Contains("subtract"))
        {
            var numbers = ExtractNumbers(input);
            if (numbers.Count >= 2)
                return $"{numbers[0]} - {numbers[1]}";
        }
        else if (words.Contains("times") || words.Contains("multiply"))
        {
            var numbers = ExtractNumbers(input);
            if (numbers.Count >= 2)
                return $"{numbers[0]} * {numbers[1]}";
        }

        return input; // Return original if can't parse
    }

    /// <summary>
    /// Extract order ID from user input
    /// </summary>
    private string ExtractOrderId(string input)
    {
        // Look for numeric patterns that could be order IDs
        var orderPattern = @"\b(\d{4,8})\b";
        var match = System.Text.RegularExpressions.Regex.Match(input, orderPattern);
        
        if (match.Success)
        {
            return match.Groups[1].Value;
        }

        return ""; // No order ID found
    }

    /// <summary>
    /// Extract numbers from text
    /// </summary>
    private List<string> ExtractNumbers(string input)
    {
        var numberPattern = @"\d+(?:\.\d+)?";
        var matches = System.Text.RegularExpressions.Regex.Matches(input, numberPattern);
        return matches.Select(m => m.Value).ToList();
    }

    /// <summary>
    /// Format tool execution result for user display
    /// </summary>
    private string FormatToolResult(string result, string toolName)
    {
        // Add emojis and formatting based on tool type
        var prefix = toolName.ToLower() switch
        {
            var t when t.Contains("weather") => "🌤️",
            var t when t.Contains("time") => "📅",
            var t when t.Contains("calculator") => "🧮",
            var t when t.Contains("order") => "📦",
            _ => "⚡"
        };

        return $"{prefix} {result}";
    }

    /// <summary>
    /// Get available capabilities for display
    /// </summary>
    public List<string> GetAvailableCapabilities()
    {
        return _toolRegistry.GetAvailableCapabilities();
    }
}