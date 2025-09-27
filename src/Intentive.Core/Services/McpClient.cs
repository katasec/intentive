using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Intentive.Core.Models;

namespace Intentive.Core.Services;

/// <summary>
/// MCP (Model Context Protocol) client for communicating with Docker-based MCP servers
/// </summary>
public class McpClient : IDisposable
{
    private readonly ILogger _logger;
    private Process? _mcpProcess;
    private StreamWriter? _processInput;
    private StreamReader? _processOutput;
    private McpServerConfig? _serverConfig;
    private bool _disposed = false;

    public McpClient(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Connect to an MCP server using the provided configuration
    /// </summary>
    public async Task<bool> ConnectAsync(McpServerConfig serverConfig)
    {
        _serverConfig = serverConfig;
        
        try
        {
            _logger.LogInformation("🔗 Starting MCP server: {Name}", serverConfig.Name);
            
            // Create Docker process with full path for macOS
            var dockerCommand = serverConfig.Transport.Command == "docker" ? "/usr/local/bin/docker" : serverConfig.Transport.Command;
            var processInfo = new ProcessStartInfo
            {
                FileName = dockerCommand,
                Arguments = string.Join(" ", serverConfig.Transport.Args.Select(EscapeArgument)),
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            _mcpProcess = Process.Start(processInfo);
            
            if (_mcpProcess == null)
            {
                _logger.LogError("❌ Failed to start MCP process for {Name}", serverConfig.Name);
                return false;
            }

            _processInput = _mcpProcess.StandardInput;
            _processOutput = _mcpProcess.StandardOutput;

            // Perform MCP initialization handshake
            var initialized = await InitializeMcpHandshakeAsync();
            
            if (initialized)
            {
                _logger.LogInformation("✅ MCP server initialized: {Name}", serverConfig.Name);
                return true;
            }
            else
            {
                _logger.LogError("❌ MCP handshake failed for {Name}", serverConfig.Name);
                await CleanupProcessAsync();
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to connect to MCP server: {Name}", serverConfig.Name);
            await CleanupProcessAsync();
            return false;
        }
    }

    /// <summary>
    /// Execute a request against the MCP server
    /// </summary>
    public async Task<string> ExecuteAsync(string userInput, Dictionary<string, object> parameters)
    {
        if (_processInput == null || _processOutput == null || _mcpProcess?.HasExited == true)
        {
            return "❌ MCP server not connected";
        }

        try
        {
            // Create MCP request based on server type and user input
            var mcpRequest = CreateMcpRequest(userInput, parameters);
            
            _logger.LogDebug("📤 Sending MCP request: {Request}", mcpRequest);
            
            // Send request to MCP server
            await _processInput.WriteLineAsync(mcpRequest);
            await _processInput.FlushAsync();

            // Read response with timeout
            var response = await ReadMcpResponseAsync(TimeSpan.FromSeconds(10));
            
            if (response != null)
            {
                var parsedResponse = ParseMcpResponse(response);
                _logger.LogDebug("📥 MCP response: {Response}", parsedResponse);
                return parsedResponse;
            }
            else
            {
                return "❌ No response from MCP server";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ MCP execution failed");
            return $"❌ MCP error: {ex.Message}";
        }
    }

    private async Task<bool> InitializeMcpHandshakeAsync()
    {
        try
        {
            // MCP initialization protocol
            var initRequest = new
            {
                jsonrpc = "2.0",
                id = "init-1",
                method = "initialize",
                @params = new
                {
                    protocolVersion = "2024-11-05",
                    capabilities = new
                    {
                        tools = new { }
                    },
                    clientInfo = new
                    {
                        name = "intentive",
                        version = "1.0.0"
                    }
                }
            };

            var initJson = JsonSerializer.Serialize(initRequest);
            await _processInput!.WriteLineAsync(initJson);
            await _processInput.FlushAsync();

            // Wait for initialization response
            var response = await ReadMcpResponseAsync(TimeSpan.FromSeconds(5));
            
            if (response?.Contains("result") == true)
            {
                // Send initialized notification
                var initializedRequest = new
                {
                    jsonrpc = "2.0",
                    method = "notifications/initialized"
                };

                var initializedJson = JsonSerializer.Serialize(initializedRequest);
                await _processInput.WriteLineAsync(initializedJson);
                await _processInput.FlushAsync();

                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ MCP handshake failed");
            return false;
        }
    }

    private string CreateMcpRequest(string userInput, Dictionary<string, object> parameters)
    {
        if (_serverConfig == null)
        {
            return "{}";
        }

        // Create appropriate MCP request based on server capabilities
        var toolName = DetermineToolName(userInput, _serverConfig.Capabilities);
        var extractedParams = ExtractParametersFromInput(userInput, toolName);

        var request = new
        {
            jsonrpc = "2.0",
            id = Guid.NewGuid().ToString(),
            method = "tools/call",
            @params = new
            {
                name = toolName,
                arguments = extractedParams.Count > 0 ? extractedParams : parameters
            }
        };

        return JsonSerializer.Serialize(request);
    }

    private string DetermineToolName(string userInput, List<string> capabilities)
    {
        var input = userInput.ToLower();
        
        // Simple heuristic-based tool selection
        if (capabilities.Contains("weather") && (input.Contains("weather") || input.Contains("temperature") || input.Contains("forecast")))
            return "get_weather";
            
        if (capabilities.Contains("current_time") && (input.Contains("time") || input.Contains("date") || input.Contains("day")))
            return "get_current_time";
            
        if (capabilities.Contains("calculate") && (input.Contains("calculate") || input.Contains("math") || ContainsMathExpression(input)))
            return "calculate";

        // Default to first capability
        return capabilities.FirstOrDefault() ?? "unknown";
    }

    private Dictionary<string, object> ExtractParametersFromInput(string userInput, string toolName)
    {
        var parameters = new Dictionary<string, object>();
        
        switch (toolName)
        {
            case "get_weather":
                var location = ExtractLocation(userInput);
                if (!string.IsNullOrEmpty(location))
                    parameters["location"] = location;
                break;
                
            case "calculate":
                var expression = ExtractMathExpression(userInput);
                if (!string.IsNullOrEmpty(expression))
                    parameters["expression"] = expression;
                break;
        }

        return parameters;
    }

    private string ExtractLocation(string input)
    {
        // Simple location extraction - in production would use NLP
        var words = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var inIndex = Array.FindIndex(words, w => w.Equals("in", StringComparison.OrdinalIgnoreCase));
        
        if (inIndex >= 0 && inIndex < words.Length - 1)
        {
            return string.Join(" ", words.Skip(inIndex + 1)).Trim('?', '.', ',');
        }

        // Look for common city patterns
        var cities = new[] { "paris", "london", "tokyo", "new york", "san francisco", "los angeles" };
        foreach (var city in cities)
        {
            if (input.Contains(city, StringComparison.OrdinalIgnoreCase))
                return city;
        }

        return "current location";
    }

    private bool ContainsMathExpression(string input)
    {
        return input.Any(c => "+-*/%".Contains(c)) || 
               input.Contains("plus") || input.Contains("minus") || 
               input.Contains("times") || input.Contains("divided");
    }

    private string ExtractMathExpression(string input)
    {
        // Extract mathematical expressions from text
        var numbers = System.Text.RegularExpressions.Regex.Matches(input, @"\d+(\.\d+)?")
            .Select(m => m.Value).ToList();
            
        if (numbers.Count >= 2)
        {
            var operators = new[] { "+", "-", "*", "/", "plus", "minus", "times", "divided" };
            var foundOp = operators.FirstOrDefault(op => input.Contains(op));
            
            if (foundOp != null)
            {
                var mathOp = foundOp switch
                {
                    "plus" => "+",
                    "minus" => "-", 
                    "times" => "*",
                    "divided" => "/",
                    _ => foundOp
                };
                
                return $"{numbers[0]} {mathOp} {numbers[1]}";
            }
        }

        return input; // Return original if can't parse
    }

    private async Task<string?> ReadMcpResponseAsync(TimeSpan timeout)
    {
        if (_processOutput == null) return null;

        var cts = new CancellationTokenSource(timeout);
        
        try
        {
            var response = await _processOutput.ReadLineAsync().WaitAsync(cts.Token);
            return response;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("⏱️ MCP response timeout");
            return null;
        }
    }

    private string ParseMcpResponse(string jsonResponse)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonResponse);
            var root = doc.RootElement;

            // Check for errors
            if (root.TryGetProperty("error", out var errorElement))
            {
                var errorMessage = errorElement.GetProperty("message").GetString();
                return $"❌ MCP Error: {errorMessage}";
            }

            // Extract result
            if (root.TryGetProperty("result", out var resultElement))
            {
                // Handle different result formats
                if (resultElement.TryGetProperty("content", out var contentElement))
                {
                    if (contentElement.ValueKind == JsonValueKind.Array)
                    {
                        var firstContent = contentElement[0];
                        if (firstContent.TryGetProperty("text", out var textElement))
                        {
                            return textElement.GetString() ?? "No content";
                        }
                    }
                    else if (contentElement.TryGetProperty("text", out var textElement))
                    {
                        return textElement.GetString() ?? "No content";
                    }
                }
                
                // Try direct string result
                if (resultElement.ValueKind == JsonValueKind.String)
                {
                    return resultElement.GetString() ?? "No result";
                }

                // Return JSON if complex structure
                return resultElement.GetRawText();
            }

            return "❌ Unexpected response format";
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse MCP response: {Response}", jsonResponse);
            return $"❌ Parse error: {jsonResponse}";
        }
    }

    private async Task CleanupProcessAsync()
    {
        try
        {
            if (_mcpProcess != null && !_mcpProcess.HasExited)
            {
                _mcpProcess.Kill();
                await _mcpProcess.WaitForExitAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during MCP process cleanup");
        }
        
        _processInput?.Dispose();
        _processOutput?.Dispose();
        _mcpProcess?.Dispose();
        
        _processInput = null;
        _processOutput = null;
        _mcpProcess = null;
    }

    private static string EscapeArgument(string argument)
    {
        if (argument.Contains(' ') || argument.Contains('"'))
        {
            return $"\"{argument.Replace("\"", "\\\"")}\"";
        }
        return argument;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            CleanupProcessAsync().Wait(TimeSpan.FromSeconds(2));
            _disposed = true;
        }
    }
}