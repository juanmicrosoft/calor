using WholesaleOrders.Domain.Entities;

namespace WholesaleOrders.Infra.Persistence;

public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<Order>> GetByCustomerAsync(Guid customerId, CancellationToken ct = default);
    Task<List<Order>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(Order order, CancellationToken ct = default);
    Task UpdateAsync(Order order, CancellationToken ct = default);
    Task<List<OrderLineItem>> GetLineItemsAsync(Guid orderId, CancellationToken ct = default);
    Task AddLineItemAsync(OrderLineItem item, CancellationToken ct = default);
}

public class OrderRepository : IOrderRepository
{
    private readonly AppDbContext _db;

    public OrderRepository(AppDbContext db) => _db = db;

    public Task<Order?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_db.WithLock(() => _db.Orders.FirstOrDefault(o => o.Id == id)));

    public Task<List<Order>> GetByCustomerAsync(Guid customerId, CancellationToken ct = default) =>
        Task.FromResult(_db.WithLock(() => _db.Orders.Where(o => o.CustomerId == customerId).ToList()));

    public Task<List<Order>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult(_db.WithLock(() => _db.Orders.ToList()));

    public Task AddAsync(Order order, CancellationToken ct = default)
    {
        _db.WithLock(() => _db.Orders.Add(order));
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Order order, CancellationToken ct = default)
    {
        _db.WithLock(() =>
        {
            var idx = _db.Orders.FindIndex(o => o.Id == order.Id);
            if (idx >= 0) _db.Orders[idx] = order;
        });
        return Task.CompletedTask;
    }

    public Task<List<OrderLineItem>> GetLineItemsAsync(Guid orderId, CancellationToken ct = default) =>
        Task.FromResult(_db.WithLock(() => _db.OrderLineItems.Where(li => li.OrderId == orderId).ToList()));

    public Task AddLineItemAsync(OrderLineItem item, CancellationToken ct = default)
    {
        _db.WithLock(() => _db.OrderLineItems.Add(item));
        return Task.CompletedTask;
    }
}
