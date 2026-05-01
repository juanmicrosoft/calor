using WholesaleOrders.Domain.Entities;

namespace WholesaleOrders.Infra.Persistence;

public interface ICustomerRepository
{
    Task<Customer?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(Customer customer, CancellationToken ct = default);
}

public class CustomerRepository : ICustomerRepository
{
    private readonly AppDbContext _db;

    public CustomerRepository(AppDbContext db) => _db = db;

    public Task<Customer?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_db.WithLock(() => _db.Customers.FirstOrDefault(c => c.Id == id)));

    public Task AddAsync(Customer customer, CancellationToken ct = default)
    {
        _db.WithLock(() => _db.Customers.Add(customer));
        return Task.CompletedTask;
    }
}
