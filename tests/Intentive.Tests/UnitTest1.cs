using Microsoft.SemanticKernel;
using Intentive.Core.Configuration;
using Xunit.Abstractions;

namespace Intentive.Tests;

public class LLMIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public LLMIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }
    [Fact]
    public void BasicConfigurationTest()
    {
        // Basic test to ensure configuration loads properly
        var config = ConfigurationExtensions.BuildOrchestrationConfig(new string[0]);
        Assert.NotNull(config);
        Assert.Equal(OrchestrationMode.Intentive, config.Mode);
    }

    [Fact]
    public async Task LLM_Integration_Test()
    {
        // Skip test if API key is not available
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var baseUrl = Environment.GetEnvironmentVariable("OPENAI_BASE_URL");
        
        if (string.IsNullOrEmpty(apiKey))
        {
            // Skip test if no API key is provided
            return;
        }

        // Build configuration
        var config = ConfigurationExtensions.BuildOrchestrationConfig(new string[0]);
        Assert.NotEmpty(config.OpenAI.ApiKey);

        // Create kernel with OpenAI connector
        var kernelBuilder = Kernel.CreateBuilder();

        // Configure with custom base URL if provided (for Groq)
        if (!string.IsNullOrEmpty(baseUrl))
        {
            var httpClient = new HttpClient();
            httpClient.BaseAddress = new Uri(baseUrl);
            
            // Use Groq-compatible model for testing
            var modelId = baseUrl.Contains("groq.com") ? "llama-3.1-8b-instant" : config.OpenAI.CheapModel;
            
            kernelBuilder.AddOpenAIChatCompletion(
                modelId: modelId,
                apiKey: config.OpenAI.ApiKey,
                httpClient: httpClient);
        }
        else
        {
            kernelBuilder.AddOpenAIChatCompletion(
                modelId: config.OpenAI.CheapModel,
                apiKey: config.OpenAI.ApiKey);
        }

        var kernel = kernelBuilder.Build();

        // Test basic LLM functionality with a simple prompt
        var prompt = "Say 'Hello, this is a test response from the LLM!' and nothing else.";
        
        var response = await kernel.InvokePromptAsync(prompt);
        
        // Verify we got a response
        Assert.NotNull(response);
        var responseText = response.GetValue<string>();
        Assert.NotNull(responseText);
        Assert.NotEmpty(responseText);
        
        // Log the response for manual verification
        _output.WriteLine($"✅ LLM Response: {responseText}");
        _output.WriteLine($"🔗 Used Base URL: {baseUrl ?? "OpenAI Default"}");
        var actualModel = baseUrl?.Contains("groq.com") == true ? "llama-3.1-8b-instant" : config.OpenAI.CheapModel;
        _output.WriteLine($"🤖 Model: {actualModel}");
        
        // Basic validation - response should contain some form of greeting
        Assert.True(responseText.Contains("Hello") || responseText.Contains("test"), 
            $"Response should contain greeting or test confirmation. Got: {responseText}");
    }

    [Fact] 
    public async Task Groq_Specific_Model_Test()
    {
        // Skip test if not using Groq
        var baseUrl = Environment.GetEnvironmentVariable("OPENAI_BASE_URL");
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        
        if (string.IsNullOrEmpty(apiKey) || !baseUrl?.Contains("groq.com") == true)
        {
            return; // Skip if not using Groq
        }

        // Test with a Groq-optimized model
        var kernelBuilder = Kernel.CreateBuilder();
        var httpClient = new HttpClient();
        httpClient.BaseAddress = new Uri(baseUrl);
        
        // Use a fast Groq model for testing
        kernelBuilder.AddOpenAIChatCompletion(
            modelId: "llama-3.1-8b-instant", // Fast Groq model
            apiKey: apiKey,
            httpClient: httpClient);

        var kernel = kernelBuilder.Build();

        // Test with a simple math question
        var prompt = "What is 2 + 2? Answer with just the number.";
        
        var response = await kernel.InvokePromptAsync(prompt);
        var responseText = response.GetValue<string>();
        
        Assert.NotNull(responseText);
        Assert.NotEmpty(responseText);
        
        _output.WriteLine($"🔢 Math Response: {responseText}");
        _output.WriteLine($"⚡ Groq Model: llama-3.1-8b-instant");
        
        // Should contain the answer "4"
        Assert.True(responseText.Contains("4"), 
            $"Response should contain the answer '4'. Got: {responseText}");
    }
}
