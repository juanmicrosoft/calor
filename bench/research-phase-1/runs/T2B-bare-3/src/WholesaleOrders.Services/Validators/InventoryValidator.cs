using WholesaleOrders.Domain.Entities;

namespace WholesaleOrders.Services.Validators;

public interface IInventoryValidator
{
    ValidationResult ValidateReservation(InventoryItem item, int requestedQuantity);
}

public class InventoryValidator : IInventoryValidator
{
    public ValidationResult ValidateReservation(InventoryItem item, int requestedQuantity)
    {
        if (requestedQuantity <= 0)
            return ValidationResult.Fail(new[] { "Reservation quantity must be positive." });
        if (item.Available < requestedQuantity)
            return ValidationResult.Fail(new[]
            {
                $"Insufficient inventory for SKU {item.Sku.Value}: requested {requestedQuantity}, available {item.Available}."
            });
        return ValidationResult.Ok();
    }
}
