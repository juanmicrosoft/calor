using WholesaleOrders.Domain.Enums;
using WholesaleOrders.Services;

namespace WholesaleOrders.Tests;

public class PromoCodeTests
{
    [Fact]
    public void PromoCodeService_Resolves_Welcome10()
    {
        var svc = new PromoCodeService();
        Assert.Equal(0.10m, svc.GetDiscountPercentage("WELCOME10"));
    }

    [Fact]
    public void PromoCodeService_Resolves_Bulk25()
    {
        var svc = new PromoCodeService();
        Assert.Equal(0.25m, svc.GetDiscountPercentage("BULK25"));
    }

    [Fact]
    public void PromoCodeService_IsCaseInsensitive()
    {
        var svc = new PromoCodeService();
        Assert.Equal(0.10m, svc.GetDiscountPercentage("welcome10"));
        Assert.Equal(0.25m, svc.GetDiscountPercentage("Bulk25"));
    }

    [Fact]
    public void PromoCodeService_UnknownCode_Throws()
    {
        var svc = new PromoCodeService();
        Assert.Throws<InvalidOperationException>(() => svc.GetDiscountPercentage("BOGUS"));
    }

    [Fact]
    public void PromoCodeService_Empty_Throws()
    {
        var svc = new PromoCodeService();
        Assert.Throws<InvalidOperationException>(() => svc.GetDiscountPercentage(""));
        Assert.Throws<InvalidOperationException>(() => svc.GetDiscountPercentage("   "));
    }

    [Fact]
    public async Task Submit_Without_PromoCode_Keeps_Full_Price()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 2, 50.00m));

        await f.Orders.SubmitAsync(order.Id);

        var fresh = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.NotNull(fresh);
        Assert.Equal(100.00m, fresh!.TotalAmount.Amount);
        Assert.Null(fresh.PromoCode);
        Assert.Equal(0m, fresh.DiscountPercentage);
        Assert.Equal(0m, fresh.DiscountAmount.Amount);
    }

    [Fact]
    public async Task Submit_With_Welcome10_Applies_10_Percent_Discount()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 2, 50.00m));

        await f.Orders.SubmitAsync(order.Id, "WELCOME10");

        var fresh = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.NotNull(fresh);
        Assert.Equal("WELCOME10", fresh!.PromoCode);
        Assert.Equal(0.10m, fresh.DiscountPercentage);
        Assert.Equal(10.00m, fresh.DiscountAmount.Amount);
        Assert.Equal(90.00m, fresh.TotalAmount.Amount);
    }

    [Fact]
    public async Task Submit_With_Bulk25_Applies_25_Percent_Discount()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 4, 25.00m));

        await f.Orders.SubmitAsync(order.Id, "BULK25");

        var fresh = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.NotNull(fresh);
        Assert.Equal("BULK25", fresh!.PromoCode);
        Assert.Equal(0.25m, fresh.DiscountPercentage);
        Assert.Equal(25.00m, fresh.DiscountAmount.Amount);
        Assert.Equal(75.00m, fresh.TotalAmount.Amount);
    }

    [Fact]
    public async Task Submit_With_Unknown_Code_Throws_And_Does_Not_Submit()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 1, 10.00m));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => f.Orders.SubmitAsync(order.Id, "NOT_A_REAL_CODE"));

        // Order should remain in Draft, no discount fields set.
        var fresh = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.NotNull(fresh);
        Assert.Equal(OrderStatus.Draft, fresh!.Status);
        Assert.Null(fresh.PromoCode);
        Assert.Equal(0m, fresh.DiscountPercentage);
    }

    [Fact]
    public async Task Submit_With_Null_PromoCode_Submits_At_Full_Price()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 1, 10.00m));

        await f.Orders.SubmitAsync(order.Id, promoCode: null);

        var fresh = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.NotNull(fresh);
        Assert.Equal(OrderStatus.Submitted, fresh!.Status);
        Assert.Equal(10.00m, fresh.TotalAmount.Amount);
        Assert.Null(fresh.PromoCode);
    }

    [Fact]
    public async Task Discount_Only_Applies_At_Submission_Not_Retroactively()
    {
        // Existing orders submitted without a promo code do NOT get a retroactive discount.
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 1, 100.00m));

        await f.Orders.SubmitAsync(order.Id);

        // The order is Submitted; cannot be re-submitted to apply a discount.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => f.Orders.SubmitAsync(order.Id, "WELCOME10"));

        var fresh = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.NotNull(fresh);
        Assert.Equal(100.00m, fresh!.TotalAmount.Amount);
        Assert.Null(fresh.PromoCode);
    }

    [Fact]
    public async Task Submit_With_Empty_PromoCode_Submits_At_Full_Price()
    {
        // Backwards-compat: empty/whitespace string is treated as "no promo".
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 1, 10.00m));

        await f.Orders.SubmitAsync(order.Id, promoCode: "");

        var fresh = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.NotNull(fresh);
        Assert.Equal(OrderStatus.Submitted, fresh!.Status);
        Assert.Equal(10.00m, fresh.TotalAmount.Amount);
        Assert.Null(fresh.PromoCode);
    }
}
