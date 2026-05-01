using WholesaleOrders.Services;

namespace WholesaleOrders.Tests;

public class PromoCodeTests
{
    [Fact]
    public async Task Submit_With_WELCOME10_Applies_10_Percent_Discount()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 2, 50.00m));
        Assert.Equal(100.00m, order.TotalAmount.Amount);

        await f.Orders.SubmitAsync(order.Id, "WELCOME10");

        var fresh = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.NotNull(fresh);
        Assert.Equal(90.00m, fresh!.TotalAmount.Amount);
        Assert.Equal("WELCOME10", fresh.PromoCode);
        Assert.Equal(10m, fresh.DiscountPercentage);
    }

    [Fact]
    public async Task Submit_With_BULK25_Applies_25_Percent_Discount()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 4, 25.00m));
        Assert.Equal(100.00m, order.TotalAmount.Amount);

        await f.Orders.SubmitAsync(order.Id, "BULK25");

        var fresh = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.NotNull(fresh);
        Assert.Equal(75.00m, fresh!.TotalAmount.Amount);
        Assert.Equal("BULK25", fresh.PromoCode);
        Assert.Equal(25m, fresh.DiscountPercentage);
    }

    [Fact]
    public async Task Submit_With_PromoCode_Is_Case_Insensitive()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 1, 100m));

        await f.Orders.SubmitAsync(order.Id, "welcome10");

        var fresh = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.Equal(90m, fresh!.TotalAmount.Amount);
        Assert.Equal(10m, fresh.DiscountPercentage);
    }

    [Fact]
    public async Task Submit_With_Unknown_PromoCode_Throws_InvalidOperation()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 1, 10m));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => f.Orders.SubmitAsync(order.Id, "NOT_A_REAL_CODE"));
    }

    [Fact]
    public async Task Submit_Without_PromoCode_Charges_Full_Price()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 2, 25m));

        await f.Orders.SubmitAsync(order.Id);

        var fresh = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.Equal(50m, fresh!.TotalAmount.Amount);
        Assert.Null(fresh.PromoCode);
        Assert.Equal(0m, fresh.DiscountPercentage);
    }

    [Fact]
    public async Task Submit_With_Null_PromoCode_Charges_Full_Price()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 2, 25m));

        await f.Orders.SubmitAsync(order.Id, promoCode: null);

        var fresh = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.Equal(50m, fresh!.TotalAmount.Amount);
        Assert.Null(fresh.PromoCode);
        Assert.Equal(0m, fresh.DiscountPercentage);
    }

    [Fact]
    public async Task Submit_With_Empty_PromoCode_Charges_Full_Price()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 2, 25m));

        await f.Orders.SubmitAsync(order.Id, promoCode: "");

        var fresh = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.Equal(50m, fresh!.TotalAmount.Amount);
        Assert.Null(fresh.PromoCode);
    }

    [Fact]
    public void PromoCodeService_GetDiscountPercentage_Known_Codes()
    {
        var svc = new PromoCodeService();
        Assert.Equal(10m, svc.GetDiscountPercentage("WELCOME10"));
        Assert.Equal(25m, svc.GetDiscountPercentage("BULK25"));
    }

    [Fact]
    public void PromoCodeService_GetDiscountPercentage_Unknown_Throws()
    {
        var svc = new PromoCodeService();
        Assert.Throws<InvalidOperationException>(() => svc.GetDiscountPercentage("NOPE"));
    }

    [Fact]
    public void PromoCodeService_TryGet_Returns_True_For_Known()
    {
        var svc = new PromoCodeService();
        Assert.True(svc.TryGetDiscountPercentage("WELCOME10", out var pct));
        Assert.Equal(10m, pct);
    }

    [Fact]
    public void PromoCodeService_TryGet_Returns_False_For_Unknown()
    {
        var svc = new PromoCodeService();
        Assert.False(svc.TryGetDiscountPercentage("NOPE", out var pct));
        Assert.Equal(0m, pct);
    }
}
