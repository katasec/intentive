using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using OpenTelemetry;
using Intentive.Core.Configuration;
using Intentive.Core.Models;
using Intentive.Core.Plugins;
using Intentive.Core.Tools;
using Intentive.Core.Services;
using System.Collections.Concurrent;

namespace Intentive.Console;

class Program
{
    static async Task Main(string[] args)
    {
        // Check for training command first
        if (args.Contains("--train-tools"))
        {
            await HandleTrainingCommandAsync(args);
            return;
        }
        
        // Build configuration from command line and environment
        var config = ConfigurationExtensions.BuildOrchestrationConfig(args);
        
        System.Console.WriteLine("🚀 Intentive - Tool-First AI Orchestration");
        System.Console.WriteLine("Architecture: Rule Gate → Tool Classification → Direct Execution OR LLM Escalation");
        System.Console.WriteLine($"Model: {config.OpenAI.CheapModel}");
        System.Console.WriteLine();
        
        if (string.IsNullOrEmpty(config.OpenAI.ApiKey))
        {
            System.Console.WriteLine("❌ OpenAI API key not found. Please set OPENAI_API_KEY environment variable or use --openai-key argument.");
            System.Console.WriteLine();
            PrintUsage();
            return;
        }
        
        // Create kernel with DI
        var services = new ServiceCollection();
        
        // Add logging - log to file by default for clean console experience
        services.AddLogging(builder => {
            // Create a simple file logger
            builder.AddProvider(new SimpleFileLoggerProvider("intentive.log"));
            
            // Only add console logging if explicitly enabled
            if (args.Contains("--console-logs"))
            {
                builder.AddConsole();
            }
            
            builder.SetMinimumLevel(
                config.Telemetry.EnableDetailedLogging ? LogLevel.Information : LogLevel.Warning);
        });
        
        // Add configuration
        services.AddSingleton(config);
        
        // Create Semantic Kernel
        var kernelBuilder = Kernel.CreateBuilder();
        kernelBuilder.Services.AddSingleton(config);
        kernelBuilder.Services.AddLogging(builder => {
            builder.AddProvider(new SimpleFileLoggerProvider("intentive.log"));
            
            // Only add console logging if explicitly enabled  
            if (args.Contains("--console-logs"))
            {
                builder.AddConsole();
            }
        });
        
        // Register ILogger explicitly to fix DI for plugins
        kernelBuilder.Services.AddSingleton<ILogger>(serviceProvider => 
            serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Intentive"));

        // Configure OpenTelemetry with SigNoz Cloud support (default OFF)
        if (config.Observability.EnableOtel)
        {
            var observabilityOptions = config.Observability.UpdateFromOrchestrationConfig(config);
            
            var backendType = config.Observability.OtlpEndpoint.Contains("localhost") ? "Local OTLP" : "Cloud Backend";
            System.Console.WriteLine($"📊 Observability enabled: {backendType}");
            System.Console.WriteLine($"🔗 Endpoint: {config.Observability.OtlpEndpoint}");
            
            // Add tracing and metrics
            kernelBuilder.Services.AddOpenTelemetry()
                .WithTracing(t => t.AddIntentiveTracing(observabilityOptions))
                .WithMetrics(m => m.AddIntentiveMetrics(observabilityOptions));
        }
        
        // Add OpenAI connector (with custom base URL support for Groq)
        if (!string.IsNullOrEmpty(config.OpenAI.BaseUrl))
        {
            var httpClient = new HttpClient();
            httpClient.BaseAddress = new Uri(config.OpenAI.BaseUrl);
            
            // Use Groq-compatible model if using Groq endpoint
            var modelId = config.OpenAI.BaseUrl.Contains("groq.com") ? "llama-3.1-8b-instant" : config.OpenAI.CheapModel;
            
            kernelBuilder.AddOpenAIChatCompletion(
                modelId: modelId,
                apiKey: config.OpenAI.ApiKey,
                httpClient: httpClient);
                
            System.Console.WriteLine($"🔗 Using custom endpoint: {config.OpenAI.BaseUrl}");
            if (config.OpenAI.BaseUrl.Contains("groq.com"))
            {
                System.Console.WriteLine($"⚡ Groq model: {modelId}");
            }
        }
        else
        {
            kernelBuilder.AddOpenAIChatCompletion(
                modelId: config.OpenAI.CheapModel,
                apiKey: config.OpenAI.ApiKey);
        }
        
        // Add the new simple orchestration plugin
        kernelBuilder.Plugins.AddFromType<SimpleOrchestrationPlugin>();
        
        // Add tools
        kernelBuilder.Plugins.AddFromType<GetOrderTool>();
        
        var kernel = kernelBuilder.Build();
        
        System.Console.WriteLine("✅ Kernel initialized successfully!");
        System.Console.WriteLine();
        
        // Initialize and display available capabilities
        await DisplayCapabilitiesAsync(kernel);
        
        PrintInstructions();
        
        // Main interaction loop
        while (true)
        {
            System.Console.Write("> ");
            var input = System.Console.ReadLine();
            
            if (string.IsNullOrWhiteSpace(input))
                continue;
                
            if (input.ToLower() is "exit" or "quit" or "q")
            {
                System.Console.WriteLine("👋 Goodbye!");
                break;
            }
            
            if (input.ToLower() is "help" or "h")
            {
                PrintInstructions();
                continue;
            }
            
            // Remove mode switching - we only use simple orchestration now
            
            try
            {
                System.Console.WriteLine();
                System.Console.WriteLine($"🤔 Processing: {input}");
                System.Console.WriteLine();
                
                // Use the new simple orchestration
                KernelArguments kernelArgs = new() { ["input"] = input, ["kernel"] = kernel };
                
                FunctionResult result = await kernel.InvokeAsync("SimpleOrchestrationPlugin", "orchestrate_simple", kernelArgs);
                
                System.Console.WriteLine("📋 Result:");
                System.Console.WriteLine(result.ToString());
                System.Console.WriteLine();
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"❌ Error: {ex.Message}");
                System.Console.WriteLine();
            }
        }
    }
    
    private static void PrintUsage()
    {
        System.Console.WriteLine("Usage:");
        System.Console.WriteLine("  --openai-key <key>              Set OpenAI API key");
        System.Console.WriteLine("  --cheap-model <model>           Set model for LLM escalation");
        System.Console.WriteLine("  --enable-logging <true|false>  Enable detailed logging");
        System.Console.WriteLine("  --train-tools                   Train ONNX model from discovered tools");
        System.Console.WriteLine("  --examples <count>              Number of training examples per tool (default: 200)");
        System.Console.WriteLine("  --model <path>                  Output model path (default: models/trained-intentive.onnx)");
        System.Console.WriteLine();
        System.Console.WriteLine("Environment Variables:");
        System.Console.WriteLine("  OPENAI_API_KEY=<your-api-key>");
        System.Console.WriteLine("  OPENAI_BASE_URL=<custom-endpoint>  # Optional: for Groq, Azure, etc.");
        System.Console.WriteLine();
    }
    
    private static void PrintInstructions()
    {
        System.Console.WriteLine("✨ How Intentive Works:");
        System.Console.WriteLine("   1. 🚨 Fast rule gate for common requests (hello, hi, etc.)");
        System.Console.WriteLine("   2. 🎯 ONNX tool classification maps your request to specific tools");
        System.Console.WriteLine("   3. 🔧 Direct tool execution for high-confidence matches");
        System.Console.WriteLine("   4. 🤖 LLM escalation for complex or ambiguous requests");
        System.Console.WriteLine();
        System.Console.WriteLine("💡 Try asking about your available capabilities:");
        System.Console.WriteLine("   - Order status 12345");
        System.Console.WriteLine("   - Hello (fast response)");
        System.Console.WriteLine("   - Any natural language query matching your tools");
        System.Console.WriteLine();
        System.Console.WriteLine("Commands:");
        System.Console.WriteLine("   help, h          - Show this help and capabilities");
        System.Console.WriteLine("   exit, quit, q    - Exit application");
        System.Console.WriteLine();
    }
    
    
    /// <summary>
    /// Initialize tool system and display available capabilities
    /// </summary>
    private static async Task DisplayCapabilitiesAsync(Kernel kernel)
    {
        try
        {
            System.Console.WriteLine("🔧 Initializing tool system...");
            
            // Create a minimal logger for tool discovery
            var loggerFactory = LoggerFactory.Create(builder => 
                builder.AddProvider(new SimpleFileLoggerProvider("intentive.log")));
            var logger = loggerFactory.CreateLogger("Startup");
            
            // Initialize tool registry to discover available tools
            var toolRegistry = new ToolRegistry(logger);
            await toolRegistry.InitializeAsync();
            
            var capabilities = toolRegistry.GetAvailableCapabilities();
            var allTools = toolRegistry.GetAllTools();
            
            if (capabilities.Count > 0)
            {
                System.Console.WriteLine($"✅ Tool system initialized - {allTools.Count} tools, {capabilities.Count} capabilities");
                System.Console.WriteLine();
                System.Console.WriteLine("🤖 Here's what I can do:");
                
                // Group capabilities by tool for better display
                foreach (var (toolName, toolDescriptor) in allTools)
                {
                    var toolType = toolDescriptor.Type == ToolType.MCP ? "🌐" : "🔧";
                    System.Console.WriteLine($"   {toolType} {toolName}: {string.Join(", ", toolDescriptor.Capabilities)}");
                }
                
                System.Console.WriteLine();
                System.Console.WriteLine($"💡 Total capabilities: {string.Join(", ", capabilities)}");
            }
            else
            {
                System.Console.WriteLine("⚠️ No tools found.");
                System.Console.WriteLine();
                System.Console.WriteLine("🔧 Quick Setup:");
                System.Console.WriteLine("   1. Check tools.json exists and has MCP server configurations");
                System.Console.WriteLine("   2. Run: ./intentive --train-tools");
                System.Console.WriteLine("   3. System will auto-discover and train from configured tools");
            }
            
            System.Console.WriteLine();
            loggerFactory.Dispose();
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"⚠️ Tool discovery failed: {ex.Message}");
            System.Console.WriteLine();
            System.Console.WriteLine("🔧 Setup Instructions:");
            System.Console.WriteLine("   1. Configure tools in tools.json");
            System.Console.WriteLine("   2. Run: ./intentive --train-tools");
            System.Console.WriteLine("   3. Start: ./intentive");
            System.Console.WriteLine();
        }
    }
    
    /// <summary>
    /// Handle the --train-tools command
    /// </summary>
    private static async Task HandleTrainingCommandAsync(string[] args)
    {
        System.Console.WriteLine("🔧 Intentive Tool Training Mode");
        System.Console.WriteLine("===============================\n");
        
        // Parse training options
        var exampleCount = GetOptionValue(args, "--examples", "200");
        var modelPath = GetOptionValue(args, "--model", "models/trained-intentive.onnx");
        
        if (!int.TryParse(exampleCount, out var examples))
        {
            examples = 200;
        }
        
        System.Console.WriteLine($"📊 Training Parameters:");
        System.Console.WriteLine($"   Examples per tool: {examples}");
        System.Console.WriteLine($"   Output model: {modelPath}");
        System.Console.WriteLine();
        
        // Create logger for training
        var loggerFactory = LoggerFactory.Create(builder => 
            builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        var logger = loggerFactory.CreateLogger("TrainingMode");
        
        try
        {
            // Initialize tool registry
            System.Console.WriteLine("🔧 Initializing Tool Registry...");
            var toolRegistry = new ToolRegistry(logger);
            await toolRegistry.InitializeAsync();
            
            var availableCapabilities = toolRegistry.GetAvailableCapabilities();
            System.Console.WriteLine($"✅ Tool Registry initialized - {availableCapabilities.Count} capabilities");
            
            if (availableCapabilities.Count == 0)
            {
                System.Console.WriteLine("❌ No tools found. Please check your tools.json configuration.");
                return;
            }
            
            System.Console.WriteLine($"📋 Available capabilities: {string.Join(", ", availableCapabilities)}");
            System.Console.WriteLine();
            
            // Run training pipeline
            var intentGenerator = new IntentGenerator(logger, toolRegistry);
            var trainedModelPath = await intentGenerator.DiscoverAndTrainAsync(examples, modelPath);
            
            System.Console.WriteLine();
            System.Console.WriteLine("✅ Training completed successfully!");
            System.Console.WriteLine($"📁 Model saved to: {trainedModelPath}");
            System.Console.WriteLine();
            System.Console.WriteLine("🚀 Tool-first orchestration is ready!");
            System.Console.WriteLine("   Architecture: Rule Gate → ONNX Classification → Direct Execution OR LLM Escalation");
            System.Console.WriteLine();
            System.Console.WriteLine("Start the system:");
            System.Console.WriteLine("   ./intentive");
            System.Console.WriteLine();
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"❌ Training failed: {ex.Message}");
            logger.LogError(ex, "Training pipeline failed");
        }
        finally
        {
            loggerFactory.Dispose();
        }
    }
    
    /// <summary>
    /// Get command line option value
    /// </summary>
    private static string GetOptionValue(string[] args, string optionName, string defaultValue)
    {
        var optionIndex = Array.IndexOf(args, optionName);
        if (optionIndex >= 0 && optionIndex < args.Length - 1)
        {
            return args[optionIndex + 1];
        }
        return defaultValue;
    }
}

// Simple file logger provider for clean console output
public class SimpleFileLoggerProvider : ILoggerProvider
{
    private readonly string _filePath;
    private readonly ConcurrentDictionary<string, SimpleFileLogger> _loggers = new();
    private readonly StreamWriter _writer;

    public SimpleFileLoggerProvider(string filePath)
    {
        _filePath = filePath;
        
        // Create or overwrite the log file
        var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(fileStream, leaveOpen: false) { AutoFlush = true };
    }

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, name => new SimpleFileLogger(name, _writer));
    }

    public void Dispose()
    {
        _writer?.Dispose();
        _loggers.Clear();
    }
}

public class SimpleFileLogger : ILogger
{
    private readonly string _categoryName;
    private readonly StreamWriter _writer;

    public SimpleFileLogger(string categoryName, StreamWriter writer)
    {
        _categoryName = categoryName;
        _writer = writer;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var message = formatter(state, exception);
        var logEntry = $"[{timestamp}] [{logLevel}] {_categoryName}: {message}";
        
        if (exception != null)
        {
            logEntry += Environment.NewLine + exception.ToString();
        }
        
        _writer.WriteLine(logEntry);
    }
}
