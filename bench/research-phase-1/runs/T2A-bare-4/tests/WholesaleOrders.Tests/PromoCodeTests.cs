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
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 2, 10m));

        var submitted = await f.Orders.SubmitAsync(order.Id);

        Assert.Equal(OrderStatus.Submitted, submitted.Status);
        Assert.Null(submitted.PromoCode);
        Assert.Equal(0m, submitted.DiscountPercent);
        Assert.Equal(20m, submitted.Subtotal.Amount);
        Assert.Equal(0m, submitted.DiscountAmount.Amount);
        Assert.Equal(20m, submitted.TotalAmount.Amount);
    }

    [Fact]
    public async Task Submit_With_Welcome10_Applies_10_Percent_Discount()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 2, 50m));

        var submitted = await f.Orders.SubmitAsync(order.Id, "WELCOME10");

        Assert.Equal("WELCOME10", submitted.PromoCode);
        Assert.Equal(10m, submitted.DiscountPercent);
        Assert.Equal(100m, submitted.Subtotal.Amount);
        Assert.Equal(10m, submitted.DiscountAmount.Amount);
        Assert.Equal(90m, submitted.TotalAmount.Amount);
    }

    [Fact]
    public async Task Submit_With_Bulk25_Applies_25_Percent_Discount()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("PALLET-100", 4, 25m));

        var submitted = await f.Orders.SubmitAsync(order.Id, "BULK25");

        Assert.Equal("BULK25", submitted.PromoCode);
        Assert.Equal(25m, submitted.DiscountPercent);
        Assert.Equal(100m, submitted.Subtotal.Amount);
        Assert.Equal(25m, submitted.DiscountAmount.Amount);
        Assert.Equal(75m, submitted.TotalAmount.Amount);
    }

    [Fact]
    public async Task Submit_With_PromoCode_Is_CaseInsensitive()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 1, 100m));

        var submitted = await f.Orders.SubmitAsync(order.Id, "welcome10");

        Assert.Equal(10m, submitted.DiscountPercent);
        Assert.Equal(90m, submitted.TotalAmount.Amount);
    }

    [Fact]
    public async Task Submit_With_Unknown_PromoCode_Throws_ArgumentException()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 1, 10m));

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => f.Orders.SubmitAsync(order.Id, "INVALID_CODE"));
        Assert.Contains("INVALID_CODE", ex.Message);

        // Order remains in Draft state — submission should not have applied.
        var fresh = await f.OrderRepo.GetByIdAsync(order.Id);
        Assert.Equal(OrderStatus.Draft, fresh!.Status);
    }

    [Fact]
    public async Task Submit_With_Empty_PromoCode_Treated_As_None()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 1, 50m));

        var submitted = await f.Orders.SubmitAsync(order.Id, "");

        Assert.Null(submitted.PromoCode);
        Assert.Equal(50m, submitted.TotalAmount.Amount);
    }

    [Fact]
    public async Task Submit_Existing_Order_Cannot_Be_Re_Submitted_With_PromoCode()
    {
        // Once submitted, validator rejects re-submission, so promo cannot be retroactively applied.
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 1, 100m));

        await f.Orders.SubmitAsync(order.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => f.Orders.SubmitAsync(order.Id, "WELCOME10"));
    }

    [Fact]
    public void PromoCodeService_GetDiscountPercent_Returns_Correct_Values()
    {
        var svc = new PromoCodeService();
        Assert.Equal(10m, svc.GetDiscountPercent("WELCOME10"));
        Assert.Equal(25m, svc.GetDiscountPercent("BULK25"));
    }

    [Fact]
    public void PromoCodeService_TryGetDiscountPercent_Handles_Unknown()
    {
        var svc = new PromoCodeService();
        Assert.False(svc.TryGetDiscountPercent("NOPE", out var p));
        Assert.Equal(0m, p);
        Assert.True(svc.TryGetDiscountPercent("BULK25", out var q));
        Assert.Equal(25m, q);
    }
}
