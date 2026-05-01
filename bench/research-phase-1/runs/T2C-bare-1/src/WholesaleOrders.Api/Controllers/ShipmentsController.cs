using Microsoft.AspNetCore.Mvc;
using WholesaleOrders.Services;

namespace WholesaleOrders.Api.Controllers;

[ApiController]
[Route("api/shipments")]
public class ShipmentsController : ControllerBase
{
    private readonly IShipmentService _shipments;

    public ShipmentsController(IShipmentService shipments) => _shipments = shipments;

    public sealed record CreateShipmentRequest(Guid OrderId, string Carrier);
    public sealed record InTransitRequest(string TrackingNumber);

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateShipmentRequest req, CancellationToken ct)
    {
        var shipment = await _shipments.CreateShipmentAsync(req.OrderId, req.Carrier, ct);
        return Ok(shipment);
    }

    [HttpPost("{shipmentId:guid}/in-transit")]
    public async Task<IActionResult> MarkInTransit(Guid shipmentId, [FromBody] InTransitRequest req, CancellationToken ct)
    {
        var shipment = await _shipments.MarkInTransitAsync(shipmentId, req.TrackingNumber, ct);
        return Ok(shipment);
    }

    [HttpPost("{shipmentId:guid}/delivered")]
    public async Task<IActionResult> MarkDelivered(Guid shipmentId, CancellationToken ct)
    {
        var shipment = await _shipments.MarkDeliveredAsync(shipmentId, ct);
        return Ok(shipment);
    }

    [HttpGet("schedulable")]
    public async Task<IActionResult> Schedulable(CancellationToken ct)
    {
        var orders = await _shipments.GetSchedulableOrdersAsync(ct);
        return Ok(orders);
    }
}
