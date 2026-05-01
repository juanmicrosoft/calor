using WholesaleOrders.Domain.Entities;
using WholesaleOrders.Domain.Enums;
using WholesaleOrders.Domain.ValueObjects;
using WholesaleOrders.Infra.Logging;
using WholesaleOrders.Infra.Persistence;
using WholesaleOrders.Services.Validators;

namespace WholesaleOrders.Services;

public interface IPaymentService
{
    Task<Payment> ChargeAsync(Guid orderId, Money amount, string customerToken, string idempotencyKey, string source, CancellationToken ct = default);
}

public class PaymentService : IPaymentService
{
    private readonly IOrderRepository _orders;
    private readonly IPaymentRepository _payments;
    private readonly IPaymentValidator _validator;
    private readonly IStructuredLogger _logger;

    public PaymentService(IOrderRepository orders, IPaymentRepository payments, IPaymentValidator validator, IStructuredLogger logger)
    {
        _orders = orders;
        _payments = payments;
        _validator = validator;
        _logger = logger;
    }
    // TODO: refactor — too many params
    public async Task<Payment> ChargeAsync(
        Guid orderId,
        Money amount,
        string customerToken,
        string idempotencyKey,
        string source,
        CancellationToken ct = default)
    {
        _logger.Info("PaymentService.ChargeAsync", new { orderId, amount = amount.Amount, source });

        var order = await _orders.GetByIdAsync(orderId, ct)
            ?? throw new InvalidOperationException($"Order {orderId} not found.");

        var validation = _validator.ValidateCharge(order, amount);
        if (!validation.IsValid)
            throw new InvalidOperationException(string.Join("; ", validation.Errors));

        var payment = new Payment
        {
            OrderId = orderId,
            Amount = amount,
            Status = PaymentStatus.Authorized,
            ProcessorReference = $"{source}:{idempotencyKey}:{customerToken[..Math.Min(8, customerToken.Length)]}",
        };
        await _payments.AddAsync(payment, ct);

        // Synthetic scaffold: capture immediately. Real flow would be auth then settle.
        payment.Status = PaymentStatus.Captured;
        payment.CapturedAt = DateTimeOffset.UtcNow;
        await _payments.UpdateAsync(payment, ct);

        return payment;
    }
}
