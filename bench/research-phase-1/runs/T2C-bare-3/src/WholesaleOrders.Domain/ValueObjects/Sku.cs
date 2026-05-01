namespace WholesaleOrders.Domain.ValueObjects;

public readonly record struct Sku(string Value)
{
    public static Sku Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("SKU cannot be empty", nameof(raw));
        if (raw.Length > 64)
            throw new ArgumentException("SKU exceeds 64 characters", nameof(raw));
        return new Sku(raw.Trim().ToUpperInvariant());
    }

    public override string ToString() => Value;
}
