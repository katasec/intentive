using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Intentive.Core.Configuration;
using Intentive.Core.Models;
using Intentive.Core.Services;

namespace Intentive.Core.Plugins;

/// <summary>
/// Intentive orchestration plugin - deterministic-first approach
/// RuleGate → ONNX Classification → Validator → Tools → LLM escalation
/// </summary>
public class IntentiveOrchestrationPlugin
{
    private readonly ILogger<IntentiveOrchestrationPlugin> _logger;
    private readonly OrchestrationConfig _config;
    private readonly Stopwatch _stopwatch;
    private readonly SimpleIntentClassifier _intentClassifier;

    public IntentiveOrchestrationPlugin(
        ILogger<IntentiveOrchestrationPlugin> logger,
        OrchestrationConfig config)
    {
        _logger = logger;
        _config = config;
        _stopwatch = new Stopwatch();
        
        // Initialize ONNX intent classifier
        _intentClassifier = new SimpleIntentClassifier(logger, _config.Intent);
    }

    /// <summary>
    /// Main orchestration function using deterministic-first approach
    /// </summary>
    [KernelFunction("orchestrate_intentive")]
    [Description("Orchestrate user requests using deterministic-first approach with MiniLM ONNX intent classification")]
    public async Task<string> OrchestateAsync(
        [Description("User input text")] string input,
        Kernel kernel,
        CancellationToken cancellationToken = default)
    {
        _stopwatch.Restart();
        var executionPath = new List<string>();

        try
        {
            _logger.LogInformation("Starting Intentive orchestration for input: {Input}", input);
            
            // Step 1: Rule Gate - Heuristic filtering
            var ruleGateResult = ProcessRuleGate(input);
            executionPath.Add("RuleGate");
            
            if (!ruleGateResult.ShouldContinue)
            {
                _logger.LogInformation("Rule gate rejected input: {Reason}", ruleGateResult.RejectReason);
                return CreateResponse(ruleGateResult.FastPathResponse ?? "Request rejected", executionPath, requiresEscalation: false);
            }

            if (!string.IsNullOrEmpty(ruleGateResult.FastPathResponse))
            {
                _logger.LogInformation("Rule gate provided fast path response");
                return CreateResponse(ruleGateResult.FastPathResponse, executionPath, requiresEscalation: false);
            }

            // Step 2: ONNX Intent Classification
            var classification = await ClassifyIntentAsync(input, cancellationToken);
            executionPath.Add("MiniLMClassifier");
            
            _logger.LogInformation("Intent classified: {TaskType}, Ambiguity: {Ambiguity}, Risk: {Risk}", 
                classification.TaskType, classification.AmbiguityScore, classification.RiskScore);

            // Step 3: Check for immediate escalation
            if (classification.RiskScore > _config.Intent.RiskThreshold || 
                classification.AmbiguityScore > _config.Intent.AmbiguityThreshold)
            {
                _logger.LogInformation("Escalating due to high risk/ambiguity");
                var escalatedPlan = await EscalateToLLMAsync(input, classification, kernel, cancellationToken);
                executionPath.Add("LLMEscalation");
                return await ExecutePlanAsync(escalatedPlan, executionPath, kernel, cancellationToken);
            }

            // Step 4: Generate plan with cheap LM probe
            var plan = await GeneratePlanAsync(input, classification, kernel, cancellationToken);
            executionPath.Add("CheapLMProbe");

            // Step 5: Validate plan
            var validationResult = ValidatePlan(plan);
            executionPath.Add("PlanValidator");

            if (!validationResult.IsValid)
            {
                _logger.LogWarning("Plan validation failed: {Errors}", string.Join(", ", validationResult.Errors));
                
                // Attempt escalation for plan repair
                var repairedPlan = await EscalateToLLMAsync(input, classification, kernel, cancellationToken);
                executionPath.Add("LLMRepair");
                return await ExecutePlanAsync(repairedPlan, executionPath, kernel, cancellationToken);
            }

            // Step 6: Execute validated plan
            return await ExecutePlanAsync(plan, executionPath, kernel, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during Intentive orchestration");
            executionPath.Add("Error");
            return CreateResponse($"An error occurred: {ex.Message}", executionPath, requiresEscalation: false);
        }
        finally
        {
            _stopwatch.Stop();
            _logger.LogInformation("Intentive orchestration completed in {ElapsedMs}ms", _stopwatch.ElapsedMilliseconds);
        }
    }

    private RuleGateResult ProcessRuleGate(string input)
    {
        // Basic rule-based filtering (can be expanded)
        if (string.IsNullOrWhiteSpace(input))
        {
            return new RuleGateResult(false, RejectReason: "Empty input");
        }

        if (input.Length > 2000)
        {
            return new RuleGateResult(false, RejectReason: "Input too long");
        }

        // Fast path for simple greetings
        var lowerInput = input.ToLower().Trim();
        if (lowerInput is "hi" or "hello" or "hey")
        {
            return new RuleGateResult(true, FastPathResponse: "Hello! How can I help you today?");
        }

        // Continue processing
        return new RuleGateResult(true);
    }

    private async Task<ClassificationResult> ClassifyIntentAsync(string input, CancellationToken cancellationToken)
    {
        return await _intentClassifier.ClassifyAsync(input, cancellationToken);
    }

    private async Task<Plan> GeneratePlanAsync(string input, ClassificationResult classification, Kernel kernel, CancellationToken cancellationToken)
    {
        // For order status queries, create a deterministic plan
        if (classification.TaskType == "OrderStatus")
        {
            // Extract order ID using regex or simple parsing
            var orderIdMatch = System.Text.RegularExpressions.Regex.Match(input, @"\b\d{4,6}\b");
            var orderId = orderIdMatch.Success ? orderIdMatch.Value : "unknown";

            return new Plan(
                Intent: "Get order status",
                Steps: new List<PlanStep>
                {
                    new("GetOrder", new Dictionary<string, object> { ["orderId"] = orderId })
                },
                Confidence: 0.9,
                ExecutionPath: "Deterministic"
            );
        }

        // For other intents, fall back to LLM planning
        return await EscalateToLLMAsync(input, classification, kernel, cancellationToken);
    }

    private ValidationResult ValidatePlan(Plan plan)
    {
        var errors = new List<string>();

        if (string.IsNullOrEmpty(plan.Intent))
        {
            errors.Add("Plan must have an intent");
        }

        if (plan.Steps.Count == 0)
        {
            errors.Add("Plan must have at least one step");
        }

        foreach (var step in plan.Steps)
        {
            if (string.IsNullOrEmpty(step.Tool))
            {
                errors.Add("Each step must specify a tool");
            }

            // Validate known tools
            if (step.Tool == "GetOrder" && !step.Parameters.ContainsKey("orderId"))
            {
                errors.Add("GetOrder tool requires 'orderId' parameter");
            }
        }

        return new ValidationResult(
            IsValid: errors.Count == 0,
            Errors: errors,
            ConfidenceScore: plan.Confidence
        );
    }

    private async Task<Plan> EscalateToLLMAsync(string input, ClassificationResult classification, Kernel kernel, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Escalating to LLM for complex planning");

        // Use SK's built-in function calling to generate a plan
        var prompt = $@"Create a plan to handle this user request: ""{input}""
        
        The user's intent appears to be: {classification.TaskType}
        Available tools: GetOrder (requires orderId parameter - for looking up order status)
        
        IMPORTANT: 
        - Respond with ONLY valid JSON. No comments, no explanations, no markdown formatting.
        - ONLY use GetOrder tool if the user is asking about a specific order status and provides an order ID
        - For requests about dates, weather, general questions, cooking, etc. use empty Steps array: ""Steps"": []
        - Do NOT force tools to be used when they don't match the request
        
        Use this exact format:
        {{
            ""Intent"": ""description of what the user wants"",
            ""Steps"": [
                {{""Tool"": ""GetOrder"", ""Parameters"": {{""orderId"": ""12345""}}}}
            ],
            ""Confidence"": 0.8
        }}
        
        If no suitable tool is available for this specific request, use empty Steps array: ""Steps"": []";

        try
        {
            var result = await kernel.InvokePromptAsync(prompt, cancellationToken: cancellationToken);
            var rawResponse = result.ToString();
            
            _logger.LogInformation("Raw LLM Response (length: {Length}): {Response}", rawResponse.Length, rawResponse);
            
            // Extract JSON from common LLM response formats
            var cleanJson = ExtractJsonFromResponse(rawResponse);
            
            _logger.LogInformation("Extracted JSON: {CleanJson}", cleanJson);
            
            // Parse the JSON response
            var planData = JsonSerializer.Deserialize<JsonElement>(cleanJson);
            
            return new Plan(
                Intent: planData.GetProperty("Intent").GetString() ?? "Unknown",
                Steps: ParseStepsFromJson(planData.GetProperty("Steps")),
                Confidence: planData.GetProperty("Confidence").GetDouble(),
                ExecutionPath: "LLMGenerated"
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM escalation failed, using fallback plan");
            
            // Fallback to a basic plan
            return new Plan(
                Intent: "Handle user request",
                Steps: new List<PlanStep>(),
                Confidence: 0.1,
                ExecutionPath: "Fallback"
            );
        }
    }

    private string ExtractJsonFromResponse(string response)
    {
        // Handle markdown-formatted JSON (```json...```)
        if (response.Contains("```json"))
        {
            var startIndex = response.IndexOf("```json") + 7; // 7 = length of "```json"
            var endIndex = response.IndexOf("```", startIndex);
            
            if (endIndex > startIndex)
            {
                return response.Substring(startIndex, endIndex - startIndex).Trim();
            }
        }
        
        // Handle simple code blocks with backticks
        if (response.StartsWith("```") && response.EndsWith("```"))
        {
            return response.Substring(3, response.Length - 6).Trim();
        }
        
        // Return as-is if no markdown formatting
        return response.Trim();
    }

    private List<PlanStep> ParseStepsFromJson(JsonElement stepsElement)
    {
        var steps = new List<PlanStep>();
        
        foreach (var stepElement in stepsElement.EnumerateArray())
        {
            // Handle different JSON formats the LLM might return
            if (stepElement.TryGetProperty("Tool", out var toolProperty))
            {
                // Standard format: {"Tool": "GetOrder", "Parameters": {...}}
                var tool = toolProperty.GetString() ?? "";
                var parameters = new Dictionary<string, object>();
                
                if (stepElement.TryGetProperty("Parameters", out var paramsElement))
                {
                    foreach (var param in paramsElement.EnumerateObject())
                    {
                        parameters[param.Name] = param.Value.ToString() ?? "";
                    }
                }
                
                steps.Add(new PlanStep(tool, parameters));
            }
            else if (stepElement.TryGetProperty("Error", out var errorProperty))
            {
                // Error format: {"Error": "No suitable tool available", "Confidence": 1}
                // Skip error entries - they indicate no actionable steps
                _logger.LogInformation("LLM indicated no suitable tool: {Error}", errorProperty.GetString());
                continue;
            }
            else
            {
                // Unknown format - log and skip
                _logger.LogWarning("Unknown step format in LLM response: {Step}", stepElement.ToString());
                continue;
            }
        }
        
        return steps;
    }

    private async Task<string> ExecutePlanAsync(Plan plan, List<string> executionPath, Kernel kernel, CancellationToken cancellationToken)
    {
        // Check indicators for response quality before execution
        var qualityIndicators = EvaluateResponseQuality(plan);
        
        if (qualityIndicators.RequiresEscalation)
        {
            executionPath.Add("QualityEscalation");
            _logger.LogInformation("Quality indicators triggered escalation: {Reasons}", string.Join(", ", qualityIndicators.Reasons));
            
            var directResponse = await GetDirectLLMResponseAsync(plan.Intent, kernel, cancellationToken);
            
            // Check if the direct response is also insufficient and needs re-escalation
            var responseQuality = EvaluateResponseContent(directResponse, plan.Intent);
            if (responseQuality.RequiresEscalation)
            {
                executionPath.Add("ResponseRefinement");
                _logger.LogInformation("Response quality insufficient, refining: {Reasons}", string.Join(", ", responseQuality.Reasons));
                
                var refinedResponse = await RefineResponseAsync(plan.Intent, directResponse, kernel, cancellationToken);
                return CreateResponse(refinedResponse, executionPath, true);
            }
            
            return CreateResponse(directResponse, executionPath, true);
        }
        
        executionPath.Add("ToolExecution");
        
        var results = new List<string>();
        
        foreach (var step in plan.Steps)
        {
            try
            {
                // Use SK's function calling to execute tools
                var kernelArgs = new KernelArguments();
                foreach (var param in step.Parameters)
                {
                    kernelArgs[param.Key] = param.Value;
                }
                
                // For now, we'll use a more direct approach since we need the kernel to know about our tools
                var result = await kernel.InvokeAsync("GetOrderTool", step.Tool, kernelArgs, cancellationToken);
                results.Add(result.ToString());
                _logger.LogInformation("Executed {Tool} with result: {Result}", step.Tool, result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute tool {Tool}", step.Tool);
                results.Add($"Error executing {step.Tool}: {ex.Message}");
            }
        }

        var finalResponse = string.Join("\n", results);
        return CreateResponse(finalResponse, executionPath, plan.ExecutionPath?.Contains("LLM") == true);
    }

    private QualityIndicators EvaluateResponseQuality(Plan plan)
    {
        var reasons = new List<string>();
        
        // Indicator 1: Very low confidence (0 or near 0)
        if (plan.Confidence <= 0.1)
        {
            reasons.Add($"Low confidence score: {plan.Confidence}");
        }
        
        // Indicator 2: No actionable steps
        if (plan.Steps.Count == 0)
        {
            reasons.Add("No actionable steps identified");
        }
        
        // Indicator 3: Intent-tool mismatch detection
        if (HasIntentToolMismatch(plan))
        {
            reasons.Add("Intent-tool mismatch detected");
        }
        
        // Indicator 4: Generic/vague intent suggests poor understanding
        if (IsGenericIntent(plan.Intent))
        {
            reasons.Add("Generic intent suggests poor understanding");
        }
        
        return new QualityIndicators(
            RequiresEscalation: reasons.Count > 0,
            Reasons: reasons
        );
    }
    
    private bool HasIntentToolMismatch(Plan plan)
    {
        // Check if tools don't match the intent
        foreach (var step in plan.Steps)
        {
            // GetOrder tool used for non-order intents
            if (step.Tool == "GetOrder" && 
                !plan.Intent.ToLower().Contains("order") && 
                !plan.Intent.ToLower().Contains("status") &&
                !plan.Intent.ToLower().Contains("track"))
            {
                return true;
            }
        }
        return false;
    }
    
    private bool IsGenericIntent(string intent)
    {
        var genericPhrases = new[] { "unknown intent", "handle user request", "help user", "unclear" };
        return genericPhrases.Any(phrase => intent.ToLower().Contains(phrase));
    }
    
    private QualityIndicators EvaluateResponseContent(string response, string intent)
    {
        var reasons = new List<string>();
        
        // Indicator 1: Response doesn't address the question
        if (IsResponseIrrelevant(response, intent))
        {
            reasons.Add("Response doesn't address the specific question");
        }
        
        // Indicator 2: Too generic/vague response
        if (IsResponseTooGeneric(response))
        {
            reasons.Add("Response is too generic or vague");
        }
        
        // Indicator 3: Response deflects instead of answering
        if (IsResponseDeflecting(response))
        {
            reasons.Add("Response deflects instead of providing useful information");
        }
        
        return new QualityIndicators(
            RequiresEscalation: reasons.Count > 0,
            Reasons: reasons
        );
    }
    
    private bool IsResponseIrrelevant(string response, string intent)
    {
        var lowerIntent = intent.ToLower();
        var lowerResponse = response.ToLower();
        
        // Check for common simple questions that should have direct answers
        if (lowerIntent.Contains("name") && !lowerResponse.Contains("name") && !lowerResponse.Contains("ai") && !lowerResponse.Contains("assistant"))
            return true;
            
        if (lowerIntent.Contains("who are you") && !lowerResponse.Contains("ai") && !lowerResponse.Contains("assistant") && !lowerResponse.Contains("language model"))
            return true;
            
        return false;
    }
    
    private bool IsResponseTooGeneric(string response)
    {
        var genericPhrases = new[] {
            "i couldn't quite understand",
            "i need more information",
            "could you please provide more context",
            "i'm not sure i understand",
            "i don't have the ability to access"
        };
        
        var lowerResponse = response.ToLower();
        return genericPhrases.Count(phrase => lowerResponse.Contains(phrase)) >= 2; // Multiple generic phrases = too generic
    }
    
    private bool IsResponseDeflecting(string response)
    {
        var deflectingPhrases = new[] {
            "i need a bit more information",
            "could you please clarify",
            "provide more context",
            "let's have a chat"
        };
        
        var lowerResponse = response.ToLower();
        return deflectingPhrases.Any(phrase => lowerResponse.Contains(phrase));
    }
    
    private async Task<string> RefineResponseAsync(string intent, string previousResponse, Kernel kernel, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Refining response for intent: {Intent}", intent);
        
        var prompt = $@"The user asked: '{intent}'
        
        The previous response was insufficient: '{previousResponse}'
        
        Please provide a better, more direct and helpful response. Be specific and answer the user's question directly.
        
        For simple questions like 'What's your name?' or 'Who are you?', give a clear, concise answer about being an AI assistant.
        For other questions, provide the most helpful response possible.
        
        Be conversational but direct. Don't ask for clarification unless absolutely necessary.";
        
        try
        {
            var result = await kernel.InvokePromptAsync(prompt, cancellationToken: cancellationToken);
            var response = result.ToString().Trim();
            
            _logger.LogInformation("Refined response: {Response}", response);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refine response");
            return "I apologize, but I'm having trouble providing a clear response right now. Could you try rephrasing your question?";
        }
    }

    private async Task<string> GetDirectLLMResponseAsync(string intent, Kernel kernel, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting direct LLM response for intent: {Intent}", intent);
        
        var prompt = $@"The user has made a request that cannot be handled by available tools.
        
        User intent: {intent}
        
        Please provide a helpful, direct response to the user. Be conversational and useful.
        If you cannot provide real-time data (like current date/time), explain this limitation politely.
        
        Respond directly to the user - no JSON, no formatting, just a natural conversational response.";
        
        try
        {
            var result = await kernel.InvokePromptAsync(prompt, cancellationToken: cancellationToken);
            var response = result.ToString().Trim();
            
            _logger.LogInformation("Direct LLM response: {Response}", response);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get direct LLM response");
            return "I understand your request, but I'm unable to provide a response at this time. Please try again or rephrase your question.";
        }
    }

    private string CreateResponse(string content, List<string> executionPath, bool requiresEscalation)
    {
        var response = new OrchestrationResult(
            Response: content,
            ExecutionPath: string.Join(" → ", executionPath),
            TokenCost: 0.001, // Mock cost calculation
            TokensUsed: 100, // Mock token count
            TotalExecutionTime: _stopwatch.Elapsed,
            RequiredEscalation: requiresEscalation
        );

        _logger.LogInformation("Response: {Response}", response.Response);
        _logger.LogInformation("Execution Path: {ExecutionPath}", response.ExecutionPath);
        _logger.LogInformation("Total Time: {TotalTime}ms", response.TotalExecutionTime.TotalMilliseconds);

        return FormatConsoleResponse(response);
    }
    
    private string FormatConsoleResponse(OrchestrationResult result)
    {
        var output = new System.Text.StringBuilder();
        
        // Main response content
        if (!string.IsNullOrEmpty(result.Response))
        {
            output.AppendLine("💬 Response:");
            output.AppendLine($"   {result.Response}");
            output.AppendLine();
        }
        
        // Execution details
        output.AppendLine("📋 Execution Details:");
        output.AppendLine($"   Path: {result.ExecutionPath}");
        output.AppendLine($"   Time: {result.TotalExecutionTime.TotalMilliseconds:F1}ms");
        output.AppendLine($"   Tokens: {result.TokensUsed} (${result.TokenCost:F4})");
        output.AppendLine($"   Escalated: {(result.RequiredEscalation ? "Yes" : "No")}");
        
        return output.ToString();
    }
}