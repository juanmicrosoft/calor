using WholesaleOrders.Domain.Entities;

namespace WholesaleOrders.Services.Validators;

public interface IInventoryValidator
{
    ValidationResult ValidateReservation(InventoryItem item, int requestedQuantity);
    ValidationResult ValidatePartialRelease(StockReservation reservation, int releaseQuantity);
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

    public ValidationResult ValidatePartialRelease(StockReservation reservation, int releaseQuantity)
    {
        if (releaseQuantity <= 0)
            return ValidationResult.Fail(new[] { "Release quantity must be positive." });
        if (releaseQuantity > reservation.Quantity)
            return ValidationResult.Fail(new[]
            {
                $"Release quantity {releaseQuantity} exceeds remaining reservation quantity {reservation.Quantity}."
            });
        return ValidationResult.Ok();
    }
}
