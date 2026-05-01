// Acceptance tests for T1.A — Order priority.
// Drop into tests/WholesaleOrders.Tests/ at grading time.
// Requires: Microsoft.AspNetCore.Mvc.Testing.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using WholesaleOrders.Domain.Entities;
using WholesaleOrders.Infra.Persistence;

namespace WholesaleOrders.Tests.Acceptance.T1A;

public class T1A_OrderPriority_AcceptanceTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public T1A_OrderPriority_AcceptanceTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<Guid> SeedCustomerAsync()
    {
        var c = new Customer { Name = "T1A", Email = "t@t", BillingAddress = "x", ShippingAddress = "x" };
        await _factory.Services.GetRequiredService<ICustomerRepository>().AddAsync(c);
        return c.Id;
    }

    [Fact]
    public async Task Acceptance_Submit_With_Priority_Persists()
    {
        var client = _factory.CreateClient();
        var customerId = await SeedCustomerAsync();
        var resp = await client.PostAsJsonAsync("/api/orders", new { customerId, currency = "USD", priority = "Critical" });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadAsStringAsync();
        Assert.Contains("Critical", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Acceptance_Submit_Without_Priority_Defaults_To_Standard()
    {
        var client = _factory.CreateClient();
        var customerId = await SeedCustomerAsync();
        var resp = await client.PostAsJsonAsync("/api/orders", new { customerId, currency = "USD" });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadAsStringAsync();
        // Priority defaults to "Standard" (or its enum-int equivalent — the prompt's "treated as Standard")
        var doc = JsonDocument.Parse(json);
        var priorityProp = doc.RootElement.EnumerateObject()
            .FirstOrDefault(p => p.Name.Equals("Priority", StringComparison.OrdinalIgnoreCase));
        // If a Priority field exists, it should be "Standard" or 0 (default enum value).
        if (priorityProp.Value.ValueKind != JsonValueKind.Undefined)
        {
            var s = priorityProp.Value.ValueKind == JsonValueKind.String ? priorityProp.Value.GetString() : priorityProp.Value.ToString();
            Assert.True(s == "Standard" || s == "0", $"Default priority should be Standard, got {s}");
        }
    }

    [Fact]
    public async Task Acceptance_Submit_With_Invalid_Priority_Returns_400()
    {
        var client = _factory.CreateClient();
        var customerId = await SeedCustomerAsync();
        var resp = await client.PostAsJsonAsync("/api/orders", new { customerId, currency = "USD", priority = "Urgent" });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Acceptance_Shipment_Schedule_Critical_Before_Expedited_Before_Standard()
    {
        // Create three orders, advance them to Paid, then check schedulable order.
        // Uses internal services because the API surface for "advance to Paid" is multi-step;
        // this stays acceptance-flavored because we still verify behavior at the schedulable endpoint.
        var services = _factory.Services;
        var orderService = services.GetRequiredService<WholesaleOrders.Services.IOrderService>();
        var paymentService = services.GetRequiredService<WholesaleOrders.Services.IPaymentService>();
        var customerId = await SeedCustomerAsync();
        var inv = services.GetRequiredService<WholesaleOrders.Services.IInventoryService>();
        var sku = WholesaleOrders.Domain.ValueObjects.Sku.Parse("ORD-A");
        await inv.AddItemAsync(sku, "OrdA", 100);

        async Task<Guid> MakeOrder(string priority)
        {
            var resp = await _factory.CreateClient().PostAsJsonAsync(
                "/api/orders",
                new { customerId, currency = "USD", priority });
            var json = await resp.Content.ReadAsStringAsync();
            var id = JsonDocument.Parse(json).RootElement.GetProperty("id").GetGuid();
            await orderService.AddLineItemAsync(id, sku, 1, 1m);
            await orderService.SubmitAsync(id);
            await paymentService.ChargeAsync(id, new WholesaleOrders.Domain.ValueObjects.Money(1m, "USD"), "tok", $"k-{id}", "test");
            await orderService.MarkPaidAsync(id);
            return id;
        }

        var standardId = await MakeOrder("Standard");
        var expeditedId = await MakeOrder("Expedited");
        var criticalId = await MakeOrder("Critical");

        var schedulableResp = await _factory.CreateClient().GetAsync("/api/shipments/schedulable");
        var schedulableJson = await schedulableResp.Content.ReadAsStringAsync();
        var schedulableDoc = JsonDocument.Parse(schedulableJson);
        var ids = schedulableDoc.RootElement.EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ToList();

        var iCritical = ids.IndexOf(criticalId);
        var iExpedited = ids.IndexOf(expeditedId);
        var iStandard = ids.IndexOf(standardId);

        Assert.True(iCritical >= 0 && iExpedited >= 0 && iStandard >= 0,
            "All three orders should appear in schedulable list");
        Assert.True(iCritical < iExpedited, "Critical must be scheduled before Expedited");
        Assert.True(iExpedited < iStandard, "Expedited must be scheduled before Standard");
    }
}
