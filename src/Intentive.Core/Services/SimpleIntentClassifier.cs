using Microsoft.Extensions.Logging;
using Microsoft.ML;
using Microsoft.ML.Data;
using Intentive.Core.Configuration;
using Intentive.Core.Models;

namespace Intentive.Core.Services;

/// <summary>
/// Tool-focused intent classifier using trained ML.NET models
/// Maps user input directly to available tools with confidence scoring
/// </summary>
public class SimpleIntentClassifier : IDisposable
{
    private readonly ILogger _logger;
    private readonly IntentConfig _config;
    private MLContext? _mlContext;
    private ITransformer? _trainedModel;
    private PredictionEngine<ModelInput, ModelOutput>? _predictionEngine;
    private readonly ToolRegistry _toolRegistry;
    private bool _disposed = false;

    public SimpleIntentClassifier(ILogger logger, IntentConfig config, ToolRegistry toolRegistry)
    {
        _logger = logger;
        _config = config;
        _toolRegistry = toolRegistry;
        _mlContext = new MLContext(seed: 0);

        LoadTrainedModelAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Classify user input to find the best matching tool
    /// </summary>
    public async Task<ToolClassificationResult> FindToolForAsync(string input, CancellationToken cancellationToken = default)
    {
        if (_predictionEngine != null && _trainedModel != null)
        {
            return ClassifyWithTrainedModel(input);
        }
        else
        {
            return ClassifyWithFallbackPatterns(input);
        }
    }

    /// <summary>
    /// Load the trained ML.NET model
    /// </summary>
    private async Task LoadTrainedModelAsync()
    {
        try
        {
            // Try different model paths
            var possiblePaths = new[]
            {
                "models/trained-intentive.zip",  // From training command
                _config.ModelPath.Replace(".onnx", ".zip"),  // Config path
                "models/trained-intentive.onnx",  // Fallback
                _config.ModelPath  // Original config
            };

            string? modelPath = null;
            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    modelPath = path;
                    break;
                }
            }

            if (modelPath != null && modelPath.EndsWith(".zip"))
            {
                _logger.LogInformation("Loading trained ML.NET model: {ModelPath}", modelPath);
                _trainedModel = _mlContext.Model.Load(modelPath, out var schema);
                _predictionEngine = _mlContext.Model.CreatePredictionEngine<ModelInput, ModelOutput>(_trainedModel);
                _logger.LogInformation("✅ Trained model loaded successfully");
            }
            else
            {
                _logger.LogWarning("⚠️ No trained model found, using fallback pattern matching");
                _logger.LogInformation("💡 Run './intentive --train-tools' to create a trained model");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load trained model, using pattern matching fallback");
        }
    }

    /// <summary>
    /// Use trained ML.NET model for classification
    /// </summary>
    private ToolClassificationResult ClassifyWithTrainedModel(string input)
    {
        try
        {
            var modelInput = new ModelInput { Text = input };
            var prediction = _predictionEngine!.Predict(modelInput);
            
            _logger.LogDebug("🤖 Model prediction: {Tool} (confidence: {Score})", 
                prediction.PredictedLabel, prediction.Score?.Max() ?? 0);
            
            // Get confidence from score array
            var confidence = prediction.Score?.Max() ?? 0.0;
            
            return new ToolClassificationResult
            {
                ToolName = prediction.PredictedLabel,
                Confidence = confidence,
                BestCapability = prediction.PredictedLabel,
                AllScores = new Dictionary<string, double> { [prediction.PredictedLabel] = confidence }
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Model prediction failed, falling back to patterns");
            return ClassifyWithFallbackPatterns(input);
        }
    }

    /// <summary>
    /// Fallback pattern matching when no trained model is available
    /// </summary>
    private ToolClassificationResult ClassifyWithFallbackPatterns(string input)
    {
        var lowerInput = input.ToLower();
        var toolScores = new Dictionary<string, double>();
        
        // Get all available tools from registry
        var availableTools = _toolRegistry.GetAllTools();
        
        foreach (var (toolName, toolDescriptor) in availableTools)
        {
            double score = 0.0;
            
            // Score based on capability keywords
            foreach (var capability in toolDescriptor.Capabilities)
            {
                if (lowerInput.Contains(capability.ToLower()))
                {
                    score += 1.0;
                }
                
                // Additional scoring for common patterns
                score += capability.ToLower() switch
                {
                    var c when c.Contains("weather") && (lowerInput.Contains("weather") || lowerInput.Contains("temperature") || lowerInput.Contains("forecast")) => 2.0,
                    var c when c.Contains("time") && (lowerInput.Contains("time") || lowerInput.Contains("date") || lowerInput.Contains("today")) => 2.0,
                    var c when c.Contains("calculate") && (lowerInput.Contains("calculate") || lowerInput.Contains("math") || ContainsMathExpression(lowerInput)) => 2.0,
                    var c when c.Contains("order") && (lowerInput.Contains("order") || lowerInput.Contains("track") || lowerInput.Contains("status")) => 2.0,
                    _ => 0.0
                };
            }
            
            // Normalize score by number of capabilities
            if (toolDescriptor.Capabilities.Count > 0)
            {
                score = score / toolDescriptor.Capabilities.Count;
            }
            
            toolScores[toolName] = score;
        }
        
        var bestMatch = toolScores.OrderByDescending(x => x.Value).FirstOrDefault();
        var confidence = bestMatch.Value;
        
        // Only return tool if confidence is reasonable
        if (confidence < 0.3)
        {
            return new ToolClassificationResult
            {
                ToolName = null,
                Confidence = 0.0,
                BestCapability = null,
                AllScores = toolScores
            };
        }
        
        return new ToolClassificationResult
        {
            ToolName = bestMatch.Key,
            Confidence = confidence,
            BestCapability = availableTools[bestMatch.Key].Capabilities.FirstOrDefault(),
            AllScores = toolScores
        };
    }
    
    /// <summary>
    /// Check if input contains mathematical expressions
    /// </summary>
    private bool ContainsMathExpression(string input)
    {
        return input.Any(c => "+-*/%".Contains(c)) || 
               input.Contains("plus") || input.Contains("minus") || 
               input.Contains("times") || input.Contains("divided") ||
               System.Text.RegularExpressions.Regex.IsMatch(input, @"\d+\s*[+\-*/]\s*\d+");
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _predictionEngine?.Dispose();
            // ML.NET objects don't have Dispose methods
            _trainedModel = null;
            _mlContext = null;
            _disposed = true;
        }
    }
}

/// <summary>
/// ML.NET input model for predictions
/// </summary>
public class ModelInput
{
    public string Text { get; set; } = "";
    public string Label { get; set; } = "";
}

/// <summary>
/// ML.NET output model for predictions
/// </summary>
public class ModelOutput
{
    public string PredictedLabel { get; set; } = "";
    public float[] Score { get; set; } = Array.Empty<float>();
}
