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
    private readonly IStructuredLogger _logger;
    private readonly IPromoCodeService _promoCodes;

    public OrderService(IOrderRepository orders, IOrderValidator validator, IStructuredLogger logger)
        : this(orders, validator, logger, new PromoCodeService())
    {
    }

    public OrderService(IOrderRepository orders, IOrderValidator validator, IStructuredLogger logger, IPromoCodeService promoCodes)
    {
        _orders = orders;
        _validator = validator;
        _logger = logger;
        _promoCodes = promoCodes;
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
    public Task<Order> SubmitAsync(Guid orderId, CancellationToken ct = default)
        => SubmitAsync(orderId, promoCode: null, ct);

    public async Task<Order> SubmitAsync(Guid orderId, string? promoCode, CancellationToken ct = default)
    {
        _logger.Info("OrderService.SubmitAsync", new { orderId, promoCode });
        var order = await _orders.GetByIdAsync(orderId, ct)
            ?? throw new InvalidOperationException($"Order {orderId} not found.");
        var result = _validator.ValidateForSubmit(order);
        if (!result.IsValid)
            throw new InvalidOperationException(string.Join("; ", result.Errors));

        // Resolve promo code (if provided) before any state mutation so unknown codes
        // reject the submission entirely.
        decimal? discountPercent = null;
        string? normalizedCode = null;
        if (!string.IsNullOrWhiteSpace(promoCode))
        {
            if (!_promoCodes.TryGetDiscountPercent(promoCode, out var pct))
                throw new InvalidOperationException($"Unknown promo code '{promoCode}'.");
            discountPercent = pct;
            normalizedCode = promoCode;
        }

        order.Status = OrderStatus.Submitted;
        order.SubmittedAt = DateTimeOffset.UtcNow;
        order.PromoCode = normalizedCode;
        order.DiscountPercent = discountPercent;
        order.TotalAmount = RecalculateTotal(order);
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
        if (order.DiscountPercent is { } pct && pct > 0m)
        {
            // Apply flat percentage discount (e.g. 10% off → multiply by 0.90).
            sum = sum * (1m - (pct / 100m));
        }
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
