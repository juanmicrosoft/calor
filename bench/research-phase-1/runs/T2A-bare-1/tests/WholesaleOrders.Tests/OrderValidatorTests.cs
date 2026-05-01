using WholesaleOrders.Domain.Entities;
using WholesaleOrders.Domain.Enums;
using WholesaleOrders.Domain.ValueObjects;
using WholesaleOrders.Services.Validators;

namespace WholesaleOrders.Tests;

public class OrderValidatorTests
{
    private readonly IOrderValidator _validator = new OrderValidator();

    [Fact]
    public void Validate_OK_For_Draft_With_Valid_Items()
    {
        var order = new Order { CustomerId = Guid.NewGuid() };
        order.LineItems.Add(new OrderLineItem { OrderId = order.Id, Sku = Sku.Parse("A"), Quantity = 1, UnitPrice = 1m });
        Assert.True(_validator.ValidateForSubmit(order).IsValid);
    }

    [Fact]
    public void Validate_Fails_For_Empty_Order()
    {
        var order = new Order { CustomerId = Guid.NewGuid() };
        Assert.False(_validator.ValidateForSubmit(order).IsValid);
    }

    [Fact]
    public void Validate_Fails_For_NonDraft_Order()
    {
        var order = new Order { CustomerId = Guid.NewGuid(), Status = OrderStatus.Submitted };
        order.LineItems.Add(new OrderLineItem { OrderId = order.Id, Sku = Sku.Parse("A"), Quantity = 1, UnitPrice = 1m });
        Assert.False(_validator.ValidateForSubmit(order).IsValid);
    }

    [Fact]
    public void Validate_Fails_For_NonPositive_Quantity()
    {
        var order = new Order { CustomerId = Guid.NewGuid() };
        order.LineItems.Add(new OrderLineItem { OrderId = order.Id, Sku = Sku.Parse("A"), Quantity = 0, UnitPrice = 1m });
        Assert.False(_validator.ValidateForSubmit(order).IsValid);
    }

    [Fact]
    public void CanTransitionToCancelled_From_Draft_Submitted_Paid()
    {
        Assert.True(_validator.CanTransitionToCancelled(OrderStatus.Draft));
        Assert.True(_validator.CanTransitionToCancelled(OrderStatus.Submitted));
        Assert.True(_validator.CanTransitionToCancelled(OrderStatus.Paid));
        Assert.False(_validator.CanTransitionToCancelled(OrderStatus.Shipped));
        Assert.False(_validator.CanTransitionToCancelled(OrderStatus.Delivered));
        Assert.False(_validator.CanTransitionToCancelled(OrderStatus.Cancelled));
    }
}
