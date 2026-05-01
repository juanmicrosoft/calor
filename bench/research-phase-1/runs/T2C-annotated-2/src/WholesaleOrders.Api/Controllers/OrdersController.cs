using Microsoft.AspNetCore.Mvc;
using WholesaleOrders.Domain.Entities;
using WholesaleOrders.Domain.ValueObjects;
using WholesaleOrders.Services;

namespace WholesaleOrders.Api.Controllers;

[ApiController]
[Route("api/orders")]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orders;

    public OrdersController(IOrderService orders) => _orders = orders;

    public sealed record CreateOrderRequest(Guid CustomerId, string Currency = "USD");
    public sealed record AddLineItemRequest(string Sku, int Quantity, decimal UnitPrice);

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateOrderRequest req, CancellationToken ct)
    {
        var order = await _orders.CreateDraftAsync(req.CustomerId, req.Currency, ct);
        return Ok(order);
    }

    [HttpPost("{orderId:guid}/items")]
    public async Task<IActionResult> AddItem(Guid orderId, [FromBody] AddLineItemRequest req, CancellationToken ct)
    {
        var order = await _orders.AddLineItemAsync(orderId, Sku.Parse(req.Sku), req.Quantity, req.UnitPrice, ct);
        return Ok(order);
    }

    [HttpPost("{orderId:guid}/submit")]
    public async Task<IActionResult> Submit(Guid orderId, CancellationToken ct)
    {
        var order = await _orders.SubmitAsync(orderId, ct);
        return Ok(order);
    }

    [HttpPost("{orderId:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid orderId, CancellationToken ct)
    {
        var order = await _orders.CancelAsync(orderId, ct);
        return Ok(order);
    }

    [HttpPost("{orderId:guid}/recall")]
    public async Task<IActionResult> Recall(Guid orderId, CancellationToken ct)
    {
        var order = await _orders.RecallAsync(orderId, ct);
        return Ok(order);
    }
}
