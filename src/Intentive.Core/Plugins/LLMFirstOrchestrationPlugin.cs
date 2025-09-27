using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Intentive.Core.Configuration;
using Intentive.Core.Models;

namespace Intentive.Core.Plugins;

/// <summary>
/// LLM-first orchestration plugin - direct SK orchestration
/// Uses Semantic Kernel's native planning and function calling
/// </summary>
public class LLMFirstOrchestrationPlugin
{
    private readonly ILogger _logger;
    private readonly OrchestrationConfig _config;
    private readonly Stopwatch _stopwatch;

    public LLMFirstOrchestrationPlugin(
        ILogger logger,
        OrchestrationConfig config)
    {
        _logger = logger;
        _config = config;
        _stopwatch = new Stopwatch();
    }

    /// <summary>
    /// Main orchestration function using LLM-first approach
    /// </summary>
    [KernelFunction("orchestrate_llm_first")]
    [Description("Orchestrate user requests using LLM-first approach with Semantic Kernel's native planning")]
    public async Task<string> OrchestateAsync(
        [Description("User input text")] string input,
        Kernel kernel,
        CancellationToken cancellationToken = default)
    {
        _stopwatch.Restart();
        var executionPath = new List<string> { "LLMFirst" };

        try
        {
            _logger.LogInformation("Starting LLM-first orchestration for input: {Input}", input);
            
            // Use SK's native approach - let the LLM decide everything
            var prompt = $@"You are a helpful assistant that can execute functions to help users.

User request: ""{input}""

Available functions:
- GetOrder: Retrieves order information (requires orderId parameter)

Think step by step about what the user wants, then call the appropriate functions to help them.
If you need to extract information (like an order ID) from their request, do so carefully.

Respond with helpful information based on the function results.";

            executionPath.Add("LLMPlanning");

            // Let SK handle the entire orchestration
            var result = await kernel.InvokePromptAsync(
                prompt, 
                new KernelArguments(),
                cancellationToken: cancellationToken);

            executionPath.Add("LLMExecution");
            
            var response = result.ToString();
            _logger.LogInformation("LLM-first orchestration completed");
            
            return CreateResponse(response, executionPath, requiresEscalation: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during LLM-first orchestration");
            executionPath.Add("Error");
            return CreateResponse($"I apologize, but I encountered an error while processing your request: {ex.Message}", executionPath, requiresEscalation: false);
        }
        finally
        {
            _stopwatch.Stop();
            _logger.LogInformation("LLM-first orchestration completed in {ElapsedMs}ms", _stopwatch.ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// Alternative orchestration function that uses SK's automatic function calling
    /// </summary>
    [KernelFunction("orchestrate_llm_auto")]
    [Description("Orchestrate user requests using LLM with automatic function calling")]
    public async Task<string> OrchestateAutoAsync(
        [Description("User input text")] string input,
        Kernel kernel,
        CancellationToken cancellationToken = default)
    {
        _stopwatch.Restart();
        var executionPath = new List<string> { "LLMAutoFunction" };

        try
        {
            _logger.LogInformation("Starting LLM auto-function orchestration for input: {Input}", input);

            // Create a more sophisticated prompt that encourages function usage
            var systemPrompt = @"You are a helpful customer service assistant. When users ask about orders, 
            you should use the GetOrder function to retrieve accurate, up-to-date information.
            Always be helpful and provide clear, actionable information.";

            var userPrompt = input;

            executionPath.Add("AutoFunctionCall");

            // Use SK's automatic function calling with system and user messages
            var chatFunction = kernel.CreateFunctionFromPrompt(
                promptTemplate: $"{systemPrompt}\n\nUser: {{{{$input}}}}",
                functionName: "ChatWithFunctions");

            var result = await chatFunction.InvokeAsync(
                kernel,
                new() { ["input"] = userPrompt },
                cancellationToken: cancellationToken);

            var response = result.ToString();
            _logger.LogInformation("LLM auto-function orchestration completed");
            
            return CreateResponse(response, executionPath, requiresEscalation: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during LLM auto-function orchestration");
            executionPath.Add("Error");
            return CreateResponse($"I apologize, but I encountered an error: {ex.Message}", executionPath, requiresEscalation: false);
        }
        finally
        {
            _stopwatch.Stop();
            _logger.LogInformation("LLM auto-function orchestration completed in {ElapsedMs}ms", _stopwatch.ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// Streaming orchestration function for real-time responses
    /// </summary>
    [KernelFunction("orchestrate_llm_stream")]
    [Description("Orchestrate user requests using LLM with streaming response")]
    public async IAsyncEnumerable<string> OrchestateStreamAsync(
        [Description("User input text")] string input,
        Kernel kernel,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _stopwatch.Restart();
        var executionPath = new List<string> { "LLMStreaming" };

        try
        {
            _logger.LogInformation("Starting LLM streaming orchestration for input: {Input}", input);
            
            var prompt = $@"You are a helpful assistant. Respond to: ""{input}""
            
            If they're asking about an order, use the GetOrder function with the order ID.
            Provide helpful, accurate information.";

            executionPath.Add("StreamingResponse");

            // For now, yield a mock streaming response
            // In a real implementation, you'd use SK's streaming capabilities
            var words = prompt.Split(' ');
            foreach (var word in words.Take(10)) // Simulate streaming
            {
                await Task.Delay(100, cancellationToken);
                yield return word + " ";
            }

            yield return CreateResponse("Streaming response completed", executionPath, requiresEscalation: false);
        }
        finally
        {
            _stopwatch.Stop();
            _logger.LogInformation("LLM streaming orchestration completed in {ElapsedMs}ms", _stopwatch.ElapsedMilliseconds);
        }
    }

    private string CreateResponse(string content, List<string> executionPath, bool requiresEscalation)
    {
        var response = new OrchestrationResult(
            Response: content,
            ExecutionPath: string.Join(" → ", executionPath),
            TokenCost: CalculateTokenCost(content), // More accurate for LLM-first
            TokensUsed: EstimateTokens(content),
            TotalExecutionTime: _stopwatch.Elapsed,
            RequiredEscalation: requiresEscalation,
            DebugInfo: new Dictionary<string, object>
            {
                ["orchestration_mode"] = "LLMFirst",
                ["model_used"] = _config.OpenAI.CheapModel,
                ["timestamp"] = DateTime.UtcNow
            }
        );

        _logger.LogInformation("Response: {Response}", response.Response);
        _logger.LogInformation("Execution Path: {ExecutionPath}", response.ExecutionPath);
        _logger.LogInformation("Total Time: {TotalTime}ms", response.TotalExecutionTime.TotalMilliseconds);
        _logger.LogInformation("Estimated Cost: ${Cost:F4}", response.TokenCost);

        return JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
    }

    private double CalculateTokenCost(string content)
    {
        // Mock cost calculation for gpt-4o-mini
        // Real implementation would use actual token counting
        var estimatedTokens = EstimateTokens(content);
        var costPerToken = 0.00015 / 1000; // Rough estimate for gpt-4o-mini
        return estimatedTokens * costPerToken;
    }

    private int EstimateTokens(string content)
    {
        // Very rough token estimation: ~4 characters per token
        return content.Length / 4;
    }
}