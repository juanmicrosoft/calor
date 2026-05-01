using WholesaleOrders.Domain.Enums;
using WholesaleOrders.Services;

namespace WholesaleOrders.Tests;

public class PromoCodeTests
{
    [Fact]
    public void PromoCodeService_Returns_Null_For_Null_Or_Empty()
    {
        var svc = new PromoCodeService();
        Assert.Null(svc.GetDiscountPercent(null));
        Assert.Null(svc.GetDiscountPercent(""));
        Assert.Null(svc.GetDiscountPercent("   "));
    }

    [Fact]
    public void PromoCodeService_Returns_Discount_For_Known_Codes()
    {
        var svc = new PromoCodeService();
        Assert.Equal(0.10m, svc.GetDiscountPercent("WELCOME10"));
        Assert.Equal(0.25m, svc.GetDiscountPercent("BULK25"));
    }

    [Fact]
    public void PromoCodeService_Is_Case_Insensitive()
    {
        var svc = new PromoCodeService();
        Assert.Equal(0.10m, svc.GetDiscountPercent("welcome10"));
        Assert.Equal(0.25m, svc.GetDiscountPercent("Bulk25"));
    }

    [Fact]
    public void PromoCodeService_Returns_Null_For_Unknown_Code()
    {
        var svc = new PromoCodeService();
        Assert.Null(svc.GetDiscountPercent("NOPE99"));
    }

    [Fact]
    public async Task Submit_Without_PromoCode_Leaves_Total_Untouched()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 2, 10m));

        await f.Orders.SubmitAsync(order.Id);

        var fresh = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.NotNull(fresh);
        Assert.Equal(OrderStatus.Submitted, fresh!.Status);
        Assert.Equal(20m, fresh.TotalAmount.Amount);
        Assert.Equal(20m, fresh.DiscountedTotal.Amount);
        Assert.Null(fresh.PromoCode);
        Assert.Equal(0m, fresh.DiscountPercent);
    }

    [Fact]
    public async Task Submit_With_WELCOME10_Applies_10_Percent_Discount()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 2, 10m));

        await f.Orders.SubmitAsync(order.Id, "WELCOME10");

        var fresh = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.NotNull(fresh);
        Assert.Equal(OrderStatus.Submitted, fresh!.Status);
        Assert.Equal(20m, fresh.TotalAmount.Amount);
        Assert.Equal(18m, fresh.DiscountedTotal.Amount);
        Assert.Equal("WELCOME10", fresh.PromoCode);
        Assert.Equal(0.10m, fresh.DiscountPercent);
    }

    [Fact]
    public async Task Submit_With_BULK25_Applies_25_Percent_Discount()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("PALLET", 4, 25m));

        await f.Orders.SubmitAsync(order.Id, "BULK25");

        var fresh = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.NotNull(fresh);
        Assert.Equal(100m, fresh!.TotalAmount.Amount);
        Assert.Equal(75m, fresh.DiscountedTotal.Amount);
        Assert.Equal("BULK25", fresh.PromoCode);
        Assert.Equal(0.25m, fresh.DiscountPercent);
    }

    [Fact]
    public async Task Submit_With_Unknown_Code_Throws()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 1, 10m));

        // ErrorHandlingMiddleware maps InvalidOperationException → HTTP 400.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => f.Orders.SubmitAsync(order.Id, "BOGUS-CODE"));

        // Order should remain in Draft state (submission did not succeed).
        var fresh = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.Equal(OrderStatus.Draft, fresh!.Status);
    }

    [Fact]
    public async Task Submit_With_Empty_PromoCode_Treated_As_None()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 2, 10m));

        await f.Orders.SubmitAsync(order.Id, "");

        var fresh = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.Equal(20m, fresh!.DiscountedTotal.Amount);
        Assert.Null(fresh.PromoCode);
    }

    [Fact]
    public async Task Submit_PromoCode_Lookup_Is_Case_Insensitive()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 1, 100m));

        await f.Orders.SubmitAsync(order.Id, "welcome10");

        var fresh = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.Equal(90m, fresh!.DiscountedTotal.Amount);
        Assert.Equal(0.10m, fresh.DiscountPercent);
    }

    [Fact]
    public async Task Existing_Submitted_Orders_Are_Not_Retroactively_Discounted()
    {
        // An order submitted without a promo code stays at full price even if
        // a later submission of a different order uses a promo.
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var orderA = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 1, 100m));
        var orderB = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-B", 1, 100m));

        await f.Orders.SubmitAsync(orderA.Id);                 // no promo
        await f.Orders.SubmitAsync(orderB.Id, "WELCOME10");    // 10% off

        var freshA = await f.OrderRepo.GetByIdAsync(orderA.Id);
        var freshB = await f.OrderRepo.GetByIdAsync(orderB.Id);
        Assert.Equal(100m, freshA!.DiscountedTotal.Amount);    // unchanged
        Assert.Equal(90m, freshB!.DiscountedTotal.Amount);
    }
}
