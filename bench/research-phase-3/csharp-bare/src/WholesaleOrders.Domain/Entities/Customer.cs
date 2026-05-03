namespace WholesaleOrders.Domain.Entities;

public class Customer
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "";
    public string Email { get; init; } = "";
    public string BillingAddress { get; init; } = "";
    public string ShippingAddress { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    [Obsolete("Use Id instead.")]
    public string LegacyCustomerCode { get; init; } = "";
}
