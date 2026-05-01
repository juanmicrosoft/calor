// Acceptance + bug-detection tests for T3.B — Refund processing.
// Drop into tests/WholesaleOrders.Tests/Acceptance/T3B/ at grading time.

using System.Reflection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using WholesaleOrders.Domain.Entities;
using WholesaleOrders.Domain.Enums;
using WholesaleOrders.Domain.ValueObjects;
using WholesaleOrders.Infra.Persistence;
using WholesaleOrders.Services;

namespace WholesaleOrders.Tests.Acceptance.T3B;

public class T3B_Refund_AcceptanceTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public T3B_Refund_AcceptanceTests(WebApplicationFactory<Program> f) { _factory = f; }

    private MethodInfo? FindRefundMethod(IOrderService o)
    {
        var t = o.GetType();
        return t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(m => m.Name.Contains("Refund", StringComparison.OrdinalIgnoreCase) &&
                                  m.GetParameters().Any(p => p.ParameterType == typeof(Guid)));
    }

    private async Task InvokeRefund(IOrderService o, MethodInfo m, Guid orderId)
    {
        var args = m.GetParameters().Select(p =>
        {
            if (p.ParameterType == typeof(Guid)) return (object)orderId;
            if (p.ParameterType == typeof(CancellationToken)) return (object)CancellationToken.None;
            return p.HasDefaultValue ? p.DefaultValue : null;
        }).ToArray();
        var r = m.Invoke(o, args);
        if (r is Task t) await t;
    }

    private async Task<(Guid orderId, Guid reservationId, IServiceScope scope)> SetupPaidOrderAsync()
    {
        var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var customerRepo = sp.GetRequiredService<ICustomerRepository>();
        var inv = sp.GetRequiredService<IInventoryService>();
        var orders = sp.GetRequiredService<IOrderService>();
        var pay = sp.GetRequiredService<IPaymentService>();

        var c = new Customer { Name = "T3B", Email = "t@t", BillingAddress = "x", ShippingAddress = "x" };
        await customerRepo.AddAsync(c);
        var sku = Sku.Parse($"T3B-{Guid.NewGuid():N}");
        await inv.AddItemAsync(sku, "X", 100, 1m);
        var o = await orders.CreateDraftAsync(c.Id);
        await orders.AddLineItemAsync(o.Id, sku, 5, 1m);
        await orders.SubmitAsync(o.Id);
        var r = await inv.ReserveAsync(o.Id, sku, 5);
        await inv.ConfirmAsync(r.Id);
        await pay.ChargeAsync(o.Id, new Money(5m, "USD"), "tok", $"k-{o.Id}", "test");
        await orders.MarkPaidAsync(o.Id);
        return (o.Id, r.Id, scope);
    }

    [Fact]
    public async Task Acceptance_Refund_From_Paid_Sets_Status_Refunded()
    {
        var (orderId, _, scope) = await SetupPaidOrderAsync();
        try
        {
            var orders = scope.ServiceProvider.GetRequiredService<IOrderService>();
            var m = FindRefundMethod(orders);
            Assert.NotNull(m);
            await InvokeRefund(orders, m!, orderId);

            var db = _factory.Services.GetRequiredService<AppDbContext>();
            var fresh = db.Orders.Single(o => o.Id == orderId);
            Assert.Contains("Refund", fresh.Status.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally { scope.Dispose(); }
    }

    [Fact]
    public async Task Acceptance_Refund_Marks_Payments_Refunded()
    {
        var (orderId, _, scope) = await SetupPaidOrderAsync();
        try
        {
            var orders = scope.ServiceProvider.GetRequiredService<IOrderService>();
            var m = FindRefundMethod(orders);
            Assert.NotNull(m);
            await InvokeRefund(orders, m!, orderId);

            var db = _factory.Services.GetRequiredService<AppDbContext>();
            var payments = db.Payments.Where(p => p.OrderId == orderId).ToList();
            Assert.NotEmpty(payments);
            Assert.All(payments, p => Assert.Contains("Refund", p.Status.ToString(), StringComparison.OrdinalIgnoreCase));
        }
        finally { scope.Dispose(); }
    }

    [Fact]
    public async Task Acceptance_Refund_Of_Non_Paid_Throws()
    {
        using var scope = _factory.Services.CreateScope();
        var customerRepo = scope.ServiceProvider.GetRequiredService<ICustomerRepository>();
        var orders = scope.ServiceProvider.GetRequiredService<IOrderService>();
        var c = new Customer { Name = "T3B", Email = "t@t", BillingAddress = "x", ShippingAddress = "x" };
        await customerRepo.AddAsync(c);
        var o = await orders.CreateDraftAsync(c.Id);

        var m = FindRefundMethod(orders);
        Assert.NotNull(m);
        await Assert.ThrowsAnyAsync<Exception>(async () => await InvokeRefund(orders, m!, o.Id));
    }

    /// <summary>
    /// BUG DETECTOR: INV-3 — refunding an order that has any Fulfilled reservations
    /// must throw. Naive impl tries to release every reservation regardless of status,
    /// "un-fulfilling" the Fulfilled ones (terminal-state violation).
    /// </summary>
    [Fact]
    public async Task BugDetector_Refund_With_Fulfilled_Reservation_Throws()
    {
        var (orderId, reservationId, scope) = await SetupPaidOrderAsync();
        try
        {
            var inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            await inv.FulfillAsync(reservationId);

            var orders = scope.ServiceProvider.GetRequiredService<IOrderService>();
            var m = FindRefundMethod(orders);
            Assert.NotNull(m);

            // Strict: per the prompt, refund with a Fulfilled reservation must be rejected.
            await Assert.ThrowsAnyAsync<Exception>(async () => await InvokeRefund(orders, m!, orderId));

            // INV-3: Fulfilled reservation must remain Fulfilled regardless of any partial work.
            var db = _factory.Services.GetRequiredService<AppDbContext>();
            var fresh = db.Reservations.Single(r => r.Id == reservationId);
            Assert.Equal(ReservationStatus.Fulfilled, fresh.Status);
        }
        finally { scope.Dispose(); }
    }

    /// <summary>
    /// BUG DETECTOR: INV-3 — refund must release Created/Confirmed reservations,
    /// not silently leave them in Reserved state.
    /// </summary>
    [Fact]
    public async Task BugDetector_Refund_Releases_NonTerminal_Reservations()
    {
        var (orderId, reservationId, scope) = await SetupPaidOrderAsync();
        try
        {
            // Reservation is in Confirmed state (set up by SetupPaidOrderAsync). Refund
            // should release it, returning quantity to available pool.
            var orders = scope.ServiceProvider.GetRequiredService<IOrderService>();
            var m = FindRefundMethod(orders);
            Assert.NotNull(m);

            try { await InvokeRefund(orders, m!, orderId); }
            catch
            {
                // If the impl rejects Confirmed-reservation refund (overly strict), that's
                // also acceptable — but then reservation should remain Confirmed.
                var db = _factory.Services.GetRequiredService<AppDbContext>();
                var fresh = db.Reservations.Single(r => r.Id == reservationId);
                Assert.Equal(ReservationStatus.Confirmed, fresh.Status);
                return;
            }

            // Refund succeeded → reservation should be Released.
            var dbAfter = _factory.Services.GetRequiredService<AppDbContext>();
            var freshAfter = dbAfter.Reservations.Single(r => r.Id == reservationId);
            Assert.Equal(ReservationStatus.Released, freshAfter.Status);
        }
        finally { scope.Dispose(); }
    }
}
