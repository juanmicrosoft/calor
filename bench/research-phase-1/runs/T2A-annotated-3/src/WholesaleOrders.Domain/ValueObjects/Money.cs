namespace WholesaleOrders.Domain.ValueObjects;

public readonly record struct Money(decimal Amount, string Currency)
{
    public static Money Zero(string currency) => new(0m, currency);

    // PURE: no effects. PRECONDITION: same currency. POSTCONDITION: result.Amount == this.Amount + other.Amount.
    public Money Add(Money other)
    {
        if (!string.Equals(Currency, other.Currency, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Cannot add {Currency} and {other.Currency}");
        return new Money(Amount + other.Amount, Currency);
    }

    // PURE: no effects. POSTCONDITION: result.Amount == this.Amount * factor.
    public Money Multiply(decimal factor) => new(Amount * factor, Currency);

    public override string ToString() => $"{Amount:0.00} {Currency}";
}
