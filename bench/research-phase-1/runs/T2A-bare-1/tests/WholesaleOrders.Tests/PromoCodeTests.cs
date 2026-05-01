using WholesaleOrders.Services;

namespace WholesaleOrders.Tests;

public class PromoCodeTests
{
    [Fact]
    public async Task Submit_Without_PromoCode_Keeps_Full_Price()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 4, 25m));

        await f.Orders.SubmitAsync(order.Id);

        var fresh = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.NotNull(fresh);
        Assert.Null(fresh!.PromoCode);
        Assert.Equal(0m, fresh.DiscountPercent);
        Assert.Equal(0m, fresh.DiscountAmount.Amount);
        Assert.Equal(100m, fresh.Subtotal.Amount);
        Assert.Equal(100m, fresh.TotalAmount.Amount);
    }

    [Fact]
    public async Task Submit_With_Welcome10_Applies_10_Percent_Discount()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 4, 25m));

        await f.Orders.SubmitAsync(order.Id, "WELCOME10");

        var fresh = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.NotNull(fresh);
        Assert.Equal("WELCOME10", fresh!.PromoCode);
        Assert.Equal(10m, fresh.DiscountPercent);
        Assert.Equal(100m, fresh.Subtotal.Amount);
        Assert.Equal(10m, fresh.DiscountAmount.Amount);
        Assert.Equal(90m, fresh.TotalAmount.Amount);
    }

    [Fact]
    public async Task Submit_With_Bulk25_Applies_25_Percent_Discount()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 8, 25m));

        await f.Orders.SubmitAsync(order.Id, "BULK25");

        var fresh = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.NotNull(fresh);
        Assert.Equal("BULK25", fresh!.PromoCode);
        Assert.Equal(25m, fresh.DiscountPercent);
        Assert.Equal(200m, fresh.Subtotal.Amount);
        Assert.Equal(50m, fresh.DiscountAmount.Amount);
        Assert.Equal(150m, fresh.TotalAmount.Amount);
    }

    [Fact]
    public async Task Submit_With_PromoCode_Is_Case_Insensitive()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 1, 100m));

        await f.Orders.SubmitAsync(order.Id, "welcome10");

        var fresh = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.Equal(10m, fresh!.DiscountPercent);
        Assert.Equal(90m, fresh.TotalAmount.Amount);
    }

    [Fact]
    public async Task Submit_With_Unknown_PromoCode_Throws_InvalidOperation()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 1, 100m));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => f.Orders.SubmitAsync(order.Id, "BOGUS-CODE"));
    }

    [Fact]
    public async Task Submit_With_Empty_PromoCode_Treated_As_None()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 2, 50m));

        await f.Orders.SubmitAsync(order.Id, "");

        var fresh = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.Null(fresh!.PromoCode);
        Assert.Equal(100m, fresh.TotalAmount.Amount);
    }

    [Fact]
    public void PromoCodeService_GetDiscountPercent_Returns_Known_Values()
    {
        var svc = new PromoCodeService();
        Assert.Equal(10m, svc.GetDiscountPercent("WELCOME10"));
        Assert.Equal(25m, svc.GetDiscountPercent("BULK25"));
    }

    [Fact]
    public void PromoCodeService_TryGetDiscountPercent_Returns_False_For_Unknown()
    {
        var svc = new PromoCodeService();
        Assert.False(svc.TryGetDiscountPercent("NOPE", out var pct));
        Assert.Equal(0m, pct);
        Assert.True(svc.TryGetDiscountPercent("BULK25", out pct));
        Assert.Equal(25m, pct);
    }

    [Fact]
    public async Task Submit_Discount_Does_Not_Apply_Retroactively_To_Existing_Submitted_Orders()
    {
        // Once an order is submitted (and locked at full price), subsequent submit calls fail —
        // existing orders cannot be retroactively discounted.
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 1, 100m));

        await f.Orders.SubmitAsync(order.Id);
        var afterFirst = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.Equal(100m, afterFirst!.TotalAmount.Amount);
        Assert.Null(afterFirst.PromoCode);

        // Re-submitting with a promo code should fail because the order is no longer in Draft.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => f.Orders.SubmitAsync(order.Id, "WELCOME10"));

        var afterSecond = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.Equal(100m, afterSecond!.TotalAmount.Amount);
        Assert.Null(afterSecond.PromoCode);
    }
}
