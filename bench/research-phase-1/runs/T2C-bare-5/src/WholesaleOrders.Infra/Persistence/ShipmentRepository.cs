using WholesaleOrders.Domain.Entities;

namespace WholesaleOrders.Infra.Persistence;

public interface IShipmentRepository
{
    Task<Shipment?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<Shipment>> GetByOrderAsync(Guid orderId, CancellationToken ct = default);
    Task AddAsync(Shipment shipment, CancellationToken ct = default);
    Task UpdateAsync(Shipment shipment, CancellationToken ct = default);
}

public class ShipmentRepository : IShipmentRepository
{
    private readonly AppDbContext _db;

    public ShipmentRepository(AppDbContext db) => _db = db;

    public Task<Shipment?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_db.WithLock(() => _db.Shipments.FirstOrDefault(s => s.Id == id)));

    public Task<List<Shipment>> GetByOrderAsync(Guid orderId, CancellationToken ct = default) =>
        Task.FromResult(_db.WithLock(() => _db.Shipments.Where(s => s.OrderId == orderId).ToList()));

    public Task AddAsync(Shipment shipment, CancellationToken ct = default)
    {
        _db.WithLock(() => _db.Shipments.Add(shipment));
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Shipment shipment, CancellationToken ct = default)
    {
        _db.WithLock(() =>
        {
            var idx = _db.Shipments.FindIndex(s => s.Id == shipment.Id);
            if (idx >= 0) _db.Shipments[idx] = shipment;
        });
        return Task.CompletedTask;
    }
}
