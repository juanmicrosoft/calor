// C# equivalent - no effect system
public class Order
{
    public string CustomerEmail { get; set; } = "";
    public decimal Total { get; set; }
}

public static class OrderService
{
    private static readonly HttpClient _httpClient = new();

    public static void SaveOrder(Order order)
    {
        // Effect: database write
        // using var db = new DbContext();
        // db.Orders.Add(order);
        // db.SaveChanges();
    }

    public static async Task NotifyCustomer(Order order)
    {
        // Effect: network write
        await _httpClient.PostAsync(
            $"https://api.email.com/send?to={order.CustomerEmail}",
            new StringContent("Order shipped"));
    }

    public static async Task ProcessOrder(Order order)
    {
        // BUG in Calor version: declares db:w but calls NotifyCustomer which has net:w
        // This would be caught by the effect system
        SaveOrder(order);
        await NotifyCustomer(order);
    }
}
