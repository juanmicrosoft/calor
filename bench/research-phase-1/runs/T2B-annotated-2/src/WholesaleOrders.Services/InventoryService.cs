using WholesaleOrders.Domain.Entities;
using WholesaleOrders.Domain.Enums;
using WholesaleOrders.Domain.ValueObjects;
using WholesaleOrders.Infra.Logging;
using WholesaleOrders.Infra.Persistence;
using WholesaleOrders.Services.Validators;

namespace WholesaleOrders.Services;

public interface IInventoryService
{
    Task<InventoryItem> AddItemAsync(Sku sku, string name, int onHand, decimal unitPrice, CancellationToken ct = default);
    Task<StockReservation> ReserveAsync(Guid orderId, Sku sku, int quantity, CancellationToken ct = default);
    Task<StockReservation> ConfirmAsync(Guid reservationId, CancellationToken ct = default);
    Task<StockReservation> ReleaseAsync(Guid reservationId, CancellationToken ct = default);
    Task<StockReservation> ReleasePartialAsync(Guid reservationId, int quantity, CancellationToken ct = default);
    Task<StockReservation> FulfillAsync(Guid reservationId, CancellationToken ct = default);
}

public class InventoryService : IInventoryService
{
    private readonly IInventoryRepository _inventory;
    private readonly IInventoryValidator _validator;
    private readonly IStructuredLogger _logger;

    public InventoryService(IInventoryRepository inventory, IInventoryValidator validator, IStructuredLogger logger)
    {
        _inventory = inventory;
        _validator = validator;
        _logger = logger;
    }

    // EFFECTS: db:w. POSTCONDITION: result.OnHand == onHand, result.Reserved == 0.
    public async Task<InventoryItem> AddItemAsync(Sku sku, string name, int onHand, decimal unitPrice, CancellationToken ct = default)
    {
        var item = new InventoryItem { Sku = sku, Name = name, OnHand = onHand, UnitPrice = unitPrice };
        await _inventory.AddAsync(item, ct);
        return item;
    }

    // EFFECTS: db:r, db:w, log, throw. PRECONDITION: inventory item exists for sku, available >= quantity. POSTCONDITION: item.Reserved increased by quantity, reservation.Status == Created.
    public async Task<StockReservation> ReserveAsync(Guid orderId, Sku sku, int quantity, CancellationToken ct = default)
    {
        var item = await _inventory.GetBySkuAsync(sku, ct);
        if (item is null)
        {
            _logger.Error("Inventory item not found", context: new { sku = sku.Value });
            throw new InvalidOperationException($"Inventory item {sku.Value} not found.");
        }
        var result = _validator.ValidateReservation(item, quantity);
        if (!result.IsValid)
            throw new InvalidOperationException(string.Join("; ", result.Errors));

        item.Reserved += quantity;
        await _inventory.UpdateAsync(item, ct);

        var reservation = new StockReservation
        {
            OrderId = orderId,
            Sku = sku,
            Quantity = quantity,
            Status = ReservationStatus.Created,
        };
        await _inventory.AddReservationAsync(reservation, ct);
        return reservation;
    }

    // EFFECTS: db:r, db:w, throw. PRECONDITION: reservation.Status == Created. POSTCONDITION: reservation.Status == Confirmed.
    public async Task<StockReservation> ConfirmAsync(Guid reservationId, CancellationToken ct = default)
    {
        var reservation = await GetReservationOrThrow(reservationId, ct);
        if (reservation.Status != ReservationStatus.Created)
            throw new InvalidOperationException($"Cannot confirm reservation in status {reservation.Status}.");
        reservation.Status = ReservationStatus.Confirmed;
        reservation.ConfirmedAt = DateTimeOffset.UtcNow;
        await _inventory.UpdateReservationAsync(reservation, ct);
        return reservation;
    }

    // EFFECTS: db:r, db:w, throw. PRECONDITION: reservation.Status not in {Released, Fulfilled}. POSTCONDITION: reservation.Status == Released, item.Reserved decreased by quantity.
    public async Task<StockReservation> ReleaseAsync(Guid reservationId, CancellationToken ct = default)
    {
        var reservation = await GetReservationOrThrow(reservationId, ct);
        if (reservation.Status == ReservationStatus.Released || reservation.Status == ReservationStatus.Fulfilled)
            throw new InvalidOperationException($"Reservation already in terminal status {reservation.Status}.");

        var item = await _inventory.GetBySkuAsync(reservation.Sku, ct);
        if (item is not null)
        {
            item.Reserved -= reservation.Quantity;
            if (item.Reserved < 0) item.Reserved = 0;
            await _inventory.UpdateAsync(item, ct);
        }

        reservation.Status = ReservationStatus.Released;
        reservation.ReleasedAt = DateTimeOffset.UtcNow;
        await _inventory.UpdateReservationAsync(reservation, ct);
        return reservation;
    }

    // EFFECTS: db:r, db:w, throw. PRECONDITION: reservation.Status not in {Released, Fulfilled}, 0 < quantity <= reservation.Quantity. POSTCONDITION: item.Reserved decreased by quantity, reservation.Quantity decreased by quantity; if remaining == 0 then reservation.Status == Released.
    public async Task<StockReservation> ReleasePartialAsync(Guid reservationId, int quantity, CancellationToken ct = default)
    {
        var reservation = await GetReservationOrThrow(reservationId, ct);
        if (reservation.Status == ReservationStatus.Released || reservation.Status == ReservationStatus.Fulfilled)
            throw new InvalidOperationException($"Reservation already in terminal status {reservation.Status}.");
        if (quantity <= 0)
            throw new InvalidOperationException("Release quantity must be positive.");
        if (quantity > reservation.Quantity)
            throw new InvalidOperationException($"Release quantity {quantity} exceeds remaining reservation quantity {reservation.Quantity}.");

        var item = await _inventory.GetBySkuAsync(reservation.Sku, ct);
        if (item is not null)
        {
            item.Reserved -= quantity;
            if (item.Reserved < 0) item.Reserved = 0;
            await _inventory.UpdateAsync(item, ct);
        }

        reservation.Quantity -= quantity;
        if (reservation.Quantity == 0)
        {
            reservation.Status = ReservationStatus.Released;
            reservation.ReleasedAt = DateTimeOffset.UtcNow;
        }
        await _inventory.UpdateReservationAsync(reservation, ct);
        return reservation;
    }

    // EFFECTS: db:r, db:w, throw. PRECONDITION: reservation.Status == Confirmed. POSTCONDITION: reservation.Status == Fulfilled, item.OnHand and item.Reserved both decreased by quantity.
    public async Task<StockReservation> FulfillAsync(Guid reservationId, CancellationToken ct = default)
    {
        var reservation = await GetReservationOrThrow(reservationId, ct);
        if (reservation.Status != ReservationStatus.Confirmed)
            throw new InvalidOperationException($"Cannot fulfill reservation in status {reservation.Status}.");

        var item = await _inventory.GetBySkuAsync(reservation.Sku, ct);
        if (item is not null)
        {
            item.OnHand -= reservation.Quantity;
            item.Reserved -= reservation.Quantity;
            if (item.OnHand < 0) item.OnHand = 0;
            if (item.Reserved < 0) item.Reserved = 0;
            await _inventory.UpdateAsync(item, ct);
        }

        reservation.Status = ReservationStatus.Fulfilled;
        reservation.FulfilledAt = DateTimeOffset.UtcNow;
        await _inventory.UpdateReservationAsync(reservation, ct);
        return reservation;
    }

    private async Task<StockReservation> GetReservationOrThrow(Guid id, CancellationToken ct)
    {
        var found = await _inventory.GetReservationByIdAsync(id, ct);
        if (found is null)
            throw new InvalidOperationException($"Reservation {id} not found.");
        return found;
    }
}
