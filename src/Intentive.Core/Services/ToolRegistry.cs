using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Intentive.Core.Models;

namespace Intentive.Core.Services;

/// <summary>
/// Tool registry that discovers local tools and MCP servers, maintaining a unified catalog
/// </summary>
public class ToolRegistry
{
    private readonly ILogger _logger;
    private readonly Dictionary<string, ToolDescriptor> _tools = new();
    private readonly Dictionary<string, McpClient> _mcpClients = new();
    private readonly List<string> _availableCapabilities = new();

    public ToolRegistry(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Initialize tool registry from configuration
    /// </summary>
    public async Task InitializeAsync(string configPath = "tools.json")
    {
        _logger.LogInformation("🔧 Initializing Tool Registry...");
        
        if (!File.Exists(configPath))
        {
            _logger.LogWarning("❌ Tools config not found: {ConfigPath}", configPath);
            return;
        }

        var json = await File.ReadAllTextAsync(configPath);
        var config = JsonSerializer.Deserialize<ToolsConfiguration>(json, new JsonSerializerOptions 
        { 
            PropertyNameCaseInsensitive = true 
        });

        if (config == null)
        {
            _logger.LogError("❌ Failed to parse tools configuration");
            return;
        }

        // 1. Discover and register local tools
        await DiscoverLocalToolsAsync(config.LocalTools);
        
        // 2. Connect to MCP servers
        await ConnectMcpServersAsync(config.McpServers);
        
        // 3. Build unified capabilities list
        BuildCapabilitiesList();
        
        _logger.LogInformation("✅ Tool Registry initialized - {ToolCount} tools, {CapabilityCount} capabilities", 
            _tools.Count, _availableCapabilities.Count);
    }

    /// <summary>
    /// Get all available capabilities for display to user
    /// </summary>
    public List<string> GetAvailableCapabilities() => new(_availableCapabilities);

    /// <summary>
    /// Find tool for given capability
    /// </summary>
    public ToolDescriptor? FindToolByCapability(string capability)
    {
        return _tools.Values.FirstOrDefault(tool => 
            tool.Capabilities.Contains(capability, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Execute a tool with given input
    /// </summary>
    public async Task<string> ExecuteToolAsync(string toolName, string userInput, Dictionary<string, object>? parameters = null)
    {
        if (!_tools.TryGetValue(toolName, out var tool))
        {
            return $"❌ Tool '{toolName}' not found";
        }

        try
        {
            _logger.LogInformation("⚡ Executing tool: {ToolName}", toolName);
            var startTime = DateTime.UtcNow;

            string result = tool.Type switch
            {
                ToolType.Local => await ExecuteLocalToolAsync(tool, parameters ?? new()),
                ToolType.MCP => await ExecuteMcpToolAsync(tool, userInput, parameters),
                _ => "❌ Unknown tool type"
            };

            var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;
            _logger.LogInformation("✅ Tool executed in {Duration}ms: {ToolName}", duration, toolName);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Tool execution failed: {ToolName}", toolName);
            return $"❌ Error executing {toolName}: {ex.Message}";
        }
    }

    /// <summary>
    /// Get all tools for debugging/display
    /// </summary>
    public Dictionary<string, ToolDescriptor> GetAllTools() => new(_tools);

    private async Task DiscoverLocalToolsAsync(List<LocalToolConfig> localToolConfigs)
    {
        _logger.LogInformation("🔍 Discovering local tools...");
        
        foreach (var toolConfig in localToolConfigs.Where(t => t.Enabled))
        {
            try
            {
                var type = Type.GetType(toolConfig.Class);
                if (type == null)
                {
                    _logger.LogWarning("⚠️ Local tool class not found: {Class}", toolConfig.Class);
                    continue;
                }

                // Find method with KernelFunction attribute
                var method = type.GetMethods()
                    .FirstOrDefault(m => m.GetCustomAttribute<KernelFunctionAttribute>()?.Name == toolConfig.Method ||
                                         m.Name == toolConfig.Method);

                if (method == null)
                {
                    _logger.LogWarning("⚠️ Tool method not found: {Method} in {Class}", toolConfig.Method, toolConfig.Class);
                    continue;
                }

                var descriptor = new ToolDescriptor
                {
                    Name = toolConfig.Name,
                    Type = ToolType.Local,
                    Description = toolConfig.Description,
                    Capabilities = toolConfig.Capabilities,
                    LocalType = type,
                    LocalMethod = method,
                    Parameters = toolConfig.Parameters
                };

                _tools[toolConfig.Name] = descriptor;
                _logger.LogInformation("✅ Local tool registered: {Name} ({CapabilityCount} capabilities)", 
                    toolConfig.Name, toolConfig.Capabilities.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to register local tool: {Name}", toolConfig.Name);
            }
        }
    }

    private async Task ConnectMcpServersAsync(List<McpServerConfig> mcpServerConfigs)
    {
        _logger.LogInformation("🌐 Connecting to MCP servers...");
        
        foreach (var serverConfig in mcpServerConfigs.Where(s => s.Enabled))
        {
            try
            {
                _logger.LogInformation("🔗 Connecting to MCP server: {Name}", serverConfig.Name);
                
                var mcpClient = new McpClient(_logger);
                var success = await mcpClient.ConnectAsync(serverConfig);
                
                if (success)
                {
                    _mcpClients[serverConfig.Name] = mcpClient;
                    
                    var descriptor = new ToolDescriptor
                    {
                        Name = serverConfig.Name,
                        Type = ToolType.MCP,
                        Description = serverConfig.Description,
                        Capabilities = serverConfig.Capabilities,
                        McpClient = mcpClient
                    };
                    
                    _tools[serverConfig.Name] = descriptor;
                    _logger.LogInformation("✅ MCP server connected: {Name} ({CapabilityCount} capabilities)", 
                        serverConfig.Name, serverConfig.Capabilities.Count);
                }
                else
                {
                    _logger.LogWarning("⚠️ Failed to connect to MCP server: {Name}", serverConfig.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ MCP server connection failed: {Name}", serverConfig.Name);
            }
        }
    }

    private void BuildCapabilitiesList()
    {
        _availableCapabilities.Clear();
        _availableCapabilities.AddRange(_tools.Values.SelectMany(t => t.Capabilities).Distinct());
        _availableCapabilities.Sort();
    }

    private async Task<string> ExecuteLocalToolAsync(ToolDescriptor tool, Dictionary<string, object> parameters)
    {
        if (tool.LocalType == null || tool.LocalMethod == null)
        {
            return "❌ Local tool not properly configured";
        }

        // Create instance of the tool class
        var toolInstance = Activator.CreateInstance(tool.LocalType, _logger);
        
        if (toolInstance == null)
        {
            return "❌ Could not create tool instance";
        }

        // Prepare method parameters
        var methodParams = tool.LocalMethod.GetParameters();
        var args = new object[methodParams.Length];
        
        for (int i = 0; i < methodParams.Length; i++)
        {
            var paramInfo = methodParams[i];
            
            if (paramInfo.ParameterType == typeof(CancellationToken))
            {
                args[i] = CancellationToken.None;
            }
            else if (parameters.TryGetValue(paramInfo.Name ?? "", out var value))
            {
                args[i] = Convert.ChangeType(value, paramInfo.ParameterType) ?? "";
            }
            else
            {
                // Use default value or empty string for strings
                args[i] = paramInfo.ParameterType == typeof(string) ? "" : 
                         paramInfo.HasDefaultValue ? paramInfo.DefaultValue! : 
                         Activator.CreateInstance(paramInfo.ParameterType)!;
            }
        }

        // Invoke the method
        var result = tool.LocalMethod.Invoke(toolInstance, args);
        
        if (result is Task<string> taskResult)
        {
            return await taskResult;
        }
        
        return result?.ToString() ?? "❌ No result from tool";
    }

    private async Task<string> ExecuteMcpToolAsync(ToolDescriptor tool, string userInput, Dictionary<string, object>? parameters)
    {
        if (tool.McpClient == null)
        {
            return "❌ MCP client not available";
        }

        return await tool.McpClient.ExecuteAsync(userInput, parameters ?? new());
    }

    public void Dispose()
    {
        foreach (var client in _mcpClients.Values)
        {
            client.Dispose();
        }
        _mcpClients.Clear();
        _tools.Clear();
    }
}