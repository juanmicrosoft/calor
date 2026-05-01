using WholesaleOrders.Domain.Entities;

namespace WholesaleOrders.Infra.Persistence;

public interface IPaymentRepository
{
    Task<Payment?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<Payment>> GetByOrderAsync(Guid orderId, CancellationToken ct = default);
    Task AddAsync(Payment payment, CancellationToken ct = default);
    Task UpdateAsync(Payment payment, CancellationToken ct = default);
}

public class PaymentRepository : IPaymentRepository
{
    private readonly AppDbContext _db;

    public PaymentRepository(AppDbContext db) => _db = db;

    public Task<Payment?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_db.WithLock(() => _db.Payments.FirstOrDefault(p => p.Id == id)));

    public Task<List<Payment>> GetByOrderAsync(Guid orderId, CancellationToken ct = default) =>
        Task.FromResult(_db.WithLock(() => _db.Payments.Where(p => p.OrderId == orderId).ToList()));

    public Task AddAsync(Payment payment, CancellationToken ct = default)
    {
        _db.WithLock(() => _db.Payments.Add(payment));
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Payment payment, CancellationToken ct = default)
    {
        _db.WithLock(() =>
        {
            var idx = _db.Payments.FindIndex(p => p.Id == payment.Id);
            if (idx >= 0) _db.Payments[idx] = payment;
        });
        return Task.CompletedTask;
    }
}
