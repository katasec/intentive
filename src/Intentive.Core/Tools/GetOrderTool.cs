using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Intentive.Core.Models;

namespace Intentive.Core.Tools;

/// <summary>
/// Sample tool that retrieves order information by ID - SK Function format
/// </summary>
public class GetOrderTool
{
    private readonly ILogger<GetOrderTool> _logger;

    public GetOrderTool(ILogger<GetOrderTool> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Retrieves order information by order ID
    /// </summary>
    [KernelFunction("GetOrder")]
    [Description("Retrieves order information by order ID")]
    public async Task<string> GetOrderAsync(
        [Description("The order ID to look up")] string orderId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Looking up order: {OrderId}", orderId);
            
            // Validate order ID
            if (string.IsNullOrWhiteSpace(orderId))
            {
                return "Error: Order ID cannot be empty";
            }

            // Simulate async database lookup
            await Task.Delay(100, cancellationToken);

            // Mock order data based on ID
            var orderData = orderId switch
            {
                "12345" => new
                {
                    OrderId = "12345",
                    Status = "Shipped",
                    CustomerName = "John Doe",
                    Items = new[] { "Widget A", "Widget B" },
                    Total = 99.99,
                    TrackingNumber = "1Z999AA1234567890",
                    EstimatedDelivery = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2))
                },
                "67890" => new
                {
                    OrderId = "67890",
                    Status = "Processing",
                    CustomerName = "Jane Smith",
                    Items = new[] { "Gadget X" },
                    Total = 149.99,
                    TrackingNumber = (string?)null,
                    EstimatedDelivery = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5))
                },
                _ => null
            };

            if (orderData == null)
            {
                _logger.LogWarning("Order not found: {OrderId}", orderId);
                return $"Order {orderId} not found. Please check the order ID and try again.";
            }

            _logger.LogInformation("Order found: {OrderId} - Status: {Status}", orderId, orderData.Status);
            
            // Return formatted order information
            var response = $@"Order Information:
- Order ID: {orderData.OrderId}
- Status: {orderData.Status}
- Customer: {orderData.CustomerName}
- Items: {string.Join(", ", orderData.Items)}
- Total: ${orderData.Total:F2}";
            
            if (!string.IsNullOrEmpty(orderData.TrackingNumber))
            {
                response += $"\n- Tracking Number: {orderData.TrackingNumber}";
            }
            
            response += $"\n- Estimated Delivery: {orderData.EstimatedDelivery}";
            
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving order {OrderId}", orderId);
            return $"Sorry, I encountered an error while looking up order {orderId}. Please try again later.";
        }
    }
}