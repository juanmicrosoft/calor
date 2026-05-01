using WholesaleOrders.Domain.Enums;
using WholesaleOrders.Domain.ValueObjects;

namespace WholesaleOrders.Domain.Entities;

public class Order
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid CustomerId { get; init; }
    public OrderStatus Status { get; set; } = OrderStatus.Draft;
    public string Currency { get; init; } = "USD";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public DateTimeOffset? ShippedAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }

    public List<OrderLineItem> LineItems { get; } = new();

    /// <summary>
    /// Promo code applied at submission. Null if no promo was used.
    /// </summary>
    public string? PromoCode { get; set; }

    /// <summary>
    /// Percentage discount applied at submission (e.g. 10m for 10% off). Zero if no promo applied.
    /// </summary>
    public decimal DiscountPercentage { get; set; }

    public Money TotalAmount { get; set; } = Money.Zero("USD");
    public Money CalculateTotal()
    {
        var sum = LineItems.Aggregate(0m, (acc, li) => acc + li.Quantity * li.UnitPrice);
        return new Money(Math.Round(sum, 2, MidpointRounding.ToEven), Currency);
    }
}
