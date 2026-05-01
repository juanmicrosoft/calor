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
    /// Promo code applied at submission time. Null when no promo was used.
    /// </summary>
    public string? PromoCode { get; set; }

    /// <summary>
    /// Discount percentage applied at submission (0..100). 0 when no promo was used.
    /// </summary>
    public decimal DiscountPercentage { get; set; } = 0m;

    /// <summary>
    /// Order subtotal (sum of line items) before any discount. Equals TotalAmount when no promo applied.
    /// </summary>
    public Money Subtotal { get; set; } = Money.Zero("USD");

    // PURE: no effects. Computes total from line items.
    // POSTCONDITION: result.Currency == Currency.
    public Money CalculateTotal()
    {
        var sum = LineItems.Aggregate(0m, (acc, li) => acc + li.Quantity * li.UnitPrice);
        return new Money(Math.Round(sum, 2, MidpointRounding.ToEven), Currency);
    }
}
