using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Intentive.Core.Configuration;
using Intentive.Core.Models;
using Intentive.Core.Services;

namespace Intentive.Core.Plugins;

/// <summary>
/// Simple orchestration plugin - clean wrapper around SimpleOrchestrationEngine
/// Replaces the 616-line IntentiveOrchestrationPlugin with ~50 lines
/// </summary>
public class SimpleOrchestrationPlugin
{
    private readonly ILogger _logger;
    private readonly OrchestrationConfig _config;
    private SimpleOrchestrationEngine? _orchestrationEngine;

    public SimpleOrchestrationPlugin(
        ILogger logger,
        OrchestrationConfig config)
    {
        _logger = logger;
        _config = config;
    }

    /// <summary>
    /// Main orchestration function using simple 3-step approach
    /// </summary>
    [KernelFunction("orchestrate_simple")]
    [Description("Orchestrate user requests using simple 3-step approach: Rule Gate → Tool Classification → Direct Execution or LLM Escalation")]
    public async Task<string> OrchestateAsync(
        [Description("User input text")] string input,
        Kernel kernel,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Initialize orchestration engine if needed (lazy initialization)
            var orchestrationEngine = await GetOrchestrationEngineAsync(kernel);
            
            // Execute the simple 3-step orchestration
            var result = await orchestrationEngine.HandleAsync(input);
            
            // Format response for console display
            return FormatConsoleResponse(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Simple orchestration failed for input: {Input}", input);
            return $"❌ Error: {ex.Message}";
        }
    }

    /// <summary>
    /// Get or create the orchestration engine (lazy initialization)
    /// </summary>
    private async Task<SimpleOrchestrationEngine> GetOrchestrationEngineAsync(Kernel kernel)
    {
        if (_orchestrationEngine != null)
        {
            return _orchestrationEngine;
        }

        // Initialize components
        var toolRegistry = new ToolRegistry(_logger);
        await toolRegistry.InitializeAsync();
        
        var classifier = new SimpleIntentClassifier(_logger, _config.Intent, toolRegistry);
        
        _orchestrationEngine = new SimpleOrchestrationEngine(
            _logger, _config, toolRegistry, classifier, kernel);
            
        _logger.LogInformation("✅ Simple orchestration engine initialized");
        
        return _orchestrationEngine;
    }

    /// <summary>
    /// Format the orchestration result for console display
    /// </summary>
    private string FormatConsoleResponse(SimpleOrchestrationResult result)
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
        output.AppendLine($"   Time: {result.ExecutionTimeMs:F1}ms");
        
        if (!string.IsNullOrEmpty(result.ToolUsed))
        {
            output.AppendLine($"   Tool: {result.ToolUsed}");
        }
        
        output.AppendLine($"   LLM Used: {(result.UsedLLM ? "Yes" : "No")}");
        output.AppendLine($"   Timestamp: {result.Timestamp:HH:mm:ss}");
        
        return output.ToString();
    }
}