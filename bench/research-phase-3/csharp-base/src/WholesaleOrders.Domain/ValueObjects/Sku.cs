namespace WholesaleOrders.Domain.ValueObjects;

public class Sku
{
    public string Value { get; }

    public Sku(string value)
    {
        Value = value;
    }

    public static Sku Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("SKU cannot be empty", nameof(raw));
        if (raw.Length > 64)
            throw new ArgumentException("SKU exceeds 64 characters", nameof(raw));
        return new Sku(raw.Trim().ToUpperInvariant());
    }

    public override string ToString() => Value;

    public override bool Equals(object? obj)
    {
        if (obj is not Sku other) return false;
        return string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);
    }

    public override int GetHashCode() => Value?.ToLowerInvariant().GetHashCode() ?? 0;

    public static bool operator ==(Sku? left, Sku? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(Sku? left, Sku? right) => !(left == right);
}
