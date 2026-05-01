// Acceptance tests for T1.C — Partial order fulfillment.
// Drop into tests/WholesaleOrders.Tests/ at grading time.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using WholesaleOrders.Domain.Entities;
using WholesaleOrders.Domain.Enums;
using WholesaleOrders.Infra.Persistence;

namespace WholesaleOrders.Tests.Acceptance.T1C;

public class T1C_PartialFulfillment_AcceptanceTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public T1C_PartialFulfillment_AcceptanceTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<(Guid orderId, List<OrderLineItem> lineItems)> SetupPaidOrderAsync(int qtyA, int qtyB)
    {
        var services = _factory.Services;
        var customerRepo = services.GetRequiredService<ICustomerRepository>();
        var inv = services.GetRequiredService<WholesaleOrders.Services.IInventoryService>();
        var orderService = services.GetRequiredService<WholesaleOrders.Services.IOrderService>();
        var payment = services.GetRequiredService<WholesaleOrders.Services.IPaymentService>();

        var customer = new Customer { Name = "T1C", Email = "c@c", BillingAddress = "x", ShippingAddress = "x" };
        await customerRepo.AddAsync(customer);

        var skuA = WholesaleOrders.Domain.ValueObjects.Sku.Parse("PRT-A");
        var skuB = WholesaleOrders.Domain.ValueObjects.Sku.Parse("PRT-B");
        await inv.AddItemAsync(skuA, "PrtA", 100);
        await inv.AddItemAsync(skuB, "PrtB", 100);

        var order = await orderService.CreateDraftAsync(customer.Id);
        await orderService.AddLineItemAsync(order.Id, skuA, qtyA, 1m);
        await orderService.AddLineItemAsync(order.Id, skuB, qtyB, 1m);
        await orderService.SubmitAsync(order.Id);

        var refreshed = await services.GetRequiredService<IOrderRepository>().GetByIdAsync(order.Id);
        await payment.ChargeAsync(order.Id, refreshed!.TotalAmount, "tok", $"k-{order.Id}", "test");
        await orderService.MarkPaidAsync(order.Id);

        var lineItems = await services.GetRequiredService<IOrderRepository>().GetLineItemsAsync(order.Id);
        return (order.Id, lineItems);
    }

    [Fact]
    public async Task Acceptance_Order_Status_Stays_Paid_After_Partial_Shipment()
    {
        var (orderId, lineItems) = await SetupPaidOrderAsync(qtyA: 3, qtyB: 5);
        var first = lineItems.First();

        var client = _factory.CreateClient();
        var shipResp = await client.PostAsJsonAsync(
            "/api/shipments",
            new { orderId, carrier = "FedEx", lineItems = new[] { new { lineItemId = first.Id, quantity = first.Quantity } } });
        Assert.True(shipResp.IsSuccessStatusCode, $"Partial shipment must succeed; got {shipResp.StatusCode}");

        var orderResp = await client.GetAsync($"/api/orders/{orderId}");
        var json = await orderResp.Content.ReadAsStringAsync();
        var status = JsonDocument.Parse(json).RootElement.GetProperty("status");
        var statusStr = status.ValueKind == JsonValueKind.String ? status.GetString() : ((OrderStatus)status.GetInt32()).ToString();
        Assert.Equal("Paid", statusStr);
    }

    [Fact]
    public async Task Acceptance_Order_Status_Becomes_Shipped_After_All_Items_Shipped()
    {
        var (orderId, lineItems) = await SetupPaidOrderAsync(qtyA: 2, qtyB: 4);
        var client = _factory.CreateClient();

        foreach (var li in lineItems)
        {
            var resp = await client.PostAsJsonAsync(
                "/api/shipments",
                new { orderId, carrier = "UPS", lineItems = new[] { new { lineItemId = li.Id, quantity = li.Quantity } } });
            Assert.True(resp.IsSuccessStatusCode, $"Shipment for line item {li.Id} failed: {resp.StatusCode}");
        }

        var orderResp = await client.GetAsync($"/api/orders/{orderId}");
        var json = await orderResp.Content.ReadAsStringAsync();
        var status = JsonDocument.Parse(json).RootElement.GetProperty("status");
        var statusStr = status.ValueKind == JsonValueKind.String ? status.GetString() : ((OrderStatus)status.GetInt32()).ToString();
        Assert.Equal("Shipped", statusStr);
    }

    [Fact]
    public async Task Acceptance_Cannot_Ship_More_Than_Line_Item_Quantity()
    {
        var (orderId, lineItems) = await SetupPaidOrderAsync(qtyA: 3, qtyB: 3);
        var first = lineItems.First();

        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync(
            "/api/shipments",
            new { orderId, carrier = "FedEx", lineItems = new[] { new { lineItemId = first.Id, quantity = first.Quantity + 1 } } });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Acceptance_Shipments_List_Returns_All_Shipments_For_Order()
    {
        var (orderId, lineItems) = await SetupPaidOrderAsync(qtyA: 2, qtyB: 4);
        var client = _factory.CreateClient();
        await client.PostAsJsonAsync(
            "/api/shipments",
            new { orderId, carrier = "FedEx", lineItems = new[] { new { lineItemId = lineItems[0].Id, quantity = 1 } } });
        await client.PostAsJsonAsync(
            "/api/shipments",
            new { orderId, carrier = "FedEx", lineItems = new[] { new { lineItemId = lineItems[0].Id, quantity = 1 } } });

        var listResp = await client.GetAsync($"/api/shipments?orderId={orderId}");
        Assert.True(listResp.IsSuccessStatusCode, $"List shipments failed: {listResp.StatusCode}");
        var listJson = await listResp.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(listJson);
        Assert.True(doc.RootElement.GetArrayLength() >= 2, $"Expected ≥2 shipments, got JSON: {listJson}");
    }
}
