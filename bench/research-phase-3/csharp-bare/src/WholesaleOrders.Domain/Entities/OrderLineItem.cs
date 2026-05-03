using WholesaleOrders.Domain.ValueObjects;

namespace WholesaleOrders.Domain.Entities;

public class OrderLineItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid OrderId { get; init; }
    public required Sku Sku { get; init; }

    public int Quantity { get; set; }
    public decimal UnitPrice { get; init; }

    public string Description { get; init; } = "";
}
