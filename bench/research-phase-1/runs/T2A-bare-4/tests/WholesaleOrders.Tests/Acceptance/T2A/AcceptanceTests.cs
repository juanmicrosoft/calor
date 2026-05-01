// Acceptance + bug-detection tests for T2.A — Promo code discount.
// Drop into tests/WholesaleOrders.Tests/Acceptance/T2A/ at grading time.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using WholesaleOrders.Domain.Entities;
using WholesaleOrders.Domain.ValueObjects;
using WholesaleOrders.Infra.Persistence;

namespace WholesaleOrders.Tests.Acceptance.T2A;

public class T2A_PromoCode_AcceptanceTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public T2A_PromoCode_AcceptanceTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<(Guid orderId, IServiceScope scope)> SetupOrderAsync(decimal totalLineItems = 100m)
    {
        var scope = _factory.Services.CreateScope();
        var customerRepo = scope.ServiceProvider.GetRequiredService<ICustomerRepository>();
        var inv = scope.ServiceProvider.GetRequiredService<WholesaleOrders.Services.IInventoryService>();
        var orders = scope.ServiceProvider.GetRequiredService<WholesaleOrders.Services.IOrderService>();

        var c = new Customer { Name = "T2A", Email = "t@t", BillingAddress = "x", ShippingAddress = "x" };
        await customerRepo.AddAsync(c);
        var sku = Sku.Parse($"T2A-{Guid.NewGuid():N}");
        await inv.AddItemAsync(sku, "X", 100, unitPrice: 1m);
        var order = await orders.CreateDraftAsync(c.Id);
        await orders.AddLineItemAsync(order.Id, sku, (int)totalLineItems, 1m);
        return (order.Id, scope);
    }

    [Fact]
    public async Task Acceptance_Submit_With_Valid_Promo_Applies_Discount()
    {
        var (orderId, scope) = await SetupOrderAsync(100m);
        try
        {
            // Try submitting via API with a promo code.
            var client = _factory.CreateClient();
            HttpResponseMessage? resp = null;
            foreach (var path in new[] { $"/api/orders/{orderId}/submit", $"/api/orders/{orderId}/submit?promoCode=WELCOME10" })
            {
                resp = await client.PostAsJsonAsync(path, new { promoCode = "WELCOME10" });
                if (resp.IsSuccessStatusCode) break;
            }
            Assert.NotNull(resp);
            // Either the response includes a discount-related field or the order's totals reflect 10% off.
            // Behavioral check: discounted total or DiscountAmount field present.
            var body = await resp!.Content.ReadAsStringAsync();
            // Accept any of: "Discount", "GrandTotal", "DiscountedTotal", or a Total of 90 (10% off 100).
            Assert.True(
                body.Contains("Discount", StringComparison.OrdinalIgnoreCase) ||
                body.Contains("90") ||
                body.Contains("0.9"),
                $"Expected discount evidence in response. Got: {body}");
        }
        finally { scope.Dispose(); }
    }

    [Fact]
    public async Task Acceptance_Submit_With_Unknown_Promo_Returns_400()
    {
        var (orderId, scope) = await SetupOrderAsync();
        try
        {
            var client = _factory.CreateClient();
            HttpResponseMessage? resp = null;
            foreach (var path in new[] { $"/api/orders/{orderId}/submit", $"/api/orders/{orderId}/submit?promoCode=BADCODE" })
            {
                resp = await client.PostAsJsonAsync(path, new { promoCode = "BADCODE" });
                if (resp.StatusCode == HttpStatusCode.BadRequest) break;
            }
            Assert.NotNull(resp);
            Assert.Equal(HttpStatusCode.BadRequest, resp!.StatusCode);
        }
        finally { scope.Dispose(); }
    }

    /// <summary>
    /// BUG DETECTOR: After applying a promo, INV-1 must still hold:
    /// Order.TotalAmount == Σ(LineItem.Quantity × LineItem.UnitPrice).
    /// The natural buggy implementation modifies TotalAmount directly to reflect the
    /// discount, breaking the invariant. A correct implementation tracks the discount
    /// in a separate field (DiscountAmount) so TotalAmount stays as the line-item sum.
    /// </summary>
    [Fact]
    public async Task BugDetector_INV1_Holds_After_Promo_Applied()
    {
        var (orderId, scope) = await SetupOrderAsync(50m);
        try
        {
            var client = _factory.CreateClient();
            foreach (var path in new[] { $"/api/orders/{orderId}/submit", $"/api/orders/{orderId}/submit?promoCode=BULK25" })
            {
                var r = await client.PostAsJsonAsync(path, new { promoCode = "BULK25" });
                if (r.IsSuccessStatusCode) break;
            }

            // Read the persisted order and check INV-1.
            var db = _factory.Services.GetRequiredService<AppDbContext>();
            var order = db.Orders.Single(o => o.Id == orderId);
            var lineItems = db.OrderLineItems.Where(li => li.OrderId == orderId).ToList();
            var lineSum = lineItems.Sum(li => li.Quantity * li.UnitPrice);

            // INV-1 holds iff TotalAmount equals the line-item sum.
            Assert.Equal(lineSum, order.TotalAmount.Amount);
        }
        finally { scope.Dispose(); }
    }
}
