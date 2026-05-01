using WholesaleOrders.Domain.Enums;
using WholesaleOrders.Domain.ValueObjects;

namespace WholesaleOrders.Tests;

public class PaymentServiceTests
{
    [Fact]
    public async Task Charge_Creates_Captured_Payment()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 1, 25m));
        await f.Orders.SubmitAsync(order.Id);

        var payment = await f.Payments.ChargeAsync(
            order.Id,
            new Money(25m, order.Currency),
            "cust_xyz",
            "idem_1",
            "stripe");
        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.NotNull(payment.CapturedAt);
    }

    [Fact]
    public async Task Charge_With_Wrong_Currency_Throws()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 1, 25m));
        await f.Orders.SubmitAsync(order.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            f.Payments.ChargeAsync(order.Id, new Money(25m, "EUR"), "tok", "k", "s"));
    }

    [Fact]
    public async Task Charge_With_Wrong_Amount_Throws()
    {
        var f = new TestFactory();
        var c = await f.CreateCustomerAsync();
        var order = await f.CreateOrderWithItemsAsync(c.Id, ("WIDGET-A", 1, 25m));
        await f.Orders.SubmitAsync(order.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            f.Payments.ChargeAsync(order.Id, new Money(20m, order.Currency), "tok", "k", "s"));
    }
}
