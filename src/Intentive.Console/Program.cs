using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using OpenTelemetry;
using Intentive.Core.Configuration;
using Intentive.Core.Plugins;
using Intentive.Core.Tools;
using System.Collections.Concurrent;

namespace Intentive.Console;

class Program
{
    static async Task Main(string[] args)
    {
        // Build configuration from command line and environment
        var config = ConfigurationExtensions.BuildOrchestrationConfig(args);
        
        System.Console.WriteLine("🚀 Intentive - Fit-for-Purpose AI Orchestration");
        System.Console.WriteLine($"Mode: {config.Mode}");
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
        
        // Add plugins based on orchestration mode
        switch (config.Mode)
        {
            case OrchestrationMode.Intentive:
                kernelBuilder.Plugins.AddFromType<IntentiveOrchestrationPlugin>();
                break;
            case OrchestrationMode.LLMFirst:
                kernelBuilder.Plugins.AddFromType<LLMFirstOrchestrationPlugin>();
                break;
        }
        
        // Add tools
        kernelBuilder.Plugins.AddFromType<GetOrderTool>();
        
        var kernel = kernelBuilder.Build();
        
        System.Console.WriteLine("✅ Kernel initialized successfully!");
        System.Console.WriteLine();
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
            
            if (input.ToLower().StartsWith("mode "))
            {
                await SwitchModeAsync(input, kernel, config);
                continue;
            }
            
            try
            {
                System.Console.WriteLine();
                System.Console.WriteLine($"🤔 Processing: {input}");
                System.Console.WriteLine();
                
                // Select orchestration function based on mode and invoke
                KernelArguments kernelArgs = new() { ["input"] = input, ["kernel"] = kernel };
                
                FunctionResult result = config.Mode switch
                {
                    OrchestrationMode.Intentive => await kernel.InvokeAsync("IntentiveOrchestrationPlugin", "orchestrate_intentive", kernelArgs),
                    OrchestrationMode.LLMFirst => await kernel.InvokeAsync("LLMFirstOrchestrationPlugin", "orchestrate_llm_first", kernelArgs),
                    _ => await kernel.InvokeAsync("IntentiveOrchestrationPlugin", "orchestrate_intentive", kernelArgs)
                };
                
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
        System.Console.WriteLine("  --mode <intentive|llmfirst>     Set orchestration mode");
        System.Console.WriteLine("  --openai-key <key>              Set OpenAI API key");
        System.Console.WriteLine("  --cheap-model <model>           Set model for cheap LM probe");
        System.Console.WriteLine("  --enable-logging <true|false>  Enable detailed logging");
        System.Console.WriteLine();
        System.Console.WriteLine("Environment Variables:");
        System.Console.WriteLine("  ORCHESTRATION_MODE=<intentive|llmfirst>");
        System.Console.WriteLine("  OPENAI_API_KEY=<your-api-key>");
        System.Console.WriteLine();
    }
    
    private static void PrintInstructions()
    {
        System.Console.WriteLine("💡 Try these examples:");
        System.Console.WriteLine("   - What is the status of order 12345?");
        System.Console.WriteLine("   - Track order 67890");
        System.Console.WriteLine("   - Hello (fast path response)");
        System.Console.WriteLine();
        System.Console.WriteLine("Commands:");
        System.Console.WriteLine("   help, h          - Show this help");
        System.Console.WriteLine("   mode <mode>      - Switch orchestration mode");
        System.Console.WriteLine("   exit, quit, q    - Exit application");
        System.Console.WriteLine();
    }
    
    private static async Task SwitchModeAsync(string input, Kernel kernel, OrchestrationConfig config)
    {
        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            System.Console.WriteLine("Usage: mode <intentive|llmfirst>");
            return;
        }
        
        if (Enum.TryParse<OrchestrationMode>(parts[1], true, out var newMode))
        {
            config.Mode = newMode;
            System.Console.WriteLine($"🔄 Switched to {newMode} mode");
            System.Console.WriteLine("Note: Restart required for full plugin reloading");
        }
        else
        {
            System.Console.WriteLine($"❌ Invalid mode: {parts[1]}. Use 'intentive' or 'llmfirst'");
        }
        
        System.Console.WriteLine();
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

    public IDisposable BeginScope<TState>(TState state) => null!;

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
