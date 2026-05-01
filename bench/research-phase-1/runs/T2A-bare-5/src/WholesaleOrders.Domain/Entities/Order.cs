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

    public Money TotalAmount { get; set; } = Money.Zero("USD");

    /// <summary>
    /// The promo code applied to this order at submission, or null if none.
    /// </summary>
    public string? PromoCode { get; set; }

    /// <summary>
    /// The flat discount percentage (0-100) applied at submission via <see cref="PromoCode"/>,
    /// or null if no discount was applied.
    /// </summary>
    public decimal? DiscountPercent { get; set; }

    public Money CalculateTotal()
    {
        var sum = LineItems.Aggregate(0m, (acc, li) => acc + li.Quantity * li.UnitPrice);
        return new Money(Math.Round(sum, 2, MidpointRounding.ToEven), Currency);
    }
}
