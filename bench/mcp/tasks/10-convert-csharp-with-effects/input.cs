public static class OrderService
{
    public static bool PlaceOrder(DbContext dbContext, string product, int quantity)
    {
        var order = new Order { Product = product, Quantity = quantity };
        dbContext.Orders.Add(order);
        var result = dbContext.SaveChanges();
        Console.WriteLine($"Order placed for {quantity}x {product}");
        return result > 0;
    }

    public static Order GetOrder(DbContext dbContext, int orderId)
    {
        return dbContext.Orders.Find(orderId);
    }
}
