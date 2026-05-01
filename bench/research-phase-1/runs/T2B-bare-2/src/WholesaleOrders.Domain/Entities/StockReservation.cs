using WholesaleOrders.Domain.Enums;
using WholesaleOrders.Domain.ValueObjects;

namespace WholesaleOrders.Domain.Entities;

public class StockReservation
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid OrderId { get; init; }
    public Sku Sku { get; init; }
    public int Quantity { get; set; }
    public ReservationStatus Status { get; set; } = ReservationStatus.Created;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ConfirmedAt { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }
    public DateTimeOffset? FulfilledAt { get; set; }
}
