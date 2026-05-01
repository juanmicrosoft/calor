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
    /// Promo code applied at submission, if any. Null when no promo was applied.
    /// </summary>
    public string? PromoCode { get; set; }

    /// <summary>
    /// Discount percent applied at submission (e.g. 0.10m for 10%). Zero when no promo applied.
    /// </summary>
    public decimal DiscountPercent { get; set; }

    /// <summary>
    /// Order total after promo discount is applied. Equals TotalAmount when no promo is applied.
    /// </summary>
    public Money DiscountedTotal { get; set; } = Money.Zero("USD");

    // PURE: no effects. Computes total from line items.
    // POSTCONDITION: result.Currency == Currency.
    public Money CalculateTotal()
    {
        var sum = LineItems.Aggregate(0m, (acc, li) => acc + li.Quantity * li.UnitPrice);
        return new Money(Math.Round(sum, 2, MidpointRounding.ToEven), Currency);
    }
}
