using System.Data;
using System.Data.Common;

namespace TestAssembly.DataAccess;

// Simulates a realistic 3-layer data access pattern:
// UserService.CreateUser() → UserRepository.Save() → DbCommand.ExecuteNonQuery()

/// <summary>
/// Service layer — calls repository methods.
/// </summary>
public class UserService
{
    private readonly UserRepository _repo = new();

    public void CreateUser(string name)
    {
        var user = new User { Name = name };
        _repo.Save(user);
    }

    public User? GetUser(int id)
    {
        return _repo.FindById(id);
    }

    public void ProcessBatch(IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            CreateUser(name);
        }
    }
}

/// <summary>
/// Repository layer — directly uses DbCommand.
/// </summary>
public class UserRepository
{
    public void Save(User user)
    {
        using var conn = CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO Users (Name) VALUES (@name)";
        cmd.ExecuteNonQuery(); // db:w seed
    }

    public User? FindById(int id)
    {
        using var conn = CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Users WHERE Id = @id";
        using var reader = cmd.ExecuteReader(); // db:r seed
        if (reader.Read())
        {
            return new User { Id = id, Name = reader.GetString(0) };
        }
        return null;
    }

    private static DbConnection CreateConnection()
    {
        // Returns a concrete connection — in tests this won't actually connect
        throw new NotImplementedException("Test assembly — not meant to be executed");
    }
}

/// <summary>
/// Pure computation — no effects.
/// </summary>
public static class MathHelper
{
    public static int Add(int a, int b) => a + b;
    public static int Multiply(int a, int b) => a * b;
    public static double Distance(double x1, double y1, double x2, double y2)
        => Math.Sqrt(Math.Pow(x2 - x1, 2) + Math.Pow(y2 - y1, 2));
}

public class User
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}
