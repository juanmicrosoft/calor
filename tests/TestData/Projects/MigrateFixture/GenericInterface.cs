namespace MigrateFixture;

public interface IReadRepository<T>
{
    T? Get(int id);
    int Count();
}

public sealed class InMemoryStringRepo : IReadRepository<string>
{
    public string? Get(int id)
    {
        return id > 0 ? $"row-{id}" : null;
    }

    public int Count()
    {
        return 42;
    }
}
