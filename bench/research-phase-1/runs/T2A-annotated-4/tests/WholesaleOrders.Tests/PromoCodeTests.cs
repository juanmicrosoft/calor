using WholesaleOrders.Domain.Enums;
using WholesaleOrders.Services;

namespace WholesaleOrders.Tests;

public class PromoCodeTests
{
    [Fact]
    public async Task Submit_With_No_PromoCode_Charges_Full_Price()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 2, 50.00m));

        await f.Orders.SubmitAsync(order.Id);

        var fresh = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.NotNull(fresh);
        Assert.Equal(OrderStatus.Submitted, fresh!.Status);
        Assert.Null(fresh.PromoCode);
        Assert.Equal(0m, fresh.DiscountPercentage);
        Assert.Equal(100.00m, fresh.TotalAmount.Amount);
        Assert.Equal(100.00m, fresh.Subtotal.Amount);
    }

    [Fact]
    public async Task Submit_With_WELCOME10_Applies_10_Percent_Discount()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 2, 50.00m));

        await f.Orders.SubmitAsync(order.Id, "WELCOME10");

        var fresh = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.NotNull(fresh);
        Assert.Equal("WELCOME10", fresh!.PromoCode);
        Assert.Equal(10m, fresh.DiscountPercentage);
        Assert.Equal(100.00m, fresh.Subtotal.Amount);
        Assert.Equal(90.00m, fresh.TotalAmount.Amount);
    }

    [Fact]
    public async Task Submit_With_BULK25_Applies_25_Percent_Discount()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 4, 25.00m));

        await f.Orders.SubmitAsync(order.Id, "BULK25");

        var fresh = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.NotNull(fresh);
        Assert.Equal("BULK25", fresh!.PromoCode);
        Assert.Equal(25m, fresh.DiscountPercentage);
        Assert.Equal(100.00m, fresh.Subtotal.Amount);
        Assert.Equal(75.00m, fresh.TotalAmount.Amount);
    }

    [Fact]
    public async Task Submit_With_PromoCode_Is_CaseInsensitive()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 1, 100.00m));

        await f.Orders.SubmitAsync(order.Id, "welcome10");

        var fresh = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.NotNull(fresh);
        Assert.Equal(10m, fresh!.DiscountPercentage);
        Assert.Equal(90.00m, fresh.TotalAmount.Amount);
    }

    [Fact]
    public async Task Submit_With_Unknown_PromoCode_Throws_InvalidOperationException()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 1, 10.00m));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => f.Orders.SubmitAsync(order.Id, "BOGUS_CODE"));

        // Order remains in Draft (submission failed).
        var fresh = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.Equal(OrderStatus.Draft, fresh!.Status);
    }

    [Fact]
    public async Task Submit_With_Empty_PromoCode_Charges_Full_Price()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 1, 10.00m));

        await f.Orders.SubmitAsync(order.Id, "   ");

        var fresh = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.NotNull(fresh);
        Assert.Null(fresh!.PromoCode);
        Assert.Equal(0m, fresh.DiscountPercentage);
        Assert.Equal(10.00m, fresh.TotalAmount.Amount);
    }

    [Fact]
    public void PromoCodeService_Recognizes_Hardcoded_Codes()
    {
        IPromoCodeService svc = new PromoCodeService();

        Assert.True(svc.TryGetDiscountPercentage("WELCOME10", out var w));
        Assert.Equal(10m, w);

        Assert.True(svc.TryGetDiscountPercentage("BULK25", out var b));
        Assert.Equal(25m, b);

        Assert.False(svc.TryGetDiscountPercentage("NOPE", out var n));
        Assert.Equal(0m, n);

        Assert.False(svc.TryGetDiscountPercentage("", out _));
        Assert.False(svc.TryGetDiscountPercentage(null!, out _));
    }
}
