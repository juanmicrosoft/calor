using WholesaleOrders.Domain.Entities;
using WholesaleOrders.Domain.ValueObjects;

namespace WholesaleOrders.Infra.Persistence;

public interface IInventoryRepository
{
    Task<InventoryItem?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<InventoryItem?> GetBySkuAsync(Sku sku, CancellationToken ct = default);
    Task AddAsync(InventoryItem item, CancellationToken ct = default);
    Task UpdateAsync(InventoryItem item, CancellationToken ct = default);
    Task<List<StockReservation>> GetReservationsForOrderAsync(Guid orderId, CancellationToken ct = default);
    Task<StockReservation?> GetReservationByIdAsync(Guid reservationId, CancellationToken ct = default);
    Task AddReservationAsync(StockReservation reservation, CancellationToken ct = default);
    Task UpdateReservationAsync(StockReservation reservation, CancellationToken ct = default);
}

public class InventoryRepository : IInventoryRepository
{
    private readonly AppDbContext _db;

    public InventoryRepository(AppDbContext db) => _db = db;

    // EFFECTS: db:r.
#pragma warning disable CS1998
    public async Task<InventoryItem?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return _db.WithLock(() => _db.InventoryItems.FirstOrDefault(i => i.Id == id));
    }
#pragma warning restore CS1998

    public Task<InventoryItem?> GetBySkuAsync(Sku sku, CancellationToken ct = default) =>
        Task.FromResult(_db.WithLock(() => _db.InventoryItems.FirstOrDefault(i => i.Sku.Value == sku.Value)));

    public Task AddAsync(InventoryItem item, CancellationToken ct = default)
    {
        _db.WithLock(() => _db.InventoryItems.Add(item));
        return Task.CompletedTask;
    }

    public Task UpdateAsync(InventoryItem item, CancellationToken ct = default)
    {
        _db.WithLock(() =>
        {
            var idx = _db.InventoryItems.FindIndex(i => i.Id == item.Id);
            if (idx >= 0) _db.InventoryItems[idx] = item;
        });
        return Task.CompletedTask;
    }

    public Task<List<StockReservation>> GetReservationsForOrderAsync(Guid orderId, CancellationToken ct = default) =>
        Task.FromResult(_db.WithLock(() => _db.Reservations.Where(r => r.OrderId == orderId).ToList()));

    public Task<StockReservation?> GetReservationByIdAsync(Guid reservationId, CancellationToken ct = default) =>
        Task.FromResult(_db.WithLock(() => _db.Reservations.FirstOrDefault(r => r.Id == reservationId)));

    public Task AddReservationAsync(StockReservation reservation, CancellationToken ct = default)
    {
        _db.WithLock(() => _db.Reservations.Add(reservation));
        return Task.CompletedTask;
    }

    public Task UpdateReservationAsync(StockReservation reservation, CancellationToken ct = default)
    {
        _db.WithLock(() =>
        {
            var idx = _db.Reservations.FindIndex(r => r.Id == reservation.Id);
            if (idx >= 0) _db.Reservations[idx] = reservation;
        });
        return Task.CompletedTask;
    }
}
