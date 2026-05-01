using WholesaleOrders.Domain.ValueObjects;

namespace WholesaleOrders.Domain.Entities;

public class InventoryItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Sku Sku { get; init; }
    public string Name { get; init; } = "";

    public int OnHand { get; set; }
    public int Reserved { get; set; }

    public int Available => OnHand - Reserved;

    public decimal UnitPrice { get; init; }
}
