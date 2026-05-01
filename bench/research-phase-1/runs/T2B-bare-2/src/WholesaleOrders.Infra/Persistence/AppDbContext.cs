using WholesaleOrders.Domain.Entities;

namespace WholesaleOrders.Infra.Persistence;

/// <summary>
/// In-memory data store. Synthetic scaffold — no EF Core, just thread-safe lists.
/// Production would replace this with EF Core + a real DB; the repository contracts
/// are written to make that swap feasible.
/// </summary>
public class AppDbContext
{
    private readonly object _lock = new();

    public List<Order> Orders { get; } = new();
    public List<OrderLineItem> OrderLineItems { get; } = new();
    public List<Customer> Customers { get; } = new();
    public List<InventoryItem> InventoryItems { get; } = new();
    public List<StockReservation> Reservations { get; } = new();
    public List<Payment> Payments { get; } = new();
    public List<Shipment> Shipments { get; } = new();

    public T WithLock<T>(Func<T> action)
    {
        lock (_lock) return action();
    }

    public void WithLock(Action action)
    {
        lock (_lock) action();
    }
}
