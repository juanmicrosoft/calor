using System.Threading.Tasks;

namespace MigrateFixture;

public sealed class UserService
{
    private readonly string? _fallbackName;

    public UserService(string? fallbackName)
    {
        _fallbackName = fallbackName;
    }

    public async Task<string> ResolveDisplayNameAsync(string? primaryName)
    {
        await Task.Yield();
        if (string.IsNullOrEmpty(primaryName))
        {
            return _fallbackName ?? "anonymous";
        }
        return primaryName;
    }

    public string? MaybeName(int userId)
    {
        return userId > 0 ? $"user-{userId}" : null;
    }
}
