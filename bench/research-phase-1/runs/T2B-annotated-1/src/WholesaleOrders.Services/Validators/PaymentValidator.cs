using WholesaleOrders.Domain.Entities;
using WholesaleOrders.Domain.ValueObjects;

namespace WholesaleOrders.Services.Validators;

public interface IPaymentValidator
{
    ValidationResult ValidateCharge(Order order, Money amount);
}

public class PaymentValidator : IPaymentValidator
{
    // PURE: no effects. POSTCONDITION: result.IsValid iff amount > 0, currency matches, and amount equals order total.
    public ValidationResult ValidateCharge(Order order, Money amount)
    {
        if (amount.Amount <= 0)
            return ValidationResult.Fail(new[] { "Payment amount must be positive." });
        if (!string.Equals(amount.Currency, order.Currency, StringComparison.OrdinalIgnoreCase))
            return ValidationResult.Fail(new[] { $"Payment currency {amount.Currency} does not match order currency {order.Currency}." });
        if (amount.Amount != order.TotalAmount.Amount)
            return ValidationResult.Fail(new[] { $"Payment amount {amount.Amount} does not match order total {order.TotalAmount.Amount}." });
        return ValidationResult.Ok();
    }
}
