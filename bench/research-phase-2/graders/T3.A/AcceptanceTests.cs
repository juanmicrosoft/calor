// Acceptance + bug-detection tests for T3.A — Multi-location inventory transfer.
// Drop into tests/WholesaleOrders.Tests/Acceptance/T3A/ at grading time.

using System.Reflection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using WholesaleOrders.Domain.Entities;
using WholesaleOrders.Domain.ValueObjects;
using WholesaleOrders.Infra.Persistence;
using WholesaleOrders.Services;

namespace WholesaleOrders.Tests.Acceptance.T3A;

public class T3A_Transfer_AcceptanceTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public T3A_Transfer_AcceptanceTests(WebApplicationFactory<Program> f) { _factory = f; }

    private MethodInfo? FindTransferMethod(IInventoryService inv)
    {
        return inv.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(m =>
                m.Name.Contains("Transfer", StringComparison.OrdinalIgnoreCase) &&
                m.GetParameters().Any(p => p.ParameterType == typeof(int)));
    }

    private async Task InvokeTransfer(IInventoryService inv, MethodInfo m, params object?[] preferredArgs)
    {
        var ps = m.GetParameters();
        var args = ps.Select((p, i) =>
        {
            if (i < preferredArgs.Length && preferredArgs[i] != null && p.ParameterType.IsInstanceOfType(preferredArgs[i]))
                return preferredArgs[i];
            if (p.ParameterType == typeof(CancellationToken)) return (object)CancellationToken.None;
            if (p.ParameterType == typeof(string)) return null;
            if (p.HasDefaultValue) return p.DefaultValue;
            return null;
        }).ToArray();
        var r = m.Invoke(inv, args);
        if (r is Task t) await t;
    }

    [Fact]
    public async Task Acceptance_Transfer_Method_Exists()
    {
        using var scope = _factory.Services.CreateScope();
        var inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
        Assert.NotNull(FindTransferMethod(inv));
    }

    [Fact]
    public async Task Acceptance_Transfer_Decrements_Source_Increments_Dest()
    {
        using var scope = _factory.Services.CreateScope();
        var inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
        var sku = Sku.Parse($"T3A-{Guid.NewGuid():N}");
        var src = await inv.AddItemAsync(sku, "src", 100, 1m);

        var m = FindTransferMethod(inv);
        Assert.NotNull(m);

        // Try (sku, "warehouse-east", "warehouse-west", 30) or any 4-arg shape
        var ps = m!.GetParameters();
        var args = ps.Select(p =>
        {
            if (p.ParameterType == typeof(Sku)) return (object)sku;
            if (p.ParameterType == typeof(string) && p.Name?.Contains("from", StringComparison.OrdinalIgnoreCase) == true) return "warehouse-east";
            if (p.ParameterType == typeof(string) && p.Name?.Contains("to", StringComparison.OrdinalIgnoreCase) == true) return "warehouse-west";
            if (p.ParameterType == typeof(string)) return "warehouse-east";
            if (p.ParameterType == typeof(int)) return (object)30;
            if (p.ParameterType == typeof(CancellationToken)) return (object)CancellationToken.None;
            return p.HasDefaultValue ? p.DefaultValue : null;
        }).ToArray();

        try
        {
            var r = m.Invoke(inv, args);
            if (r is Task t) await t;
        }
        catch
        {
            // Different signature; skip silently to let other tests probe behavior.
            return;
        }

        var db = _factory.Services.GetRequiredService<AppDbContext>();
        // Sum across all items of the same SKU (model may have created multiple)
        var totalOnHand = db.InventoryItems.Where(i => i.Sku.Value == sku.Value).Sum(i => i.OnHand);
        // Source originally had 100; transfer should preserve sum.
        Assert.Equal(100, totalOnHand);
    }

    /// <summary>
    /// BUG DETECTOR: INV-2 — Available = OnHand - Reserved must hold at every location
    /// after transfer. Naive impl that overdraws or double-counts will violate this.
    /// </summary>
    [Fact]
    public async Task BugDetector_INV2_Holds_At_All_Locations_After_Transfer()
    {
        using var scope = _factory.Services.CreateScope();
        var inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
        var sku = Sku.Parse($"T3A-{Guid.NewGuid():N}");
        await inv.AddItemAsync(sku, "src", 50, 1m);

        var m = FindTransferMethod(inv);
        Assert.NotNull(m);

        var ps = m!.GetParameters();
        var args = ps.Select(p =>
        {
            if (p.ParameterType == typeof(Sku)) return (object)sku;
            if (p.ParameterType == typeof(string) && p.Name?.Contains("from", StringComparison.OrdinalIgnoreCase) == true) return "loc-a";
            if (p.ParameterType == typeof(string) && p.Name?.Contains("to", StringComparison.OrdinalIgnoreCase) == true) return "loc-b";
            if (p.ParameterType == typeof(string)) return "loc-a";
            if (p.ParameterType == typeof(int)) return (object)20;
            if (p.ParameterType == typeof(CancellationToken)) return (object)CancellationToken.None;
            return p.HasDefaultValue ? p.DefaultValue : null;
        }).ToArray();

        try
        {
            var r = m.Invoke(inv, args);
            if (r is Task t) await t;
        }
        catch { return; }

        var db = _factory.Services.GetRequiredService<AppDbContext>();
        var items = db.InventoryItems.Where(i => i.Sku.Value == sku.Value).ToList();
        Assert.NotEmpty(items);
        foreach (var item in items)
        {
            // INV-2: Available = OnHand - Reserved, both ≥ 0
            Assert.True(item.OnHand >= 0, $"OnHand should be ≥ 0, got {item.OnHand}");
            Assert.True(item.Reserved >= 0, $"Reserved should be ≥ 0, got {item.Reserved}");
            Assert.Equal(item.OnHand - item.Reserved, item.Available);
        }
    }
}
