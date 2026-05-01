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
    /// The promo code applied at submission, if any. Null when no code was used.
    /// Set only at order submission and never modified afterwards.
    /// </summary>
    public string? PromoCode { get; set; }

    /// <summary>
    /// The discount percentage applied at submission, in the range [0, 1].
    /// Zero when no promo code was used.
    /// </summary>
    public decimal DiscountPercentage { get; set; }

    /// <summary>
    /// The discount amount in the order's currency. Zero when no promo code was used.
    /// </summary>
    public Money DiscountAmount { get; set; } = Money.Zero("USD");

    // PURE: no effects. Computes total from line items.
    // POSTCONDITION: result.Currency == Currency.
    public Money CalculateTotal()
    {
        var sum = LineItems.Aggregate(0m, (acc, li) => acc + li.Quantity * li.UnitPrice);
        return new Money(Math.Round(sum, 2, MidpointRounding.ToEven), Currency);
    }
}
