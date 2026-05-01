using Microsoft.AspNetCore.Mvc;
using WholesaleOrders.Domain.ValueObjects;
using WholesaleOrders.Services;

namespace WholesaleOrders.Api.Controllers;

[ApiController]
[Route("api/inventory")]
public class InventoryController : ControllerBase
{
    private readonly IInventoryService _inventory;

    public InventoryController(IInventoryService inventory) => _inventory = inventory;

    public sealed record AddItemRequest(string Sku, string Name, int OnHand, decimal UnitPrice);
    public sealed record ReserveRequest(Guid OrderId, string Sku, int Quantity);
    public sealed record PartialReleaseRequest(int Quantity);

    [HttpPost("items")]
    public async Task<IActionResult> Add([FromBody] AddItemRequest req, CancellationToken ct)
    {
        var item = await _inventory.AddItemAsync(Sku.Parse(req.Sku), req.Name, req.OnHand, req.UnitPrice, ct);
        return Ok(item);
    }

    [HttpPost("reserve")]
    public async Task<IActionResult> Reserve([FromBody] ReserveRequest req, CancellationToken ct)
    {
        var reservation = await _inventory.ReserveAsync(req.OrderId, Sku.Parse(req.Sku), req.Quantity, ct);
        return Ok(reservation);
    }

    [HttpPost("reservations/{reservationId:guid}/confirm")]
    public async Task<IActionResult> Confirm(Guid reservationId, CancellationToken ct)
    {
        var reservation = await _inventory.ConfirmAsync(reservationId, ct);
        return Ok(reservation);
    }

    [HttpPost("reservations/{reservationId:guid}/release")]
    public async Task<IActionResult> Release(Guid reservationId, CancellationToken ct)
    {
        var reservation = await _inventory.ReleaseAsync(reservationId, ct);
        return Ok(reservation);
    }

    [HttpPost("reservations/{reservationId:guid}/partial-release")]
    public async Task<IActionResult> PartialRelease(Guid reservationId, [FromBody] PartialReleaseRequest req, CancellationToken ct)
    {
        var reservation = await _inventory.PartialReleaseAsync(reservationId, req.Quantity, ct);
        return Ok(reservation);
    }
}
