namespace WholesaleOrders.Domain.ValueObjects;

public class Money
{
    public decimal Amount { get; }
    public string Currency { get; }

    public Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public static Money Zero(string currency) => new Money(0m, currency);

    // PURE: no effects. PRECONDITION: same currency. POSTCONDITION: result.Amount == this.Amount + other.Amount.
    public Money Add(Money other)
    {
        if (!string.Equals(Currency, other.Currency, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Cannot add {Currency} and {other.Currency}");
        return new Money(Amount + other.Amount, Currency);
    }

    // PURE: no effects. POSTCONDITION: result.Amount == this.Amount * factor.
    public Money Multiply(decimal factor) => new Money(Amount * factor, Currency);

    public override string ToString() => $"{Amount:0.00} {Currency}";

    public override bool Equals(object? obj)
    {
        if (obj is not Money other) return false;
        return Amount == other.Amount && string.Equals(Currency, other.Currency, StringComparison.OrdinalIgnoreCase);
    }

    public override int GetHashCode() => HashCode.Combine(Amount, Currency.ToLowerInvariant());

    public static bool operator ==(Money? left, Money? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(Money? left, Money? right) => !(left == right);
}
