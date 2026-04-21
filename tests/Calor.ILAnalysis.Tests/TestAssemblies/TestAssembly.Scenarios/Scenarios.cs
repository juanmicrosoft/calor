using System.Data.Common;
using System.Runtime.CompilerServices;

namespace TestAssembly.Scenarios;

// ============================================================
// Async scenarios
// ============================================================

public class AsyncService
{
    public async Task SaveAsync()
    {
        await Task.Delay(1);
        var repo = new RealDbCaller();
        repo.ExecuteWrite();
    }

    public async Task<int> ReadAsync()
    {
        await Task.Delay(1);
        var repo = new RealDbCaller();
        return (int)(repo.ExecuteRead() ?? 0);
    }
}

// ============================================================
// Iterator scenarios
// ============================================================

public class IteratorService
{
    public IEnumerable<int> GetItemsWithSideEffect()
    {
        var caller = new RealDbCaller();
        caller.ExecuteWrite();
        yield return 1;
        yield return 2;
        yield return 3;
    }
}

// ============================================================
// Virtual dispatch scenarios
// ============================================================

public interface IStore
{
    void Save(string data);
}

public class SqlStore : IStore
{
    public void Save(string data)
    {
        var caller = new RealDbCaller();
        caller.ExecuteWrite();
    }
}

public class FileStore : IStore
{
    public void Save(string data)
    {
        File.WriteAllText("/tmp/test.txt", data); // fs:w
    }
}

public class ServiceWithInterface
{
    private readonly IStore _store;
    public ServiceWithInterface(IStore store) { _store = store; }
    public void Process(string data) => _store.Save(data);
}

// ============================================================
// Delegate / ldftn scenarios
// ============================================================

public class DelegateService
{
    public void ProcessWithDelegate()
    {
        var items = new[] { "a", "b", "c" };
        var caller = new RealDbCaller();
        // This generates ldftn for the lambda
        Array.ForEach(items, item => caller.ExecuteWrite());
    }

    public void ProcessWithAction()
    {
        Action doWork = () =>
        {
            var c = new RealDbCaller();
            c.ExecuteWrite();
        };
        doWork();
    }
}

// ============================================================
// Overload scenarios (same-arity)
// ============================================================

public class OverloadService
{
    public void Process(int count)
    {
        // Pure — just arithmetic
        var result = count * 2;
        _ = result;
    }

    public void Process(string data)
    {
        // Effectful — writes to DB
        var caller = new RealDbCaller();
        caller.ExecuteWrite();
    }
}

// ============================================================
// Deep chain scenario
// ============================================================

public static class DeepChain
{
    public static void Level0() => Level1();
    public static void Level1() => Level2();
    public static void Level2() => Level3();
    public static void Level3() => Level4();
    public static void Level4() => Level5();
    public static void Level5() => Level6();
    public static void Level6() => Level7();
    public static void Level7() => Level8();
    public static void Level8() => Level9();
    public static void Level9() => Level10();
    public static void Level10() => Level11();
    public static void Level11() => Level12();
    public static void Level12() => Level13();
    public static void Level13() => Level14();
    public static void Level14() => Level15();
    public static void Level15()
    {
        var caller = new RealDbCaller();
        caller.ExecuteWrite();
    }
}

// ============================================================
// Circular / recursive scenarios
// ============================================================

public static class CircularCalls
{
    public static void A()
    {
        B();
    }

    public static void B()
    {
        var caller = new RealDbCaller();
        caller.ExecuteWrite();
        A(); // mutual recursion
    }
}

// ============================================================
// Helper: wraps actual DbCommand calls for IL analysis to trace
// ============================================================

public class RealDbCaller
{
    private readonly DbConnection _connection = null!;

    public void ExecuteWrite()
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "INSERT INTO T VALUES (1)";
        cmd.ExecuteNonQuery(); // db:w seed
    }

    public object? ExecuteRead()
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT 1";
        return cmd.ExecuteScalar(); // db:r seed
    }
}
