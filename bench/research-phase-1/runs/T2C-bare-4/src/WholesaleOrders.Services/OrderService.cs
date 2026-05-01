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
    Task<Order> MarkPaidAsync(Guid orderId, CancellationToken ct = default);
    Task<Order> MarkShippedAsync(Guid orderId, CancellationToken ct = default);
    Task<Order> MarkDeliveredAsync(Guid orderId, CancellationToken ct = default);
    Task<Order> CancelAsync(Guid orderId, CancellationToken ct = default);
    Task<Order> RecallAsync(Guid orderId, CancellationToken ct = default);

    Money RecalculateTotal(Order order);
    decimal EstimateTotal(Order order);
}

public class OrderService : IOrderService
{
    private readonly IOrderRepository _orders;
    private readonly IOrderValidator _validator;
    private readonly IStructuredLogger _logger;
    private readonly IInventoryRepository? _inventory;
    private readonly INotificationService? _notifications;

    public OrderService(IOrderRepository orders, IOrderValidator validator, IStructuredLogger logger)
    {
        _orders = orders;
        _validator = validator;
        _logger = logger;
    }

    public OrderService(
        IOrderRepository orders,
        IOrderValidator validator,
        IStructuredLogger logger,
        IInventoryRepository inventory,
        INotificationService notifications)
    {
        _orders = orders;
        _validator = validator;
        _logger = logger;
        _inventory = inventory;
        _notifications = notifications;
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
    public async Task<Order> SubmitAsync(Guid orderId, CancellationToken ct = default)
    {
        _logger.Info("OrderService.SubmitAsync", new { orderId });
        var order = await _orders.GetByIdAsync(orderId, ct)
            ?? throw new InvalidOperationException($"Order {orderId} not found.");
        var result = _validator.ValidateForSubmit(order);
        if (!result.IsValid)
            throw new InvalidOperationException(string.Join("; ", result.Errors));
        order.Status = OrderStatus.Submitted;
        order.SubmittedAt = DateTimeOffset.UtcNow;
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
    public async Task<Order> RecallAsync(Guid orderId, CancellationToken ct = default)
    {
        _logger.Info("OrderService.RecallAsync", new { orderId });
        var order = await _orders.GetByIdAsync(orderId, ct)
            ?? throw new InvalidOperationException($"Order {orderId} not found.");

        if (order.Status != OrderStatus.Shipped)
            throw new InvalidOperationException($"Cannot recall an order in status {order.Status}.");

        // Release all reservations associated with the order, returning inventory to the available pool.
        if (_inventory is not null)
        {
            var reservations = await _inventory.GetReservationsForOrderAsync(orderId, ct);
            foreach (var reservation in reservations)
            {
                if (reservation.Status == ReservationStatus.Released)
                    continue;

                var item = await _inventory.GetBySkuAsync(reservation.Sku, ct);
                if (item is not null)
                {
                    if (reservation.Status == ReservationStatus.Fulfilled)
                    {
                        // Return previously fulfilled stock back to on-hand.
                        item.OnHand += reservation.Quantity;
                    }
                    else
                    {
                        item.Reserved -= reservation.Quantity;
                        if (item.Reserved < 0) item.Reserved = 0;
                    }
                    await _inventory.UpdateAsync(item, ct);
                }

                reservation.Status = ReservationStatus.Released;
                reservation.ReleasedAt = DateTimeOffset.UtcNow;
                await _inventory.UpdateReservationAsync(reservation, ct);
            }
        }

        order.Status = OrderStatus.Submitted;
        order.RecalledAt = DateTimeOffset.UtcNow;
        order.ShippedAt = null;
        await _orders.UpdateAsync(order, ct);

        if (_notifications is not null)
        {
            await _notifications.NotifyAsync(
                topic: "order.recalled",
                message: $"Order {order.Id} has been recalled and returned to Submitted.",
                payload: new { orderId = order.Id, customerId = order.CustomerId, recalledAt = order.RecalledAt },
                ct: ct);
        }

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
