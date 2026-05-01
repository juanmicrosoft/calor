using WholesaleOrders.Domain.Entities;
using WholesaleOrders.Domain.Enums;
using WholesaleOrders.Infra.Logging;
using WholesaleOrders.Infra.Persistence;

namespace WholesaleOrders.Services;

public interface IShipmentService
{
    Task<Shipment> CreateShipmentAsync(Guid orderId, string carrier, CancellationToken ct = default);
    Task<Shipment> MarkInTransitAsync(Guid shipmentId, string trackingNumber, CancellationToken ct = default);
    Task<Shipment> MarkDeliveredAsync(Guid shipmentId, CancellationToken ct = default);
    Task<List<Order>> GetSchedulableOrdersAsync(CancellationToken ct = default);
}

public class ShipmentService : IShipmentService
{
    private readonly IShipmentRepository _shipments;
    private readonly IOrderRepository _orders;
    private readonly IStructuredLogger _logger;

    public ShipmentService(IShipmentRepository shipments, IOrderRepository orders, IStructuredLogger logger)
    {
        _shipments = shipments;
        _orders = orders;
        _logger = logger;
    }
    public async Task<Shipment> CreateShipmentAsync(Guid orderId, string carrier, CancellationToken ct = default)
    {
        var order = await _orders.GetByIdAsync(orderId, ct)
            ?? throw new InvalidOperationException($"Order {orderId} not found.");
        if (order.Status != OrderStatus.Paid)
            throw new InvalidOperationException($"Cannot create shipment for order in status {order.Status}.");

        var shipment = new Shipment { OrderId = orderId, Carrier = carrier };
        await _shipments.AddAsync(shipment, ct);
        return shipment;
    }
    public async Task<Shipment> MarkInTransitAsync(Guid shipmentId, string trackingNumber, CancellationToken ct = default)
    {
        var shipment = await _shipments.GetByIdAsync(shipmentId, ct)
            ?? throw new InvalidOperationException($"Shipment {shipmentId} not found.");
        if (shipment.Status != ShipmentStatus.Pending)
            throw new InvalidOperationException($"Cannot mark in-transit a shipment in status {shipment.Status}.");
        shipment.Status = ShipmentStatus.InTransit;
        shipment.TrackingNumber = trackingNumber;
        shipment.ShippedAt = DateTimeOffset.UtcNow;
        await _shipments.UpdateAsync(shipment, ct);
        return shipment;
    }
    public async Task<Shipment> MarkDeliveredAsync(Guid shipmentId, CancellationToken ct = default)
    {
        var shipment = await _shipments.GetByIdAsync(shipmentId, ct)
            ?? throw new InvalidOperationException($"Shipment {shipmentId} not found.");
        if (shipment.Status != ShipmentStatus.InTransit)
            throw new InvalidOperationException($"Cannot mark delivered a shipment in status {shipment.Status}.");
        shipment.Status = ShipmentStatus.Delivered;
        shipment.DeliveredAt = DateTimeOffset.UtcNow;
        await _shipments.UpdateAsync(shipment, ct);
        return shipment;
    }

    /// <summary>
    /// Returns Paid orders that are eligible to be scheduled for shipment, in
    /// the order they should be processed. Today: FIFO by SubmittedAt.
    /// </summary>
    public async Task<List<Order>> GetSchedulableOrdersAsync(CancellationToken ct = default)
    {
        var all = await _orders.GetAllAsync(ct);
        return all
            .Where(o => o.Status == OrderStatus.Paid)
            .OrderBy(o => o.SubmittedAt ?? o.CreatedAt)
            .ToList();
    }
}
