using WholesaleOrders.Domain.Enums;
using WholesaleOrders.Services;

namespace WholesaleOrders.Tests;

public class PromoCodeTests
{
    [Fact]
    public async Task Submit_Without_PromoCode_Keeps_Full_Price()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 2, 50m));

        await f.Orders.SubmitAsync(order.Id);

        var fresh = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.NotNull(fresh);
        Assert.Equal(OrderStatus.Submitted, fresh!.Status);
        Assert.Null(fresh.PromoCode);
        Assert.Equal(0m, fresh.DiscountPercent);
        Assert.Equal(0m, fresh.DiscountAmount.Amount);
        Assert.Equal(100m, fresh.TotalAmount.Amount);
        Assert.Equal(100m, fresh.Subtotal.Amount);
    }

    [Fact]
    public async Task Submit_With_Welcome10_Applies_10_Percent_Discount()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 2, 50m));

        await f.Orders.SubmitAsync(order.Id, "WELCOME10");

        var fresh = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.NotNull(fresh);
        Assert.Equal("WELCOME10", fresh!.PromoCode);
        Assert.Equal(10m, fresh.DiscountPercent);
        Assert.Equal(10m, fresh.DiscountAmount.Amount);
        Assert.Equal(100m, fresh.Subtotal.Amount);
        Assert.Equal(90m, fresh.TotalAmount.Amount);
    }

    [Fact]
    public async Task Submit_With_Bulk25_Applies_25_Percent_Discount()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 4, 25m));

        await f.Orders.SubmitAsync(order.Id, "BULK25");

        var fresh = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.NotNull(fresh);
        Assert.Equal("BULK25", fresh!.PromoCode);
        Assert.Equal(25m, fresh.DiscountPercent);
        Assert.Equal(25m, fresh.DiscountAmount.Amount);
        Assert.Equal(100m, fresh.Subtotal.Amount);
        Assert.Equal(75m, fresh.TotalAmount.Amount);
    }

    [Fact]
    public async Task Submit_With_Unknown_PromoCode_Throws()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 1, 10m));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => f.Orders.SubmitAsync(order.Id, "NOPE99"));

        // Order should remain in Draft after a failed submit.
        var fresh = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.NotNull(fresh);
        Assert.Equal(OrderStatus.Draft, fresh!.Status);
    }

    [Fact]
    public async Task Submit_With_Empty_PromoCode_Is_Treated_As_None()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 1, 10m));

        await f.Orders.SubmitAsync(order.Id, "");

        var fresh = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.NotNull(fresh);
        Assert.Equal(OrderStatus.Submitted, fresh!.Status);
        Assert.Null(fresh.PromoCode);
        Assert.Equal(10m, fresh.TotalAmount.Amount);
    }

    [Fact]
    public async Task Submit_PromoCode_Is_Case_Insensitive()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 1, 100m));

        await f.Orders.SubmitAsync(order.Id, "welcome10");

        var fresh = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.NotNull(fresh);
        Assert.Equal(10m, fresh!.DiscountPercent);
        Assert.Equal(90m, fresh.TotalAmount.Amount);
    }

    [Fact]
    public void PromoCodeService_Returns_Correct_Percentages()
    {
        var svc = new PromoCodeService();
        Assert.Equal(10m, svc.GetDiscountPercent("WELCOME10"));
        Assert.Equal(25m, svc.GetDiscountPercent("BULK25"));
    }

    [Fact]
    public void PromoCodeService_Throws_For_Unknown_Code()
    {
        var svc = new PromoCodeService();
        Assert.Throws<InvalidOperationException>(() => svc.GetDiscountPercent("BOGUS"));
    }

    [Fact]
    public void PromoCodeService_TryGet_Returns_False_For_Unknown_Code()
    {
        var svc = new PromoCodeService();
        Assert.False(svc.TryGetDiscountPercent("BOGUS", out var pct));
        Assert.Equal(0m, pct);
    }
}
