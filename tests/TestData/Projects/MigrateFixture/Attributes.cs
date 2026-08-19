using System;

namespace MigrateFixture;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class AuditableAttribute : Attribute
{
    public string Category { get; }

    public AuditableAttribute(string category)
    {
        Category = category;
    }
}

[Auditable("payments")]
public sealed class PaymentGateway
{
    [Obsolete("Use ChargeAsync instead")]
    public int Charge(int cents)
    {
        return cents * 2;
    }
}
