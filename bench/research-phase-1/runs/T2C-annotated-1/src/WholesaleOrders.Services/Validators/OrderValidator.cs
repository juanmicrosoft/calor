using WholesaleOrders.Domain.Entities;
using WholesaleOrders.Domain.Enums;

namespace WholesaleOrders.Services.Validators;

public interface IOrderValidator
{
    ValidationResult ValidateForSubmit(Order order);
    bool CanTransitionToCancelled(OrderStatus current);
}

public class OrderValidator : IOrderValidator
{
    // PURE: no effects. POSTCONDITION: result.IsValid iff order has line items, all positive, and Status == Draft.
    public ValidationResult ValidateForSubmit(Order order)
    {
        var errors = new List<string>();
        if (order.LineItems.Count == 0)
            errors.Add("Order must have at least one line item.");
        foreach (var li in order.LineItems)
        {
            if (li.Quantity <= 0)
                errors.Add($"Line item {li.Id} has non-positive quantity.");
            if (li.UnitPrice < 0)
                errors.Add($"Line item {li.Id} has negative unit price.");
        }
        if (order.Status != OrderStatus.Draft)
            errors.Add($"Cannot submit order in status {order.Status}.");
        return errors.Count == 0 ? ValidationResult.Ok() : ValidationResult.Fail(errors);
    }

    // PURE: no effects.
    public bool CanTransitionToCancelled(OrderStatus current) =>
        current is OrderStatus.Draft or OrderStatus.Submitted or OrderStatus.Paid;
}

public sealed record ValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static ValidationResult Ok() => new(true, Array.Empty<string>());
    public static ValidationResult Fail(IEnumerable<string> errors) => new(false, errors.ToList());
}
