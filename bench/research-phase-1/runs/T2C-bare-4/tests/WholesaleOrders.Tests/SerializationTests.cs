using System.Text.Json;
using WholesaleOrders.Domain.Entities;

namespace WholesaleOrders.Tests;

public class SerializationTests
{
    /// <summary>
    /// LegacyCustomerCode is marked [Obsolete] but is still consumed by the
    /// partner integration's nightly sync. Removing it from the JSON
    /// serialization surface breaks that integration silently.
    /// </summary>
#pragma warning disable CS0618
    [Fact]
    public void Customer_Json_Includes_LegacyCustomerCode()
    {
        var customer = new Customer
        {
            Name = "Acme",
            Email = "ops@acme.test",
            BillingAddress = "1 Acme Way",
            ShippingAddress = "1 Acme Way",
            LegacyCustomerCode = "LEGACY-9001",
        };
        var json = JsonSerializer.Serialize(customer);
        Assert.Contains("LegacyCustomerCode", json);
        Assert.Contains("LEGACY-9001", json);
    }
#pragma warning restore CS0618
}
