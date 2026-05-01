using WholesaleOrders.Domain.Enums;
using WholesaleOrders.Domain.ValueObjects;
using WholesaleOrders.Services;

namespace WholesaleOrders.Tests;

public class PromoCodeTests
{
    [Fact]
    public void PromoCodeService_Resolves_Known_Codes()
    {
        var svc = new PromoCodeService();
        Assert.Equal(10m, svc.GetDiscountPercent("WELCOME10"));
        Assert.Equal(25m, svc.GetDiscountPercent("BULK25"));
    }

    [Fact]
    public void PromoCodeService_Is_Case_Insensitive()
    {
        var svc = new PromoCodeService();
        Assert.Equal(10m, svc.GetDiscountPercent("welcome10"));
        Assert.Equal(25m, svc.GetDiscountPercent("Bulk25"));
    }

    [Fact]
    public void PromoCodeService_Rejects_Unknown_Code()
    {
        var svc = new PromoCodeService();
        Assert.Throws<InvalidOperationException>(() => svc.GetDiscountPercent("NOPE"));
        Assert.False(svc.TryGetDiscountPercent("NOPE", out var pct));
        Assert.Equal(0m, pct);
    }

    [Fact]
    public async Task Submit_Without_PromoCode_Charges_Full_Price()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 2, 50m));

        await f.Orders.SubmitAsync(order.Id);

        var fresh = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.Equal(OrderStatus.Submitted, fresh!.Status);
        Assert.Null(fresh.PromoCode);
        Assert.Null(fresh.DiscountPercent);
        Assert.Equal(100m, fresh.TotalAmount.Amount);
    }

    [Fact]
    public async Task Submit_With_Welcome10_Applies_10_Percent_Off()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 2, 50m));

        await f.Orders.SubmitAsync(order.Id, "WELCOME10");

        var fresh = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.Equal(OrderStatus.Submitted, fresh!.Status);
        Assert.Equal("WELCOME10", fresh.PromoCode);
        Assert.Equal(10m, fresh.DiscountPercent);
        Assert.Equal(90m, fresh.TotalAmount.Amount);
    }

    [Fact]
    public async Task Submit_With_Bulk25_Applies_25_Percent_Off()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 4, 25m));

        await f.Orders.SubmitAsync(order.Id, "BULK25");

        var fresh = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.Equal("BULK25", fresh!.PromoCode);
        Assert.Equal(25m, fresh.DiscountPercent);
        Assert.Equal(75m, fresh.TotalAmount.Amount);
    }

    [Fact]
    public async Task Submit_With_Unknown_PromoCode_Throws()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 1, 10m));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => f.Orders.SubmitAsync(order.Id, "BOGUS"));

        // Order must remain in Draft and untouched after rejection.
        var fresh = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.Equal(OrderStatus.Draft, fresh!.Status);
        Assert.Null(fresh.PromoCode);
        Assert.Null(fresh.DiscountPercent);
        Assert.Null(fresh.SubmittedAt);
    }

    [Fact]
    public async Task Submit_With_Empty_PromoCode_Treated_As_None()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 1, 20m));

        await f.Orders.SubmitAsync(order.Id, "");

        var fresh = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.Equal(OrderStatus.Submitted, fresh!.Status);
        Assert.Null(fresh.PromoCode);
        Assert.Null(fresh.DiscountPercent);
        Assert.Equal(20m, fresh.TotalAmount.Amount);
    }

    [Fact]
    public async Task Discount_Not_Retroactive_To_Existing_Submitted_Orders()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 2, 50m));

        // Submit with no promo first.
        await f.Orders.SubmitAsync(order.Id);
        var afterFirst = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.Equal(100m, afterFirst!.TotalAmount.Amount);

        // Re-submitting an already-submitted order must fail (validator rejects non-Draft).
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => f.Orders.SubmitAsync(order.Id, "WELCOME10"));

        var stillFull = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.Null(stillFull!.PromoCode);
        Assert.Null(stillFull.DiscountPercent);
        Assert.Equal(100m, stillFull.TotalAmount.Amount);
    }

    [Fact]
    public async Task Submit_PromoCode_Lookup_Is_Case_Insensitive()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 1, 100m));

        await f.Orders.SubmitAsync(order.Id, "welcome10");

        var fresh = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.Equal(10m, fresh!.DiscountPercent);
        Assert.Equal(90m, fresh.TotalAmount.Amount);
    }
}
