using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Intentive.Core.Configuration;
using Intentive.Core.Models;

namespace Intentive.Core.Services;

/// <summary>
/// Simple BERT-style intent classifier using ONNX
/// Uses basic tokenization and pre-defined intent patterns
/// </summary>
public class SimpleIntentClassifier : IDisposable
{
    private readonly ILogger _logger;
    private readonly IntentConfig _config;
    private readonly InferenceSession? _session;
    private readonly Dictionary<string, string[]> _intentPatterns;
    private bool _disposed = false;

    public SimpleIntentClassifier(ILogger logger, IntentConfig config)
    {
        _logger = logger;
        _config = config;

        // Try to load ONNX model if it exists, otherwise use pattern matching
        if (File.Exists(_config.ModelPath))
        {
            try
            {
                _session = new InferenceSession(_config.ModelPath);
                _logger.LogInformation("ONNX model loaded: {ModelPath}", _config.ModelPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load ONNX model, using pattern matching fallback");
                _session = null;
            }
        }
        else
        {
            _logger.LogInformation("ONNX model not found, using pattern matching");
            _session = null;
        }

        _intentPatterns = new Dictionary<string, string[]>
        {
            ["OrderStatus"] = new[] { "order", "status", "track", "package", "shipment", "delivery" },
            ["GeneralHelp"] = new[] { "help", "support", "assist", "question", "issue" },
            ["ProductInquiry"] = new[] { "product", "item", "details", "specs", "information" },
            ["Greeting"] = new[] { "hi", "hello", "hey", "greetings", "good morning" }
        };
    }

    public async Task<ClassificationResult> ClassifyAsync(string input, CancellationToken cancellationToken = default)
    {
        if (_session != null)
        {
            return await ClassifyWithOnnxAsync(input, cancellationToken);
        }
        else
        {
            return ClassifyWithPatterns(input);
        }
    }

    private async Task<ClassificationResult> ClassifyWithOnnxAsync(string input, CancellationToken cancellationToken)
    {
        try
        {
            // Simple tokenization (in production, use proper BERT tokenizer)
            var tokens = SimpleTokenize(input);
            var inputIds = tokens.Select(t => (long)Math.Abs(t.GetHashCode()) % 30000).ToArray();
            
            // Pad/truncate to 512 tokens
            const int maxLength = 512;
            if (inputIds.Length > maxLength)
            {
                inputIds = inputIds.Take(maxLength).ToArray();
            }
            else if (inputIds.Length < maxLength)
            {
                var padding = Enumerable.Repeat(0L, maxLength - inputIds.Length).ToArray();
                inputIds = inputIds.Concat(padding).ToArray();
            }

            var attentionMask = inputIds.Select(id => id > 0 ? 1L : 0L).ToArray();

            // Create tensors
            var inputIdsTensor = new DenseTensor<long>(inputIds, new[] { 1, inputIds.Length });
            var attentionMaskTensor = new DenseTensor<long>(attentionMask, new[] { 1, attentionMask.Length });

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
                NamedOnnxValue.CreateFromTensor("attention_mask", attentionMaskTensor)
            };

            using var results = _session!.Run(inputs);
            var output = results.FirstOrDefault()?.AsTensor<float>();

            if (output != null)
            {
                // Use ONNX output to influence pattern matching
                var onnxScore = output.Sum() / output.Length; // Simple aggregation
                return ClassifyWithPatterns(input, onnxScore);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ONNX classification failed, falling back to patterns");
        }

        return ClassifyWithPatterns(input);
    }

    private ClassificationResult ClassifyWithPatterns(string input, float onnxBoost = 0.0f)
    {
        var lowerInput = input.ToLower();
        var scores = new Dictionary<string, double>();

        foreach (var intent in _intentPatterns)
        {
            var matchCount = intent.Value.Count(pattern => lowerInput.Contains(pattern));
            var score = (double)matchCount / intent.Value.Length + onnxBoost * 0.1;
            scores[intent.Key] = score;
        }

        var bestMatch = scores.OrderByDescending(x => x.Value).FirstOrDefault();
        var confidence = bestMatch.Value;
        
        // Calculate ambiguity and risk
        var sortedScores = scores.OrderByDescending(x => x.Value).ToList();
        var secondBest = sortedScores.Count > 1 ? sortedScores[1].Value : 0.0;
        var ambiguityScore = confidence > 0 ? Math.Max(0.0, 1.0 - (confidence - secondBest)) : 0.8;
        var riskScore = 1.0 - confidence;

        var taskType = confidence >= 0.3 ? bestMatch.Key : "Unknown";
        
        return new ClassificationResult(
            TaskType: taskType,
            AmbiguityScore: Math.Max(0.0, Math.Min(1.0, ambiguityScore)),
            RiskScore: Math.Max(0.0, Math.Min(1.0, riskScore)),
            Domain: GetDomain(taskType),
            IntentScores: scores
        );
    }

    private static string[] SimpleTokenize(string input)
    {
        return input.ToLower()
            .Split(new[] { ' ', '\t', '\n', '\r', '.', ',', '!', '?' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length > 0)
            .ToArray();
    }

    private static string GetDomain(string taskType) => taskType switch
    {
        "OrderStatus" or "ProductInquiry" => "Ecommerce",
        "GeneralHelp" => "Support",
        "Greeting" => "General",
        _ => "Unknown"
    };

    public void Dispose()
    {
        if (!_disposed)
        {
            _session?.Dispose();
            _disposed = true;
        }
    }
}