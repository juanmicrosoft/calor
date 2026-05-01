// Acceptance + bug-detection tests for T2.C — Order recall (Shipped → Submitted).
// Drop into tests/WholesaleOrders.Tests/Acceptance/T2C/ at grading time.

using System.Reflection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using WholesaleOrders.Domain.Entities;
using WholesaleOrders.Domain.Enums;
using WholesaleOrders.Domain.ValueObjects;
using WholesaleOrders.Infra.Persistence;
using WholesaleOrders.Services;

namespace WholesaleOrders.Tests.Acceptance.T2C;

public class T2C_OrderRecall_AcceptanceTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public T2C_OrderRecall_AcceptanceTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private MethodInfo? FindRecallMethod(IOrderService orders)
    {
        var t = orders.GetType();
        return t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(m => m.Name.Contains("Recall", StringComparison.OrdinalIgnoreCase) &&
                                  m.GetParameters().Any(p => p.ParameterType == typeof(Guid)));
    }

    private async Task<object?> InvokeRecall(IOrderService orders, MethodInfo method, Guid orderId)
    {
        var args = method.GetParameters().Select(p =>
        {
            if (p.ParameterType == typeof(Guid)) return (object)orderId;
            if (p.ParameterType == typeof(CancellationToken)) return (object)CancellationToken.None;
            return p.HasDefaultValue ? p.DefaultValue : null;
        }).ToArray();
        var result = method.Invoke(orders, args);
        if (result is Task t) await t;
        return null;
    }

    private async Task<(Guid orderId, Guid reservationId, IServiceScope scope)> ShipOrderAsync()
    {
        var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var customerRepo = sp.GetRequiredService<ICustomerRepository>();
        var inv = sp.GetRequiredService<IInventoryService>();
        var orders = sp.GetRequiredService<IOrderService>();
        var pay = sp.GetRequiredService<IPaymentService>();
        var ship = sp.GetRequiredService<IShipmentService>();

        var c = new Customer { Name = "T2C", Email = "t@t", BillingAddress = "x", ShippingAddress = "x" };
        await customerRepo.AddAsync(c);
        var sku = Sku.Parse($"T2C-{Guid.NewGuid():N}");
        await inv.AddItemAsync(sku, "X", 100, unitPrice: 1m);
        var order = await orders.CreateDraftAsync(c.Id);
        await orders.AddLineItemAsync(order.Id, sku, 5, 1m);
        await orders.SubmitAsync(order.Id);
        var res = await inv.ReserveAsync(order.Id, sku, 5);
        await inv.ConfirmAsync(res.Id);
        await pay.ChargeAsync(order.Id, new Money(5m, "USD"), "tok", $"k-{order.Id}", "test");
        await orders.MarkPaidAsync(order.Id);
        try { await orders.MarkShippedAsync(order.Id); } catch { /* some impls auto-transition via shipment */ }
        return (order.Id, res.Id, scope);
    }

    [Fact]
    public async Task Acceptance_Recall_Method_Exists()
    {
        using var scope = _factory.Services.CreateScope();
        var orders = scope.ServiceProvider.GetRequiredService<IOrderService>();
        Assert.NotNull(FindRecallMethod(orders));
    }

    [Fact]
    public async Task Acceptance_Recall_From_Shipped_Returns_To_Submitted()
    {
        var (orderId, _, scope) = await ShipOrderAsync();
        try
        {
            var orders = scope.ServiceProvider.GetRequiredService<IOrderService>();
            var method = FindRecallMethod(orders);
            Assert.NotNull(method);
            await InvokeRecall(orders, method!, orderId);

            var db = _factory.Services.GetRequiredService<AppDbContext>();
            var fresh = db.Orders.Single(o => o.Id == orderId);
            Assert.Equal(OrderStatus.Submitted, fresh.Status);
        }
        finally { scope.Dispose(); }
    }

    [Fact]
    public async Task Acceptance_Recall_Of_Non_Shipped_Throws()
    {
        // Create an order in Draft and try to recall.
        using var scope = _factory.Services.CreateScope();
        var customerRepo = scope.ServiceProvider.GetRequiredService<ICustomerRepository>();
        var orders = scope.ServiceProvider.GetRequiredService<IOrderService>();
        var c = new Customer { Name = "T2C", Email = "t@t", BillingAddress = "x", ShippingAddress = "x" };
        await customerRepo.AddAsync(c);
        var order = await orders.CreateDraftAsync(c.Id);

        var method = FindRecallMethod(orders);
        Assert.NotNull(method);
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await InvokeRecall(orders, method!, order.Id);
        });
    }

    /// <summary>
    /// BUG DETECTOR: INV-3 says Fulfilled reservations are absorbing. A naive recall
    /// implementation might try to "Release" all reservations including Fulfilled ones,
    /// violating the invariant. A correct implementation either skips Fulfilled or
    /// rejects recall when any reservation is Fulfilled.
    /// </summary>
    [Fact]
    public async Task BugDetector_Recall_Does_Not_Mutate_Fulfilled_Reservations()
    {
        var (orderId, reservationId, scope) = await ShipOrderAsync();
        try
        {
            var inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            await inv.FulfillAsync(reservationId);

            var orders = scope.ServiceProvider.GetRequiredService<IOrderService>();
            var method = FindRecallMethod(orders);
            Assert.NotNull(method);

            // Recall should either reject (because of Fulfilled reservations) OR succeed but leave Fulfilled untouched.
            try
            {
                await InvokeRecall(orders, method!, orderId);
            }
            catch
            {
                // Rejection is acceptable
                return;
            }

            // If recall succeeded, INV-3 must still hold: the Fulfilled reservation stays Fulfilled.
            var db = _factory.Services.GetRequiredService<AppDbContext>();
            var fresh = db.Reservations.Single(r => r.Id == reservationId);
            Assert.Equal(ReservationStatus.Fulfilled, fresh.Status);
        }
        finally { scope.Dispose(); }
    }
}
