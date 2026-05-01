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

    public OrderService(IOrderRepository orders, IOrderValidator validator, IStructuredLogger logger)
    {
        _orders = orders;
        _validator = validator;
        _logger = logger;
    }

    // EFFECTS: db:w, log. POSTCONDITION: result.Status == Draft, result.CustomerId == customerId.
    public async Task<Order> CreateDraftAsync(Guid customerId, string currency = "USD", CancellationToken ct = default)
    {
        _logger.Info("OrderService.CreateDraftAsync", new { customerId, currency });
        var order = new Order { CustomerId = customerId, Currency = currency };
        await _orders.AddAsync(order, ct);
        return order;
    }

    // EFFECTS: db:r, db:w, log. PRECONDITION: order exists, order.Status == Draft. POSTCONDITION: order.LineItems contains item, order.TotalAmount updated.
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

    // EFFECTS: db:r, db:w, log, throw. PRECONDITION: order exists, order.Status == Draft, validator passes. POSTCONDITION: order.Status == Submitted.
    public Task<Order> SubmitAsync(Guid orderId, CancellationToken ct = default)
        => SubmitAsync(orderId, promoCode: null, ct);

    // EFFECTS: db:r, db:w, log, throw. PRECONDITION: order exists, order.Status == Draft, validator passes, promoCode is null/empty or a known code.
    // POSTCONDITION: order.Status == Submitted, discount applied to TotalAmount when promoCode is recognized.
    public async Task<Order> SubmitAsync(Guid orderId, string? promoCode, CancellationToken ct = default)
    {
        _logger.Info("OrderService.SubmitAsync", new { orderId, promoCode });
        var order = await _orders.GetByIdAsync(orderId, ct)
            ?? throw new InvalidOperationException($"Order {orderId} not found.");
        var result = _validator.ValidateForSubmit(order);
        if (!result.IsValid)
            throw new InvalidOperationException(string.Join("; ", result.Errors));

        decimal discountPercent = 0m;
        string? appliedCode = null;
        if (!string.IsNullOrWhiteSpace(promoCode))
        {
            if (!PromoCodes.TryGetDiscount(promoCode, out discountPercent))
                throw new InvalidOperationException($"Unknown promo code '{promoCode}'.");
            appliedCode = promoCode;
        }

        order.Status = OrderStatus.Submitted;
        order.SubmittedAt = DateTimeOffset.UtcNow;
        order.PromoCode = appliedCode;
        order.DiscountPercent = discountPercent;
        var subtotal = RecalculateTotal(order);
        var discounted = subtotal.Amount * (1m - discountPercent / 100m);
        order.TotalAmount = new Money(Math.Round(discounted, 4, MidpointRounding.ToEven), order.Currency);

        await _orders.UpdateAsync(order, ct);
        return order;
    }

    // EFFECTS: db:r, db:w, log, throw. PRECONDITION: order.Status == Submitted. POSTCONDITION: order.Status == Paid.
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

    // EFFECTS: db:r, db:w, log, throw. PRECONDITION: order.Status == Paid. POSTCONDITION: order.Status == Shipped.
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

    // EFFECTS: db:r, db:w, log, throw. PRECONDITION: order.Status == Shipped. POSTCONDITION: order.Status == Delivered.
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

    // EFFECTS: db:r, db:w, log, throw. PRECONDITION: order.Status in {Draft, Submitted}. POSTCONDITION: order.Status == Cancelled.
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

    // PURE: no effects. Computes total from line items.
    public Money RecalculateTotal(Order order)
    {
        var sum = order.LineItems.Aggregate(0m, (acc, li) => acc + li.Quantity * li.UnitPrice);
        return new Money(Math.Round(sum, 4, MidpointRounding.ToEven), order.Currency);
    }

    // PURE: no effects. Returns an estimate of the order total.
    public decimal EstimateTotal(Order order)
    {
        double estimate = 0.0;
        foreach (var li in order.LineItems)
            estimate += (double)li.Quantity * (double)li.UnitPrice;
        return (decimal)estimate;
    }
}

// PURE: no effects. Hardcoded promo code registry. Returns false for unknown codes.
internal static class PromoCodes
{
    private static readonly Dictionary<string, decimal> _codes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["WELCOME10"] = 10m,
        ["BULK25"] = 25m,
    };

    public static bool TryGetDiscount(string code, out decimal discountPercent)
        => _codes.TryGetValue(code, out discountPercent);
}
