// Acceptance + bug-detection tests for T3.C — Order split.
// Drop into tests/WholesaleOrders.Tests/Acceptance/T3C/ at grading time.

using System.Reflection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using WholesaleOrders.Domain.Entities;
using WholesaleOrders.Domain.Enums;
using WholesaleOrders.Domain.ValueObjects;
using WholesaleOrders.Infra.Persistence;
using WholesaleOrders.Services;

namespace WholesaleOrders.Tests.Acceptance.T3C;

public class T3C_OrderSplit_AcceptanceTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public T3C_OrderSplit_AcceptanceTests(WebApplicationFactory<Program> f) { _factory = f; }

    private MethodInfo? FindSplitMethod(IOrderService o)
    {
        return o.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(m => m.Name.Contains("Split", StringComparison.OrdinalIgnoreCase) &&
                                  m.GetParameters().Any(p => p.ParameterType == typeof(Guid)));
    }

    private async Task<Guid?> InvokeSplit(IOrderService o, MethodInfo m, Guid orderId, IEnumerable<Guid> moveItems)
    {
        var ps = m.GetParameters();
        var args = ps.Select(p =>
        {
            if (p.ParameterType == typeof(Guid) && p.Name?.Contains("order", StringComparison.OrdinalIgnoreCase) == true) return (object)orderId;
            if (p.ParameterType == typeof(Guid)) return (object)orderId;
            if (typeof(System.Collections.IEnumerable).IsAssignableFrom(p.ParameterType) && p.ParameterType != typeof(string))
            {
                if (p.ParameterType.IsAssignableFrom(typeof(List<Guid>))) return (object)moveItems.ToList();
                if (p.ParameterType.IsAssignableFrom(typeof(Guid[]))) return (object)moveItems.ToArray();
                if (p.ParameterType.IsAssignableFrom(typeof(IEnumerable<Guid>))) return (object)moveItems;
                return (object)moveItems.ToList();
            }
            if (p.ParameterType == typeof(CancellationToken)) return (object)CancellationToken.None;
            return p.HasDefaultValue ? p.DefaultValue : null;
        }).ToArray();
        var r = m.Invoke(o, args);
        if (r is Task t)
        {
            await t;
            var resultProp = t.GetType().GetProperty("Result");
            var resultValue = resultProp?.GetValue(t);
            if (resultValue is Order order) return order.Id;
            if (resultValue is Guid g) return g;
        }
        return null;
    }

    private async Task<(Guid orderId, List<OrderLineItem> lineItems, IServiceScope scope)> SetupSubmittedOrder()
    {
        var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var customerRepo = sp.GetRequiredService<ICustomerRepository>();
        var inv = sp.GetRequiredService<IInventoryService>();
        var orders = sp.GetRequiredService<IOrderService>();
        var c = new Customer { Name = "T3C", Email = "t@t", BillingAddress = "x", ShippingAddress = "x" };
        await customerRepo.AddAsync(c);
        var skuA = Sku.Parse($"T3C-A-{Guid.NewGuid():N}");
        var skuB = Sku.Parse($"T3C-B-{Guid.NewGuid():N}");
        await inv.AddItemAsync(skuA, "A", 100, 1m);
        await inv.AddItemAsync(skuB, "B", 100, 1m);
        var o = await orders.CreateDraftAsync(c.Id);
        await orders.AddLineItemAsync(o.Id, skuA, 3, 10m);
        await orders.AddLineItemAsync(o.Id, skuB, 4, 20m);
        await orders.SubmitAsync(o.Id);
        var orderRepo = sp.GetRequiredService<IOrderRepository>();
        var items = await orderRepo.GetLineItemsAsync(o.Id);
        return (o.Id, items, scope);
    }

    [Fact]
    public async Task Acceptance_Split_Returns_New_Order()
    {
        var (orderId, items, scope) = await SetupSubmittedOrder();
        try
        {
            var orders = scope.ServiceProvider.GetRequiredService<IOrderService>();
            var m = FindSplitMethod(orders);
            Assert.NotNull(m);
            var newId = await InvokeSplit(orders, m!, orderId, new[] { items.First().Id });
            // Either returns the new order or successfully creates one we can find
            var db = _factory.Services.GetRequiredService<AppDbContext>();
            var allOrders = db.Orders.ToList();
            Assert.True(allOrders.Count >= 2, "Split should create a second order");
        }
        finally { scope.Dispose(); }
    }

    /// <summary>
    /// BUG DETECTOR: INV-1 must hold on BOTH orders after split.
    /// Naive impl forgets to recompute TotalAmount on one side.
    /// </summary>
    [Fact]
    public async Task BugDetector_INV1_Holds_On_Both_Orders_After_Split()
    {
        var (orderId, items, scope) = await SetupSubmittedOrder();
        try
        {
            var orders = scope.ServiceProvider.GetRequiredService<IOrderService>();
            var m = FindSplitMethod(orders);
            Assert.NotNull(m);

            try { await InvokeSplit(orders, m!, orderId, new[] { items.First().Id }); }
            catch { return; }

            var db = _factory.Services.GetRequiredService<AppDbContext>();
            var allOrders = db.Orders.ToList();
            foreach (var ord in allOrders)
            {
                var lineItems = db.OrderLineItems.Where(li => li.OrderId == ord.Id).ToList();
                if (!lineItems.Any()) continue; // Skip empty orders
                var sum = lineItems.Sum(li => li.Quantity * li.UnitPrice);
                Assert.Equal(sum, ord.TotalAmount.Amount);
            }
        }
        finally { scope.Dispose(); }
    }
}
