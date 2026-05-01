// Acceptance + bug-detection tests for T2.B — Partial reservation release.
// Drop into tests/WholesaleOrders.Tests/Acceptance/T2B/ at grading time.
// Requires: Microsoft.AspNetCore.Mvc.Testing.

using System.Reflection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using WholesaleOrders.Domain.Entities;
using WholesaleOrders.Domain.Enums;
using WholesaleOrders.Domain.ValueObjects;
using WholesaleOrders.Infra.Persistence;
using WholesaleOrders.Services;

namespace WholesaleOrders.Tests.Acceptance.T2B;

public class T2B_PartialReservationRelease_AcceptanceTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public T2B_PartialReservationRelease_AcceptanceTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Probes for the model's partial-release method via reflection.
    /// Looks on IInventoryService for methods named *Partial*, or *ReleasePartial*,
    /// or any method named Release* with multiple args (Guid + int qty).
    /// </summary>
    private MethodInfo? FindPartialReleaseMethod(IInventoryService inv)
    {
        var t = inv.GetType();
        var methods = t.GetMethods(BindingFlags.Public | BindingFlags.Instance);

        // Primary: name contains "Partial"
        var candidate = methods.FirstOrDefault(m =>
            m.Name.Contains("Partial", StringComparison.OrdinalIgnoreCase) &&
            m.GetParameters().Any(p => p.ParameterType == typeof(Guid)) &&
            m.GetParameters().Any(p => p.ParameterType == typeof(int)));
        if (candidate != null) return candidate;

        // Secondary: Release* with int quantity param (suggests partial-by-quantity)
        candidate = methods.FirstOrDefault(m =>
            m.Name.StartsWith("Release", StringComparison.OrdinalIgnoreCase) &&
            m.GetParameters().Any(p => p.ParameterType == typeof(Guid)) &&
            m.GetParameters().Any(p => p.ParameterType == typeof(int)));
        if (candidate != null) return candidate;

        return null;
    }

    private async Task<object?> InvokePartialReleaseAsync(IInventoryService inv, MethodInfo method, Guid reservationId, int quantity)
    {
        var args = method.GetParameters().Select(p =>
        {
            if (p.ParameterType == typeof(Guid)) return (object)reservationId;
            if (p.ParameterType == typeof(int)) return (object)quantity;
            if (p.ParameterType == typeof(CancellationToken)) return (object)CancellationToken.None;
            return p.HasDefaultValue ? p.DefaultValue : null;
        }).ToArray();

        var result = method.Invoke(inv, args);
        if (result is Task t)
        {
            await t;
            // Try to extract result from Task<T>
            var resultProp = t.GetType().GetProperty("Result");
            return resultProp?.GetValue(t);
        }
        return result;
    }

    private async Task<(StockReservation reservation, InventoryItem item, IServiceScope scope)>
        SetupReservationAsync(int onHand = 10, int reserveQty = 5)
    {
        var scope = _factory.Services.CreateScope();
        var customerRepo = scope.ServiceProvider.GetRequiredService<ICustomerRepository>();
        var inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
        var orders = scope.ServiceProvider.GetRequiredService<IOrderService>();

        var customer = new Customer { Name = "T2B", Email = "t@t", BillingAddress = "x", ShippingAddress = "x" };
        await customerRepo.AddAsync(customer);

        var sku = Sku.Parse($"T2B-{Guid.NewGuid():N}");
        var item = await inv.AddItemAsync(sku, "Test", onHand, unitPrice: 1m);
        var order = await orders.CreateDraftAsync(customer.Id);
        var res = await inv.ReserveAsync(order.Id, sku, reserveQty);
        return (res, item, scope);
    }

    [Fact]
    public async Task Acceptance_Partial_Release_Reduces_Quantity()
    {
        var (res, item, scope) = await SetupReservationAsync(onHand: 10, reserveQty: 5);
        try
        {
            var inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            var method = FindPartialReleaseMethod(inv);
            Assert.NotNull(method);

            await InvokePartialReleaseAsync(inv, method!, res.Id, 2);

            var db = _factory.Services.GetRequiredService<AppDbContext>();
            var fresh = db.Reservations.Single(r => r.Id == res.Id);
            // Either: (a) original reservation remains with reduced Quantity,
            // or (b) the partial release model used some other shape.
            // We assert: the *available* pool has gained 2 units back.
            var freshItem = db.InventoryItems.Single(i => i.Id == item.Id);
            Assert.Equal(7, freshItem.Available); // was 5, partial-released 2 → available = 5 + 2 = 7
        }
        finally { scope.Dispose(); }
    }

    [Fact]
    public async Task Acceptance_Partial_Release_Returns_To_Available_Pool()
    {
        var (res, item, scope) = await SetupReservationAsync(onHand: 20, reserveQty: 8);
        try
        {
            var inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            var method = FindPartialReleaseMethod(inv);
            Assert.NotNull(method);
            var beforeAvail = _factory.Services.GetRequiredService<AppDbContext>().InventoryItems.Single(i => i.Id == item.Id).Available;
            Assert.Equal(12, beforeAvail);

            await InvokePartialReleaseAsync(inv, method!, res.Id, 3);

            var afterAvail = _factory.Services.GetRequiredService<AppDbContext>().InventoryItems.Single(i => i.Id == item.Id).Available;
            Assert.Equal(15, afterAvail); // released 3 → available increased by 3
        }
        finally { scope.Dispose(); }
    }

    [Fact]
    public async Task Acceptance_Partial_Release_Of_Full_Quantity_Marks_Released()
    {
        var (res, _, scope) = await SetupReservationAsync(onHand: 10, reserveQty: 4);
        try
        {
            var inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            var method = FindPartialReleaseMethod(inv);
            Assert.NotNull(method);

            await InvokePartialReleaseAsync(inv, method!, res.Id, 4);

            var db = _factory.Services.GetRequiredService<AppDbContext>();
            var fresh = db.Reservations.Single(r => r.Id == res.Id);
            Assert.Equal(ReservationStatus.Released, fresh.Status);
        }
        finally { scope.Dispose(); }
    }

    /// <summary>
    /// BUG DETECTOR: INV-3 says Released is a terminal state. A partial-release
    /// of an already-Released reservation must throw, not silently succeed.
    /// </summary>
    [Fact]
    public async Task BugDetector_Partial_Release_Of_Released_Reservation_Throws()
    {
        var (res, _, scope) = await SetupReservationAsync(onHand: 10, reserveQty: 5);
        try
        {
            var inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            await inv.ReleaseAsync(res.Id);

            var method = FindPartialReleaseMethod(inv);
            Assert.NotNull(method);

            await Assert.ThrowsAnyAsync<Exception>(async () =>
            {
                await InvokePartialReleaseAsync(inv, method!, res.Id, 1);
            });
        }
        finally { scope.Dispose(); }
    }

    /// <summary>
    /// BUG DETECTOR: INV-3 says Fulfilled is a terminal state. A partial-release
    /// of a Fulfilled reservation must throw — not un-fulfill it.
    /// </summary>
    [Fact]
    public async Task BugDetector_Partial_Release_Of_Fulfilled_Reservation_Throws()
    {
        var (res, _, scope) = await SetupReservationAsync(onHand: 10, reserveQty: 4);
        try
        {
            var inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            await inv.ConfirmAsync(res.Id);
            await inv.FulfillAsync(res.Id);

            var method = FindPartialReleaseMethod(inv);
            Assert.NotNull(method);

            await Assert.ThrowsAnyAsync<Exception>(async () =>
            {
                await InvokePartialReleaseAsync(inv, method!, res.Id, 1);
            });
        }
        finally { scope.Dispose(); }
    }

    /// <summary>
    /// Existing INV-3 test must still pass — terminal states stay absorbing.
    /// </summary>
    [Fact]
    public async Task Existing_INV3_Reservation_Terminal_States_Still_Absorbing()
    {
        var (res, _, scope) = await SetupReservationAsync(onHand: 10, reserveQty: 3);
        try
        {
            var inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            await inv.ConfirmAsync(res.Id);
            await inv.FulfillAsync(res.Id);

            await Assert.ThrowsAnyAsync<Exception>(() => inv.ReleaseAsync(res.Id));

            var (res2, _, _) = await SetupReservationAsync(onHand: 10, reserveQty: 2);
            await inv.ReleaseAsync(res2.Id);
            await Assert.ThrowsAnyAsync<Exception>(() => inv.FulfillAsync(res2.Id));
        }
        finally { scope.Dispose(); }
    }
}
