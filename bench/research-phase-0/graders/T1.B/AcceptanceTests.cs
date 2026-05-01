// Acceptance tests for T1.B — Inventory reservation expiry.
// Drop into tests/WholesaleOrders.Tests/ at grading time.
// Requires: Microsoft.AspNetCore.Mvc.Testing package on the test project.
//
// Probes the model's implementation through HTTP + reflection. Does NOT bind
// to specific type names beyond the public API contracts described in the
// T1.B prompt: "expired reservations release inventory and send a notification."

using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using WholesaleOrders.Domain.Entities;
using WholesaleOrders.Domain.Enums;
using WholesaleOrders.Infra.Persistence;
using WholesaleOrders.Services;

namespace WholesaleOrders.Tests.Acceptance.T1B;

public class T1B_ReservationExpiry_AcceptanceTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public T1B_ReservationExpiry_AcceptanceTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Acceptance #1: A Created reservation that is not Confirmed within 30 minutes
    /// transitions to Released and frees its quantity back to inventory Available.
    /// </summary>
    [Fact]
    public async Task Acceptance_Reservation_Expires_After_30_Minutes_If_Not_Confirmed()
    {
        var (reservationId, inventoryId, db, services) = await SetupReservationAsync(quantity: 4, onHand: 10);

        // Probe for the expiry trigger.
        var triggered = await TryTriggerExpirySweepAsync(services, advanceBy: TimeSpan.FromMinutes(31));
        Assert.True(triggered, "Model must expose a way to trigger expiry processing in tests (prompt: 'the test suite checks behavior').");

        var reservation = db.Reservations.Single(r => r.Id == reservationId);
        Assert.Equal(ReservationStatus.Released, reservation.Status);

        var item = db.InventoryItems.Single(i => i.Id == inventoryId);
        Assert.Equal(10, item.Available);
    }

    /// <summary>
    /// Acceptance #2: Releasing an expired reservation returns the reserved quantity to OnHand-derived Available.
    /// </summary>
    [Fact]
    public async Task Acceptance_Expired_Reservation_Releases_Inventory()
    {
        var (reservationId, inventoryId, db, services) = await SetupReservationAsync(quantity: 5, onHand: 12);

        var beforeAvail = db.InventoryItems.Single(i => i.Id == inventoryId).Available;
        Assert.Equal(7, beforeAvail);

        await TryTriggerExpirySweepAsync(services, advanceBy: TimeSpan.FromMinutes(31));

        var afterAvail = db.InventoryItems.Single(i => i.Id == inventoryId).Available;
        Assert.Equal(12, afterAvail);
    }

    /// <summary>
    /// Acceptance #3: An expired reservation produces a notification (NotificationService.Sent gains an entry).
    /// </summary>
    [Fact]
    public async Task Acceptance_Expired_Reservation_Sends_Notification()
    {
        var (reservationId, _, _, services) = await SetupReservationAsync(quantity: 2, onHand: 8);
        var notify = services.GetRequiredService<INotificationService>();
        var beforeCount = notify.Sent.Count;

        await TryTriggerExpirySweepAsync(services, advanceBy: TimeSpan.FromMinutes(31));

        Assert.True(notify.Sent.Count > beforeCount, "Expiry must trigger a notification per T1.B prompt.");
    }

    /// <summary>
    /// Acceptance #4: A Confirmed reservation does NOT transition to Released by the expiry sweep,
    /// regardless of how much time has passed.
    /// </summary>
    [Fact]
    public async Task Acceptance_Confirmed_Reservation_Does_Not_Expire()
    {
        var (reservationId, _, db, services) = await SetupReservationAsync(quantity: 3, onHand: 9);
        var inventoryService = services.GetRequiredService<IInventoryService>();
        await inventoryService.ConfirmAsync(reservationId);

        await TryTriggerExpirySweepAsync(services, advanceBy: TimeSpan.FromHours(2));

        var reservation = db.Reservations.Single(r => r.Id == reservationId);
        Assert.Equal(ReservationStatus.Confirmed, reservation.Status);
    }

    /// <summary>
    /// Adversarial #1: A reservation that was already Released manually does not get "released again"
    /// by the expiry sweep — operation must be idempotent.
    /// </summary>
    [Fact]
    public async Task Adversarial_Already_Released_Reservation_Stays_Released()
    {
        var (reservationId, inventoryId, db, services) = await SetupReservationAsync(quantity: 2, onHand: 10);
        var inv = services.GetRequiredService<IInventoryService>();
        await inv.ReleaseAsync(reservationId);

        var reservedBeforeSweep = db.InventoryItems.Single(i => i.Id == inventoryId).Reserved;
        await TryTriggerExpirySweepAsync(services, advanceBy: TimeSpan.FromMinutes(31));

        var reservedAfterSweep = db.InventoryItems.Single(i => i.Id == inventoryId).Reserved;
        Assert.Equal(reservedBeforeSweep, reservedAfterSweep);
        Assert.Equal(ReservationStatus.Released, db.Reservations.Single(r => r.Id == reservationId).Status);
    }

    /// <summary>
    /// Adversarial #2: A Fulfilled reservation must never be Released by expiry — terminal-state respect (INV-3).
    /// </summary>
    [Fact]
    public async Task Adversarial_Fulfilled_Reservation_Never_Expires()
    {
        var (reservationId, _, db, services) = await SetupReservationAsync(quantity: 2, onHand: 10);
        var inv = services.GetRequiredService<IInventoryService>();
        await inv.ConfirmAsync(reservationId);
        await inv.FulfillAsync(reservationId);

        await TryTriggerExpirySweepAsync(services, advanceBy: TimeSpan.FromHours(2));

        Assert.Equal(ReservationStatus.Fulfilled, db.Reservations.Single(r => r.Id == reservationId).Status);
    }

    /// <summary>
    /// Adversarial #3: Inventory.Available stays consistent (INV-2: Available = OnHand − Reserved, both ≥ 0)
    /// while the expiry sweep runs concurrently with new reservations.
    /// </summary>
    [Fact]
    public async Task Adversarial_Inventory_Stays_Consistent_During_Sweep()
    {
        var services = _factory.Services;
        var db = services.GetRequiredService<AppDbContext>();
        var inventoryService = services.GetRequiredService<IInventoryService>();

        // Seed inventory and a few reservations, some confirmed, some not.
        var sku = WholesaleOrders.Domain.ValueObjects.Sku.Parse("MIX-A");
        var inventoryItem = await inventoryService.AddItemAsync(sku, "Mix-A", onHand: 50);
        var orderId = Guid.NewGuid();

        var r1 = await inventoryService.ReserveAsync(orderId, sku, 5);
        var r2 = await inventoryService.ReserveAsync(orderId, sku, 7);
        await inventoryService.ConfirmAsync(r2.Id);
        var r3 = await inventoryService.ReserveAsync(orderId, sku, 3);

        await TryTriggerExpirySweepAsync(services, advanceBy: TimeSpan.FromMinutes(31));

        var item = db.InventoryItems.Single(i => i.Id == inventoryItem.Id);
        Assert.True(item.OnHand >= 0);
        Assert.True(item.Reserved >= 0);
        Assert.Equal(item.OnHand - item.Reserved, item.Available);
    }

    // --- helpers ----------------------------------------------------------------

    private async Task<(Guid reservationId, Guid inventoryItemId, AppDbContext db, IServiceProvider services)>
        SetupReservationAsync(int quantity, int onHand)
    {
        var services = _factory.Services;
        var db = services.GetRequiredService<AppDbContext>();
        var inventoryService = services.GetRequiredService<IInventoryService>();
        var orderService = services.GetRequiredService<IOrderService>();

        var customer = new Customer { Name = "Test", Email = "test@test", BillingAddress = "x", ShippingAddress = "x" };
        services.GetRequiredService<ICustomerRepository>().AddAsync(customer).Wait();

        var sku = WholesaleOrders.Domain.ValueObjects.Sku.Parse($"SKU-{Guid.NewGuid():N}");
        var item = await inventoryService.AddItemAsync(sku, "Test Item", onHand);

        var order = await orderService.CreateDraftAsync(customer.Id);
        var reservation = await inventoryService.ReserveAsync(order.Id, sku, quantity);

        return (reservation.Id, item.Id, db, services);
    }

    /// <summary>
    /// Probes for the expiry mechanism the model implemented. Tries (in order):
    ///   1) Setting an ExpiresAt-style field on each reservation to (now - 1 minute) via reflection,
    ///      then invoking a public method named *Expir* / *Sweep* on the inventory service or a new service.
    ///   2) An HTTP endpoint at any of the conventional paths (/api/inventory/sweep-expired-reservations,
    ///      /api/inventory/reservations/sweep, /api/inventory/expire, etc.).
    ///   3) An ITestClock-style shim if registered in DI.
    /// Returns true if any path produced an effect; false otherwise.
    /// </summary>
    private async Task<bool> TryTriggerExpirySweepAsync(IServiceProvider services, TimeSpan advanceBy)
    {
        // Strategy 1 — reflection-driven sweep method.
        var inventoryService = services.GetRequiredService<IInventoryService>();
        var sweepMethod = inventoryService.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(m =>
                m.Name.Contains("Expir", StringComparison.OrdinalIgnoreCase) ||
                m.Name.Contains("Sweep", StringComparison.OrdinalIgnoreCase));

        if (sweepMethod is not null)
        {
            ForceReservationsExpired(services, advanceBy);
            var args = sweepMethod.GetParameters().Select(p => p.HasDefaultValue ? p.DefaultValue : null).ToArray();
            var result = sweepMethod.Invoke(inventoryService, args);
            if (result is Task t) await t;
            return true;
        }

        // Strategy 2 — look for a hosted/background service with a public sweep method.
        var hostedServices = services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().ToList();
        foreach (var hs in hostedServices)
        {
            var m = hs.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic)
                .FirstOrDefault(mi =>
                    mi.Name.Contains("Expir", StringComparison.OrdinalIgnoreCase) ||
                    mi.Name.Contains("Sweep", StringComparison.OrdinalIgnoreCase) ||
                    mi.Name.Contains("Process", StringComparison.OrdinalIgnoreCase));
            if (m is null) continue;
            ForceReservationsExpired(services, advanceBy);
            var args = m.GetParameters().Select(p => p.HasDefaultValue ? p.DefaultValue : null).ToArray();
            var result = m.Invoke(hs, args);
            if (result is Task t) await t;
            return true;
        }

        // Strategy 3 — HTTP endpoint probing.
        using var client = _factory.CreateClient();
        var candidates = new[]
        {
            "/api/inventory/sweep-expired-reservations",
            "/api/inventory/reservations/sweep",
            "/api/inventory/expire",
            "/api/inventory/reservations/expire",
            "/api/reservations/sweep-expired",
        };
        foreach (var path in candidates)
        {
            ForceReservationsExpired(services, advanceBy);
            var resp = await client.PostAsync(path, JsonContent.Create(new { }));
            if (resp.StatusCode != HttpStatusCode.NotFound) return true;
        }

        return false;
    }

    /// <summary>
    /// Best-effort: if the model added an ExpiresAt-style field on StockReservation,
    /// set it to a time in the past so the sweep can find them.
    /// </summary>
    private void ForceReservationsExpired(IServiceProvider services, TimeSpan advanceBy)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var prop = typeof(StockReservation).GetProperties()
            .FirstOrDefault(p => p.Name.Contains("Expir", StringComparison.OrdinalIgnoreCase) && p.CanWrite);
        if (prop is null) return;

        var pastTime = DateTimeOffset.UtcNow - advanceBy;
        foreach (var r in db.Reservations.Where(r => r.Status == ReservationStatus.Created).ToList())
        {
            if (prop.PropertyType == typeof(DateTimeOffset)) prop.SetValue(r, pastTime);
            else if (prop.PropertyType == typeof(DateTimeOffset?)) prop.SetValue(r, (DateTimeOffset?)pastTime);
            else if (prop.PropertyType == typeof(DateTime)) prop.SetValue(r, pastTime.UtcDateTime);
            else if (prop.PropertyType == typeof(DateTime?)) prop.SetValue(r, (DateTime?)pastTime.UtcDateTime);
        }
    }
}
