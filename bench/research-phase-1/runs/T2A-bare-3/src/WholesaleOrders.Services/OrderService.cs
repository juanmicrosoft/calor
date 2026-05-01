using WholesaleOrders.Domain.Entities;
using WholesaleOrders.Domain.Enums;
using WholesaleOrders.Domain.ValueObjects;
using WholesaleOrders.Infra.Logging;
using WholesaleOrders.Infra.Persistence;
using WholesaleOrders.Services.Validators;

namespace WholesaleOrders.Services;

public interface IOrderService
{
    Task<Order> CreateDraftAsync(Guid customerId, string currency = "USD", CancellationToken ct = default);
    Task<Order> AddLineItemAsync(Guid orderId, Sku sku, int quantity, decimal unitPrice, CancellationToken ct = default);
    Task<Order> SubmitAsync(Guid orderId, CancellationToken ct = default);
    Task<Order> SubmitAsync(Guid orderId, string? promoCode, CancellationToken ct = default);
    Task<Order> MarkPaidAsync(Guid orderId, CancellationToken ct = default);
    Task<Order> MarkShippedAsync(Guid orderId, CancellationToken ct = default);
    Task<Order> MarkDeliveredAsync(Guid orderId, CancellationToken ct = default);
    Task<Order> CancelAsync(Guid orderId, CancellationToken ct = default);

    Money RecalculateTotal(Order order);
    decimal EstimateTotal(Order order);
}

public class OrderService : IOrderService
{
    private readonly IOrderRepository _orders;
    private readonly IOrderValidator _validator;
    private readonly IPromoCodeService _promoCodes;
    private readonly IStructuredLogger _logger;

    public OrderService(IOrderRepository orders, IOrderValidator validator, IStructuredLogger logger)
        : this(orders, validator, new PromoCodeService(), logger)
    {
    }

    public OrderService(IOrderRepository orders, IOrderValidator validator, IPromoCodeService promoCodes, IStructuredLogger logger)
    {
        _orders = orders;
        _validator = validator;
        _promoCodes = promoCodes;
        _logger = logger;
    }
    public async Task<Order> CreateDraftAsync(Guid customerId, string currency = "USD", CancellationToken ct = default)
    {
        _logger.Info("OrderService.CreateDraftAsync", new { customerId, currency });
        var order = new Order { CustomerId = customerId, Currency = currency };
        await _orders.AddAsync(order, ct);
        return order;
    }
    public async Task<Order> AddLineItemAsync(Guid orderId, Sku sku, int quantity, decimal unitPrice, CancellationToken ct = default)
    {
        _logger.Info("OrderService.AddLineItemAsync", new { orderId, sku = sku.Value, quantity });
        var order = await _orders.GetByIdAsync(orderId, ct)
            ?? throw new InvalidOperationException($"Order {orderId} not found.");
        if (order.Status != OrderStatus.Draft)
            throw new InvalidOperationException($"Cannot add line items to order in status {order.Status}.");
        var item = new OrderLineItem
        {
            OrderId = orderId,
            Sku = sku,
            Quantity = quantity,
            UnitPrice = unitPrice,
        };
        order.LineItems.Add(item);
        await _orders.AddLineItemAsync(item, ct);
        order.TotalAmount = RecalculateTotal(order);
        await _orders.UpdateAsync(order, ct);
        return order;
    }
    public Task<Order> SubmitAsync(Guid orderId, CancellationToken ct = default) =>
        SubmitAsync(orderId, promoCode: null, ct);

    public async Task<Order> SubmitAsync(Guid orderId, string? promoCode, CancellationToken ct = default)
    {
        _logger.Info("OrderService.SubmitAsync", new { orderId, promoCode });
        var order = await _orders.GetByIdAsync(orderId, ct)
            ?? throw new InvalidOperationException($"Order {orderId} not found.");
        var result = _validator.ValidateForSubmit(order);
        if (!result.IsValid)
            throw new InvalidOperationException(string.Join("; ", result.Errors));

        var subtotal = RecalculateTotal(order);
        order.Subtotal = subtotal;

        if (!string.IsNullOrWhiteSpace(promoCode))
        {
            // Throws InvalidOperationException for unknown codes (mapped to HTTP 400 by middleware).
            var percent = _promoCodes.GetDiscountPercent(promoCode);
            order.PromoCode = promoCode;
            order.DiscountPercent = percent;
            var discountAmt = Math.Round(subtotal.Amount * (percent / 100m), 2, MidpointRounding.ToEven);
            order.DiscountAmount = new Money(discountAmt, order.Currency);
            order.TotalAmount = new Money(
                Math.Round(subtotal.Amount - discountAmt, 4, MidpointRounding.ToEven),
                order.Currency);
        }
        else
        {
            order.PromoCode = null;
            order.DiscountPercent = 0m;
            order.DiscountAmount = Money.Zero(order.Currency);
            order.TotalAmount = subtotal;
        }

        order.Status = OrderStatus.Submitted;
        order.SubmittedAt = DateTimeOffset.UtcNow;
        await _orders.UpdateAsync(order, ct);
        return order;
    }
    public async Task<Order> MarkPaidAsync(Guid orderId, CancellationToken ct = default)
    {
        _logger.Info("OrderService.MarkPaidAsync", new { orderId });
        var order = await _orders.GetByIdAsync(orderId, ct)
            ?? throw new InvalidOperationException($"Order {orderId} not found.");
        if (order.Status != OrderStatus.Submitted)
            throw new InvalidOperationException($"Cannot mark paid an order in status {order.Status}.");
        order.Status = OrderStatus.Paid;
        order.PaidAt = DateTimeOffset.UtcNow;
        await _orders.UpdateAsync(order, ct);
        return order;
    }
    public async Task<Order> MarkShippedAsync(Guid orderId, CancellationToken ct = default)
    {
        _logger.Info("OrderService.MarkShippedAsync", new { orderId });
        var order = await _orders.GetByIdAsync(orderId, ct)
            ?? throw new InvalidOperationException($"Order {orderId} not found.");
        if (order.Status != OrderStatus.Paid)
            throw new InvalidOperationException($"Cannot mark shipped an order in status {order.Status}.");
        order.Status = OrderStatus.Shipped;
        order.ShippedAt = DateTimeOffset.UtcNow;
        await _orders.UpdateAsync(order, ct);
        return order;
    }
    public async Task<Order> MarkDeliveredAsync(Guid orderId, CancellationToken ct = default)
    {
        _logger.Info("OrderService.MarkDeliveredAsync", new { orderId });
        var order = await _orders.GetByIdAsync(orderId, ct)
            ?? throw new InvalidOperationException($"Order {orderId} not found.");
        if (order.Status != OrderStatus.Shipped)
            throw new InvalidOperationException($"Cannot mark delivered an order in status {order.Status}.");
        order.Status = OrderStatus.Delivered;
        order.DeliveredAt = DateTimeOffset.UtcNow;
        await _orders.UpdateAsync(order, ct);
        return order;
    }
    public async Task<Order> CancelAsync(Guid orderId, CancellationToken ct = default)
    {
        _logger.Info("OrderService.CancelAsync", new { orderId });
        var order = await _orders.GetByIdAsync(orderId, ct)
            ?? throw new InvalidOperationException($"Order {orderId} not found.");

        if (order.Status != OrderStatus.Draft && order.Status != OrderStatus.Submitted)
            throw new InvalidOperationException($"Cannot cancel an order in status {order.Status}.");

        order.Status = OrderStatus.Cancelled;
        order.CancelledAt = DateTimeOffset.UtcNow;
        await _orders.UpdateAsync(order, ct);
        return order;
    }
    public Money RecalculateTotal(Order order)
    {
        var sum = order.LineItems.Aggregate(0m, (acc, li) => acc + li.Quantity * li.UnitPrice);
        return new Money(Math.Round(sum, 4, MidpointRounding.ToEven), order.Currency);
    }
    public decimal EstimateTotal(Order order)
    {
        double estimate = 0.0;
        foreach (var li in order.LineItems)
            estimate += (double)li.Quantity * (double)li.UnitPrice;
        return (decimal)estimate;
    }
}
